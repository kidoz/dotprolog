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
/// <param name="Quoted">Whether an atom token was written in quotes, which suppresses operator interpretation.</param>
internal readonly record struct Token(
    TokenKind Kind,
    string Text,
    SourceSpan Span,
    bool PrecededByLayout,
    long Integer = 0,
    double Float = 0,
    bool Quoted = false
)
{
    /// <summary>Whether this token is the punctuation <paramref name="text"/>.</summary>
    public bool IsPunctuation(string text) => Kind == TokenKind.Punctuation && Text == text;
}
