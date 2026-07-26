namespace Prolog.Syntax;

/// <summary>A variable occurrence. Scope is the enclosing clause.</summary>
/// <param name="Name">The variable's source name; <c>_</c> denotes a fresh anonymous variable.</param>
/// <param name="Span">Source range the term was read from.</param>
public sealed record VariableTerm(string Name, SourceSpan Span) : SyntaxTerm(Span)
{
    /// <summary>Whether this occurrence is the anonymous variable <c>_</c>, which never shares bindings.</summary>
    public bool IsAnonymous => Name == "_";
}
