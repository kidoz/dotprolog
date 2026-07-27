using System.Globalization;

namespace DotProlog.Runtime;

/// <summary>
/// A Prolog term marshalled into plain .NET objects, detached from the heap.
/// </summary>
/// <remarks>
/// A host cannot hold a <see cref="Cell"/> across a call to <see cref="Machine.Redo"/>: backtracking
/// truncates the heap the cell points into. Marshalling each answer as it is produced is what makes
/// solutions safe to keep, collect, and hand around.
/// </remarks>
public abstract record PrologValue
{
    /// <summary>Marshals <paramref name="term"/> and everything it refers to.</summary>
    /// <remarks>Traversal is iterative, so a deeply nested term cannot exhaust the CLR stack.</remarks>
    public static PrologValue FromTerm(Machine machine, Cell term)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Cell root = machine.Dereference(term);
        if (root.Tag != CellTag.Structure)
        {
            return Convert(machine, root);
        }

        var pending = new Stack<Frame>();
        pending.Push(Frame.For(machine, root));

        while (true)
        {
            Frame frame = pending.Peek();

            if (frame.Index < frame.Arguments.Length)
            {
                Cell argument = machine.Dereference(frame.Arguments[frame.Index]);
                if (argument.Tag == CellTag.Structure)
                {
                    pending.Push(Frame.For(machine, argument));
                    continue;
                }

                frame.Values[frame.Index++] = Convert(machine, argument);
                continue;
            }

            pending.Pop();
            var completed = new PrologCompound(frame.Name, frame.Values);

            if (pending.Count == 0)
            {
                return completed;
            }

            Frame parent = pending.Peek();
            parent.Values[parent.Index++] = completed;
        }
    }

    /// <summary>Reads the value as a Prolog list, if it is a proper one ending in <c>[]</c>.</summary>
    public bool TryGetList(out IReadOnlyList<PrologValue> items)
    {
        List<PrologValue> collected = [];
        PrologValue current = this;

        while (current is PrologCompound { Name: ".", Arguments.Count: 2 } pair)
        {
            collected.Add(pair.Arguments[0]);
            current = pair.Arguments[1];
        }

        if (current is PrologAtom { Name: "[]" })
        {
            items = collected;
            return true;
        }

        items = [];
        return false;
    }

    private static PrologValue Convert(Machine machine, Cell cell) =>
        cell.Tag switch
        {
            CellTag.Atom => new PrologAtom(machine.Symbols.AtomName(cell.Index)),
            CellTag.Integer => new PrologInteger(cell.Integer),
            CellTag.Float => new PrologFloat(machine.Symbols.GetFloat(cell.Index)),
            CellTag.Reference => new PrologVariable(string.Create(CultureInfo.InvariantCulture, $"_G{cell.Index}")),
            _ => throw new PrologException($"Cannot marshal a {cell.Tag} cell."),
        };

    private sealed class Frame(string name, Cell[] arguments)
    {
        internal static Frame For(Machine machine, Cell structure)
        {
            Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(structure.Index).Index);
            var arguments = new Cell[functor.Arity];
            for (int i = 0; i < functor.Arity; i++)
            {
                arguments[i] = machine.HeapAt(structure.Index + 1 + i);
            }

            return new Frame(machine.Symbols.AtomName(functor.NameAtom), arguments);
        }

        internal string Name { get; } = name;

        internal Cell[] Arguments { get; } = arguments;

        internal PrologValue[] Values { get; } = new PrologValue[arguments.Length];

        internal int Index { get; set; }
    }
}
