using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>
/// The result of evaluating an arithmetic expression: an integer, a big integer, a rational, or a
/// float. Construction normalizes — an integer in the fixnum range is always the plain kind, a
/// larger one the big kind, and a rational is canonical with the sign on the numerator
/// and a denominator greater than 1, demoting to the integer kinds when it divides out.
/// </summary>
public readonly struct PrologNumber : IEquatable<PrologNumber>
{
    private enum Kind : byte
    {
        Integer,
        Big,
        Rational,
        Float,
    }

    private readonly Kind _kind;
    private readonly long _integer;
    private readonly double _real;
    private readonly BigInteger _big;
    private readonly BigInteger _denominator;

    private PrologNumber(Kind kind, long integer, double real, BigInteger big, BigInteger denominator)
    {
        _kind = kind;
        _integer = integer;
        _real = real;
        _big = big;
        _denominator = denominator;
    }

    /// <summary>Whether the value is a float rather than an integer or rational.</summary>
    public bool IsFloat => _kind == Kind.Float;

    /// <summary>Whether the value is an integer outside the fixnum range.</summary>
    public bool IsBig => _kind == Kind.Big;

    /// <summary>Whether the value is a rational with a denominator greater than 1.</summary>
    public bool IsRational => _kind == Kind.Rational;

    /// <summary>Whether the value is an integer of either representation.</summary>
    public bool IsInteger => _kind is Kind.Integer or Kind.Big;

    /// <summary>The integer value. Meaningful only when the value is a plain integer.</summary>
    public long Integer => _integer;

    /// <summary>The float value. Meaningful only when <see cref="IsFloat"/> is <see langword="true"/>.</summary>
    public double Real => _real;

    /// <summary>The integer value at full width, whichever integer kind it is.</summary>
    public BigInteger Big => _kind == Kind.Big ? _big : _integer;

    /// <summary>The numerator: the integer value itself, or the rational's numerator.</summary>
    public BigInteger Numerator => _kind == Kind.Rational ? _big : Big;

    /// <summary>The denominator: 1 for integers, or the rational's denominator.</summary>
    public BigInteger Denominator => _kind == Kind.Rational ? _denominator : BigInteger.One;

    /// <summary>The value widened to a double, whichever kind it is. A big value may widen to infinity.</summary>
    public double AsDouble =>
        _kind switch
        {
            Kind.Float => _real,
            Kind.Big => (double)_big,
            Kind.Rational => (double)_big / (double)_denominator,
            _ => _integer,
        };

    /// <summary>Creates an integer value, widening to the big kind when it is outside the fixnum range.</summary>
    public static PrologNumber FromInteger(long value) =>
        Cell.FitsInteger(value)
            ? new PrologNumber(Kind.Integer, value, 0, default, default)
            : new PrologNumber(Kind.Big, 0, 0, value, default);

    /// <summary>Creates a float value.</summary>
    public static PrologNumber FromReal(double value) => new(Kind.Float, 0, value, default, default);

    /// <summary>Creates an integer value from a full-width integer, normalizing to the plain kind when it fits.</summary>
    public static PrologNumber FromBig(BigInteger value) =>
        value >= Cell.MinInteger && value <= Cell.MaxInteger
            ? new PrologNumber(Kind.Integer, (long)value, 0, default, default)
            : new PrologNumber(Kind.Big, 0, 0, value, default);

    /// <summary>
    /// Creates the canonical rational <paramref name="numerator"/> over
    /// <paramref name="denominator"/>: the sign moves to the numerator, the gcd divides out, and
    /// a denominator of 1 demotes to the integer kinds. The denominator must not be zero.
    /// </summary>
    public static PrologNumber FromRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (!divisor.IsOne)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        return denominator.IsOne ? FromBig(numerator) : new PrologNumber(Kind.Rational, 0, 0, numerator, denominator);
    }

    /// <inheritdoc />
    public bool Equals(PrologNumber other) =>
        _kind == other._kind
        && _kind switch
        {
            Kind.Float => _real.Equals(other._real),
            Kind.Big => _big.Equals(other._big),
            Kind.Rational => _big.Equals(other._big) && _denominator.Equals(other._denominator),
            _ => _integer == other._integer,
        };

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PrologNumber other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _kind switch
        {
            Kind.Float => _real.GetHashCode(),
            Kind.Big => _big.GetHashCode(),
            Kind.Rational => HashCode.Combine(_big, _denominator),
            _ => _integer.GetHashCode(),
        };

    /// <summary>Compares two numbers by value.</summary>
    public static bool operator ==(PrologNumber left, PrologNumber right) => left.Equals(right);

    /// <summary>Compares two numbers by value.</summary>
    public static bool operator !=(PrologNumber left, PrologNumber right) => !left.Equals(right);
}
