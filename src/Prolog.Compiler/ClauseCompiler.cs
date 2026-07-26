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

        List<SyntaxTerm> goals = [];
        if (body is not null)
        {
            FlattenConjunction(body, goals);
        }

        if (goals.Count == 0)
        {
            _program.Emit(OpCode.Deallocate);
            _program.Emit(OpCode.Proceed);
        }
        else
        {
            for (int i = 0; i < goals.Count; i++)
            {
                CompileGoal(goals[i], isLast: i == goals.Count - 1);
            }
        }

        _program.Patch(slotCountOperand, _slotCount);
        return _failed ? -1 : start;
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
            _program.Emit(OpCode.Cut);
            if (isLast)
            {
                _program.Emit(OpCode.Deallocate);
                _program.Emit(OpCode.Proceed);
            }

            return;
        }

        if (goal is VariableTerm)
        {
            Report(
                CompilerDiagnosticIds.UnsupportedGoal,
                "A variable goal requires call/1, which this release does not provide.",
                goal.Span
            );
            return;
        }

        if (goal is not (AtomTerm or CompoundTerm))
        {
            Report(CompilerDiagnosticIds.UnsupportedGoal, "A goal must be a callable term.", goal.Span);
            return;
        }

        string name = goal is CompoundTerm compound ? compound.Name : ((AtomTerm)goal).Name;
        int arity = goal is CompoundTerm withArguments ? withArguments.Arity : 0;

        if (name is ";" or "->" or "*->" or "\\+" && arity is 1 or 2)
        {
            Report(
                CompilerDiagnosticIds.UnsupportedGoal,
                $"The control construct {name}/{arity} is not compiled by this release.",
                goal.Span
            );
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
            if (isLast)
            {
                _program.Emit(OpCode.Deallocate);
                _program.Emit(OpCode.Proceed);
            }

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
