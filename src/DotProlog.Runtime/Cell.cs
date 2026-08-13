namespace DotProlog.Runtime;

/// <summary>
/// A tagged term cell: a four-bit <see cref="CellTag"/> over a 60-bit payload, packed into eight
/// bytes. Heap, environment slots, argument registers, and the constant pool all hold cells, so the
/// engine never needs an object graph to represent a term.
/// </summary>
public readonly struct Cell : IEquatable<Cell>
{
    private const int TagShift = 60;
    private const ulong PayloadMask = (1UL << TagShift) - 1UL;

    /// <summary>The largest integer an <see cref="CellTag.Integer"/> cell can hold.</summary>
    public const long MaxInteger = (1L << 59) - 1;

    /// <summary>The smallest integer an <see cref="CellTag.Integer"/> cell can hold.</summary>
    public const long MinInteger = -(1L << 59);

    private readonly ulong _bits;

    private Cell(ulong bits) => _bits = bits;

    /// <summary>The cell's type tag.</summary>
    public CellTag Tag => (CellTag)(_bits >> TagShift);

    /// <summary>
    /// The payload read as an index: a heap address for <see cref="CellTag.Reference"/> and
    /// <see cref="CellTag.Structure"/>, or a table identifier for the remaining tags.
    /// </summary>
    public int Index => (int)(_bits & PayloadMask);

    /// <summary>The payload read as a signed integer. Only meaningful for <see cref="CellTag.Integer"/>.</summary>
    public long Integer => (long)(_bits << (64 - TagShift)) >> (64 - TagShift);

    /// <summary>Whether this cell is an unbound or bound variable reference.</summary>
    public bool IsReference => Tag == CellTag.Reference;

    /// <summary>Creates a variable reference to <paramref name="address"/>.</summary>
    public static Cell Reference(int address) => Make(CellTag.Reference, address);

    /// <summary>Creates a compound-term cell pointing at the functor cell at <paramref name="address"/>.</summary>
    public static Cell Structure(int address) => Make(CellTag.Structure, address);

    /// <summary>Creates a heap functor header for <paramref name="functorId"/>.</summary>
    public static Cell Functor(int functorId) => Make(CellTag.Functor, functorId);

    /// <summary>Creates an atom cell for <paramref name="atomId"/>.</summary>
    public static Cell Atom(int atomId) => Make(CellTag.Atom, atomId);

    /// <summary>Creates a float cell referring to interned float <paramref name="floatId"/>.</summary>
    public static Cell Float(int floatId) => Make(CellTag.Float, floatId);

    /// <summary>Creates a string cell whose text is the interned atom <paramref name="atomId"/>.</summary>
    public static Cell String(int atomId) => Make(CellTag.String, atomId);

    /// <summary>Creates an integer cell.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> does not fit in 60 bits.</exception>
    public static Cell Integer60(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, MinInteger);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxInteger);
        return new Cell(((ulong)CellTag.Integer << TagShift) | ((ulong)value & PayloadMask));
    }

    /// <summary>Whether <paramref name="value"/> fits in an integer cell.</summary>
    public static bool FitsInteger(long value) => value is >= MinInteger and <= MaxInteger;

    private static Cell Make(CellTag tag, int payload) => new(((ulong)tag << TagShift) | ((uint)payload & PayloadMask));

    /// <inheritdoc />
    public bool Equals(Cell other) => _bits == other._bits;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Cell other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _bits.GetHashCode();

    /// <summary>Compares two cells by their raw bits.</summary>
    public static bool operator ==(Cell left, Cell right) => left.Equals(right);

    /// <summary>Compares two cells by their raw bits.</summary>
    public static bool operator !=(Cell left, Cell right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Tag == CellTag.Integer ? $"Integer({Integer})" : $"{Tag}({Index})";
}
