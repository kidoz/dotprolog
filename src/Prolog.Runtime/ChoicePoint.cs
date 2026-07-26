namespace Prolog.Runtime;

/// <summary>
/// A saved machine state to resume on backtracking. Argument registers are not stored inline;
/// <see cref="ArgumentBase"/> points into the machine's argument-save stack.
/// </summary>
internal struct ChoicePoint
{
    /// <summary>Address of the next clause or retry instruction.</summary>
    public int Alternative;

    /// <summary>Index into the argument-save stack where this frame's saved registers begin.</summary>
    public int ArgumentBase;

    /// <summary>Number of argument registers saved.</summary>
    public int ArgumentCount;

    /// <summary>Heap top to reset to.</summary>
    public int HeapTop;

    /// <summary>Trail top to unwind to.</summary>
    public int TrailTop;

    /// <summary>Environment-stack top to reset to.</summary>
    public int StackTop;

    /// <summary>Current environment to restore.</summary>
    public int Environment;

    /// <summary>Continuation address to restore.</summary>
    public int Continuation;

    /// <summary>Cut barrier to restore.</summary>
    public int CutBarrier;

    /// <summary>
    /// Address of the recovery code when this is a <c>catch/3</c> frame, or -1 when it is an ordinary
    /// choice point. A catch frame is a choice point so that it is discarded on backtracking and
    /// restored on unwinding by the same bookkeeping as everything else.
    /// </summary>
    public int CatchRecovery;

    /// <summary>Environment slot holding the catcher term, for a catch frame.</summary>
    public int CatcherSlot;

    /// <summary>
    /// Whether a catch frame is currently in scope. It is cleared once the guarded goal succeeds, so
    /// a later throw outside <c>catch/3</c> is not caught by it, and set again if execution
    /// backtracks into the goal.
    /// </summary>
    public bool CatchActive;

    /// <summary>Depth of the <c>findall/3</c> collection stack to restore.</summary>
    public int CollectDepth;

    /// <summary>
    /// The next clause to try for a dynamic predicate. Held as a reference rather than an index
    /// because <c>asserta/1</c> can prepend a clause while this choice point is still in flight.
    /// </summary>
    public DynamicClause? NextClause;

    /// <summary>Clause generation the call started at, which fixes the clauses it can see.</summary>
    public int ClauseGeneration;

    /// <summary>Identifier of the builtin to resume, for a nondeterministic native predicate.</summary>
    public int BuiltinId;

    /// <summary>Address to continue at once that builtin yields another solution.</summary>
    public int BuiltinResume;

    /// <summary>Opaque state the builtin carries from one solution to the next.</summary>
    public long BuiltinState;
}
