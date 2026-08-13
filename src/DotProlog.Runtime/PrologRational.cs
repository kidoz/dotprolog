using System.Globalization;
using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>A marshalled rational number, canonical with the sign on the numerator.</summary>
/// <param name="Numerator">The numerator.</param>
/// <param name="Denominator">The denominator, greater than 1.</param>
public sealed record PrologRational(BigInteger Numerator, BigInteger Denominator) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Numerator}r{Denominator}");
}
