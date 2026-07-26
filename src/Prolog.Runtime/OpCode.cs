namespace Prolog.Runtime;

/// <summary>
/// The bytecode instruction set executed by <see cref="Machine"/>. Instructions are a WAM-derived
/// set specialised for this engine: every clause variable lives on the heap and is addressed through
/// an environment slot (<c>Y</c>), while <c>X</c> registers are used only to pass arguments.
/// </summary>
public enum OpCode
{
    /// <summary>Return control to the host with the current goal proved. Occupies address zero.</summary>
    Stop = 0,

    /// <summary>Push an environment with the operand's number of variable slots.</summary>
    Allocate,

    /// <summary>Pop the current environment, restoring the saved continuation.</summary>
    Deallocate,

    /// <summary>Operands: functor identifier, arity. Save the continuation and jump to the predicate.</summary>
    Call,

    /// <summary>Operands: functor identifier, arity. Jump to the predicate without saving a continuation.</summary>
    Execute,

    /// <summary>Operands: builtin identifier, arity. Invoke a deterministic native predicate in place.</summary>
    CallBuiltin,

    /// <summary>Return to the saved continuation.</summary>
    Proceed,

    /// <summary>Discard every choice point created since the current predicate was entered.</summary>
    Cut,

    /// <summary>
    /// Operands: slot, address. Record the current choice-point depth in the slot, then push a
    /// choice point whose alternative is the address. Used for control constructs inside a clause
    /// body, which need their own barrier without disturbing the argument registers.
    /// </summary>
    TryBranch,

    /// <summary>
    /// Operand: slot. Record the current choice-point depth in the slot without pushing anything.
    /// A cut inside the condition of if-then-else or negation cuts to this, so it prunes only the
    /// choice points the condition itself created.
    /// </summary>
    MarkBarrier,

    /// <summary>Operand: address. Continue execution at the address.</summary>
    Jump,

    /// <summary>Operand: slot. Discard every choice point created since the branch recorded in the slot.</summary>
    CutTo,

    /// <summary>
    /// Operand: slot. Neutralise just the choice point recorded in the slot, leaving any created
    /// since it intact. This is the soft cut behind <c>*-&gt;/2</c>.
    /// </summary>
    SoftCut,

    /// <summary>
    /// Call the goal held in argument register zero, resolving its functor at run time. This is
    /// <c>call/1</c>; it dispatches to a builtin in place, or jumps to a predicate as <see cref="Call"/> does.
    /// </summary>
    MetaCall,

    /// <summary>
    /// Operands: catcher slot, recovery address. Push a <c>catch/3</c> frame. On ordinary
    /// backtracking the frame simply pops; a thrown ball that unifies with the catcher resumes at the
    /// recovery address.
    /// </summary>
    PushCatch,

    /// <summary>
    /// Operands: frame-index slot, reactivation address. Take the catch frame out of scope now that
    /// its goal has succeeded. If the frame is the newest choice point it is simply popped; otherwise
    /// the goal left alternatives, so the frame is deactivated and a choice point is pushed that
    /// reactivates it should execution backtrack into the goal.
    /// </summary>
    PopCatch,

    /// <summary>Operand: frame-index slot. Bring a deactivated catch frame back into scope.</summary>
    ReactivateCatch,

    /// <summary>Operand: address. Push a choice point whose alternative is the operand.</summary>
    TryMeElse,

    /// <summary>Operand: address. Retarget the current choice point's alternative to the operand.</summary>
    RetryMeElse,

    /// <summary>Pop the current choice point; the clause that follows is the last alternative.</summary>
    TrustMe,

    /// <summary>Operands: slot, argument register. Copy the argument into a fresh environment slot.</summary>
    GetVariable,

    /// <summary>Operands: slot, argument register. Unify the argument with a bound environment slot.</summary>
    GetValue,

    /// <summary>Operands: constant index, argument register. Unify the argument with a constant.</summary>
    GetConstant,

    /// <summary>Operands: functor identifier, argument register. Match or build a compound term.</summary>
    GetStructureArgument,

    /// <summary>Operands: functor identifier, slot. Match or build a compound term held in a slot.</summary>
    GetStructureSlot,

    /// <summary>Operand: slot. Read or write the next argument of the current structure.</summary>
    UnifyVariable,

    /// <summary>Operand: slot. Unify or emit the next argument of the current structure.</summary>
    UnifyValue,

    /// <summary>Operand: constant index. Match or emit the next argument of the current structure.</summary>
    UnifyConstant,

    /// <summary>Operands: slot, argument register. Create a fresh variable in both.</summary>
    PutVariable,

    /// <summary>
    /// Operand: slot. Create a fresh variable in the slot alone. Used to bring a variable into
    /// existence before a control construct's choice point, so that backtracking into a later branch
    /// cannot discard the heap cell the slot refers to.
    /// </summary>
    InitVariable,

    /// <summary>Operands: slot, argument register. Copy a slot into an argument register.</summary>
    PutValue,

    /// <summary>Operands: constant index, argument register. Load a constant into an argument register.</summary>
    PutConstant,

    /// <summary>Operands: functor identifier, argument register. Begin building a structure in a register.</summary>
    PutStructureArgument,

    /// <summary>Operands: functor identifier, slot. Begin building a structure in an environment slot.</summary>
    PutStructureSlot,

    /// <summary>Fail unconditionally, forcing backtracking.</summary>
    Fail,
}
