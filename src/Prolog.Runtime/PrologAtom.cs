namespace Prolog.Runtime;

/// <summary>A marshalled atom.</summary>
/// <param name="Name">The atom's text.</param>
public sealed record PrologAtom(string Name) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
