namespace Prolog.Runtime;

/// <summary>
/// A detached copy of one or more terms, held outside the heap.
/// </summary>
/// <remarks>
/// <para>
/// Two features need the same thing: a term that survives the heap being truncated. A thrown ball
/// must outlive the unwind back to <c>catch/3</c>, and each solution <c>findall/3</c> collects must
/// outlive the backtracking that produces the next one.
/// </para>
/// <para>
/// The copy is flat. Cells are stored contiguously with buffer-relative addressing, so materialising
/// is a blit plus a constant offset added to every reference and structure cell — no second
/// traversal. Variables are copied consistently within a single <see cref="Copy"/> call and
/// independently across calls, which is exactly the sharing <c>findall/3</c> wants between solutions.
/// </para>
/// </remarks>
internal sealed class TermBuffer
{
    private readonly Dictionary<int, int> _variables = [];
    private readonly List<(Cell Source, int Slot)> _work = [];
    private Cell[] _cells = new Cell[32];
    private int _count;

    /// <summary>Number of cells the buffer holds.</summary>
    internal int Count => _count;

    /// <summary>Discards everything copied so far.</summary>
    internal void Clear()
    {
        _count = 0;
        _variables.Clear();
    }

    /// <summary>
    /// Appends a copy of <paramref name="term"/> and returns the slot its root cell occupies.
    /// Variables are renamed apart from any previous copy in this buffer.
    /// </summary>
    internal int Copy(Machine machine, Cell term)
    {
        _variables.Clear();
        _work.Clear();

        int root = Reserve(1);
        _work.Add((term, root));

        while (_work.Count > 0)
        {
            (Cell source, int slot) = _work[^1];
            _work.RemoveAt(_work.Count - 1);

            Cell cell = machine.Dereference(source);
            switch (cell.Tag)
            {
                case CellTag.Reference:
                {
                    if (!_variables.TryGetValue(cell.Index, out int variable))
                    {
                        variable = Reserve(1);
                        _cells[variable] = Cell.Reference(variable);
                        _variables[cell.Index] = variable;
                    }

                    _cells[slot] = Cell.Reference(variable);
                    break;
                }

                case CellTag.Structure:
                {
                    int arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
                    int structure = Reserve(arity + 1);
                    _cells[structure] = machine.HeapAt(cell.Index);
                    _cells[slot] = Cell.Structure(structure);

                    for (int i = 1; i <= arity; i++)
                    {
                        _work.Add((machine.HeapAt(cell.Index + i), structure + i));
                    }

                    break;
                }

                default:
                    _cells[slot] = cell;
                    break;
            }
        }

        return root;
    }

    /// <summary>
    /// Writes the whole buffer onto the heap and returns the address it starts at. A term whose root
    /// slot was <c>r</c> is then the heap cell at <c>base + r</c>.
    /// </summary>
    internal int Materialize(Machine machine)
    {
        int origin = machine.ReserveHeap(_count);

        for (int i = 0; i < _count; i++)
        {
            Cell cell = _cells[i];
            machine.WriteHeap(
                origin + i,
                cell.Tag switch
                {
                    CellTag.Reference => Cell.Reference(cell.Index + origin),
                    CellTag.Structure => Cell.Structure(cell.Index + origin),
                    _ => cell,
                }
            );
        }

        return origin;
    }

    private int Reserve(int count)
    {
        if (_count + count > _cells.Length)
        {
            Array.Resize(ref _cells, Math.Max(_cells.Length * 2, _count + count));
        }

        int slot = _count;
        _count += count;
        return slot;
    }
}
