namespace DotProlog.Runtime;

/// <summary>A marshalled string term (ADR 0047).</summary>
/// <param name="Value">The string's text.</param>
public sealed record PrologString(string Value) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
