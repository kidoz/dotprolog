namespace DotProlog.Runtime;

/// <summary>The result of evaluating an arithmetic expression: either an integer or a float.</summary>
public readonly struct PrologNumber : IEquatable<PrologNumber>
{
    private PrologNumber(bool isFloat, long integer, double real)
    {
        IsFloat = isFloat;
        Integer = integer;
        Real = real;
    }

    /// <summary>Whether the value is a float rather than an integer.</summary>
    public bool IsFloat { get; }

    /// <summary>The integer value. Meaningful only when <see cref="IsFloat"/> is <see langword="false"/>.</summary>
    public long Integer { get; }

    /// <summary>The float value. Meaningful only when <see cref="IsFloat"/> is <see langword="true"/>.</summary>
    public double Real { get; }

    /// <summary>The value widened to a double, whichever kind it is.</summary>
    public double AsDouble => IsFloat ? Real : Integer;

    /// <summary>Creates an integer value.</summary>
    public static PrologNumber FromInteger(long value) => new(false, value, 0);

    /// <summary>Creates a float value.</summary>
    public static PrologNumber FromReal(double value) => new(true, 0, value);

    /// <inheritdoc />
    public bool Equals(PrologNumber other) =>
        IsFloat == other.IsFloat && (IsFloat ? Real.Equals(other.Real) : Integer == other.Integer);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PrologNumber other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsFloat ? Real.GetHashCode() : Integer.GetHashCode();

    /// <summary>Compares two numbers by value.</summary>
    public static bool operator ==(PrologNumber left, PrologNumber right) => left.Equals(right);

    /// <summary>Compares two numbers by value.</summary>
    public static bool operator !=(PrologNumber left, PrologNumber right) => !left.Equals(right);
}
