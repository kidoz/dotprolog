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
        Assert.True(program.Builtins.TryGetId(program.Symbols.InternFunctor(name, arity), out int id));
        return id;
    }

    [Fact]
    public void ProvesAFactAndReturnsToTheHost()
    {
        BytecodeProgram program = NewProgram();
        int p = program.Symbols.InternFunctor("p", 0);

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
        int p = program.Symbols.InternFunctor("p", 0);
        int fail = BuiltinId(program, "fail", 0);

        program.DefinePredicate(p, program.CodeLength);
        int alternative = program.Emit(OpCode.TryMeElse, 0) + 1;
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
        int p = program.Symbols.InternFunctor("p", 0);
        int fail = BuiltinId(program, "fail", 0);

        program.DefinePredicate(p, program.CodeLength);
        int alternative = program.Emit(OpCode.TryMeElse, 0) + 1;
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
        int p = program.Symbols.InternFunctor("p", 0);
        int q = program.Symbols.InternFunctor("q", 0);
        int write = BuiltinId(program, "write", 1);
        int done = program.AddConstant(Cell.Atom(program.Symbols.InternAtom("done")));

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
        int p = program.Symbols.InternFunctor("p", 0);
        int halt = BuiltinId(program, "halt", 1);
        int seven = program.AddConstant(Cell.Integer60(7));

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
        int missing = program.Symbols.InternFunctor("missing", 2);

        PrologException exception = Assert.Throws<PrologException>(() => new Machine(program).Solve(missing));

        Assert.Contains("existence_error(procedure, missing/2)", exception.Message, StringComparison.Ordinal);
    }
}
