using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// Rewrites reader output into the subset the clause compiler lowers, including the representation
/// selected by the <c>double_quotes</c> flag.
/// </summary>
/// <remarks>
/// A character or code list holds one element per character: per Unicode code point when
/// <c>codePoints</c> is set, as in <c>Modern</c>, and per UTF-16 code unit in strict ISO mode.
/// </remarks>
internal static class TermNormalizer
{
    internal static SyntaxTerm Normalize(
        SyntaxTerm term,
        DoubleQuotesMode doubleQuotes = DoubleQuotesMode.Codes,
        bool codePoints = true
    ) =>
        term switch
        {
            StringTerm text => NormalizeString(text, doubleQuotes, codePoints),
            CompoundTerm compound => NormalizeCompound(compound, doubleQuotes, codePoints),
            _ => term,
        };

    private static CompoundTerm NormalizeCompound(CompoundTerm compound, DoubleQuotesMode doubleQuotes, bool codePoints)
    {
        SyntaxTerm[]? rewritten = null;
        for (var i = 0; i < compound.Arguments.Count; i++)
        {
            SyntaxTerm normalized = Normalize(compound.Arguments[i], doubleQuotes, codePoints);
            if (!ReferenceEquals(normalized, compound.Arguments[i]) && rewritten is null)
            {
                rewritten = [.. compound.Arguments];
            }

            rewritten?[i] = normalized;
        }

        return rewritten is null ? compound : compound with { Arguments = rewritten };
    }

    private static SyntaxTerm NormalizeString(StringTerm text, DoubleQuotesMode doubleQuotes, bool codePoints)
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
        var value = text.Value;

        for (var end = value.Length; end > 0; )
        {
            var width =
                codePoints && end >= 2 && char.IsLowSurrogate(value[end - 1]) && char.IsHighSurrogate(value[end - 2]) ? 2 : 1;
            end -= width;

            SyntaxTerm character =
                doubleQuotes == DoubleQuotesMode.Codes
                    ? new IntegerTerm(width == 2 ? char.ConvertToUtf32(value[end], value[end + 1]) : value[end], text.Span)
                    : new AtomTerm(value.Substring(end, width), text.Span);

            result = new CompoundTerm(TermReader.ListFunctor, [character, result], text.Span);
        }

        return result;
    }
}
