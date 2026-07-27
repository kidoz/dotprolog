using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// Rewrites reader output into the subset the clause compiler lowers: today that means expanding
/// double-quoted literals into lists of character codes, which is the <c>codes</c> setting of the
/// <c>double_quotes</c> flag.
/// </summary>
internal static class TermNormalizer
{
    internal static SyntaxTerm Normalize(SyntaxTerm term) =>
        term switch
        {
            StringTerm text => ToCodeList(text),
            CompoundTerm compound => NormalizeCompound(compound),
            _ => term,
        };

    private static CompoundTerm NormalizeCompound(CompoundTerm compound)
    {
        SyntaxTerm[]? rewritten = null;
        for (int i = 0; i < compound.Arguments.Count; i++)
        {
            SyntaxTerm normalized = Normalize(compound.Arguments[i]);
            if (!ReferenceEquals(normalized, compound.Arguments[i]) && rewritten is null)
            {
                rewritten = [.. compound.Arguments];
            }

            if (rewritten is not null)
            {
                rewritten[i] = normalized;
            }
        }

        return rewritten is null ? compound : compound with { Arguments = rewritten };
    }

    private static SyntaxTerm ToCodeList(StringTerm text)
    {
        SyntaxTerm result = new AtomTerm(TermReader.EmptyListAtom, text.Span);
        for (int i = text.Value.Length - 1; i >= 0; i--)
        {
            result = new CompoundTerm(TermReader.ListFunctor, [new IntegerTerm(text.Value[i], text.Span), result], text.Span);
        }

        return result;
    }
}
