using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

/// <summary>
/// Exercises the dispatch loop against hand-assembled bytecode, so a failure here points at the
/// engine rather than at the reader or the clause compiler.
/// </summary>
public sealed class MachineTests
{
    [Theory]
    [InlineData(1, false, RunResult.Success)]
    [InlineData(1, true, RunResult.Success)]
    [InlineData(3, false, RunResult.Failure)]
    [InlineData(3, true, RunResult.Failure)]
    public void BoundCompiledIndexDoesNotResolveUnusedFallbackTargets(int argument, bool aliased, RunResult expected)
    {
        var program = new BytecodeProgram();
        var first = program.Emit(OpCode.GetConstant, program.AddConstant(Cell.Integer60(1)), 0);
        program.Emit(OpCode.Stop);
        var last = program.Emit(OpCode.GetConstant, program.AddConstant(Cell.Integer60(2)), 0);
        program.Emit(OpCode.Stop);
        var index = program.AddStaticIndex([first, last], [Cell.Integer60(1), Cell.Integer60(2)]);
        // Deliberately omit fallback targets: a bound hit or miss must never read them.
        var compiled = new CompiledProgram([], [], [], 0, [index]);
        var entry = program.RegisterCompiledBlock(
            static (ref Machine.CompiledExecution execution, CompiledProgram symbols) =>
                execution.EnterStatic(symbols.StaticIndex(0), symbols, 0, 1),
            compiled
        );
        var predicate = program.Symbols.InternFunctor("indexed", 1);
        program.DefinePredicate(predicate, entry);
        using var machine = new Machine(program);
        machine.BeginCall();
        var value = Cell.Integer60(argument);
        if (aliased)
        {
            value = machine.CreateVariable();
            Assert.True(machine.Unify(value, Cell.Integer60(argument)));
        }
        Assert.Equal(expected, machine.Call(predicate, [value]));
        Assert.False(machine.HasAlternatives);
    }

    [Fact]
    public void UnboundCompiledIndexResolvesRelocatedTargetsAndRestoresArgumentsOnRedo()
    {
        var program = new BytecodeProgram();
        var compiled = new CompiledProgram([], [], [], 3, [0]);
        var first = program.RegisterCompiledBlock(
            static (ref Machine.CompiledExecution execution, CompiledProgram _) =>
                execution.GetConstant(Cell.Integer60(1), 0, BytecodeProgram.TopLevelReturnAddress),
            compiled
        );
        var last = program.Emit(OpCode.GetConstant, program.AddConstant(Cell.Integer60(2)), 0);
        program.Emit(OpCode.Stop);
        Assert.Equal(0, program.AddStaticIndex([first, last], [Cell.Integer60(1), Cell.Integer60(2)]));
        var alternative = program.RegisterCompiledBlock(
            static (ref Machine.CompiledExecution execution, CompiledProgram symbols) => execution.TrustMe(symbols.Target(2)),
            compiled
        );
        compiled.SetTarget(0, first);
        compiled.SetTarget(1, alternative);
        compiled.SetTarget(2, last);
        var entry = program.RegisterCompiledBlock(
            static (ref Machine.CompiledExecution execution, CompiledProgram symbols) =>
                execution.EnterStatic(symbols.StaticIndex(0), symbols, 0, 1),
            compiled
        );
        var predicate = program.Symbols.InternFunctor("indexed", 1);
        program.DefinePredicate(predicate, entry);
        using var machine = new Machine(program);
        machine.BeginCall();
        Assert.Equal(RunResult.Success, machine.Call(predicate, [machine.CreateVariable()]));
        Assert.Equal(Cell.Integer60(1), machine.Argument(0));
        Assert.True(machine.HasAlternatives);
        Assert.Equal(RunResult.Success, machine.Redo());
        Assert.Equal(Cell.Integer60(2), machine.Argument(0));
        Assert.False(machine.HasAlternatives);
        Assert.Equal(RunResult.Failure, machine.Redo());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedRedoRestoresArgumentsAcrossCompiledAndBytecodeClauses(bool boundArgument)
    {
        var program = new BytecodeProgram();
        var compiled = new CompiledProgram([], [], [], 0);
        var one = program.RegisterCompiledBlock(
            static (ref Machine.CompiledExecution execution, CompiledProgram _) =>
                execution.GetConstant(Cell.Integer60(1), 0, BytecodeProgram.TopLevelReturnAddress),
            compiled
        );
        var two = program.Emit(OpCode.GetConstant, program.AddConstant(Cell.Integer60(2)), 0);
        program.Emit(OpCode.Stop);
        var index = program.AddStaticIndex([one, two, one], [Cell.Integer60(1), Cell.Integer60(2), Cell.Integer60(1)]);
        var predicate = program.Symbols.InternFunctor("indexed", 1);
        program.DefinePredicate(predicate, program.Emit(OpCode.EnterStatic, index));

        using var machine = new Machine(program);
        machine.BeginCall();
        var argument = boundArgument ? Cell.Integer60(1) : machine.CreateVariable();
        Assert.Equal(RunResult.Success, machine.Call(predicate, [argument]));
        Assert.Equal(Cell.Integer60(1), machine.Argument(0));
        Assert.True(machine.HasAlternatives);
        if (!boundArgument)
        {
            Assert.Equal(RunResult.Success, machine.Redo());
            Assert.Equal(Cell.Integer60(2), machine.Argument(0));
            Assert.True(machine.HasAlternatives);
        }
        Assert.Equal(RunResult.Success, machine.Redo());
        Assert.Equal(Cell.Integer60(1), machine.Argument(0));
        Assert.False(machine.HasAlternatives);
        Assert.Equal(RunResult.Failure, machine.Redo());
    }

    private static BytecodeProgram NewProgram()
    {
        var program = new BytecodeProgram();
        CoreBuiltins.RegisterAll(program);
        return program;
    }

    private static int BuiltinId(BytecodeProgram program, string name, int arity)
    {
        Assert.True(program.Builtins.TryGetId(program.Symbols.InternFunctor(name, arity), out var id));
        return id;
    }

    [Fact]
    public void ProvesAFactAndReturnsToTheHost()
    {
        var program = NewProgram();
        var p = program.Symbols.InternFunctor("p", 0);

        program.DefinePredicate(p, program.CodeLength);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        using var machine = new Machine(program);
        Assert.Equal(RunResult.Success, machine.Solve(p));
    }

    [Fact]
    public void BacktracksFromAFailedClauseIntoTheNextAlternative()
    {
        var program = NewProgram();
        var p = program.Symbols.InternFunctor("p", 0);
        var fail = BuiltinId(program, "fail", 0);

        program.DefinePredicate(p, program.CodeLength);
        var alternative = program.Emit(OpCode.TryMeElse, 0) + 1;
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.CallBuiltin, fail, 0);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        program.Patch(alternative, program.CodeLength);
        program.Emit(OpCode.TrustMe);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        using var machine = new Machine(program);
        Assert.Equal(RunResult.Success, machine.Solve(p));
    }

    [Fact]
    public void FailsWhenEveryAlternativeIsExhausted()
    {
        var program = NewProgram();
        var p = program.Symbols.InternFunctor("p", 0);
        var fail = BuiltinId(program, "fail", 0);

        program.DefinePredicate(p, program.CodeLength);
        var alternative = program.Emit(OpCode.TryMeElse, 0) + 1;
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.CallBuiltin, fail, 0);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        program.Patch(alternative, program.CodeLength);
        program.Emit(OpCode.TrustMe);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.CallBuiltin, fail, 0);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        using var machine = new Machine(program);
        Assert.Equal(RunResult.Failure, machine.Solve(p));
    }

    [Fact]
    public void CallReturnsToItsContinuation()
    {
        var program = NewProgram();
        var p = program.Symbols.InternFunctor("p", 0);
        var q = program.Symbols.InternFunctor("q", 0);
        var write = BuiltinId(program, "write", 1);
        var done = program.AddConstant(Cell.Atom(program.Symbols.InternAtom("done")));

        program.DefinePredicate(q, program.CodeLength);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        program.DefinePredicate(p, program.CodeLength);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.Call, q, 0);
        program.Emit(OpCode.PutConstant, done, 0);
        program.Emit(OpCode.CallBuiltin, write, 1);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        var output = new StringWriter();
        using var machine = new Machine(program) { Output = output };

        Assert.Equal(RunResult.Success, machine.Solve(p));
        Assert.Equal("done", output.ToString());
    }

    [Fact]
    public void HaltStopsTheRunAndReportsItsExitCode()
    {
        var program = NewProgram();
        var p = program.Symbols.InternFunctor("p", 0);
        var halt = BuiltinId(program, "halt", 1);
        var seven = program.AddConstant(Cell.Integer60(7));

        program.DefinePredicate(p, program.CodeLength);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.PutConstant, seven, 0);
        program.Emit(OpCode.CallBuiltin, halt, 1);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        using var machine = new Machine(program);

        Assert.Equal(RunResult.Halted, machine.Solve(p));
        Assert.Equal(7, machine.ExitCode);
    }

    [Fact]
    public void CallingAnUndefinedPredicateRaisesAnExistenceError()
    {
        var program = NewProgram();
        var missing = program.Symbols.InternFunctor("missing", 2);

        var exception = Assert.Throws<PrologException>(() => new Machine(program).Solve(missing));

        Assert.Contains("existence_error(procedure, missing/2)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OccursCheckRejectsADirectCycleAndRestoresTheVariable()
    {
        var program = NewProgram();
        using var machine = new Machine(program);
        var variable = machine.CreateVariable();
        var recursive = machine.CreateStructure(program.Symbols.InternFunctor("f", 1), [variable]);

        Assert.False(machine.UnifyWithOccursCheck(variable, recursive));
        Assert.Equal(variable, machine.Dereference(variable));
    }

    [Fact]
    public void OccursCheckRetainsFiniteBindings()
    {
        var program = NewProgram();
        using var machine = new Machine(program);
        var variable = machine.CreateVariable();
        var value = machine.CreateStructure(program.Symbols.InternFunctor("point", 2), [Cell.Integer60(2), Cell.Integer60(3)]);

        Assert.True(machine.UnifyWithOccursCheck(variable, value));
        Assert.Equal(value, machine.Dereference(variable));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OccursCheckHandlesHeapGrowthBeforeAndAfterItsFirstUse(bool checkBeforeGrowth)
    {
        var program = NewProgram();
        using var machine = new Machine(program);
        var functor = program.Symbols.InternFunctor("f", 1);
        var earlyVariable = machine.CreateVariable();
        var earlyTerm = machine.CreateStructure(functor, [earlyVariable]);
        if (checkBeforeGrowth)
        {
            Assert.False(machine.UnifyWithOccursCheck(earlyVariable, earlyTerm));
        }

        // Cross the initial heap capacity before constructing the terms to inspect.
        for (var i = 0; i < 70_000; i++)
        {
            machine.CreateVariable();
        }

        var variable = machine.CreateVariable();
        var recursive = machine.CreateStructure(functor, [variable]);
        Assert.False(machine.UnifyWithOccursCheck(variable, recursive));
        Assert.Equal(variable, machine.Dereference(variable));

        // A later traversal must revisit both old and new heap addresses under a fresh epoch.
        Assert.False(machine.UnifyWithOccursCheck(earlyVariable, earlyTerm));
        Assert.Equal(earlyVariable, machine.Dereference(earlyVariable));
        Assert.True(machine.Unify(variable, recursive));
        var independent = machine.CreateVariable();
        Assert.True(machine.UnifyWithOccursCheck(independent, recursive));
        Assert.Equal(recursive, machine.Dereference(independent));
    }

    [Fact]
    public void FirstHeapReservationCanMaterializeMoreThanTheInitialCapacity()
    {
        using var machine = new Machine(NewProgram());
        var cells = new Cell[70_000];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = Cell.Integer60(i);
        }

        var buffer = TermBuffer.FromCells(cells);
        var origin = buffer.Materialize(machine);
        Assert.Equal(0, origin);
        var variable = machine.CreateVariable();
        Assert.True(machine.Unify(variable, machine.HeapAt(origin + cells.Length - 1)));
        Assert.Equal(cells[0], machine.HeapAt(origin));
        Assert.Equal(cells[^1], machine.Dereference(variable));
    }

    [Fact]
    public void OccursCheckRestoresBindingsAfterALaterMismatch()
    {
        var program = NewProgram();
        using var machine = new Machine(program);
        var variable = machine.CreateVariable();
        var pair = program.Symbols.InternFunctor("pair", 2);
        var left = machine.CreateStructure(pair, [variable, variable]);
        var right = machine.CreateStructure(
            pair,
            [Cell.Atom(program.Symbols.InternAtom("a")), Cell.Atom(program.Symbols.InternAtom("b"))]
        );

        Assert.False(machine.UnifyWithOccursCheck(left, right));
        Assert.Equal(variable, machine.Dereference(variable));
    }

    [Fact]
    public void OccursCheckTerminatesWhenTheCandidateContainsAnExistingCycle()
    {
        var program = NewProgram();
        using var machine = new Machine(program);
        var cyclicVariable = machine.CreateVariable();
        var cyclicTerm = machine.CreateStructure(program.Symbols.InternFunctor("cycle", 1), [cyclicVariable]);
        Assert.True(machine.Unify(cyclicVariable, cyclicTerm));

        var variable = machine.CreateVariable();
        var wrapper = machine.CreateStructure(program.Symbols.InternFunctor("wrapper", 1), [cyclicTerm]);

        Assert.True(machine.UnifyWithOccursCheck(variable, wrapper));
        Assert.Equal(wrapper, machine.Dereference(variable));
    }

    [Fact]
    public void OrdinaryUnificationTerminatesWhenCyclicIntermediateBindingsPrecedeAMismatch()
    {
        var program = NewProgram();
        using var machine = new Machine(program);
        var a = program.Symbols.InternFunctor("a", 1);
        var f = program.Symbols.InternFunctor("f", 4);
        var x = machine.CreateVariable();
        var y = machine.CreateVariable();
        var left = machine.CreateStructure(f, [x, y, x, Cell.Integer60(1)]);
        var right = machine.CreateStructure(
            f,
            [machine.CreateStructure(a, [x]), machine.CreateStructure(a, [y]), y, Cell.Integer60(2)]
        );

        Assert.False(machine.Unify(left, right));
    }

    [Fact]
    public void TermIdentityTerminatesForEquivalentRationalTreesWithDifferentPeriods()
    {
        var program = NewProgram();
        using var machine = new Machine(program);
        var shortTail = machine.CreateVariable();
        var shortCycle = machine.CreateList([Cell.Integer60(1), Cell.Integer60(2), Cell.Integer60(3)], shortTail);
        Assert.True(machine.Unify(shortTail, shortCycle));

        var longTail = machine.CreateVariable();
        var longCycle = machine.CreateList(
            [Cell.Integer60(1), Cell.Integer60(2), Cell.Integer60(3), Cell.Integer60(1), Cell.Integer60(2), Cell.Integer60(3)],
            longTail
        );
        Assert.True(machine.Unify(longTail, longCycle));

        Assert.True(TermOrder.AreIdentical(machine, shortCycle, longCycle));
    }

    [Fact]
    public void ProgramDistinguishesUserPredicatesFromBuiltinsAndInternalBytecode()
    {
        var program = NewProgram();
        var user = program.Symbols.InternFunctor("user_predicate", 0);
        var write = program.Symbols.InternFunctor("write", 1);
        var catchFunctor = program.Symbols.InternFunctor("catch", 3);

        program.DefinePredicate(user, program.CodeLength);

        Assert.True(program.IsUserPredicate(user));
        Assert.False(program.IsUserPredicate(write));
        Assert.False(program.IsUserPredicate(catchFunctor));
    }

    [Fact]
    public void AbolishingADynamicPredicateRemovesEveryAlias()
    {
        var program = NewProgram();
        var target = program.Symbols.InternFunctor("module:entry", 1);
        var alias = program.Symbols.InternFunctor("entry", 1);
        program.DeclareDynamic(target);
        Assert.True(program.AliasPredicate(alias, target));

        Assert.True(program.AbolishDynamic(alias));
        Assert.False(program.IsDefined(target));
        Assert.False(program.IsDefined(alias));
        Assert.False(program.IsUserPredicate(target));
        Assert.False(program.IsUserPredicate(alias));
    }

    [Theory]
    [InlineData("solve")]
    [InlineData("run")]
    [InlineData("begin")]
    [InlineData("call")]
    [InlineData("redo")]
    public void PublicExecutionEntriesRejectReentrancyBeforeMutation(string operation)
    {
        var program = NewProgram();
        var predicate = program.Symbols.InternFunctor("outer", 1);
        var item = program.Symbols.InternFunctor("item", 1);
        var entry = 0;
        var builtin = program.Builtins.Register(
            "reenter",
            1,
            machine =>
            {
                var before = machine.Argument(0);
                var header = machine.HeapAt(before.Index);

                Assert.Throws<InvalidOperationException>(() =>
                {
                    switch (operation)
                    {
                        case "solve":
                            machine.Solve(predicate);
                            break;
                        case "run":
                            machine.Run(entry);
                            break;
                        case "begin":
                            machine.BeginCall();
                            break;
                        case "call":
                            machine.Call(predicate, [Cell.Atom(program.Symbols.InternAtom("replacement"))]);
                            break;
                        case "redo":
                            machine.Redo();
                            break;
                    }
                });

                Assert.True(machine.IsRunning);
                Assert.Equal(before, machine.Argument(0));
                Assert.Equal(header, machine.HeapAt(before.Index));
                return true;
            }
        );

        entry = program.CodeLength;
        program.DefinePredicate(predicate, entry);
        program.Emit(OpCode.CallBuiltin, builtin, 1);
        program.Emit(OpCode.Stop);

        using var machine = new Machine(program);
        machine.BeginCall();
        var original = machine.CreateStructure(item, [Cell.Atom(program.Symbols.InternAtom("original"))]);
        Assert.Equal(RunResult.Success, machine.Call(predicate, [original]));
    }

    [Theory]
    [InlineData(UnknownProcedureAction.Fail)]
    [InlineData(UnknownProcedureAction.Error)]
    public void FailedSolveLookupDiscardsThePreviousResumableGoal(UnknownProcedureAction unknown)
    {
        var program = NewProgram();
        program.Flags.Unknown = unknown;
        var predicate = program.Symbols.InternFunctor("choice", 0);
        var missing = program.Symbols.InternFunctor("missing", 0);

        program.DefinePredicate(predicate, program.CodeLength);
        var alternative = program.Emit(OpCode.TryMeElse, 0) + 1;
        program.Emit(OpCode.Stop);
        program.Patch(alternative, program.CodeLength);
        program.Emit(OpCode.TrustMe);
        program.Emit(OpCode.Stop);

        using var machine = new Machine(program);
        Assert.Equal(RunResult.Success, machine.Solve(predicate));
        Assert.True(machine.HasAlternatives);

        if (unknown == UnknownProcedureAction.Fail)
        {
            Assert.Equal(RunResult.Failure, machine.Solve(missing));
        }
        else
        {
            Assert.Throws<PrologException>(() => machine.Solve(missing));
        }

        Assert.False(machine.HasAlternatives);
        Assert.Equal(RunResult.Failure, machine.Redo());
    }
}
