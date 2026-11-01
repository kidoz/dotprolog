using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Finds variables whose occurrences in alternative branches communicate no bindings.</summary>
internal static class PrologSemanticSingletonLinter
{
    internal static void Analyze(IReadOnlyList<SyntaxTerm> clauses, string? fileName, List<Diagnostic> diagnostics)
    {
        foreach (SyntaxTerm clause in clauses)
        {
            Dictionary<string, int> clauseCounts = Counts(clause);
            HashSet<string> reported = new(StringComparer.Ordinal);
            foreach (SyntaxTerm body in LintGoalWalker.Bodies(clause))
            {
                Stack<SyntaxTerm> pending = new();
                pending.Push(body);
                while (pending.TryPop(out SyntaxTerm? current))
                {
                    if (current is not CompoundTerm compound)
                    {
                        continue;
                    }

                    if (compound.Arity == 2 && compound.Name is ";" or "|")
                    {
                        List<SyntaxTerm> alternatives = Alternatives(compound);
                        CheckAlternatives(alternatives, clauseCounts, reported, fileName, diagnostics);
                    }

                    for (var index = compound.Arguments.Count - 1; index >= 0; index--)
                    {
                        pending.Push(compound.Arguments[index]);
                    }
                }
            }
        }
    }

    private static void CheckAlternatives(
        List<SyntaxTerm> alternatives,
        Dictionary<string, int> clauseCounts,
        HashSet<string> reported,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        List<Dictionary<string, int>> counts = [.. alternatives.Select(Counts)];
        var names = counts.SelectMany(count => count.Keys).Distinct(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (
                name.StartsWith('_')
                || reported.Contains(name)
                || counts.Any(count => count.GetValueOrDefault(name) != 1)
                || clauseCounts.GetValueOrDefault(name) != alternatives.Count
                || IntroducedInConditionalCondition(alternatives[0], name)
            )
            {
                continue;
            }

            VariableTerm occurrence = LintSyntax.Variables(alternatives[0]).First(variable => variable.Name == name);
            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.SemanticSingletonVariable,
                    $"Variable '{name}' occurs once in each alternative and nowhere else, so it communicates no binding between branches; use '_' or rename separate variables.",
                    occurrence.Span,
                    fileName
                )
            );
            reported.Add(name);
        }
    }

    private static bool IntroducedInConditionalCondition(SyntaxTerm firstAlternative, string name) =>
        firstAlternative is CompoundTerm { Name: "->" or "*->", Arity: 2 } conditional
        && LintSyntax.Variables(conditional.Arguments[0]).Any(variable => variable.Name == name);

    private static List<SyntaxTerm> Alternatives(SyntaxTerm term)
    {
        List<SyntaxTerm> result = [];
        Stack<SyntaxTerm> pending = new();
        pending.Push(term);
        while (pending.TryPop(out SyntaxTerm? current))
        {
            if (current is CompoundTerm { Name: ";" or "|", Arity: 2 } disjunction)
            {
                pending.Push(disjunction.Arguments[1]);
                pending.Push(disjunction.Arguments[0]);
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    private static Dictionary<string, int> Counts(SyntaxTerm term)
    {
        Dictionary<string, int> result = new(StringComparer.Ordinal);
        foreach (VariableTerm variable in LintSyntax.Variables(term))
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
