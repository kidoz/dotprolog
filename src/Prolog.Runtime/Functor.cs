namespace Prolog.Runtime;

/// <summary>A predicate or compound-term functor: an atom identifier paired with an arity.</summary>
/// <param name="NameAtom">Identifier of the functor's name in the owning <see cref="SymbolTable"/>.</param>
/// <param name="Arity">Number of arguments.</param>
public readonly record struct Functor(int NameAtom, int Arity);
