using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// Rewrites reader output into the subset the clause compiler lowers, including the representation
/// selected by the <c>double_quotes</c> flag.
/// </summary>
internal static class TermNormalizer
{
    internal static SyntaxTerm Normalize(SyntaxTerm term, DoubleQuotesMode doubleQuotes = DoubleQuotesMode.Codes) =>
        term switch
        {
            StringTerm text => NormalizeString(text, doubleQuotes),
            CompoundTerm compound => NormalizeCompound(compound, doubleQuotes),
            _ => term,
        };

    private static CompoundTerm NormalizeCompound(CompoundTerm compound, DoubleQuotesMode doubleQuotes)
    {
        SyntaxTerm[]? rewritten = null;
        for (var i = 0; i < compound.Arguments.Count; i++)
        {
            SyntaxTerm normalized = Normalize(compound.Arguments[i], doubleQuotes);
            if (!ReferenceEquals(normalized, compound.Arguments[i]) && rewritten is null)
            {
                rewritten = [.. compound.Arguments];
            }

            rewritten?[i] = normalized;
        }

        return rewritten is null ? compound : compound with { Arguments = rewritten };
    }

    private static SyntaxTerm NormalizeString(StringTerm text, DoubleQuotesMode doubleQuotes)
    {
        if (doubleQuotes == DoubleQuotesMode.Atom)
        {
            return new AtomTerm(text.Value, text.Span);
        }

        if (doubleQuotes == DoubleQuotesMode.String)
        {
            return new StringValueTerm(text.Value, text.Span);
        }

        SyntaxTerm result = new AtomTerm(TermReader.EmptyListAtom, text.Span);

        for (var i = text.Value.Length - 1; i >= 0; i--)
        {
            SyntaxTerm character =
                doubleQuotes == DoubleQuotesMode.Codes
                    ? new IntegerTerm(text.Value[i], text.Span)
                    : new AtomTerm(text.Value[i].ToString(), text.Span);

            result = new CompoundTerm(TermReader.ListFunctor, [character, result], text.Span);
        }

        return result;
    }
}
