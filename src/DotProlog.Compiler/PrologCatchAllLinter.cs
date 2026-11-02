using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in direct catch goals with variable catchers.</summary>
internal static class PrologCatchAllLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.PossibleCatchAll) != true)
        {
            return;
        }

        foreach (SyntaxTerm clause in clauses)
        {
            AnalyzeClause(clause, fileName, diagnostics);
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.PossibleCatchAll) != true)
        {
            return;
        }

        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            AnalyzeClause(clause.Clause, clause.Source.FileName, diagnostics);
        }
    }

    private static void AnalyzeClause(SyntaxTerm clause, string? fileName, List<Diagnostic> diagnostics)
    {
        var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        if (
            clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 } rule
            || !LintSyntax.TryPredicate(LintSyntax.ClauseHead(rule), grammar, out PredicateIndicator indicator)
        )
        {
            return;
        }

        AnalyzeGoals(rule.Arguments[1], indicator, grammar, fileName, diagnostics);
    }

    private static void AnalyzeGoals(
        SyntaxTerm goal,
        PredicateIndicator indicator,
        bool grammar,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        switch (goal)
        {
            case CompoundTerm { Name: "catch", Arity: 3 } catchGoal:
                if (catchGoal.Arguments[1] is VariableTerm catcher)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            LintDiagnosticIds.PossibleCatchAll,
                            DiagnosticSeverity.Warning,
                            $"Variable catcher in {(grammar ? "nonterminal" : "predicate")} {LintSyntax.Display(indicator, grammar)} may match every thrown term; review whether broad recovery is intentional.",
                            catcher.Span,
                            fileName
                        )
                    );
                }

                AnalyzeGoals(catchGoal.Arguments[0], indicator, grammar, fileName, diagnostics);
                AnalyzeGoals(catchGoal.Arguments[2], indicator, grammar, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary when grammar:
                AnalyzeGoals(ordinary.Arguments[0], indicator, grammar: false, fileName, diagnostics);
                return;
            case CompoundTerm { Name: ",", Arity: 2 } conjunction:
                AnalyzeGoals(conjunction.Arguments[0], indicator, grammar, fileName, diagnostics);
                AnalyzeGoals(conjunction.Arguments[1], indicator, grammar, fileName, diagnostics);
                return;
            case CompoundTerm { Name: ";", Arity: 2 } disjunction:
                if (disjunction.Arguments[0] is CompoundTerm { Name: "->" or "*->", Arity: 2 } ifConditional)
                {
                    AnalyzeGoals(ifConditional.Arguments[0], indicator, grammar, fileName, diagnostics);
                    AnalyzeGoals(ifConditional.Arguments[1], indicator, grammar, fileName, diagnostics);
                }
                else
                {
                    AnalyzeGoals(disjunction.Arguments[0], indicator, grammar, fileName, diagnostics);
                }

                AnalyzeGoals(disjunction.Arguments[1], indicator, grammar, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "->" or "*->", Arity: 2 } conditional:
                AnalyzeGoals(conditional.Arguments[0], indicator, grammar, fileName, diagnostics);
                AnalyzeGoals(conditional.Arguments[1], indicator, grammar, fileName, diagnostics);
                return;
        }
    }
}
