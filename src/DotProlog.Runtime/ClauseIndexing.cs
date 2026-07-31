namespace DotProlog.Runtime;

/// <summary>
/// First-argument clause indexing: derives a compact key from a clause head's first argument and
/// from a call's first argument, so dispatch can skip clauses that could never unify.
/// </summary>
/// <remarks>
/// A key is an ordinary <see cref="Cell"/>. Atoms, integers, and floats are canonical by their
/// bits — floats because the symbol table interns them by value — and a compound term is keyed by
/// its <see cref="CellTag.Functor"/> cell. A <see cref="CellTag.Reference"/> cell is the "matches
/// anything" key: it never occurs as a derived constant key, and an unbound argument constrains
/// nothing. Filtering is conservative: a skipped clause is one head unification would provably
/// reject, so solutions and their order are unchanged.
/// </remarks>
internal static class ClauseIndexing
{
    /// <summary>The key that matches every call: an unbound or unknown first argument.</summary>
    internal static Cell AnyKey => Cell.Reference(0);

    /// <summary>Whether a clause with <paramref name="clauseKey"/> can match <paramref name="callKey"/>.</summary>
    internal static bool Matches(Cell clauseKey, Cell callKey) =>
        clauseKey.IsReference || callKey.IsReference || clauseKey == callKey;

    /// <summary>
    /// Returns the first index at or after <paramref name="from"/> whose key can match
    /// <paramref name="callKey"/>, or -1 when none can.
    /// </summary>
    internal static int NextMatch(Cell[] keys, int from, Cell callKey)
    {
        for (int i = from; i < keys.Length; i++)
        {
            if (Matches(keys[i], callKey))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Derives the call key from a dereferenced first argument on the machine heap.</summary>
    internal static Cell CallKey(Machine machine, Cell argument)
    {
        Cell cell = machine.Dereference(argument);
        return cell.Tag switch
        {
            CellTag.Reference => AnyKey,
            CellTag.Structure => machine.HeapAt(cell.Index),
            _ => cell,
        };
    }

    /// <summary>
    /// Derives the clause key from a stored clause term, which is either a bare head or a
    /// <c>Head :- Body</c> structure identified by <paramref name="ruleFunctorId"/>. The cells use
    /// the buffer-relative layout <see cref="TermBuffer"/> produces. The head must actually be
    /// <paramref name="headFunctorId"/> — a stored source form that the compiler transformed, such
    /// as a grammar rule, keys as matching everything instead.
    /// </summary>
    internal static Cell ClauseKeyFromBuffer(ReadOnlySpan<Cell> cells, int root, int ruleFunctorId, int headFunctorId)
    {
        Cell clause = DereferenceBuffer(cells, cells[root]);
        Cell head = clause;
        if (clause.Tag == CellTag.Structure && cells[clause.Index].Index == ruleFunctorId)
        {
            head = DereferenceBuffer(cells, cells[clause.Index + 1]);
        }

        if (head.Tag != CellTag.Structure || cells[head.Index].Index != headFunctorId)
        {
            return AnyKey;
        }

        Cell first = DereferenceBuffer(cells, cells[head.Index + 1]);
        return first.Tag switch
        {
            CellTag.Reference => AnyKey,
            CellTag.Structure => cells[first.Index],
            _ => first,
        };
    }

    /// <summary>
    /// Derives the clause key from a live clause term on the machine heap. The head must actually
    /// be <paramref name="headFunctorId"/> — a source form that the compiler transformed, such as
    /// a grammar rule, keys as matching everything instead.
    /// </summary>
    internal static Cell ClauseKeyFromHeap(Machine machine, Cell clause, int ruleFunctorId, int headFunctorId)
    {
        Cell term = machine.Dereference(clause);
        Cell head = term;
        if (term.Tag == CellTag.Structure && machine.HeapAt(term.Index).Index == ruleFunctorId)
        {
            head = machine.Dereference(machine.HeapAt(term.Index + 1));
        }

        if (head.Tag != CellTag.Structure || machine.HeapAt(head.Index).Index != headFunctorId)
        {
            return AnyKey;
        }

        return CallKey(machine, machine.HeapAt(head.Index + 1));
    }

    private static Cell DereferenceBuffer(ReadOnlySpan<Cell> cells, Cell cell)
    {
        while (cell.Tag == CellTag.Reference)
        {
            Cell target = cells[cell.Index];
            if (target.Tag == CellTag.Reference && target.Index == cell.Index)
            {
                return cell;
            }

            cell = target;
        }

        return cell;
    }
}
