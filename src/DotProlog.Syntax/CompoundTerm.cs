namespace DotProlog.Syntax;

/// <summary>A compound term such as <c>foo(X, bar)</c>, including operator applications like <c>X is Y + 1</c>.</summary>
/// <param name="Name">The functor name.</param>
/// <param name="Arguments">The arguments; always at least one.</param>
/// <param name="Span">Source range the term was read from.</param>
public sealed record CompoundTerm(string Name, IReadOnlyList<SyntaxTerm> Arguments, SourceSpan Span) : SyntaxTerm(Span)
{
    /// <summary>The functor's arity.</summary>
    public int Arity => Arguments.Count;
}
