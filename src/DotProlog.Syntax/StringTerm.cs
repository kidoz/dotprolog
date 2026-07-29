namespace DotProlog.Syntax;

/// <summary>
/// A double-quoted literal. The reader keeps it verbatim; the compiler lowers it according to the
/// <c>double_quotes</c> flag.
/// </summary>
/// <param name="Value">The literal's text, with escapes already resolved.</param>
/// <param name="Span">Source range the term was read from.</param>
public sealed record StringTerm(string Value, SourceSpan Span) : SyntaxTerm(Span);
