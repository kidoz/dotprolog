namespace Prolog.Runtime;

/// <summary>
/// Reading and building Prolog lists, which most library builtins need and none should re-derive.
/// </summary>
/// <remarks>
/// A cyclic list makes these loop forever. Only a term like <c>X = [a|X]</c> can create one, since
/// unification has no occurs check, and that is the same bargain the rest of the engine makes.
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
        Cell cell = machine.Dereference(list);

        while (cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == machine.Symbols.ListFunctor)
        {
            elements.Add(machine.HeapAt(cell.Index + 1));
            cell = machine.Dereference(machine.HeapAt(cell.Index + 2));
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
        Cell tail = Read(machine, list, elements);

        if (IsEmpty(machine, tail))
        {
            return elements;
        }

        throw tail.Tag == CellTag.Reference
            ? PrologErrors.Instantiation(machine)
            : PrologErrors.Type(machine, "list", machine.Dereference(list));
    }

    /// <summary>Whether <paramref name="list"/> is a proper list, without collecting its elements.</summary>
    internal static bool IsProper(Machine machine, Cell list)
    {
        Cell cell = machine.Dereference(list);

        while (cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == machine.Symbols.ListFunctor)
        {
            cell = machine.Dereference(machine.HeapAt(cell.Index + 2));
        }

        return IsEmpty(machine, cell);
    }

    /// <summary>Builds a proper list holding <paramref name="items"/>.</summary>
    internal static Cell Build(Machine machine, ReadOnlySpan<Cell> items) =>
        machine.CreateList(items, Cell.Atom(machine.Symbols.EmptyList));
}
