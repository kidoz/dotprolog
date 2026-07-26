using System.Globalization;

namespace Prolog.Runtime;

/// <summary>A marshalled integer.</summary>
/// <param name="Value">The value.</param>
public sealed record PrologInteger(long Value) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
