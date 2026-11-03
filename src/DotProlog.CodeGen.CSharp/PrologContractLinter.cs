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

        PrologLintProgramModel model = PrologLintProgramModel.Build(sources);
        List<Diagnostic> diagnostics = [];
        foreach (ContractExport export in contract.Exports)
        {
            AnalyzeExport(model, export, contractFileName, diagnostics);
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
        PrologLintProgramModel model,
        ContractExport export,
        string contractFileName,
        List<Diagnostic> diagnostics
    )
    {
        var indicator = new PredicateIndicator(export.PredicateName, export.Arity);
        string[] modules =
        [
            .. model
                .Modules.Catalog.Definitions.Select(definition => definition.Name)
                .Where(module => model.Modules.ExportsOf(module).Contains(indicator)),
        ];
        if (modules is not [string module] || model.HasNonPredicateEffects(module))
        {
            return;
        }

        if (model.IsDynamicOrMultifile(module, indicator) || model.IsNonterminal(module, indicator))
        {
            return;
        }

        PrologLintProgramModel.ClauseContext[] clauses =
        [
            .. model.Clauses.Where(clause => clause.Module == module && clause.Indicator == indicator),
        ];
        if (clauses is not [PrologLintProgramModel.ClauseContext first, ..] || first.Grammar || !IsFact(first.Clause))
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
