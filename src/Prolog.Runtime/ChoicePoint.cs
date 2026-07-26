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
}
