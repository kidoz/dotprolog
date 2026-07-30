namespace DotProlog.Runtime;

/// <summary>
/// The ISO standard order of terms: <c>Variable @&lt; Number @&lt; Atom @&lt; Compound</c>.
/// Numbers compare by value across kinds, and a float precedes an integer it equals. Compound terms
/// compare by arity, then by functor name, then argument by argument.
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

            int rank = RankOf(a).CompareTo(RankOf(b));
            if (rank != 0)
            {
                return rank;
            }

            switch (a.Tag)
            {
                case CellTag.Reference:
                    return a.Index.CompareTo(b.Index);

                case CellTag.Atom:
                {
                    int names = string.CompareOrdinal(machine.Symbols.AtomName(a.Index), machine.Symbols.AtomName(b.Index));
                    if (names != 0)
                    {
                        return names;
                    }

                    continue;
                }

                case CellTag.Integer:
                case CellTag.Float:
                {
                    int numbers = CompareNumbers(machine, a, b);
                    if (numbers != 0)
                    {
                        return numbers;
                    }

                    continue;
                }

                default:
                {
                    ulong pair = ((ulong)(uint)a.Index << 32) | (uint)b.Index;
                    if (!visitedStructures.Add(pair))
                    {
                        continue;
                    }

                    int structures = CompareStructures(machine, a, b, work);
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
        int leftFunctor = machine.HeapAt(a.Index).Index;
        int rightFunctor = machine.HeapAt(b.Index).Index;
        Functor left = machine.Symbols.GetFunctor(leftFunctor);
        Functor right = machine.Symbols.GetFunctor(rightFunctor);

        int arity = left.Arity.CompareTo(right.Arity);
        if (arity != 0)
        {
            return arity;
        }

        int name = string.CompareOrdinal(machine.Symbols.AtomName(left.NameAtom), machine.Symbols.AtomName(right.NameAtom));
        if (name != 0)
        {
            return name;
        }

        // Pushed in reverse so the leftmost argument is compared first.
        for (int i = left.Arity; i >= 1; i--)
        {
            work.Add((machine.HeapAt(a.Index + i), machine.HeapAt(b.Index + i)));
        }

        return 0;
    }

    private static int CompareNumbers(Machine machine, Cell a, Cell b)
    {
        if (a.Tag == b.Tag)
        {
            return a.Tag == CellTag.Integer
                ? a.Integer.CompareTo(b.Integer)
                : machine.Symbols.GetFloat(a.Index).CompareTo(machine.Symbols.GetFloat(b.Index));
        }

        // Mixed kinds compare by value; when the two are arithmetically equal the float precedes.
        if (a.Tag == CellTag.Integer)
        {
            int order = CompareIntegerToFloat(a.Integer, machine.Symbols.GetFloat(b.Index));
            return order != 0 ? order : 1;
        }

        int reversed = CompareIntegerToFloat(b.Integer, machine.Symbols.GetFloat(a.Index));
        return reversed != 0 ? -reversed : -1;
    }

    /// <summary>
    /// Compares exactly: a double above 2^53 does not represent every tagged integer, so the float's
    /// integral part is brought to a long rather than the integer being widened to a double.
    /// </summary>
    private static int CompareIntegerToFloat(long integer, double value)
    {
        if (double.IsNaN(value))
        {
            return 1;
        }

        // 2^63 and beyond exceed every long; an integral double inside the range converts exactly.
        if (value >= 9223372036854775808.0)
        {
            return -1;
        }

        if (value < -9223372036854775808.0)
        {
            return 1;
        }

        double floor = Math.Floor(value);
        long floorInteger = (long)floor;
        if (integer != floorInteger)
        {
            return integer < floorInteger ? -1 : 1;
        }

        return value > floor ? -1 : 0;
    }

    private static int RankOf(Cell cell) =>
        cell.Tag switch
        {
            CellTag.Reference => 0,
            CellTag.Float or CellTag.Integer => 1,
            CellTag.Atom => 2,
            _ => 3,
        };
}
