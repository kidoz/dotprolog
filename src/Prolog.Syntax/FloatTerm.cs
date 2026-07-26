namespace Prolog.Syntax;

/// <summary>A floating-point literal.</summary>
/// <param name="Value">The literal's value.</param>
/// <param name="Span">Source range the term was read from.</param>
public sealed record FloatTerm(double Value, SourceSpan Span) : SyntaxTerm(Span);
