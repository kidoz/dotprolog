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
        PrologNumber value = PrologNumber.FromRational(numerator, denominator);
        return value.IsRational
            ? new PrologInput(PrologInputKind.Rational, null, 0, 0, null, value.Numerator, value.Denominator)
            : Big(value.Big);
    }

    /// <summary>Passes an integer of any magnitude.</summary>
    public static PrologInput Big(BigInteger value) =>
        value >= Cell.MinInteger && value <= Cell.MaxInteger
            ? new PrologInput(PrologInputKind.Integer, null, (long)value, 0, null)
            : new PrologInput(PrologInputKind.BigInteger, null, 0, 0, null, value);

    /// <summary>Passes a floating-point number.</summary>
    public static PrologInput Float(double value) => new(PrologInputKind.Float, null, 0, value, null);

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

        return value switch
        {
            PrologAtom atom => Atom(atom.Name),
            PrologString text => String(text.Value),
            PrologInteger integer => Integer(integer.Value),
            PrologBigInteger big => Big(big.Value),
            PrologRational rational => Rational(rational.Numerator, rational.Denominator),
            PrologFloat real => Float(real.Value),
            PrologCompound compound => Compound(compound.Name, [.. compound.Arguments.Select(FromValue)]),

            // A variable that was unbound when it was marshalled goes back as a fresh one.
            _ => Output,
        };
    }

    /// <summary>Materialises this argument on <paramref name="machine"/>'s heap.</summary>
    /// <remarks>Only valid between <see cref="Machine.BeginCall"/> and <see cref="Machine.Call"/>.</remarks>
    public Cell Build(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        switch (_kind)
        {
            case PrologInputKind.Output:
                return machine.CreateVariable();

            case PrologInputKind.Atom:
                return Cell.Atom(machine.Symbols.InternAtom(_text!));

            case PrologInputKind.String:
                return Cell.String(machine.Symbols.InternAtom(_text!));

            case PrologInputKind.Integer:
                return Cell.Integer60(_integer);

            case PrologInputKind.BigInteger:
                return Cell.Big(machine.Symbols.InternBig(_big));

            case PrologInputKind.Rational:
                return Cell.Rational(machine.Symbols.InternRational(_big, _denominator));

            case PrologInputKind.Float:
                return Cell.Float(machine.Symbols.InternFloat(_real));

            case PrologInputKind.List:
            {
                var items = new Cell[_items!.Length];
                for (var i = 0; i < items.Length; i++)
                {
                    items[i] = _items[i].Build(machine);
                }

                return machine.CreateList(items, Cell.Atom(machine.Symbols.EmptyList));
            }

            default:
            {
                var arguments = new Cell[_items!.Length];
                for (var i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = _items[i].Build(machine);
                }

                return machine.CreateStructure(machine.Symbols.InternFunctor(_text!, arguments.Length), arguments);
            }
        }
    }
}
