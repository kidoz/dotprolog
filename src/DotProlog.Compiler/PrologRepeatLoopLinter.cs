using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in direct repeat goals without a cut in the clause's own cut scope.</summary>
internal static class PrologRepeatLoopLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.PossibleRepeatWithoutCut) != true)
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

        if (options?.Enables(LintDiagnosticIds.PossibleRepeatWithoutCut) != true)
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
            || LintCutScope.FirstClauseCut(rule.Arguments[1], grammar) is not null
            || FirstRepeat(rule.Arguments[1], grammar) is not { } repeat
        )
        {
            return;
        }

        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.PossibleRepeatWithoutCut,
                DiagnosticSeverity.Warning,
                $"Direct repeat/0 in {(grammar ? "nonterminal" : "predicate")} {LintSyntax.Display(indicator, grammar)} has no cut in the clause's own cut scope; review backtracking and loop-exit behavior.",
                repeat,
                fileName
            )
        );
    }

    private static SourceSpan? FirstRepeat(SyntaxTerm body, bool grammar)
    {
        switch (body)
        {
            case AtomTerm { Name: "repeat" } repeat:
                return repeat.Span;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary when grammar:
                return FirstRepeat(ordinary.Arguments[0], grammar: false);
            case CompoundTerm { Name: ",", Arity: 2 } conjunction:
                return FirstRepeat(conjunction.Arguments[0], grammar) ?? FirstRepeat(conjunction.Arguments[1], grammar);
            case CompoundTerm { Name: ";", Arity: 2 } disjunction:
                if (disjunction.Arguments[0] is CompoundTerm { Name: "->" or "*->", Arity: 2 } ifConditional)
                {
                    return FirstRepeat(ifConditional.Arguments[0], grammar)
                        ?? FirstRepeat(ifConditional.Arguments[1], grammar)
                        ?? FirstRepeat(disjunction.Arguments[1], grammar);
                }

                return FirstRepeat(disjunction.Arguments[0], grammar) ?? FirstRepeat(disjunction.Arguments[1], grammar);
            case CompoundTerm { Name: "->" or "*->", Arity: 2 } conditional:
                return FirstRepeat(conditional.Arguments[0], grammar) ?? FirstRepeat(conditional.Arguments[1], grammar);
            default:
                return null;
        }
    }
}
