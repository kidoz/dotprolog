using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp;

/// <summary>Checks statically provable mismatches between Prolog clauses and their .NET contracts.</summary>
public static class PrologContractLinter
{
    /// <summary>Analyzes contract exports against explicitly supplied, already-read source units.</summary>
    /// <param name="sources">Physical Prolog sources forming one analysis unit.</param>
    /// <param name="contract">The parsed .NET contract paired with one source unit.</param>
    /// <param name="contractFileName">Physical contract path used in findings.</param>
    /// <param name="options">Lint options for the default fact and ground-guarded rule checks.</param>
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
        var analyzeLater = options?.Enables(LintDiagnosticIds.LaterContractOutputTypeMismatch) != false;
        var analyzeGroundRules = options?.Enables(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch) != false;
        if (
            options is not null
            && !options.Enables(LintDiagnosticIds.ContractOutputTypeMismatch)
            && !analyzeLater
            && !analyzeGroundRules
        )
        {
            return [];
        }

        HashSet<PredicateIndicator> contracted = [];
        foreach (ContractExport export in contract.Exports)
        {
            contracted.Add(new PredicateIndicator(export.PredicateName, export.Arity));
        }

        PrologLintProgramModel model = PrologLintProgramModel.BuildForPredicates(
            sources,
            contracted,
            leadingOnly: !analyzeLater && !analyzeGroundRules
        );
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
                if (analyzeLater || analyzeGroundRules)
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
        using Machine? unificationMachine =
            analyzeGroundRules
            || (analyzeLater && contract.Exports.Any(export => export.Determinism is Determinism.Det or Determinism.Semidet))
                ? new Machine(new BytecodeProgram())
                : null;
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
                AnalyzeLaterExport(laterModule, indicator, export, table, contractFileName, unificationMachine, diagnostics);
            }

            if (
                analyzeGroundRules
                && eligibleModules.TryGetValue(indicator, out string? groundModule)
                && factTables.TryGetValue((groundModule, indicator), out List<PrologLintProgramModel.ClauseContext>? groundTable)
            )
            {
                AnalyzeGroundRuleExport(
                    groundModule,
                    indicator,
                    export,
                    groundTable,
                    contractFileName,
                    unificationMachine!,
                    diagnostics
                );
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
        Machine? unificationMachine,
        List<Diagnostic> diagnostics
    )
    {
        if (clauses.Count < 2 || !TryBuildRows(export, clauses, out ClauseRow[]? rows))
        {
            return;
        }

        for (var candidate = 1; candidate < rows.Length; candidate++)
        {
            if (
                export.Determinism is Determinism.Det or Determinism.Semidet
                && EarlierInputUnifies(export, rows, candidate, unificationMachine!)
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

    private static void AnalyzeGroundRuleExport(
        string module,
        PredicateIndicator indicator,
        ContractExport export,
        List<PrologLintProgramModel.ClauseContext> clauses,
        string contractFileName,
        Machine unificationMachine,
        List<Diagnostic> diagnostics
    )
    {
        if (!TryBuildGuardedRows(export, clauses, unificationMachine, out ClauseRow[]? rows))
        {
            return;
        }

        for (var candidate = 0; candidate < rows.Length; candidate++)
        {
            ClauseRow row = rows[candidate];
            if (!row.IsRule || !row.BodySucceeds)
            {
                continue;
            }

            if (
                export.Determinism is Determinism.Det or Determinism.Semidet
                && EarlierInputUnifies(export, rows, candidate, unificationMachine, successfulBodiesOnly: true)
            )
            {
                continue;
            }

            for (var index = 0; index < export.Arguments.Count; index++)
            {
                ContractArgument argument = export.Arguments[index];
                PrologValue value = row.Values[index];
                if (argument.Mode == ArgumentMode.Out && !argument.Type.CanMarshalOutput(value))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            LintDiagnosticIds.GroundRuleContractOutputTypeMismatch,
                            DiagnosticSeverity.Warning,
                            $"Output {argument.Name} of {module}:{indicator} has ground shape {ContractTermCompatibility.ShapeOf(value)}, incompatible with contract type {argument.Type} declared by {contractFileName}; the ground rule output cannot be marshalled by the typed facade.",
                            row.Arguments[index].Span,
                            row.Clause.Source.FileName
                        )
                    );
                }
            }
        }
    }

    private static bool TryBuildRows(
        ContractExport export,
        List<PrologLintProgramModel.ClauseContext> clauses,
        out ClauseRow[] rows
    )
    {
        rows = new ClauseRow[clauses.Count];
        for (var clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            PrologLintProgramModel.ClauseContext clause = clauses[clauseIndex];
            if (!IsFact(clause.Clause) || !TryBuildHeadRow(export, clause, out ClauseRow? row))
            {
                rows = null!;
                return false;
            }

            rows[clauseIndex] = row;
        }

        return true;
    }

    private static bool TryBuildGuardedRows(
        ContractExport export,
        List<PrologLintProgramModel.ClauseContext> clauses,
        Machine unificationMachine,
        out ClauseRow[] rows
    )
    {
        rows = new ClauseRow[clauses.Count];
        for (var clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            PrologLintProgramModel.ClauseContext clause = clauses[clauseIndex];
            if (!TryBuildHeadRow(export, clause, out ClauseRow? row))
            {
                rows = null!;
                return false;
            }

            if (row.IsRule)
            {
                var rule = (CompoundTerm)clause.Clause;
                if (!TryEvaluateGroundBody(rule.Arguments[1], unificationMachine, out bool bodySucceeds))
                {
                    rows = null!;
                    return false;
                }

                row = row with { BodySucceeds = bodySucceeds };
            }

            rows[clauseIndex] = row;
        }

        return true;
    }

    private static bool TryBuildHeadRow(ContractExport export, PrologLintProgramModel.ClauseContext clause, out ClauseRow row)
    {
        row = null!;
        if (clause.Grammar)
        {
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
                return false;
            }

            values[index] = value;
        }

        var isRule = clause.Clause is CompoundTerm { Name: ":-", Arity: 2 };
        row = new ClauseRow(clause, arguments, values, isRule, !isRule);
        return true;
    }

    private static bool TryEvaluateGroundBody(SyntaxTerm body, Machine unificationMachine, out bool succeeds)
    {
        if (body is CompoundTerm { Name: ",", Arity: 2 } conjunction)
        {
            if (
                !TryEvaluateGroundBody(conjunction.Arguments[0], unificationMachine, out bool leftSucceeds)
                || !TryEvaluateGroundBody(conjunction.Arguments[1], unificationMachine, out bool rightSucceeds)
            )
            {
                succeeds = false;
                return false;
            }

            succeeds = leftSucceeds && rightSucceeds;
            return true;
        }

        if (body is AtomTerm { Name: "true" })
        {
            succeeds = true;
            return true;
        }

        if (
            body is CompoundTerm { Name: "=", Arity: 2 } unification
            && ContractTermCompatibility.TryGroundValue(unification.Arguments[0], out PrologValue? left)
            && ContractTermCompatibility.TryGroundValue(unification.Arguments[1], out PrologValue? right)
        )
        {
            succeeds = RuntimeUnifiable(left, right, unificationMachine);
            return true;
        }

        succeeds = false;
        return false;
    }

    private static bool EarlierInputUnifies(
        ContractExport export,
        IReadOnlyList<ClauseRow> rows,
        int candidate,
        Machine machine,
        bool successfulBodiesOnly = false
    )
    {
        for (var earlier = 0; earlier < candidate; earlier++)
        {
            if (successfulBodiesOnly && !rows[earlier].BodySucceeds)
            {
                continue;
            }

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
        switch (left)
        {
            case PrologAtom leftAtom when right is PrologAtom rightAtom:
                return leftAtom.Name == rightAtom.Name;
            case PrologString leftString when right is PrologString rightString:
                return leftString.Value == rightString.Value;
            case PrologInteger leftInteger when right is PrologInteger rightInteger:
                return leftInteger.Value == rightInteger.Value;
            case PrologBigInteger leftBig when right is PrologBigInteger rightBig:
                return leftBig.Value == rightBig.Value;
            case PrologRational leftRational when right is PrologRational rightRational:
                return leftRational.Numerator == rightRational.Numerator && leftRational.Denominator == rightRational.Denominator;
            case PrologFloat leftFloat when right is PrologFloat rightFloat:
                return leftFloat.Value.Equals(rightFloat.Value);
        }

        Cell leftCell = PrologInput.FromValue(left).Build(machine);
        Cell rightCell = PrologInput.FromValue(right).Build(machine);
        return machine.CanUnify(leftCell, rightCell);
    }

    private sealed record ClauseRow(
        PrologLintProgramModel.ClauseContext Clause,
        IReadOnlyList<SyntaxTerm> Arguments,
        PrologValue[] Values,
        bool IsRule,
        bool BodySucceeds
    );

    private static bool IsFact(SyntaxTerm clause) => clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 };
}
