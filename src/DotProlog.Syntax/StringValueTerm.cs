namespace DotProlog.Syntax;

/// <summary>
/// A string value: an already-lowered string term, as distinct from <see cref="StringTerm"/>, the
/// double-quoted token the reader emits. The distinction is what keeps a string stable once made —
/// normalization interprets the token under the <c>double_quotes</c> flag in force, while a value
/// reified from the heap must survive recompilation under any flag.
/// </summary>
/// <param name="Value">The string's text.</param>
/// <param name="Span">Source range, or the span of the operation that produced the value.</param>
public sealed record StringValueTerm(string Value, SourceSpan Span) : SyntaxTerm(Span);
