using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in rules above a configurable direct-subgoal limit.</summary>
internal static class PrologExcessiveSubgoalCountLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.ExcessiveSubgoalCount) != true)
        {
            return;
        }

        int maximumSubgoals = MaximumSubgoals(options);
        var module = ModuleTable.UserModule;
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm directive] })
            {
                module = NextModule(directive, module);
                continue;
            }

            AnalyzeClause(clause, module, maximumSubgoals, fileName, diagnostics);
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.ExcessiveSubgoalCount) != true)
        {
            return;
        }

        int maximumSubgoals = MaximumSubgoals(options);
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            AnalyzeClause(clause.Clause, clause.Module, maximumSubgoals, clause.Source.FileName, diagnostics);
        }
    }

    private static int MaximumSubgoals(PrologLintOptions options)
    {
        if (options.MaxClauseSubgoals < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxClauseSubgoals,
                "Maximum clause subgoal count must be positive."
            );
        }

        return options.MaxClauseSubgoals;
    }

    private static void AnalyzeClause(
        SyntaxTerm clause,
        string module,
        int maximumSubgoals,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        if (
            clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 } rule
            || !LintSyntax.TryPredicate(LintSyntax.ClauseHead(rule), grammar, out PredicateIndicator indicator)
        )
        {
            return;
        }

        int count = CountGoals(rule.Arguments[1], grammar);
        if (count <= maximumSubgoals)
        {
            return;
        }

        SyntaxTerm head = LintSyntax.ClauseHead(rule);
        SourceSpan nameSpan = head is CompoundTerm { NameSpan: { } span } ? span : head.Span;
        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.ExcessiveSubgoalCount,
                DiagnosticSeverity.Warning,
                $"{(grammar ? "Grammar rule for nonterminal" : "Rule for predicate")} {module}:{LintSyntax.Display(indicator, grammar)} has {count} direct subgoals, exceeding the configured maximum of {maximumSubgoals}; review whether the clause organization is intentional.",
                nameSpan,
                fileName
            )
        );
    }

    private static int CountGoals(SyntaxTerm goal, bool grammar)
    {
        switch (goal)
        {
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary when grammar:
                return CountGoals(ordinary.Arguments[0], grammar: false);
            case CompoundTerm { Name: "," or ";" or "->" or "*->", Arity: 2 } control:
                return CountGoals(control.Arguments[0], grammar) + CountGoals(control.Arguments[1], grammar);
            default:
                return 1;
        }
    }

    private static string NextModule(CompoundTerm directive, string module) =>
        (directive.Name, directive.Arity, directive.Arguments[0]) switch
        {
            ("module", 1 or 2, AtomTerm name) => name.Name,
            ("body", 1, AtomTerm name) => name.Name,
            ("end_module" or "end_body", 1, _) => ModuleTable.UserModule,
            _ => module,
        };
}
