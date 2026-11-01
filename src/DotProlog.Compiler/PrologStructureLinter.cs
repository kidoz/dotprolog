using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Checks file-local clause organization, declarations, duplication, and direct left recursion.</summary>
internal static class PrologStructureLinter
{
    internal static void Analyze(IReadOnlyList<SyntaxTerm> clauses, string? fileName, List<Diagnostic> diagnostics)
    {
        List<Declaration> declarations = Declarations(clauses);
        HashSet<PredicateIndicator> discontiguous =
        [
            .. declarations
                .Where(declaration => declaration.Kind == "discontiguous")
                .Select(declaration => declaration.Indicator),
        ];
        HashSet<PredicateIndicator> definitions = [];
        HashSet<string> canonicalClauses = new(StringComparer.Ordinal);
        HashSet<PredicateIndicator> seen = [];
        PredicateIndicator? previous = null;

        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1 })
            {
                continue;
            }

            var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
            SyntaxTerm head = LintSyntax.ClauseHead(clause);
            if (!LintSyntax.TryPredicate(head, grammar, out PredicateIndicator indicator))
            {
                continue;
            }

            definitions.Add(indicator);
            if (seen.Contains(indicator) && previous != indicator && !discontiguous.Contains(indicator))
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.DiscontiguousPredicate,
                        $"Clauses of {LintSyntax.Display(indicator, grammar)} are not together; group them or declare the predicate discontiguous.",
                        head.Span,
                        fileName
                    )
                );
            }

            seen.Add(indicator);
            previous = indicator;
            var canonical = LintSyntax.CanonicalForm(clause);
            if (!canonicalClauses.Add(canonical))
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.DuplicateClause,
                        "This clause duplicates an earlier clause up to variable renaming.",
                        clause.Span,
                        fileName
                    )
                );
            }

            CheckLeftRecursion(clause, head, fileName, diagnostics);
        }

        CheckDeclarations(declarations, definitions, fileName, diagnostics);
    }

    private static List<Declaration> Declarations(IReadOnlyList<SyntaxTerm> clauses)
    {
        List<Declaration> result = [];
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is not CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm declaration] })
            {
                continue;
            }

            if (declaration.Name == "meta_predicate" && declaration.Arity == 1)
            {
                foreach (SyntaxTerm item in LintSyntax.SequenceItems(declaration.Arguments[0]))
                {
                    if (LintSyntax.TryPredicate(item, grammar: false, out PredicateIndicator indicator))
                    {
                        result.Add(new Declaration("meta_predicate", indicator, LintSyntax.CanonicalForm(item), item.Span));
                    }
                }

                continue;
            }

            if (declaration.Name is not ("dynamic" or "discontiguous" or "multifile") || declaration.Arity != 1)
            {
                continue;
            }

            foreach (SyntaxTerm item in LintSyntax.SequenceItems(declaration.Arguments[0]))
            {
                if (LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out _))
                {
                    result.Add(new Declaration(declaration.Name, indicator, LintSyntax.CanonicalForm(item), item.Span));
                }
            }
        }

        return result;
    }

    private static void CheckDeclarations(
        IEnumerable<Declaration> declarations,
        HashSet<PredicateIndicator> definitions,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        HashSet<(string Kind, PredicateIndicator Indicator, string Canonical)> seen = [];
        HashSet<(string Kind, PredicateIndicator Indicator)> undefined = [];
        foreach (Declaration declaration in declarations)
        {
            if (!seen.Add((declaration.Kind, declaration.Indicator, declaration.Canonical)))
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.DuplicateDirective,
                        $"The {declaration.Kind} declaration for {LintSyntax.Display(declaration.Indicator)} is duplicated.",
                        declaration.Span,
                        fileName
                    )
                );
            }

            if (
                declaration.Kind is "discontiguous" or "meta_predicate"
                && !definitions.Contains(declaration.Indicator)
                && undefined.Add((declaration.Kind, declaration.Indicator))
            )
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.DeclaredButUndefined,
                        $"The {declaration.Kind} declaration for {LintSyntax.Display(declaration.Indicator)} has no definition in this file.",
                        declaration.Span,
                        fileName
                    )
                );
            }
        }
    }

    private static void CheckLeftRecursion(SyntaxTerm clause, SyntaxTerm head, string? fileName, List<Diagnostic> diagnostics)
    {
        SyntaxTerm? first = clause switch
        {
            CompoundTerm { Name: ":-", Arity: 2 } rule => LeftmostConjunct(rule.Arguments[1]),
            CompoundTerm { Name: "-->", Arity: 2 } grammar => LeftmostConjunct(grammar.Arguments[1]),
            _ => null,
        };
        if (first is null or CompoundTerm { Name: "{}" or "." })
        {
            return;
        }

        if (LintSyntax.CanonicalForm(first) == LintSyntax.CanonicalForm(head))
        {
            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.DirectLeftRecursion,
                    "The leftmost goal is a variant of the clause head, causing direct left recursion.",
                    first.Span,
                    fileName
                )
            );
        }
    }

    private static SyntaxTerm LeftmostConjunct(SyntaxTerm term) =>
        term is CompoundTerm { Name: ",", Arity: 2 } conjunction ? LeftmostConjunct(conjunction.Arguments[0]) : term;

    private static Diagnostic Warning(string id, string message, SourceSpan span, string? fileName) =>
        new(id, DiagnosticSeverity.Warning, message, span, fileName);

    private sealed record Declaration(string Kind, PredicateIndicator Indicator, string Canonical, SourceSpan Span);
}
