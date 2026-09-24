using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Performs non-executing source analysis over parsed Prolog clauses.</summary>
/// <remarks>
/// The linter consumes syntax terms rather than consulting them, so directives are inspected but
/// never run. Variable scope is one clause, including a directive or grammar rule. Double-quoted
/// text is judged under the <c>double_quotes</c> value in force where it appears: the initial value
/// the caller supplies, moved by <c>:- set_prolog_flag(double_quotes, Value).</c> directives in the
/// file.
/// </remarks>
public static class PrologLinter
{
    /// <summary>Analyzes <paramref name="clauses"/> and returns warnings in source order.</summary>
    /// <param name="clauses">Parsed clauses and directives in source order.</param>
    /// <param name="fileName">Source file used in diagnostics, when known.</param>
    /// <param name="initialDoubleQuotes">
    /// The <c>double_quotes</c> value the file starts under; the default mode's <c>chars</c> when omitted.
    /// </param>
    public static IReadOnlyList<Diagnostic> Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName = null,
        DoubleQuotesMode initialDoubleQuotes = DoubleQuotesMode.Chars
    )
    {
        ArgumentNullException.ThrowIfNull(clauses);

        List<Diagnostic> diagnostics = [];
        foreach (SyntaxTerm clause in clauses)
        {
            AnalyzeClause(clause, fileName, diagnostics);
        }

        DoubleQuotesLinter.Analyze(clauses, initialDoubleQuotes, fileName, diagnostics);

        return [.. diagnostics.OrderBy(diagnostic => diagnostic.Span.Start).ThenBy(diagnostic => diagnostic.Id)];
    }

    /// <summary>
    /// Analyzes parsed clauses together with their original source, applying semantic rules and the
    /// source-text rules selected by <paramref name="options"/>.
    /// </summary>
    /// <param name="source">The exact source from which <paramref name="clauses"/> were read.</param>
    /// <param name="clauses">Parsed clauses and directives in source order.</param>
    /// <param name="fileName">Source file used in diagnostics, when known.</param>
    /// <param name="options">Layout policy; semantic-only analysis is used when omitted.</param>
    /// <param name="initialDoubleQuotes">
    /// The <c>double_quotes</c> value the file starts under; the default mode's <c>chars</c> when omitted.
    /// </param>
    /// <param name="codePoints">
    /// Whether a character is a Unicode code point, as in the default mode, or a UTF-16 code unit, as
    /// in strict ISO mode; the layout rules count characters that way.
    /// </param>
    public static IReadOnlyList<Diagnostic> AnalyzeSource(
        string source,
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName = null,
        PrologLintOptions? options = null,
        DoubleQuotesMode initialDoubleQuotes = DoubleQuotesMode.Chars,
        bool codePoints = true
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clauses);

        List<Diagnostic> diagnostics = [.. Analyze(clauses, fileName, initialDoubleQuotes)];
        PrologLayoutLinter.Analyze(source, clauses, options ?? PrologLintOptions.SemanticOnly, fileName, diagnostics, codePoints);

        return [.. diagnostics.OrderBy(diagnostic => diagnostic.Span.Start).ThenBy(diagnostic => diagnostic.Id)];
    }

    private static void AnalyzeClause(SyntaxTerm clause, string? fileName, List<Diagnostic> diagnostics)
    {
        List<VariableTerm> occurrences = VariableOccurrences(clause);
        Dictionary<string, List<VariableTerm>> byName = new(StringComparer.Ordinal);

        foreach (VariableTerm variable in occurrences)
        {
            if (variable.IsAnonymous)
            {
                continue;
            }

            if (!byName.TryGetValue(variable.Name, out List<VariableTerm>? namedOccurrences))
            {
                namedOccurrences = [];
                byName.Add(variable.Name, namedOccurrences);
            }

            namedOccurrences.Add(variable);
        }

        List<Diagnostic> clauseDiagnostics = [];
        foreach ((var name, List<VariableTerm> namedOccurrences) in byName)
        {
            if (name.StartsWith('_'))
            {
                if (namedOccurrences.Count > 1)
                {
                    clauseDiagnostics.Add(
                        Warning(
                            LintDiagnosticIds.RepeatedSingletonMarker,
                            $"Variable '{name}' is marked as singleton but appears more than once in this clause.",
                            namedOccurrences[1].Span,
                            fileName
                        )
                    );
                }

                continue;
            }

            if (namedOccurrences.Count == 1)
            {
                clauseDiagnostics.Add(
                    Warning(
                        LintDiagnosticIds.SingletonVariable,
                        $"Singleton variable '{name}'; prefix it with '_' when the single occurrence is intentional.",
                        namedOccurrences[0].Span,
                        fileName
                    )
                );
            }
        }

        diagnostics.AddRange(clauseDiagnostics.OrderBy(diagnostic => diagnostic.Span.Start));
    }

    private static List<VariableTerm> VariableOccurrences(SyntaxTerm clause)
    {
        List<VariableTerm> occurrences = [];
        Stack<SyntaxTerm> pending = new();
        pending.Push(clause);

        while (pending.TryPop(out SyntaxTerm? term))
        {
            if (term is VariableTerm variable)
            {
                occurrences.Add(variable);
                continue;
            }

            if (term is not CompoundTerm compound)
            {
                continue;
            }

            for (var index = compound.Arguments.Count - 1; index >= 0; index--)
            {
                pending.Push(compound.Arguments[index]);
            }
        }

        return occurrences;
    }

    private static Diagnostic Warning(string id, string message, SourceSpan span, string? fileName) =>
        new(id, DiagnosticSeverity.Warning, message, span, fileName);
}
