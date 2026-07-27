namespace DotProlog.Runtime;

/// <summary>What a <see cref="PrologInput"/> describes.</summary>
internal enum PrologInputKind
{
    /// <summary>A hole for the call to fill in.</summary>
    Output,

    /// <summary>An atom.</summary>
    Atom,

    /// <summary>An integer.</summary>
    Integer,

    /// <summary>A floating-point number.</summary>
    Float,

    /// <summary>A list.</summary>
    List,

    /// <summary>A compound term.</summary>
    Compound,
}
