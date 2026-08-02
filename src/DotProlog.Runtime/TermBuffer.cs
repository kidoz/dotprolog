namespace DotProlog.Runtime;

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
    private readonly HashSet<int> _active = [];
    private Cell[] _cells = new Cell[32];
    private int _count;

    /// <summary>Number of cells the buffer holds.</summary>
    internal int Count => _count;

    internal ReadOnlySpan<Cell> Cells => _cells.AsSpan(0, _count);

    internal static TermBuffer FromCells(ReadOnlySpan<Cell> cells)
    {
        var buffer = new TermBuffer { _cells = cells.ToArray(), _count = cells.Length };
        return buffer;
    }

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
        _active.Clear();

        var root = Reserve(1);
        _work.Add((term, root));

        while (_work.Count > 0)
        {
            (Cell source, var slot) = _work[^1];
            _work.RemoveAt(_work.Count - 1);

            // A negative slot marks leaving the structure whose heap index the entry carries.
            if (slot < 0)
            {
                _active.Remove(source.Index);
                continue;
            }

            Cell cell = machine.Dereference(source);
            switch (cell.Tag)
            {
                case CellTag.Reference:
                {
                    if (!_variables.TryGetValue(cell.Index, out var variable))
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
                    // The machine's unification tolerates rational trees, but every consumer of a
                    // detached copy — collected solutions, stored clauses, thrown balls — expects a
                    // finite term, so a cyclic term is rejected with a catchable error.
                    if (!_active.Add(cell.Index))
                    {
                        throw PrologErrors.Representation(machine, "cyclic_term");
                    }

                    _work.Add((cell, -1));

                    var arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
                    var structure = Reserve(arity + 1);
                    _cells[structure] = machine.HeapAt(cell.Index);
                    _cells[slot] = Cell.Structure(structure);

                    for (var i = 1; i <= arity; i++)
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
        var origin = machine.ReserveHeap(_count);

        for (var i = 0; i < _count; i++)
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

        var slot = _count;
        _count += count;
        return slot;
    }
}
