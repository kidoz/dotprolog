namespace DotProlog.Runtime;

/// <summary>Which kinds of term a text-accepting predicate reads as text.</summary>
[Flags]
internal enum TextKinds
{
    /// <summary>An atom's name.</summary>
    Atom = 1,

    /// <summary>A string's text.</summary>
    String = 2,

    /// <summary>A number, written as the writer writes it.</summary>
    Number = 4,

    /// <summary>A proper list of characters or codes; <c>[]</c> is then empty text.</summary>
    List = 8,

    /// <summary>Atoms, strings, and numbers: the atomic text kinds.</summary>
    Atomic = Atom | String | Number,

    /// <summary>Every kind of text.</summary>
    Any = Atomic | List,
}
