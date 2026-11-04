using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp;

/// <summary>Checks statically provable mismatches between Prolog facts and their .NET contracts.</summary>
public static class PrologContractLinter
{
    /// <summary>Analyzes contract exports against explicitly supplied, already-read source units.</summary>
    /// <param name="sources">Physical Prolog sources forming one analysis unit.</param>
    /// <param name="contract">The parsed .NET contract paired with one source unit.</param>
    /// <param name="contractFileName">Physical contract path used in findings.</param>
    /// <param name="options">Lint options; the contract output rule runs only when explicitly enabled.</param>
    public static IReadOnlyList<Diagnostic> Analyze(
        IReadOnlyList<PrologLintSource> sources,
        ModuleContract contract,
        string contractFileName,
        PrologLintOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contractFileName);
        if (options?.Enables(LintDiagnosticIds.ContractOutputTypeMismatch) != true)
        {
            return [];
        }

        HashSet<PredicateIndicator> contracted = [];
        foreach (ContractExport export in contract.Exports)
        {
            contracted.Add(new PredicateIndicator(export.PredicateName, export.Arity));
        }

        PrologLintProgramModel model = PrologLintProgramModel.BuildForPredicates(sources, contracted);
        Dictionary<PredicateIndicator, string> exportingModules = [];
        HashSet<PredicateIndicator> ambiguous = [];
        foreach (ModuleDefinition definition in model.Modules.Catalog.Definitions)
        {
            foreach (PredicateIndicator indicator in model.Modules.ExportsOf(definition.Name))
            {
                if (contracted.Contains(indicator) && !exportingModules.TryAdd(indicator, definition.Name))
                {
                    ambiguous.Add(indicator);
                }
            }
        }

        Dictionary<PredicateIndicator, string> eligibleModules = [];
        foreach (PredicateIndicator indicator in contracted)
        {
            if (
                !ambiguous.Contains(indicator)
                && exportingModules.TryGetValue(indicator, out string? module)
                && !model.HasNonPredicateEffects(module)
                && !model.IsDynamicOrMultifile(module, indicator)
                && !model.IsNonterminal(module, indicator)
            )
            {
                eligibleModules.Add(indicator, module);
            }
        }

        Dictionary<(string Module, PredicateIndicator Indicator), PrologLintProgramModel.ClauseContext> firstClauses = [];
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            if (eligibleModules.TryGetValue(clause.Indicator, out string? module) && clause.Module == module)
            {
                firstClauses.TryAdd((module, clause.Indicator), clause);
            }
        }

        List<Diagnostic> diagnostics = [];
        foreach (ContractExport export in contract.Exports)
        {
            var indicator = new PredicateIndicator(export.PredicateName, export.Arity);
            if (
                eligibleModules.TryGetValue(indicator, out string? module)
                && firstClauses.TryGetValue((module, indicator), out PrologLintProgramModel.ClauseContext? first)
            )
            {
                AnalyzeExport(module, indicator, export, first, contractFileName, diagnostics);
            }
        }

        return
        [
            .. diagnostics
                .OrderBy(diagnostic => diagnostic.FileName, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Span.Start)
                .ThenBy(diagnostic => diagnostic.Id),
        ];
    }

    private static void AnalyzeExport(
        string module,
        PredicateIndicator indicator,
        ContractExport export,
        PrologLintProgramModel.ClauseContext first,
        string contractFileName,
        List<Diagnostic> diagnostics
    )
    {
        if (first.Grammar || !IsFact(first.Clause))
        {
            return;
        }

        SyntaxTerm head = LintSyntax.ClauseHead(first.Clause);
        IReadOnlyList<SyntaxTerm> headArguments = head switch
        {
            AtomTerm when export.Arity == 0 => [],
            CompoundTerm compound when compound.Arity == export.Arity => compound.Arguments,
            _ => null!,
        };
        if (headArguments is null)
        {
            return;
        }

        for (var index = 0; index < export.Arguments.Count; index++)
        {
            ContractArgument argument = export.Arguments[index];
            if (
                argument.Mode == ArgumentMode.In
                && !ContractTermCompatibility.MatchesGroundInput(argument.Type, headArguments[index])
            )
            {
                return;
            }
        }

        for (var index = 0; index < export.Arguments.Count; index++)
        {
            ContractArgument argument = export.Arguments[index];
            if (
                argument.Mode == ArgumentMode.Out
                && ContractTermCompatibility.TryGroundValue(headArguments[index], out PrologValue? value)
                && !argument.Type.CanMarshalOutput(value)
            )
            {
                diagnostics.Add(
                    new Diagnostic(
                        LintDiagnosticIds.ContractOutputTypeMismatch,
                        DiagnosticSeverity.Warning,
                        $"Output {argument.Name} of {module}:{indicator} has ground shape {ContractTermCompatibility.ShapeOf(value)}, incompatible with contract type {argument.Type} declared by {contractFileName}; the fact output cannot be marshalled by the typed facade.",
                        headArguments[index].Span,
                        first.Source.FileName
                    )
                );
            }
        }
    }

    private static bool IsFact(SyntaxTerm clause) => clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 };
}
