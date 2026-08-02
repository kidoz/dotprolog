using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

/// <summary>
/// Exercises the dispatch loop against hand-assembled bytecode, so a failure here points at the
/// engine rather than at the reader or the clause compiler.
/// </summary>
public sealed class MachineTests
{
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
        BytecodeProgram program = NewProgram();
        var p = program.Symbols.InternFunctor("p", 0);

        program.DefinePredicate(p, program.CodeLength);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        Assert.Equal(RunResult.Success, new Machine(program).Solve(p));
    }

    [Fact]
    public void BacktracksFromAFailedClauseIntoTheNextAlternative()
    {
        BytecodeProgram program = NewProgram();
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

        Assert.Equal(RunResult.Success, new Machine(program).Solve(p));
    }

    [Fact]
    public void FailsWhenEveryAlternativeIsExhausted()
    {
        BytecodeProgram program = NewProgram();
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

        Assert.Equal(RunResult.Failure, new Machine(program).Solve(p));
    }

    [Fact]
    public void CallReturnsToItsContinuation()
    {
        BytecodeProgram program = NewProgram();
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
        var machine = new Machine(program) { Output = output };

        Assert.Equal(RunResult.Success, machine.Solve(p));
        Assert.Equal("done", output.ToString());
    }

    [Fact]
    public void HaltStopsTheRunAndReportsItsExitCode()
    {
        BytecodeProgram program = NewProgram();
        var p = program.Symbols.InternFunctor("p", 0);
        var halt = BuiltinId(program, "halt", 1);
        var seven = program.AddConstant(Cell.Integer60(7));

        program.DefinePredicate(p, program.CodeLength);
        program.Emit(OpCode.Allocate, 0);
        program.Emit(OpCode.PutConstant, seven, 0);
        program.Emit(OpCode.CallBuiltin, halt, 1);
        program.Emit(OpCode.Deallocate);
        program.Emit(OpCode.Proceed);

        var machine = new Machine(program);

        Assert.Equal(RunResult.Halted, machine.Solve(p));
        Assert.Equal(7, machine.ExitCode);
    }

    [Fact]
    public void CallingAnUndefinedPredicateRaisesAnExistenceError()
    {
        BytecodeProgram program = NewProgram();
        var missing = program.Symbols.InternFunctor("missing", 2);

        PrologException exception = Assert.Throws<PrologException>(() => new Machine(program).Solve(missing));

        Assert.Contains("existence_error(procedure, missing/2)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OccursCheckRejectsADirectCycleAndRestoresTheVariable()
    {
        BytecodeProgram program = NewProgram();
        var machine = new Machine(program);
        Cell variable = machine.CreateVariable();
        Cell recursive = machine.CreateStructure(program.Symbols.InternFunctor("f", 1), [variable]);

        Assert.False(machine.UnifyWithOccursCheck(variable, recursive));
        Assert.Equal(variable, machine.Dereference(variable));
    }

    [Fact]
    public void OccursCheckRetainsFiniteBindings()
    {
        BytecodeProgram program = NewProgram();
        var machine = new Machine(program);
        Cell variable = machine.CreateVariable();
        Cell value = machine.CreateStructure(program.Symbols.InternFunctor("point", 2), [Cell.Integer60(2), Cell.Integer60(3)]);

        Assert.True(machine.UnifyWithOccursCheck(variable, value));
        Assert.Equal(value, machine.Dereference(variable));
    }

    [Fact]
    public void OccursCheckRestoresBindingsAfterALaterMismatch()
    {
        BytecodeProgram program = NewProgram();
        var machine = new Machine(program);
        Cell variable = machine.CreateVariable();
        var pair = program.Symbols.InternFunctor("pair", 2);
        Cell left = machine.CreateStructure(pair, [variable, variable]);
        Cell right = machine.CreateStructure(
            pair,
            [Cell.Atom(program.Symbols.InternAtom("a")), Cell.Atom(program.Symbols.InternAtom("b"))]
        );

        Assert.False(machine.UnifyWithOccursCheck(left, right));
        Assert.Equal(variable, machine.Dereference(variable));
    }

    [Fact]
    public void OccursCheckTerminatesWhenTheCandidateContainsAnExistingCycle()
    {
        BytecodeProgram program = NewProgram();
        var machine = new Machine(program);
        Cell cyclicVariable = machine.CreateVariable();
        Cell cyclicTerm = machine.CreateStructure(program.Symbols.InternFunctor("cycle", 1), [cyclicVariable]);
        Assert.True(machine.Unify(cyclicVariable, cyclicTerm));

        Cell variable = machine.CreateVariable();
        Cell wrapper = machine.CreateStructure(program.Symbols.InternFunctor("wrapper", 1), [cyclicTerm]);

        Assert.True(machine.UnifyWithOccursCheck(variable, wrapper));
        Assert.Equal(wrapper, machine.Dereference(variable));
    }

    [Fact]
    public void OrdinaryUnificationTerminatesWhenCyclicIntermediateBindingsPrecedeAMismatch()
    {
        BytecodeProgram program = NewProgram();
        var machine = new Machine(program);
        var a = program.Symbols.InternFunctor("a", 1);
        var f = program.Symbols.InternFunctor("f", 4);
        Cell x = machine.CreateVariable();
        Cell y = machine.CreateVariable();
        Cell left = machine.CreateStructure(f, [x, y, x, Cell.Integer60(1)]);
        Cell right = machine.CreateStructure(
            f,
            [machine.CreateStructure(a, [x]), machine.CreateStructure(a, [y]), y, Cell.Integer60(2)]
        );

        Assert.False(machine.Unify(left, right));
    }

    [Fact]
    public void TermIdentityTerminatesForEquivalentRationalTreesWithDifferentPeriods()
    {
        BytecodeProgram program = NewProgram();
        var machine = new Machine(program);
        Cell shortTail = machine.CreateVariable();
        Cell shortCycle = machine.CreateList([Cell.Integer60(1), Cell.Integer60(2), Cell.Integer60(3)], shortTail);
        Assert.True(machine.Unify(shortTail, shortCycle));

        Cell longTail = machine.CreateVariable();
        Cell longCycle = machine.CreateList(
            [Cell.Integer60(1), Cell.Integer60(2), Cell.Integer60(3), Cell.Integer60(1), Cell.Integer60(2), Cell.Integer60(3)],
            longTail
        );
        Assert.True(machine.Unify(longTail, longCycle));

        Assert.True(TermOrder.AreIdentical(machine, shortCycle, longCycle));
    }

    [Fact]
    public void ProgramDistinguishesUserPredicatesFromBuiltinsAndInternalBytecode()
    {
        BytecodeProgram program = NewProgram();
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
        BytecodeProgram program = NewProgram();
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
}
