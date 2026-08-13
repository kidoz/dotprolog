using System.Globalization;
using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>A marshalled integer outside the range of <see cref="PrologInteger"/>.</summary>
/// <param name="Value">The value.</param>
public sealed record PrologBigInteger(BigInteger Value) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
