using System.Globalization;

namespace DotProlog.Runtime;

/// <summary>A marshalled integer.</summary>
/// <param name="Value">The value.</param>
public sealed record PrologInteger(long Value) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
