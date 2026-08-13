namespace DotProlog.Syntax;

/// <summary>A single lexical token.</summary>
/// <param name="Kind">The token's category.</param>
/// <param name="Text">Token text: the resolved atom, variable, or punctuation.</param>
/// <param name="Span">Source range.</param>
/// <param name="PrecededByLayout">
/// Whether layout or a comment separated this token from the previous one. The reader needs this to
/// distinguish <c>foo(X)</c> (a compound term) from <c>foo (X)</c> (an atom applied as an operator).
/// </param>
/// <param name="Integer">Value of an <see cref="TokenKind.Integer"/> token.</param>
/// <param name="Float">Value of a <see cref="TokenKind.Float"/> token.</param>
/// <param name="Quoted">Whether an atom token was written in quotes.</param>
/// <param name="IntegerOverflow">Whether an integer token exceeds <see cref="Integer"/>; the value is then in <see cref="Big"/>.</param>
/// <param name="Big">The exact value of an integer token whose magnitude exceeds <see cref="Integer"/>; for a rational token, its numerator.</param>
/// <param name="RationalDenominator">The denominator of a rational token, or zero for other tokens.</param>
/// <param name="FloatOverflow">Whether a float token exceeds the finite implementation range.</param>
internal readonly record struct Token(
    TokenKind Kind,
    string Text,
    SourceSpan Span,
    bool PrecededByLayout,
    long Integer = 0,
    double Float = 0,
    bool Quoted = false,
    bool IntegerOverflow = false,
    System.Numerics.BigInteger Big = default,
    System.Numerics.BigInteger RationalDenominator = default,
    bool FloatOverflow = false
)
{
    /// <summary>Whether this token is the punctuation <paramref name="text"/>.</summary>
    public bool IsPunctuation(string text) => Kind == TokenKind.Punctuation && Text == text;
}
