using System.Globalization;

namespace DotProlog.Runtime;

/// <summary>A marshalled floating-point number.</summary>
/// <param name="Value">The value.</param>
public sealed record PrologFloat(double Value) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);
}
