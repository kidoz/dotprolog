namespace Prolog.Runtime;

/// <summary>
/// Sorting by the standard order of terms: <c>msort/2</c>, <c>sort/2</c>, <c>sort/4</c>, and
/// <c>keysort/2</c>.
/// </summary>
/// <remarks>
/// Every sort here is stable, which <c>keysort/2</c> is required to be and the others are not. It
/// costs one comparison of positions on a tie and makes the output of a sort predictable, which is
/// worth more than the freedom to reorder equal elements.
/// </remarks>
internal static class SortBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("msort", 2, static machine => Sort(machine, key: 0, descending: false, dedupe: false));
        registry.Register("sort", 2, static machine => Sort(machine, key: 0, descending: false, dedupe: true));
        registry.Register("sort", 4, Sort4);
        registry.Register("keysort", 2, KeySort);
    }

    /// <summary>
    /// <c>sort(+Key, +Order, +List, -Sorted)</c>. Key 0 sorts on the whole term, and any other key
    /// sorts on that argument of each element. The order atom decides direction and whether
    /// duplicates survive: <c>@&lt;</c> and <c>@&gt;</c> remove them, <c>@=&lt;</c> and <c>@&gt;=</c> keep them.
    /// </summary>
    private static bool Sort4(Machine machine)
    {
        Cell keyCell = machine.Argument(0);
        if (keyCell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (keyCell.Tag != CellTag.Integer || keyCell.Integer < 0)
        {
            throw PrologErrors.Type(machine, "integer", keyCell);
        }

        Cell orderCell = machine.Argument(1);
        if (orderCell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        (bool descending, bool dedupe) =
            orderCell.Tag == CellTag.Atom
                ? machine.Symbols.AtomName(orderCell.Index) switch
                {
                    "@<" => (false, true),
                    "@=<" => (false, false),
                    "@>" => (true, true),
                    "@>=" => (true, false),
                    _ => throw PrologErrors.Domain(machine, "order", orderCell),
                }
                : throw PrologErrors.Domain(machine, "order", orderCell);

        return Sort(machine, (int)keyCell.Integer, descending, dedupe, listIndex: 2, resultIndex: 3);
    }

    private static bool Sort(Machine machine, int key, bool descending, bool dedupe, int listIndex = 0, int resultIndex = 1)
    {
        List<Cell> elements = TermList.ReadProper(machine, machine.Argument(listIndex));
        Cell[] sorted = Arrange(machine, elements, element => KeyOf(machine, element, key), descending, dedupe);
        return machine.Unify(machine.Argument(resultIndex), TermList.Build(machine, sorted));
    }

    /// <summary>
    /// <c>keysort(+Pairs, -Sorted)</c>: sorts <c>Key-Value</c> pairs on the key alone, leaving pairs
    /// with equal keys in their original order.
    /// </summary>
    private static bool KeySort(Machine machine)
    {
        List<Cell> elements = TermList.ReadProper(machine, machine.Argument(0));
        int pair = machine.Symbols.InternFunctor("-", 2);

        Cell[] sorted = Arrange(
            machine,
            elements,
            element =>
            {
                Cell cell = machine.Dereference(element);

                if (cell.Tag == CellTag.Reference)
                {
                    throw PrologErrors.Instantiation(machine);
                }

                return cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == pair
                    ? machine.HeapAt(cell.Index + 1)
                    : throw PrologErrors.Type(machine, "pair", cell);
            },
            descending: false,
            dedupe: false
        );

        return machine.Unify(machine.Argument(1), TermList.Build(machine, sorted));
    }

    private static Cell[] Arrange(Machine machine, List<Cell> elements, Func<Cell, Cell> keyOf, bool descending, bool dedupe)
    {
        var keyed = new (Cell Key, Cell Element, int Position)[elements.Count];
        for (int i = 0; i < elements.Count; i++)
        {
            keyed[i] = (keyOf(elements[i]), elements[i], i);
        }

        Array.Sort(
            keyed,
            (left, right) =>
            {
                int order = TermOrder.Compare(machine, left.Key, right.Key);
                if (order != 0)
                {
                    return descending ? -order : order;
                }

                // The original position breaks every tie, which is what makes the sort stable.
                return left.Position.CompareTo(right.Position);
            }
        );

        if (!dedupe)
        {
            return [.. keyed.Select(entry => entry.Element)];
        }

        List<Cell> unique = [];
        for (int i = 0; i < keyed.Length; i++)
        {
            if (i == 0 || TermOrder.Compare(machine, keyed[i - 1].Key, keyed[i].Key) != 0)
            {
                unique.Add(keyed[i].Element);
            }
        }

        return [.. unique];
    }

    /// <summary>The sort key of one element: the element itself for key 0, or that argument of it.</summary>
    private static Cell KeyOf(Machine machine, Cell element, int key)
    {
        if (key == 0)
        {
            return element;
        }

        Cell cell = machine.Dereference(element);

        if (cell.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "compound", cell);
        }

        int arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
        return key <= arity ? machine.HeapAt(cell.Index + key) : throw PrologErrors.Type(machine, "compound", cell);
    }
}
