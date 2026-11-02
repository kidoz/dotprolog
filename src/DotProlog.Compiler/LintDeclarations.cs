using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reads predicate declarations used by source-local lint rules.</summary>
internal static class LintDeclarations
{
    internal static HashSet<PredicateIndicator> MultifileIndicators(IReadOnlyList<SyntaxTerm> clauses)
    {
        HashSet<PredicateIndicator> result = [];
        foreach (
            CompoundTerm directive in clauses
                .OfType<CompoundTerm>()
                .Where(clause => clause is { Name: ":-", Arity: 1, Arguments: [CompoundTerm { Name: "multifile", Arity: 1 }] })
        )
        {
            CompoundTerm declaration = (CompoundTerm)directive.Arguments[0];
            foreach (SyntaxTerm item in LintSyntax.SequenceItems(declaration.Arguments[0]))
            {
                if (LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out _))
                {
                    result.Add(indicator);
                }
            }
        }

        return result;
    }
}
