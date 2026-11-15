using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>
/// One argument of a call from .NET into Prolog: either a value to pass in, or a hole to read a
/// result out of.
/// </summary>
/// <remarks>
/// An argument is a description rather than a <see cref="Cell"/> because the machine is reset before
/// a call, which wipes the heap any cell would live on. The description is turned into a term after
/// the reset, by <see cref="Build"/>.
/// </remarks>
public readonly record struct PrologInput
{
    private readonly PrologInputKind _kind;
    private readonly string? _text;
    private readonly long _integer;
    private readonly double _real;
    private readonly PrologInput[]? _items;
    private readonly BigInteger _big;
    private readonly BigInteger _denominator;

    private PrologInput(
        PrologInputKind kind,
        string? text,
        long integer,
        double real,
        PrologInput[]? items,
        BigInteger big = default,
        BigInteger denominator = default
    )
    {
        _kind = kind;
        _text = text;
        _integer = integer;
        _real = real;
        _items = items;
        _big = big;
        _denominator = denominator;
    }

    /// <summary>An unbound argument, whose value the call is expected to produce.</summary>
    public static PrologInput Output => new(PrologInputKind.Output, null, 0, 0, null);

    /// <summary>Whether this argument is a hole rather than a value.</summary>
    public bool IsOutput => _kind == PrologInputKind.Output;

    /// <summary>Passes an atom.</summary>
    public static PrologInput Atom(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new PrologInput(PrologInputKind.Atom, name, 0, 0, null);
    }

    /// <summary>Passes a string term.</summary>
    public static PrologInput String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PrologInput(PrologInputKind.String, value, 0, 0, null);
    }

    /// <summary>Passes an integer, widening to the big representation when it is outside the fixnum range.</summary>
    public static PrologInput Integer(long value) =>
        Cell.FitsInteger(value) ? new PrologInput(PrologInputKind.Integer, null, value, 0, null) : Big(value);

    /// <summary>Passes a rational number, canonicalized; a denominator that divides out passes an integer.</summary>
    public static PrologInput Rational(BigInteger numerator, BigInteger denominator)
    {
        var value = PrologNumber.FromRational(numerator, denominator);
        return value.IsRational
            ? new PrologInput(PrologInputKind.Rational, null, 0, 0, null, value.Numerator, value.Denominator)
            : Big(value.Big);
    }

    /// <summary>Passes an integer of any magnitude.</summary>
    public static PrologInput Big(BigInteger value) =>
        value >= Cell.MinInteger && value <= Cell.MaxInteger
            ? new PrologInput(PrologInputKind.Integer, null, (long)value, 0, null)
            : new PrologInput(PrologInputKind.BigInteger, null, 0, 0, null, value);

    /// <summary>Passes a finite floating-point number.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite.</exception>
    public static PrologInput Float(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Prolog floats must be finite.");
        }

        return new PrologInput(PrologInputKind.Float, null, 0, value, null);
    }

    /// <summary>Passes a list.</summary>
    public static PrologInput List(params PrologInput[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new PrologInput(PrologInputKind.List, null, 0, 0, items);
    }

    /// <summary>Passes a compound term.</summary>
    public static PrologInput Compound(string name, params PrologInput[] arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return new PrologInput(PrologInputKind.Compound, name, 0, 0, arguments);
    }

    /// <summary>Passes a term that was marshalled out of an earlier call.</summary>
    public static PrologInput FromValue(PrologValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not PrologCompound root)
        {
            return ConvertNonCompound(value);
        }

        var pending = new Stack<ValueFrame>();
        pending.Push(new ValueFrame(root));

        while (true)
        {
            var frame = pending.Peek();

            if (frame.Index < frame.Arguments.Count)
            {
                var argument = frame.Arguments[frame.Index];
                ArgumentNullException.ThrowIfNull(argument);
                if (argument is PrologCompound compound)
                {
                    pending.Push(new ValueFrame(compound));
                    continue;
                }

                frame.Values[frame.Index++] = ConvertNonCompound(argument);
                continue;
            }

            pending.Pop();
            var completed = Compound(frame.Name, frame.Values);

            if (pending.Count == 0)
            {
                return completed;
            }

            var parent = pending.Peek();
            parent.Values[parent.Index++] = completed;
        }
    }

    private static PrologInput ConvertNonCompound(PrologValue value) =>
        value switch
        {
            PrologAtom atom => Atom(atom.Name),
            PrologString text => String(text.Value),
            PrologInteger integer => Integer(integer.Value),
            PrologBigInteger big => Big(big.Value),
            PrologRational rational => Rational(rational.Numerator, rational.Denominator),
            PrologFloat real => Float(real.Value),

            // A variable that was unbound when it was marshalled goes back as a fresh one.
            _ => Output,
        };

    /// <summary>Materialises this argument on <paramref name="machine"/>'s heap.</summary>
    /// <remarks>Only valid between <see cref="Machine.BeginCall"/> and <see cref="Machine.Call"/>.</remarks>
    public Cell Build(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (_kind is not PrologInputKind.List and not PrologInputKind.Compound)
        {
            return BuildLeaf(machine);
        }

        var pending = new Stack<BuildFrame>();
        pending.Push(new BuildFrame(this));

        while (true)
        {
            var frame = pending.Peek();

            if (frame.Index < frame.Items.Length)
            {
                var item = frame.Items[frame.Index];
                if (item._kind is PrologInputKind.List or PrologInputKind.Compound)
                {
                    pending.Push(new BuildFrame(item));
                    continue;
                }

                frame.Values[frame.Index++] = item.BuildLeaf(machine);
                continue;
            }

            pending.Pop();
            var completed = frame.Build(machine);

            if (pending.Count == 0)
            {
                return completed;
            }

            var parent = pending.Peek();
            parent.Values[parent.Index++] = completed;
        }
    }

    private Cell BuildLeaf(Machine machine) =>
        _kind switch
        {
            PrologInputKind.Output => machine.CreateVariable(),
            PrologInputKind.Atom => Cell.Atom(machine.Symbols.InternAtom(_text!)),
            PrologInputKind.String => Cell.String(machine.Symbols.InternAtom(_text!)),
            PrologInputKind.Integer => Cell.Integer60(_integer),
            PrologInputKind.BigInteger => Cell.Big(machine.Symbols.InternBig(_big)),
            PrologInputKind.Rational => Cell.Rational(machine.Symbols.InternRational(_big, _denominator)),
            PrologInputKind.Float => Cell.Float(machine.Symbols.InternFloat(_real)),
            _ => throw new InvalidOperationException("A compound input is not a leaf."),
        };

    private sealed class ValueFrame(PrologCompound compound)
    {
        internal string Name { get; } = compound.Name;

        internal IReadOnlyList<PrologValue> Arguments { get; } = compound.Arguments;

        internal PrologInput[] Values { get; } = new PrologInput[compound.Arguments.Count];

        internal int Index { get; set; }
    }

    private sealed class BuildFrame(PrologInput input)
    {
        internal PrologInput[] Items { get; } = input._items!;

        internal Cell[] Values { get; } = new Cell[input._items!.Length];

        internal int Index { get; set; }

        internal Cell Build(Machine machine) =>
            input._kind == PrologInputKind.List
                ? machine.CreateList(Values, Cell.Atom(machine.Symbols.EmptyList))
                : machine.CreateStructure(machine.Symbols.InternFunctor(input._text!, Values.Length), Values);
    }
}
