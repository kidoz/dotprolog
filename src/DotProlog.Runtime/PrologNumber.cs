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
    private readonly Machine? _owner;

    private PrologNumber(Kind kind, long integer, double real, BigInteger big, BigInteger denominator, Machine? owner = null)
    {
        _kind = kind;
        _integer = integer;
        _real = real;
        _big = big;
        _denominator = denominator;
        _owner = owner;
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

    /// <summary>The value rounded to the nearest binary64 float, with ties to even. Overflow gives infinity.</summary>
    public double AsDouble =>
        _kind switch
        {
            Kind.Float => _real,
            Kind.Big => IntegerToDouble(_big),
            Kind.Rational => RationalToDouble(_big, _denominator),
            _ => _integer,
        };

    internal Machine? Owner => _owner;

    internal PrologNumber WithOwner(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return new PrologNumber(_kind, _integer, _real, _big, _denominator, machine);
    }

    /// <summary>Creates an integer value, widening to the big kind when it is outside the fixnum range.</summary>
    public static PrologNumber FromInteger(long value) =>
        Cell.FitsInteger(value)
            ? new PrologNumber(Kind.Integer, value, 0, default, default)
            : new PrologNumber(Kind.Big, 0, 0, value, default);

    /// <summary>Creates a finite float value.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite.</exception>
    public static PrologNumber FromReal(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Prolog floats must be finite.");
        }

        return new PrologNumber(Kind.Float, 0, value, default, default);
    }

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
        if (denominator.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "A rational denominator must not be zero.");
        }

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (!divisor.IsOne)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        return denominator.IsOne ? FromBig(numerator) : new PrologNumber(Kind.Rational, 0, 0, numerator, denominator);
    }

    private static double IntegerToDouble(BigInteger value)
    {
        if (value.IsZero)
        {
            return 0;
        }

        var negative = value.Sign < 0;
        var magnitude = BigInteger.Abs(value);
        var bitLength = magnitude.GetBitLength();
        if (bitLength <= 53)
        {
            return negative ? -(double)magnitude : (double)magnitude;
        }

        if (bitLength > int.MaxValue + 53L)
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        var shift = (int)(bitLength - 53);
        var significand = magnitude >> shift;
        var remainder = magnitude - (significand << shift);
        var halfway = BigInteger.One << (shift - 1);
        if (remainder > halfway || (remainder == halfway && !significand.IsEven))
        {
            significand++;
        }

        if (significand.GetBitLength() > 53)
        {
            significand >>= 1;
            shift++;
        }

        var result = Math.ScaleB((double)significand, shift);
        return negative ? -result : result;
    }

    private static double RationalToDouble(BigInteger numerator, BigInteger denominator)
    {
        if (numerator.IsZero)
        {
            return 0;
        }

        var negative = numerator.Sign < 0;
        numerator = BigInteger.Abs(numerator);
        var exponent = numerator.GetBitLength() - denominator.GetBitLength();
        if (exponent > 1024)
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        if (exponent < -1076)
        {
            return negative ? -0.0 : 0.0;
        }

        var below = exponent >= 0 ? numerator < (denominator << (int)exponent) : (numerator << (int)-exponent) < denominator;
        if (below)
        {
            exponent--;
        }

        if (exponent > 1023)
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        if (exponent < -1075)
        {
            return negative ? -0.0 : 0.0;
        }

        BigInteger significand;
        int resultExponent;
        if (exponent < -1022)
        {
            var quotient = BigInteger.DivRem(numerator << 1074, denominator, out var remainder);
            significand = RoundQuotient(quotient, remainder, denominator);
            resultExponent = -1074;
        }
        else
        {
            var scale = 52 - (int)exponent;
            var scaledNumerator = scale >= 0 ? numerator << scale : numerator;
            var scaledDenominator = scale < 0 ? denominator << -scale : denominator;
            var quotient = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
            significand = RoundQuotient(quotient, remainder, scaledDenominator);
            resultExponent = (int)exponent - 52;
            if (significand.GetBitLength() > 53)
            {
                significand >>= 1;
                resultExponent++;
            }
        }

        var result = Math.ScaleB((double)significand, resultExponent);
        return negative ? -result : result;
    }

    private static BigInteger RoundQuotient(BigInteger quotient, BigInteger remainder, BigInteger denominator)
    {
        var comparison = (remainder << 1).CompareTo(denominator);
        return comparison > 0 || (comparison == 0 && !quotient.IsEven) ? quotient + 1 : quotient;
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
