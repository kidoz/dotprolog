namespace DotProlog.Runtime;

/// <summary>
/// A predicate whose clauses can change while the program runs, through <c>assertz/1</c>,
/// <c>asserta/1</c>, <c>retract/1</c>, or a <c>:- dynamic</c> declaration.
/// </summary>
/// <remarks>
/// Static predicates keep the try/retry/trust chain the clause compiler emits, which is faster and
/// unchanged. A dynamic predicate instead has a one-instruction trampoline that walks this list, so
/// only predicates that actually need mutability pay for it.
/// </remarks>
internal sealed class DynamicPredicate
{
    /// <summary>The predicate's functor identifier.</summary>
    internal int FunctorId { get; init; }

    /// <summary>Address of the trampoline that callers jump to.</summary>
    internal int TrampolineAddress { get; init; }

    /// <summary>The predicate's arity, cached so dispatch need not consult the symbol table.</summary>
    internal int Arity { get; init; }

    /// <summary>First clause in declaration order.</summary>
    internal DynamicClause? First { get; private set; }

    private DynamicClause? Last { get; set; }

    /// <summary>Adds a clause after the existing ones, as <c>assertz/1</c> does.</summary>
    internal void Append(DynamicClause clause)
    {
        if (Last is null)
        {
            First = clause;
            Last = clause;
            return;
        }

        Last.Next = clause;
        Last = clause;
    }

    /// <summary>Adds a clause before the existing ones, as <c>asserta/1</c> does.</summary>
    internal void Prepend(DynamicClause clause)
    {
        clause.Next = First;
        First = clause;
        Last ??= clause;
    }

    /// <summary>Marks every current clause dead at <paramref name="generation"/>.</summary>
    internal int Abolish(int generation)
    {
        var count = 0;
        for (DynamicClause? clause = First; clause is not null; clause = clause.Next)
        {
            if (clause.Death == int.MaxValue)
            {
                clause.Death = generation;
                count++;
            }
        }
        return count;
    }

    // No surviving call can see a clause that died at or before the oldest call generation.
    internal int Reclaim(BytecodeProgram program, int oldestGeneration)
    {
        var count = 0;
        DynamicClause? previous = null;
        var clause = First;
        while (clause is not null)
        {
            var next = clause.Next;
            if (clause.Death != int.MaxValue && clause.Death <= oldestGeneration)
            {
                if (previous is null)
                    First = next;
                else
                    previous.Next = next;
                if (ReferenceEquals(Last, clause))
                    Last = previous;
                program.ReleaseDynamicCode(clause.CodeStorage);
                clause.Next = null;
                count++;
            }
            else
                previous = clause;
            clause = next;
        }
        return count;
    }

    /// <summary>
    /// Returns the first clause at or after <paramref name="from"/> that a goal started at
    /// <paramref name="generation"/> can see.
    /// </summary>
    internal static DynamicClause? FirstVisible(DynamicClause? from, int generation)
    {
        for (DynamicClause? clause = from; clause is not null; clause = clause.Next)
        {
            if (clause.IsVisibleAt(generation))
            {
                return clause;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first clause at or after <paramref name="from"/> that a goal started at
    /// <paramref name="generation"/> can see and whose first-argument key can match
    /// <paramref name="callKey"/>.
    /// </summary>
    internal static DynamicClause? FirstVisibleMatching(DynamicClause? from, int generation, Cell callKey)
    {
        for (DynamicClause? clause = from; clause is not null; clause = clause.Next)
        {
            if (clause.IsVisibleAt(generation) && ClauseIndexing.Matches(clause.IndexKey, callKey))
            {
                return clause;
            }
        }

        return null;
    }
}
