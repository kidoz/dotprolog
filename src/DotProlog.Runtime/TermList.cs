namespace DotProlog.Runtime;

/// <summary>
/// Reading and building Prolog lists, which most library builtins need and none should re-derive.
/// </summary>
/// <remarks>
/// Cycles are detected by Brent's method. Reading one raises <c>type_error(list, List)</c> with the
/// original cyclic term as culprit; <see cref="IsProper"/> simply fails.
/// </remarks>
internal static class TermList
{
    /// <summary>
    /// Appends the elements of <paramref name="list"/> to <paramref name="elements"/> and returns
    /// what it ends in: the empty list for a proper list, an unbound cell for a partial one, and
    /// anything else for a term that is not a list at all.
    /// </summary>
    internal static Cell Read(Machine machine, Cell list, List<Cell> elements)
    {
        var cell = machine.Dereference(list);
        var mark = cell;
        long power = 1,
            steps = 0;

        while (cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == machine.Symbols.ListFunctor)
        {
            elements.Add(machine.HeapAt(cell.Index + 1));
            cell = machine.Dereference(machine.HeapAt(cell.Index + 2));
            if (cell == mark)
            {
                throw PrologErrors.Type(machine, "list", machine.Dereference(list));
            }

            if (++steps == power)
            {
                mark = cell;
                power *= 2;
                steps = 0;
            }
        }

        return cell;
    }

    /// <summary>Whether a tail returned by <see cref="Read"/> means the list was proper.</summary>
    internal static bool IsEmpty(Machine machine, Cell tail) =>
        tail.Tag == CellTag.Atom && tail.Index == machine.Symbols.EmptyList;

    /// <summary>Reads a proper list, or reports why it is not one.</summary>
    /// <exception cref="PrologException">
    /// <c>instantiation_error</c> for a partial list, <c>type_error(list, List)</c> otherwise.
    /// </exception>
    internal static List<Cell> ReadProper(Machine machine, Cell list)
    {
        List<Cell> elements = [];
        var tail = Read(machine, list, elements);

        if (IsEmpty(machine, tail))
        {
            return elements;
        }

        throw tail.Tag == CellTag.Reference
            ? PrologErrors.Instantiation(machine)
            : PrologErrors.Type(machine, "list", machine.Dereference(list));
    }

    /// <summary>Whether <paramref name="list"/> is a proper list, without collecting its elements.</summary>
    internal static bool IsProper(Machine machine, Cell list) =>
        TrySkip(machine, list, out _, out var tail) && IsEmpty(machine, tail);

    /// <summary>
    /// Counts the cells of <paramref name="list"/> and finds what it ends in, as
    /// <see cref="Read"/> does but without collecting the elements, and without looping on a cyclic
    /// list: that is found by Brent's method and answered with <see langword="false"/>.
    /// </summary>
    internal static bool TrySkip(Machine machine, Cell list, out long count, out Cell tail)
    {
        var cell = machine.Dereference(list);
        var mark = cell;
        long power = 1,
            steps = 0;
        count = 0;

        while (cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == machine.Symbols.ListFunctor)
        {
            count++;
            cell = machine.Dereference(machine.HeapAt(cell.Index + 2));
            if (cell == mark)
            {
                tail = default;
                return false;
            }

            if (++steps == power)
            {
                mark = cell;
                power *= 2;
                steps = 0;
            }
        }

        tail = cell;
        return true;
    }

    /// <summary>Builds a proper list holding <paramref name="items"/>.</summary>
    internal static Cell Build(Machine machine, ReadOnlySpan<Cell> items) =>
        machine.CreateList(items, Cell.Atom(machine.Symbols.EmptyList));
}
