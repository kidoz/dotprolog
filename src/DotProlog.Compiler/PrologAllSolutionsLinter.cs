using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Checks variable usage and failure-driven iteration in all-solutions predicates.</summary>
internal static class PrologAllSolutionsLinter
{
    internal static void Analyze(IReadOnlyList<SyntaxTerm> clauses, string? fileName, List<Diagnostic> diagnostics)
    {
        foreach (SyntaxTerm clause in clauses)
        {
            Dictionary<string, int> clauseCounts = VariableCounts(clause);
            foreach (SyntaxTerm body in LintGoalWalker.Bodies(clause))
            {
                foreach (CompoundTerm goal in LintGoalWalker.Goals(body))
                {
                    if (goal.Name is not ("findall" or "bagof" or "setof") || goal.Arity != 3)
                    {
                        continue;
                    }

                    SyntaxTerm template = goal.Arguments[0];
                    SyntaxTerm collectionGoal = StripExistentialQuantifiers(goal.Arguments[1], fileName, diagnostics);
                    CheckSharedVariables(template, collectionGoal, fileName, diagnostics);
                    if (goal.Name is "bagof" or "setof")
                    {
                        CheckGoalSingletons(collectionGoal, clauseCounts, fileName, diagnostics);
                    }
                }

                CheckFindallBacktracking(body, fileName, diagnostics);
            }
        }
    }

    private static SyntaxTerm StripExistentialQuantifiers(SyntaxTerm goal, string? fileName, List<Diagnostic> diagnostics)
    {
        SyntaxTerm current = goal;
        while (current is CompoundTerm { Name: "^", Arity: 2 } quantified)
        {
            HashSet<string> bound = [.. LintSyntax.Variables(quantified.Arguments[1]).Select(variable => variable.Name)];
            foreach (VariableTerm variable in LintSyntax.Variables(quantified.Arguments[0]))
            {
                if (!variable.IsAnonymous && !bound.Contains(variable.Name))
                {
                    diagnostics.Add(
                        Warning(
                            LintDiagnosticIds.UnusedExistentialVariable,
                            $"Existentially qualified variable '{variable.Name}' does not occur in the qualified goal.",
                            variable.Span,
                            fileName
                        )
                    );
                }
            }

            current = quantified.Arguments[1];
        }

        return current;
    }

    private static void CheckSharedVariables(SyntaxTerm template, SyntaxTerm goal, string? fileName, List<Diagnostic> diagnostics)
    {
        HashSet<string> templateVariables = NamedVariables(template);
        HashSet<string> goalVariables = NamedVariables(goal);
        if (templateVariables.Count == 0 && goalVariables.Count == 0 || templateVariables.Overlaps(goalVariables))
        {
            return;
        }

        diagnostics.Add(
            Warning(
                LintDiagnosticIds.UnsharedAllSolutionsTemplate,
                "The all-solutions template and goal share no named variables; each collected item is independent of the goal's solutions.",
                template.Span,
                fileName
            )
        );
    }

    private static void CheckGoalSingletons(
        SyntaxTerm goal,
        Dictionary<string, int> clauseCounts,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        foreach (
            var group in LintSyntax.Variables(goal).Where(variable => !variable.IsAnonymous).GroupBy(variable => variable.Name)
        )
        {
            if (group.Key.StartsWith('_') || group.Count() != 1 || clauseCounts[group.Key] == 1)
            {
                continue;
            }

            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.AllSolutionsSingleton,
                    $"Variable '{group.Key}' occurs only once inside this all-solutions goal; check whether it should be shared, existential, or anonymous.",
                    group.First().Span,
                    fileName
                )
            );
        }
    }

    private static void CheckFindallBacktracking(SyntaxTerm body, string? fileName, List<Diagnostic> diagnostics)
    {
        Stack<SyntaxTerm> pending = new();
        pending.Push(body);
        HashSet<CompoundTerm> reported = [];

        while (pending.TryPop(out SyntaxTerm? current))
        {
            if (current is not CompoundTerm { Name: ",", Arity: 2 } conjunction)
            {
                continue;
            }

            pending.Push(conjunction.Arguments[1]);
            pending.Push(conjunction.Arguments[0]);
            if (
                RightmostConjunct(conjunction.Arguments[0]) is CompoundTerm { Name: "findall", Arity: 3 } findall
                && LeftmostConjunct(conjunction.Arguments[1]) is AtomTerm { Name: "fail" or "false" }
                && reported.Add(findall)
            )
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.FindallBacktracking,
                        "This findall/3 result is discarded by immediate failure; use a clearer failure-driven loop when collection is not intended.",
                        findall.Span,
                        fileName
                    )
                );
            }
        }
    }

    private static SyntaxTerm RightmostConjunct(SyntaxTerm term) =>
        term is CompoundTerm { Name: ",", Arity: 2 } conjunction ? RightmostConjunct(conjunction.Arguments[1]) : term;

    private static SyntaxTerm LeftmostConjunct(SyntaxTerm term) =>
        term is CompoundTerm { Name: ",", Arity: 2 } conjunction ? LeftmostConjunct(conjunction.Arguments[0]) : term;

    private static HashSet<string> NamedVariables(SyntaxTerm term) =>
        [.. LintSyntax.Variables(term).Where(variable => !variable.IsAnonymous).Select(variable => variable.Name)];

    private static Dictionary<string, int> VariableCounts(SyntaxTerm clause)
    {
        Dictionary<string, int> result = new(StringComparer.Ordinal);
        foreach (VariableTerm variable in LintSyntax.Variables(clause))
        {
            if (!variable.IsAnonymous)
            {
                result[variable.Name] = result.GetValueOrDefault(variable.Name) + 1;
            }
        }

        return result;
    }

    private static Diagnostic Warning(string id, string message, SourceSpan span, string? fileName) =>
        new(id, DiagnosticSeverity.Warning, message, span, fileName);
}
