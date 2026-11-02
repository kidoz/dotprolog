using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Finds cuts whose scope is the source clause rather than an opaque nested goal.</summary>
internal static class LintCutScope
{
    internal static SourceSpan? FirstClauseCut(SyntaxTerm body, bool grammar)
    {
        switch (body)
        {
            case AtomTerm { Name: "!" } cut:
                return cut.Span;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary when grammar:
                return FirstClauseCut(ordinary.Arguments[0], grammar: false);
            case CompoundTerm { Name: ",", Arity: 2 } conjunction:
                return FirstClauseCut(conjunction.Arguments[0], grammar) ?? FirstClauseCut(conjunction.Arguments[1], grammar);
            case CompoundTerm { Name: ";", Arity: 2 } disjunction:
                if (disjunction.Arguments[0] is CompoundTerm { Name: "->" or "*->", Arity: 2 } ifConditional)
                {
                    return FirstClauseCut(ifConditional.Arguments[1], grammar)
                        ?? FirstClauseCut(disjunction.Arguments[1], grammar);
                }

                return FirstClauseCut(disjunction.Arguments[0], grammar) ?? FirstClauseCut(disjunction.Arguments[1], grammar);
            case CompoundTerm { Name: "->", Arity: 2 } conditional:
                return FirstClauseCut(conditional.Arguments[1], grammar);
            case CompoundTerm { Name: "*->", Arity: 2 } softConditional:
                return FirstClauseCut(softConditional.Arguments[0], grammar)
                    ?? FirstClauseCut(softConditional.Arguments[1], grammar);
            default:
                return null;
        }
    }
}
