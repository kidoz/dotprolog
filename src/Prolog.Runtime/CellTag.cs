namespace Prolog.Runtime;

/// <summary>The type tag carried in the top four bits of a <see cref="Cell"/>.</summary>
public enum CellTag : byte
{
    /// <summary>A variable reference. An unbound variable is a self-referencing cell.</summary>
    Reference = 0,

    /// <summary>A compound term. The payload is the heap address of its <see cref="Functor"/> cell.</summary>
    Structure = 1,

    /// <summary>A functor header stored on the heap. The payload is a functor identifier.</summary>
    Functor = 2,

    /// <summary>An atom. The payload is an atom identifier.</summary>
    Atom = 3,

    /// <summary>An integer. The payload is the value itself, sign-extended from 60 bits.</summary>
    Integer = 4,

    /// <summary>A float. The payload indexes the interned float table.</summary>
    Float = 5,
}
