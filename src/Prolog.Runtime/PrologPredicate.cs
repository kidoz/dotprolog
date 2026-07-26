namespace Prolog.Runtime;

/// <summary>
/// A predicate resolved once so that calling it costs no symbol lookup.
/// </summary>
/// <param name="FunctorId">The predicate's functor identifier.</param>
/// <param name="Name">The predicate's name, for diagnostics.</param>
/// <param name="Arity">The predicate's arity.</param>
public readonly record struct PrologPredicate(int FunctorId, string Name, int Arity)
{
    /// <inheritdoc />
    public override string ToString() => $"{Name}/{Arity}";
}
