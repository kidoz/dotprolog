namespace Prolog.Runtime;

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

    private PrologInput(PrologInputKind kind, string? text, long integer, double real, PrologInput[]? items)
    {
        _kind = kind;
        _text = text;
        _integer = integer;
        _real = real;
        _items = items;
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

    /// <summary>Passes an integer.</summary>
    public static PrologInput Integer(long value) => new(PrologInputKind.Integer, null, value, 0, null);

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

            case PrologInputKind.Integer:
                return Cell.Integer60(_integer);

            case PrologInputKind.Float:
                return Cell.Float(machine.Symbols.InternFloat(_real));

            case PrologInputKind.List:
            {
                var items = new Cell[_items!.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = _items[i].Build(machine);
                }

                return machine.CreateList(items, Cell.Atom(machine.Symbols.EmptyList));
            }

            default:
            {
                var arguments = new Cell[_items!.Length];
                for (int i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = _items[i].Build(machine);
                }

                return machine.CreateStructure(machine.Symbols.InternFunctor(_text!, arguments.Length), arguments);
            }
        }
    }
}
