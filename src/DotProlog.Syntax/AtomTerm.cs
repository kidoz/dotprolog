namespace DotProlog.Syntax;

/// <summary>An atom, such as <c>foo</c>, <c>'Hello! World!'</c>, <c>[]</c>, or <c>+</c>.</summary>
/// <param name="Name">The atom's text, with quoting and escapes already resolved.</param>
/// <param name="Span">Source range the term was read from.</param>
public sealed record AtomTerm(string Name, SourceSpan Span) : SyntaxTerm(Span);
