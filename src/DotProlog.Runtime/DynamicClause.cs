namespace DotProlog.Runtime;

/// <summary>
/// One clause of a dynamic predicate: its compiled code, the term it was asserted from, and the
/// generations between which it is visible.
/// </summary>
/// <remarks>
/// Clauses form a singly-linked list rather than an array so that a choice point can hold the next
/// clause directly. An index would be invalidated by <c>asserta/1</c> prepending during iteration;
/// a reference is not.
/// </remarks>
internal sealed class DynamicClause
{
    /// <summary>Address of the clause's compiled code.</summary>
    internal int CodeAddress { get; init; }

    /// <summary>The clause as a <c>Head :- Body</c> term, kept for <c>retract/1</c> to match against.</summary>
    internal TermBuffer Term { get; init; } = new();

    /// <summary>Slot of the clause term's root inside <see cref="Term"/>.</summary>
    internal int TermRoot { get; init; }

    /// <summary>Generation at which the clause became visible.</summary>
    internal int Birth { get; init; }

    /// <summary>
    /// First-argument index key, derived when the clause is stored. Defaults to the key that
    /// matches every call, which is always correct and merely unoptimised.
    /// </summary>
    internal Cell IndexKey { get; init; } = ClauseIndexing.AnyKey;

    /// <summary>Generation at which the clause stopped being visible; <see cref="int.MaxValue"/> while alive.</summary>
    internal int Death { get; set; } = int.MaxValue;

    /// <summary>The next clause of the same predicate.</summary>
    internal DynamicClause? Next { get; set; }

    /// <summary>Whether this clause is visible to a goal that started at <paramref name="generation"/>.</summary>
    internal bool IsVisibleAt(int generation) => Birth <= generation && generation < Death;
}
