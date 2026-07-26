namespace Prolog.Runtime;

/// <summary>A variable that was still unbound when the solution was marshalled.</summary>
/// <param name="Name">A generated name, unique within the solution it came from.</param>
public sealed record PrologVariable(string Name) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
