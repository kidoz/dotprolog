namespace DotProlog.Runtime;

/// <summary>
/// The ISO standard order of terms: <c>Variable @&lt; Float @&lt; Integer @&lt; Atom @&lt; Compound</c>.
/// Numbers of the same type compare by value. Compound terms compare by arity, then by functor name,
/// then argument by argument.
/// </summary>
/// <remarks>
/// Traversal is iterative over an explicit work list, so comparing deeply nested terms cannot
/// exhaust the CLR stack.
/// </remarks>
public static class TermOrder
{
    /// <summary>Compares two terms, returning a negative value, zero, or a positive value.</summary>
    /// <param name="machine">Machine owning the heap the terms live on.</param>
    /// <param name="left">First term.</param>
    /// <param name="right">Second term.</param>
    public static int Compare(Machine machine, Cell left, Cell right)
    {
        ArgumentNullException.ThrowIfNull(machine);

        List<(Cell Left, Cell Right)> work = [(left, right)];
        var visitedStructures = new HashSet<ulong>();

        while (work.Count > 0)
        {
            (Cell a, Cell b) = work[^1];
            work.RemoveAt(work.Count - 1);

            a = machine.Dereference(a);
            b = machine.Dereference(b);

            if (a == b)
            {
                continue;
            }

            var rank = RankOf(a).CompareTo(RankOf(b));
            if (rank != 0)
            {
                return rank;
            }

            switch (a.Tag)
            {
                case CellTag.Reference:
                    return a.Index.CompareTo(b.Index);

                case CellTag.Atom:
                case CellTag.String:
                {
                    var names = string.CompareOrdinal(machine.Symbols.AtomName(a.Index), machine.Symbols.AtomName(b.Index));
                    if (names != 0)
                    {
                        return names;
                    }

                    continue;
                }

                case CellTag.Integer:
                case CellTag.BigInteger:
                case CellTag.Float:
                {
                    var numbers = CompareNumbers(machine, a, b);
                    if (numbers != 0)
                    {
                        return numbers;
                    }

                    continue;
                }

                default:
                {
                    var pair = ((ulong)(uint)a.Index << 32) | (uint)b.Index;
                    if (!visitedStructures.Add(pair))
                    {
                        continue;
                    }

                    var structures = CompareStructures(machine, a, b, work);
                    if (structures != 0)
                    {
                        return structures;
                    }

                    continue;
                }
            }
        }

        return 0;
    }

    /// <summary>Whether two terms are identical under the standard order.</summary>
    public static bool AreIdentical(Machine machine, Cell left, Cell right) => Compare(machine, left, right) == 0;

    private static int CompareStructures(Machine machine, Cell a, Cell b, List<(Cell, Cell)> work)
    {
        var leftFunctor = machine.HeapAt(a.Index).Index;
        var rightFunctor = machine.HeapAt(b.Index).Index;
        Functor left = machine.Symbols.GetFunctor(leftFunctor);
        Functor right = machine.Symbols.GetFunctor(rightFunctor);

        var arity = left.Arity.CompareTo(right.Arity);
        if (arity != 0)
        {
            return arity;
        }

        var name = string.CompareOrdinal(machine.Symbols.AtomName(left.NameAtom), machine.Symbols.AtomName(right.NameAtom));
        if (name != 0)
        {
            return name;
        }

        // Pushed in reverse so the leftmost argument is compared first.
        for (var i = left.Arity; i >= 1; i--)
        {
            work.Add((machine.HeapAt(a.Index + i), machine.HeapAt(b.Index + i)));
        }

        return 0;
    }

    private static int CompareNumbers(Machine machine, Cell a, Cell b)
    {
        if (a.Tag == CellTag.Float)
        {
            return machine.Symbols.GetFloat(a.Index).CompareTo(machine.Symbols.GetFloat(b.Index));
        }

        if (a.Tag == CellTag.Integer && b.Tag == CellTag.Integer)
        {
            return a.Integer.CompareTo(b.Integer);
        }

        // At least one side is big, and a big cell never holds a fixnum-range value,
        // so widening the other side compares exactly.
        System.Numerics.BigInteger left = a.Tag == CellTag.BigInteger ? machine.Symbols.GetBig(a.Index) : a.Integer;
        System.Numerics.BigInteger right = b.Tag == CellTag.BigInteger ? machine.Symbols.GetBig(b.Index) : b.Integer;
        return left.CompareTo(right);
    }

    // Strings rank after numbers and before atoms — SWI-Prolog 10's probed order, which its
    // manual has not caught up with.
    private static int RankOf(Cell cell) =>
        cell.Tag switch
        {
            CellTag.Reference => 0,
            CellTag.Float => 1,
            CellTag.Integer or CellTag.BigInteger => 2,
            CellTag.String => 3,
            CellTag.Atom => 4,
            _ => 5,
        };
}
