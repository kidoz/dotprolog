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
    /// <param name="options">Lint options for the default leading-fact and opt-in later-fact rules.</param>
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
        var analyzeLater = options?.Enables(LintDiagnosticIds.LaterContractOutputTypeMismatch) == true;
        if (options is not null && !options.Enables(LintDiagnosticIds.ContractOutputTypeMismatch) && !analyzeLater)
        {
            return [];
        }

        HashSet<PredicateIndicator> contracted = [];
        foreach (ContractExport export in contract.Exports)
        {
            contracted.Add(new PredicateIndicator(export.PredicateName, export.Arity));
        }

        PrologLintProgramModel model = PrologLintProgramModel.BuildForPredicates(sources, contracted, leadingOnly: !analyzeLater);
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
        Dictionary<(string Module, PredicateIndicator Indicator), List<PrologLintProgramModel.ClauseContext>> factTables = [];
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            if (eligibleModules.TryGetValue(clause.Indicator, out string? module) && clause.Module == module)
            {
                var key = (module, clause.Indicator);
                firstClauses.TryAdd(key, clause);
                if (analyzeLater)
                {
                    if (!factTables.TryGetValue(key, out List<PrologLintProgramModel.ClauseContext>? table))
                    {
                        table = [];
                        factTables.Add(key, table);
                    }

                    table.Add(clause);
                }
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

            if (
                analyzeLater
                && eligibleModules.TryGetValue(indicator, out string? laterModule)
                && factTables.TryGetValue((laterModule, indicator), out List<PrologLintProgramModel.ClauseContext>? table)
            )
            {
                AnalyzeLaterExport(laterModule, indicator, export, table, contractFileName, diagnostics);
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

    private static void AnalyzeLaterExport(
        string module,
        PredicateIndicator indicator,
        ContractExport export,
        List<PrologLintProgramModel.ClauseContext> clauses,
        string contractFileName,
        List<Diagnostic> diagnostics
    )
    {
        if (clauses.Count < 2 || !TryBuildRows(export, clauses, out FactRow[]? rows))
        {
            return;
        }

        using var machine = new Machine(new BytecodeProgram());
        for (var candidate = 1; candidate < rows.Length; candidate++)
        {
            if (
                export.Determinism is Determinism.Det or Determinism.Semidet
                && EarlierInputUnifies(export, rows, candidate, machine)
            )
            {
                continue;
            }

            for (var index = 0; index < export.Arguments.Count; index++)
            {
                ContractArgument argument = export.Arguments[index];
                PrologValue value = rows[candidate].Values[index];
                if (argument.Mode == ArgumentMode.Out && !argument.Type.CanMarshalOutput(value))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            LintDiagnosticIds.LaterContractOutputTypeMismatch,
                            DiagnosticSeverity.Warning,
                            $"Output {argument.Name} of {module}:{indicator} has ground shape {ContractTermCompatibility.ShapeOf(value)}, incompatible with contract type {argument.Type} declared by {contractFileName}; the later fact output cannot be marshalled by the typed facade.",
                            rows[candidate].Arguments[index].Span,
                            rows[candidate].Clause.Source.FileName
                        )
                    );
                }
            }
        }
    }

    private static bool TryBuildRows(
        ContractExport export,
        List<PrologLintProgramModel.ClauseContext> clauses,
        out FactRow[] rows
    )
    {
        rows = new FactRow[clauses.Count];
        for (var clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            PrologLintProgramModel.ClauseContext clause = clauses[clauseIndex];
            if (clause.Grammar || !IsFact(clause.Clause))
            {
                rows = null!;
                return false;
            }

            SyntaxTerm head = LintSyntax.ClauseHead(clause.Clause);
            IReadOnlyList<SyntaxTerm> arguments = head switch
            {
                AtomTerm when export.Arity == 0 => [],
                CompoundTerm compound when compound.Arity == export.Arity => compound.Arguments,
                _ => null!,
            };
            if (arguments is null)
            {
                rows = null!;
                return false;
            }

            var values = new PrologValue[arguments.Count];
            for (var index = 0; index < arguments.Count; index++)
            {
                ContractArgument argument = export.Arguments[index];
                if (
                    !ContractTermCompatibility.TryGroundValue(arguments[index], out PrologValue? value)
                    || (
                        argument.Mode == ArgumentMode.In
                        && !ContractTermCompatibility.MatchesGroundInput(argument.Type, arguments[index])
                    )
                )
                {
                    rows = null!;
                    return false;
                }

                values[index] = value;
            }

            rows[clauseIndex] = new FactRow(clause, arguments, values);
        }

        return true;
    }

    private static bool EarlierInputUnifies(ContractExport export, IReadOnlyList<FactRow> rows, int candidate, Machine machine)
    {
        for (var earlier = 0; earlier < candidate; earlier++)
        {
            var unifies = true;
            for (var index = 0; index < export.Arguments.Count; index++)
            {
                if (
                    export.Arguments[index].Mode == ArgumentMode.In
                    && !RuntimeUnifiable(rows[earlier].Values[index], rows[candidate].Values[index], machine)
                )
                {
                    unifies = false;
                    break;
                }
            }

            if (unifies)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RuntimeUnifiable(PrologValue left, PrologValue right, Machine machine)
    {
        machine.BeginCall();
        Cell leftCell = PrologInput.FromValue(left).Build(machine);
        Cell rightCell = PrologInput.FromValue(right).Build(machine);
        return machine.CanUnify(leftCell, rightCell);
    }

    private sealed record FactRow(
        PrologLintProgramModel.ClauseContext Clause,
        IReadOnlyList<SyntaxTerm> Arguments,
        PrologValue[] Values
    );

    private static bool IsFact(SyntaxTerm clause) => clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 };
}
