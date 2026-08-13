namespace DotProlog.Runtime;

/// <summary>
/// Reads C# values out of marshalled terms. Generated facades call these, which is why the failure
/// messages name the contract mismatch rather than the cell shape.
/// </summary>
public static class PrologMarshal
{
    /// <summary>Reads an atom as a string.</summary>
    public static string ToAtom(PrologValue value) => value as PrologAtom is { } atom ? atom.Name : throw Mismatch(value, "atom");

    /// <summary>Reads an integer.</summary>
    public static long ToInteger(PrologValue value) =>
        value as PrologInteger is { } integer ? integer.Value : throw Mismatch(value, "integer");

    /// <summary>Reads an integer of any magnitude, accepting both integer representations.</summary>
    public static System.Numerics.BigInteger ToBigInteger(PrologValue value) =>
        value switch
        {
            PrologInteger integer => integer.Value,
            PrologBigInteger big => big.Value,
            _ => throw Mismatch(value, "integer"),
        };

    /// <summary>Reads a number as a double, accepting an integer where a float was declared.</summary>
    public static double ToFloat(PrologValue value) =>
        value switch
        {
            PrologFloat real => real.Value,
            PrologInteger integer => integer.Value,
            PrologBigInteger big => (double)big.Value,
            PrologRational rational => (double)rational.Numerator / (double)rational.Denominator,
            _ => throw Mismatch(value, "float"),
        };

    /// <summary>Reads a proper list, converting each element with <paramref name="element"/>.</summary>
    public static IReadOnlyList<T> ToList<T>(PrologValue value, Func<PrologValue, T> element)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(element);

        if (!value.TryGetList(out IReadOnlyList<PrologValue> items))
        {
            throw Mismatch(value, "list");
        }

        var converted = new T[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            converted[i] = element(items[i]);
        }

        return converted;
    }

    private static PrologException Mismatch(PrologValue value, string expected) =>
        new($"type_error({expected}, {value}) — the contract declared {expected}.");
}
