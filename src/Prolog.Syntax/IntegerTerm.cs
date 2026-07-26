namespace Prolog.Syntax;

/// <summary>An integer literal.</summary>
/// <param name="Value">The literal's value.</param>
/// <param name="Span">Source range the term was read from.</param>
public sealed record IntegerTerm(long Value, SourceSpan Span) : SyntaxTerm(Span);
