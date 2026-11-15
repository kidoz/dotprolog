namespace DotProlog.Runtime;

/// <summary>
/// A predicate resolved once so that calling it costs no symbol lookup.
/// </summary>
/// <remarks>Obtain a callable handle from <see cref="PrologHost.Bind"/>. Handles belong to that host's machine.</remarks>
/// <param name="FunctorId">The predicate's functor identifier.</param>
/// <param name="Name">The predicate's name, for diagnostics.</param>
/// <param name="Arity">The predicate's arity.</param>
public readonly record struct PrologPredicate(int FunctorId, string Name, int Arity)
{
    internal Machine? Owner { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Name}/{Arity}";
}
