namespace DotProlog.Runtime;

/// <summary>
/// Predicates the engine defines directly in bytecode, because they need instructions no clause
/// compiler emits from ordinary Prolog source.
/// </summary>
internal static class ControlPredicates
{
    /// <summary>Environment slot holding <c>catch/3</c>'s catcher. The unwinder reads it by this index.</summary>
    private const int CatcherSlot = 1;

    /// <summary>Environment slot holding the choice-point index the catch frame occupies.</summary>
    private const int FrameIndexSlot = 3;

    /// <summary>Emits <c>catch/3</c> into <paramref name="program"/>.</summary>
    /// <remarks>
    /// <code>
    /// catch(Goal, Catcher, Recovery)
    ///     Allocate 4                   Y0 = Goal, Y1 = Catcher, Y2 = Recovery, Y3 = frame index
    ///     MarkBarrier Y3               the index the catch frame is about to occupy
    ///     PushCatch Y1, recovery       frame fails through on ordinary backtracking
    ///     call Goal
    ///     PopCatch Y3, reactivate      take the frame out of scope now Goal has succeeded
    ///     Deallocate / Proceed
    /// recovery:
    ///     call Recovery                reached only when a ball unified with Y1
    ///     Deallocate / Proceed
    /// reactivate:
    ///     TrustMe / ReactivateCatch Y3 / Fail
    /// </code>
    /// <para>
    /// The frame must stop applying once Goal succeeds, or a throw from a later goal entirely outside
    /// the <c>catch/3</c> would be caught by it. It must equally come back into scope if execution
    /// backtracks into Goal, because ISO keeps the catcher active across a redo. <c>PopCatch</c>
    /// handles both: it pops the frame outright when Goal was deterministic, and otherwise
    /// deactivates it behind a choice point that reactivates it on the way back in.
    /// </para>
    /// </remarks>
    internal static void Install(BytecodeProgram program)
    {
        var catchFunctor = program.Symbols.InternFunctor("catch", 3);
        var entry = program.CodeLength;

        program.Emit(OpCode.Allocate, 4);
        program.Emit(OpCode.GetVariable, 0, 0);
        program.Emit(OpCode.GetVariable, CatcherSlot, 1);
        program.Emit(OpCode.GetVariable, 2, 2);
        program.Emit(OpCode.MarkBarrier, FrameIndexSlot);

        var recoveryOperand = program.Emit(OpCode.PushCatch, CatcherSlot, 0) + 2;

        program.Emit(OpCode.PutValue, 0, 0);
        program.Emit(OpCode.MetaCall);
        var reactivateOperand = program.Emit(OpCode.PopCatch, FrameIndexSlot, 0) + 2;
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        program.Patch(recoveryOperand, program.CodeLength);
        program.Emit(OpCode.PutValue, 2, 0);
        program.Emit(OpCode.MetaCall);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        program.Patch(reactivateOperand, program.CodeLength);
        program.Emit(OpCode.TrustMe);
        program.Emit(OpCode.ReactivateCatch, FrameIndexSlot);
        program.Emit(OpCode.Fail);

        program.DefinePredicate(catchFunctor, entry, userDefined: false);
    }
}
