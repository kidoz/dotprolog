using Prolog.Runtime;
using Prolog.Syntax;

namespace Prolog.Compiler;

/// <summary>
/// Lowers one clause to bytecode.
/// </summary>
/// <remarks>
/// Every clause variable — named or temporary — gets an environment slot holding a heap cell, so the
/// engine never has to distinguish stack variables from heap variables. Argument registers are used
/// only to pass arguments across a call. That costs one environment per call, including for facts,
/// and is the first thing to revisit when the engine is benchmarked.
/// </remarks>
internal sealed class ClauseCompiler
{
    private readonly BytecodeProgram _program;
    private readonly ConstantPool _constants;
    private readonly List<Diagnostic> _diagnostics;
    private readonly string? _fileName;
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);
    private int _slotCount;
    private bool _failed;

    /// <summary>Environment slot holding the barrier a <c>!</c> should cut to, or -1 for the clause barrier.</summary>
    private int _cutSlot = -1;

    internal ClauseCompiler(BytecodeProgram program, ConstantPool constants, List<Diagnostic> diagnostics, string? fileName)
    {
        _program = program;
        _constants = constants;
        _diagnostics = diagnostics;
        _fileName = fileName;
    }

    /// <summary>
    /// Emits <paramref name="head"/> and <paramref name="body"/> and returns the address of the
    /// clause's first instruction, or -1 if the clause could not be compiled.
    /// </summary>
    internal int Compile(SyntaxTerm head, SyntaxTerm? body)
    {
        head = TermNormalizer.Normalize(head);
        body = body is null ? null : TermNormalizer.Normalize(body);

        if (head is not (AtomTerm or CompoundTerm))
        {
            Report(CompilerDiagnosticIds.InvalidClauseHead, "A clause head must be an atom or a compound term.", head.Span);
            return -1;
        }

        int start = _program.Emit(OpCode.Allocate, 0);
        int slotCountOperand = start + 1;

        CompileHead(head);

        if (body is null)
        {
            _program.Emit(OpCode.Deallocate);
            _program.Emit(OpCode.Proceed);
        }
        else
        {
            CompileSequence(body, isTail: true);
        }

        _program.Patch(slotCountOperand, _slotCount);
        return _failed ? -1 : start;
    }

    /// <summary>
    /// Compiles a conjunction of goals. When <paramref name="isTail"/> is set, the last goal returns
    /// from the clause; otherwise control falls through to whatever follows.
    /// </summary>
    private void CompileSequence(SyntaxTerm body, bool isTail)
    {
        List<SyntaxTerm> goals = [];
        FlattenConjunction(body, goals);

        if (goals.Count == 0)
        {
            if (isTail)
            {
                _program.Emit(OpCode.Deallocate);
                _program.Emit(OpCode.Proceed);
            }

            return;
        }

        for (int i = 0; i < goals.Count; i++)
        {
            CompileGoal(goals[i], isLast: isTail && i == goals.Count - 1);
        }
    }

    private void CompileHead(SyntaxTerm head)
    {
        if (head is not CompoundTerm compound)
        {
            return;
        }

        if (!CheckArity(compound.Arity, compound.Span))
        {
            return;
        }

        List<(int Slot, CompoundTerm Term)> deferred = [];

        for (int i = 0; i < compound.Arity; i++)
        {
            SyntaxTerm argument = compound.Arguments[i];
            switch (argument)
            {
                case VariableTerm variable:
                {
                    bool isFirst = ResolveSlot(variable, out int slot);
                    _program.Emit(isFirst ? OpCode.GetVariable : OpCode.GetValue, slot, i);
                    break;
                }

                case CompoundTerm nested:
                    _program.Emit(OpCode.GetStructureArgument, FunctorOf(nested), i);
                    EmitStructureArguments(nested, deferred);
                    break;

                default:
                    _program.Emit(OpCode.GetConstant, ConstantIndexOf(argument), i);
                    break;
            }
        }

        // Nested structures are matched breadth-first; the list grows while it is being walked.
        for (int i = 0; i < deferred.Count; i++)
        {
            (int slot, CompoundTerm term) = deferred[i];
            _program.Emit(OpCode.GetStructureSlot, FunctorOf(term), slot);
            EmitStructureArguments(term, deferred);
        }
    }

    private void EmitStructureArguments(CompoundTerm term, List<(int Slot, CompoundTerm Term)> deferred)
    {
        if (!CheckArity(term.Arity, term.Span))
        {
            return;
        }

        foreach (SyntaxTerm argument in term.Arguments)
        {
            switch (argument)
            {
                case VariableTerm variable:
                {
                    bool isFirst = ResolveSlot(variable, out int slot);
                    _program.Emit(isFirst ? OpCode.UnifyVariable : OpCode.UnifyValue, slot);
                    break;
                }

                case CompoundTerm nested:
                {
                    int slot = AllocateTemporary();
                    _program.Emit(OpCode.UnifyVariable, slot);
                    deferred.Add((slot, nested));
                    break;
                }

                default:
                    _program.Emit(OpCode.UnifyConstant, ConstantIndexOf(argument));
                    break;
            }
        }
    }

    private void CompileGoal(SyntaxTerm goal, bool isLast)
    {
        if (goal is AtomTerm { Name: "!" })
        {
            // A cut inside the condition of if-then-else is local to that condition; everywhere else
            // it prunes the whole clause.
            if (_cutSlot < 0)
            {
                _program.Emit(OpCode.Cut);
            }
            else
            {
                _program.Emit(OpCode.CutTo, _cutSlot);
            }

            EmitReturnIfLast(isLast);
            return;
        }

        // A variable goal is a meta-call: 'p :- X' means the same as 'p :- call(X)'.
        if (goal is VariableTerm variableGoal)
        {
            EmitMetaCall(variableGoal, isLast);
            return;
        }

        if (goal is not (AtomTerm or CompoundTerm))
        {
            Report(CompilerDiagnosticIds.UnsupportedGoal, "A goal must be a callable term.", goal.Span);
            return;
        }

        string name = goal is CompoundTerm compound ? compound.Name : ((AtomTerm)goal).Name;
        int arity = goal is CompoundTerm withArguments ? withArguments.Arity : 0;

        if (goal is CompoundTerm control && CompileControlConstruct(control, name, isLast))
        {
            return;
        }

        if (!CheckArity(arity, goal.Span))
        {
            return;
        }

        if (goal is CompoundTerm callable)
        {
            EmitArguments(callable);
        }

        int functorId = _program.Symbols.InternFunctor(name, arity);

        if (_program.Builtins.TryGetId(functorId, out int builtinId))
        {
            _program.Emit(OpCode.CallBuiltin, builtinId, arity);
            EmitReturnIfLast(isLast);
            return;
        }

        if (isLast)
        {
            // Last-call optimisation: drop the environment before jumping so tail recursion is flat.
            _program.Emit(OpCode.Deallocate);
            _program.Emit(OpCode.Execute, functorId, arity);
            return;
        }

        _program.Emit(OpCode.Call, functorId, arity);
    }

    /// <summary>
    /// Compiles <c>;/2</c>, <c>-&gt;/2</c>, <c>*-&gt;/2</c>, <c>\+/1</c>, and <c>call/1</c> in place
    /// rather than as calls, and reports whether it did. Compiling them inline is what gives cut its
    /// correct scope: the condition of if-then-else gets its own barrier, while the branches stay
    /// transparent to a cut that should prune the whole clause.
    /// </summary>
    private bool CompileControlConstruct(CompoundTerm goal, string name, bool isLast)
    {
        switch (name)
        {
            case "," when goal.Arity == 2:
                CompileSequence(goal, isLast);
                return true;

            case "call" when goal.Arity == 1:
                EmitMetaCall(goal.Arguments[0], isLast);
                return true;

            case ";" when goal.Arity == 2:
                CreateVariablesBeforeBranching(goal);
                CompileDisjunction(goal.Arguments[0], goal.Arguments[1], isLast);
                return true;

            case "->" when goal.Arity == 2:
                // A bare if-then fails when its condition fails.
                CreateVariablesBeforeBranching(goal);
                CompileIfThenElse(goal.Arguments[0], goal.Arguments[1], elseGoal: null, soft: false, isLast);
                return true;

            case "*->" when goal.Arity == 2:
                // A bare soft-cut if-then is just a conjunction: every solution of the condition is kept.
                CompileSequence(new CompoundTerm(",", [goal.Arguments[0], goal.Arguments[1]], goal.Span), isLast);
                return true;

            case "\\+" when goal.Arity == 1:
                CreateVariablesBeforeBranching(goal);
                CompileNegation(goal.Arguments[0], isLast);
                return true;

            default:
                return false;
        }
    }

    private void CompileDisjunction(SyntaxTerm left, SyntaxTerm right, bool isLast)
    {
        // '(C -> T ; E)' and '(C *-> T ; E)' are single constructs, not a disjunction of two goals.
        if (left is CompoundTerm { Name: "->", Arity: 2 } ifThen)
        {
            CompileIfThenElse(ifThen.Arguments[0], ifThen.Arguments[1], right, soft: false, isLast);
            return;
        }

        if (left is CompoundTerm { Name: "*->", Arity: 2 } softIfThen)
        {
            CompileIfThenElse(softIfThen.Arguments[0], softIfThen.Arguments[1], right, soft: true, isLast);
            return;
        }

        int slot = AllocateTemporary();
        int alternative = _program.Emit(OpCode.TryBranch, slot, 0) + 2;

        CompileSequence(left, isTail: false);
        int end = _program.Emit(OpCode.Jump, 0) + 1;

        _program.Patch(alternative, _program.CodeLength);
        _program.Emit(OpCode.TrustMe);
        CompileSequence(right, isTail: false);

        _program.Patch(end, _program.CodeLength);
        EmitReturnIfLast(isLast);
    }

    private void CompileIfThenElse(SyntaxTerm condition, SyntaxTerm then, SyntaxTerm? elseGoal, bool soft, bool isLast)
    {
        int slot = AllocateTemporary();
        int conditionSlot = AllocateTemporary();
        int alternative = _program.Emit(OpCode.TryBranch, slot, 0) + 2;
        _program.Emit(OpCode.MarkBarrier, conditionSlot);

        CompileCondition(condition, conditionSlot);

        // A hard commit discards the condition's own alternatives; a soft cut keeps them and only
        // removes the branch to the else goal.
        _program.Emit(soft ? OpCode.SoftCut : OpCode.CutTo, slot);
        CompileSequence(then, isTail: false);
        int end = _program.Emit(OpCode.Jump, 0) + 1;

        _program.Patch(alternative, _program.CodeLength);
        _program.Emit(OpCode.TrustMe);

        if (elseGoal is null)
        {
            _program.Emit(OpCode.Fail);
        }
        else
        {
            CompileSequence(elseGoal, isTail: false);
        }

        _program.Patch(end, _program.CodeLength);
        EmitReturnIfLast(isLast);
    }

    private void CompileNegation(SyntaxTerm goal, bool isLast)
    {
        int slot = AllocateTemporary();
        int goalSlot = AllocateTemporary();
        int alternative = _program.Emit(OpCode.TryBranch, slot, 0) + 2;
        _program.Emit(OpCode.MarkBarrier, goalSlot);

        CompileCondition(goal, goalSlot);

        // The goal succeeded, so '\+ Goal' fails — and backtracking undoes the goal's bindings.
        _program.Emit(OpCode.CutTo, slot);
        _program.Emit(OpCode.Fail);

        _program.Patch(alternative, _program.CodeLength);
        _program.Emit(OpCode.TrustMe);
        EmitReturnIfLast(isLast);
    }

    /// <summary>
    /// Creates every variable <paramref name="construct"/> mentions for the first time, before the
    /// construct pushes its choice point.
    /// </summary>
    /// <remarks>
    /// A variable created inside a branch lives above that branch's choice point, so backtracking
    /// into a later branch truncates the heap out from under the environment slot still naming it.
    /// Creating it first also makes bindings to it older than the choice point, which is what gets
    /// them trailed and therefore undone.
    /// </remarks>
    private void CreateVariablesBeforeBranching(SyntaxTerm construct)
    {
        List<SyntaxTerm> work = [construct];

        while (work.Count > 0)
        {
            SyntaxTerm term = work[^1];
            work.RemoveAt(work.Count - 1);

            switch (term)
            {
                // An anonymous variable is never read back, so each occurrence can stay branch-local.
                case VariableTerm { IsAnonymous: false } variable when !_slots.ContainsKey(variable.Name):
                {
                    int slot = AllocateTemporary();
                    _slots[variable.Name] = slot;
                    _program.Emit(OpCode.InitVariable, slot);
                    break;
                }

                case CompoundTerm compound:
                    work.AddRange(compound.Arguments);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Compiles a goal whose cut is local to it: the condition of if-then-else, or of negation.</summary>
    private void CompileCondition(SyntaxTerm condition, int slot)
    {
        int enclosing = _cutSlot;
        _cutSlot = slot;
        CompileSequence(condition, isTail: false);
        _cutSlot = enclosing;
    }

    private void EmitMetaCall(SyntaxTerm goal, bool isLast)
    {
        switch (goal)
        {
            case VariableTerm variable:
            {
                bool isFirst = ResolveSlot(variable, out int slot);
                _program.Emit(isFirst ? OpCode.PutVariable : OpCode.PutValue, slot, 0);
                break;
            }

            case CompoundTerm nested:
                BuildStructure(nested, OpCode.PutStructureArgument, 0);
                break;

            default:
                _program.Emit(OpCode.PutConstant, ConstantIndexOf(goal), 0);
                break;
        }

        _program.Emit(OpCode.MetaCall);
        EmitReturnIfLast(isLast);
    }

    private void EmitReturnIfLast(bool isLast)
    {
        if (!isLast)
        {
            return;
        }

        _program.Emit(OpCode.Deallocate);
        _program.Emit(OpCode.Proceed);
    }

    private void EmitArguments(CompoundTerm goal)
    {
        for (int i = 0; i < goal.Arity; i++)
        {
            SyntaxTerm argument = goal.Arguments[i];
            switch (argument)
            {
                case VariableTerm variable:
                {
                    bool isFirst = ResolveSlot(variable, out int slot);
                    _program.Emit(isFirst ? OpCode.PutVariable : OpCode.PutValue, slot, i);
                    break;
                }

                case CompoundTerm nested:
                    BuildStructure(nested, OpCode.PutStructureArgument, i);
                    break;

                default:
                    _program.Emit(OpCode.PutConstant, ConstantIndexOf(argument), i);
                    break;
            }
        }
    }

    private void BuildStructure(CompoundTerm term, OpCode target, int targetIndex)
    {
        if (!CheckArity(term.Arity, term.Span))
        {
            return;
        }

        // Inner structures must exist on the heap before the outer one references them.
        int[] childSlots = new int[term.Arity];
        for (int i = 0; i < term.Arity; i++)
        {
            if (term.Arguments[i] is CompoundTerm nested)
            {
                childSlots[i] = AllocateTemporary();
                BuildStructure(nested, OpCode.PutStructureSlot, childSlots[i]);
            }
            else
            {
                childSlots[i] = -1;
            }
        }

        _program.Emit(target, FunctorOf(term), targetIndex);

        for (int i = 0; i < term.Arity; i++)
        {
            if (childSlots[i] >= 0)
            {
                _program.Emit(OpCode.UnifyValue, childSlots[i]);
                continue;
            }

            switch (term.Arguments[i])
            {
                case VariableTerm variable:
                {
                    bool isFirst = ResolveSlot(variable, out int slot);
                    _program.Emit(isFirst ? OpCode.UnifyVariable : OpCode.UnifyValue, slot);
                    break;
                }

                default:
                    _program.Emit(OpCode.UnifyConstant, ConstantIndexOf(term.Arguments[i]));
                    break;
            }
        }
    }

    private static void FlattenConjunction(SyntaxTerm body, List<SyntaxTerm> goals)
    {
        // Iterative on the right spine: ','/2 is right-associative, so bodies nest arbitrarily deep.
        while (body is CompoundTerm { Name: ",", Arity: 2 } conjunction)
        {
            goals.Add(conjunction.Arguments[0]);
            body = conjunction.Arguments[1];
        }

        if (body is AtomTerm { Name: "true" })
        {
            return;
        }

        goals.Add(body);
    }

    private int FunctorOf(CompoundTerm term) => _program.Symbols.InternFunctor(term.Name, term.Arity);

    private bool ResolveSlot(VariableTerm variable, out int slot)
    {
        if (variable.IsAnonymous)
        {
            slot = AllocateTemporary();
            return true;
        }

        if (_slots.TryGetValue(variable.Name, out slot))
        {
            return false;
        }

        slot = AllocateTemporary();
        _slots[variable.Name] = slot;
        return true;
    }

    private int AllocateTemporary() => _slotCount++;

    private int ConstantIndexOf(SyntaxTerm term)
    {
        switch (term)
        {
            case AtomTerm atom:
                return _constants.IndexOf(Cell.Atom(_program.Symbols.InternAtom(atom.Name)));

            case IntegerTerm integer when Cell.FitsInteger(integer.Value):
                return _constants.IndexOf(Cell.Integer60(integer.Value));

            case IntegerTerm integer:
                Report(
                    CompilerDiagnosticIds.IntegerOutOfRange,
                    $"Integer {integer.Value} does not fit in a term cell; the supported range is {Cell.MinInteger} to {Cell.MaxInteger}.",
                    integer.Span
                );
                return _constants.IndexOf(Cell.Integer60(0));

            case FloatTerm number:
                return _constants.IndexOf(Cell.Float(_program.Symbols.InternFloat(number.Value)));

            default:
                Report(CompilerDiagnosticIds.UnsupportedGoal, $"Cannot lower {term.GetType().Name} to a constant.", term.Span);
                return _constants.IndexOf(Cell.Atom(_program.Symbols.InternAtom("[]")));
        }
    }

    private bool CheckArity(int arity, SourceSpan span)
    {
        if (arity < Machine.ArgumentRegisterCount)
        {
            return true;
        }

        Report(
            CompilerDiagnosticIds.ArityTooLarge,
            $"Arity {arity} exceeds the maximum of {Machine.ArgumentRegisterCount - 1}.",
            span
        );
        return false;
    }

    private void Report(string id, string message, SourceSpan span)
    {
        _failed = true;
        _diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, _fileName));
    }
}
