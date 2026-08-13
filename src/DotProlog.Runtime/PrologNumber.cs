using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>
/// The result of evaluating an arithmetic expression: an integer, a big integer, or a float.
/// An integer value in the fixnum range is always the plain integer kind — <see cref="FromBig"/>
/// normalizes, so the big kind never holds a value that fits.
/// </summary>
public readonly struct PrologNumber : IEquatable<PrologNumber>
{
    private readonly BigInteger _big;

    private PrologNumber(bool isFloat, bool isBig, long integer, double real, BigInteger big)
    {
        IsFloat = isFloat;
        IsBig = isBig;
        Integer = integer;
        Real = real;
        _big = big;
    }

    /// <summary>Whether the value is a float rather than an integer.</summary>
    public bool IsFloat { get; }

    /// <summary>Whether the value is an integer outside the fixnum range.</summary>
    public bool IsBig { get; }

    /// <summary>The integer value. Meaningful only when the value is a plain integer.</summary>
    public long Integer { get; }

    /// <summary>The float value. Meaningful only when <see cref="IsFloat"/> is <see langword="true"/>.</summary>
    public double Real { get; }

    /// <summary>The integer value at full width, whichever integer kind it is.</summary>
    public BigInteger Big => IsBig ? _big : Integer;

    /// <summary>The value widened to a double, whichever kind it is. A big integer may widen to infinity.</summary>
    public double AsDouble =>
        IsFloat ? Real
        : IsBig ? (double)_big
        : Integer;

    /// <summary>Creates an integer value, widening to the big kind when it is outside the fixnum range.</summary>
    public static PrologNumber FromInteger(long value) =>
        Cell.FitsInteger(value) ? new PrologNumber(false, false, value, 0, default) : new PrologNumber(false, true, 0, 0, value);

    /// <summary>Creates a float value.</summary>
    public static PrologNumber FromReal(double value) => new(true, false, 0, value, default);

    /// <summary>Creates an integer value from a full-width integer, normalizing to the plain kind when it fits.</summary>
    public static PrologNumber FromBig(BigInteger value) =>
        value >= Cell.MinInteger && value <= Cell.MaxInteger
            ? new PrologNumber(false, false, (long)value, 0, default)
            : new PrologNumber(false, true, 0, 0, value);

    /// <inheritdoc />
    public bool Equals(PrologNumber other) =>
        IsFloat == other.IsFloat
        && IsBig == other.IsBig
        && (
            IsFloat ? Real.Equals(other.Real)
            : IsBig ? _big.Equals(other._big)
            : Integer == other.Integer
        );

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PrologNumber other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        IsFloat ? Real.GetHashCode()
        : IsBig ? _big.GetHashCode()
        : Integer.GetHashCode();

    /// <summary>Compares two numbers by value.</summary>
    public static bool operator ==(PrologNumber left, PrologNumber right) => left.Equals(right);

    /// <summary>Compares two numbers by value.</summary>
    public static bool operator !=(PrologNumber left, PrologNumber right) => !left.Equals(right);
}
