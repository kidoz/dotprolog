using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

public sealed class CompiledDispatchTests
{
    [Fact]
    public void ConsecutiveCompiledBlocksRefreshGrownCodeAndConstantsWhenEnteringBytecode()
    {
        var program = new BytecodeProgram();
        var originalCode = program.Code;
        var originalConstants = program.Constants;
        var compiled = new CompiledProgram([], [], [], 2);
        var entry = program.RegisterCompiledBlock(
            (ref Machine.CompiledExecution execution, CompiledProgram targets) =>
            {
                while (ReferenceEquals(originalCode, program.Code))
                {
                    program.Emit(OpCode.Stop);
                }
                int constant;
                do
                {
                    constant = program.AddConstant(Cell.Integer60(42));
                } while (ReferenceEquals(originalConstants, program.Constants));

                targets.SetTarget(1, program.CodeLength);
                program.Emit(OpCode.PutConstant, constant, 0);
                program.Emit(OpCode.Stop);
                return execution.Jump(targets.Target(0));
            },
            compiled
        );
        compiled.SetTarget(
            0,
            program.RegisterCompiledBlock(
                static (ref Machine.CompiledExecution execution, CompiledProgram targets) => execution.Jump(targets.Target(1)),
                compiled
            )
        );

        var machine = new Machine(program);
        Assert.Equal(RunResult.Success, machine.Run(entry));
        Assert.Equal(Cell.Integer60(42), machine.Argument(0));
        Assert.NotSame(originalCode, program.Code);
        Assert.NotSame(originalConstants, program.Constants);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompiledFailureBacktracksOnceIntoEitherExecutionPath(bool compiledAlternative)
    {
        var program = new BytecodeProgram();
        var compiled = new CompiledProgram([], [], [], 2);
        var entry = program.RegisterCompiledBlock(
            static (ref Machine.CompiledExecution execution, CompiledProgram targets) =>
                execution.TryMeElse(targets.Target(0), targets.Target(1)),
            compiled
        );
        compiled.SetTarget(
            1,
            program.RegisterCompiledBlock(
                static (ref Machine.CompiledExecution execution, CompiledProgram _) => execution.Fail(),
                compiled
            )
        );
        if (compiledAlternative)
        {
            compiled.SetTarget(
                0,
                program.RegisterCompiledBlock(
                    static (ref Machine.CompiledExecution execution, CompiledProgram _) => execution.TrustMe(0),
                    compiled
                )
            );
        }
        else
        {
            compiled.SetTarget(0, program.CodeLength);
            program.Emit(OpCode.TrustMe);
            program.Emit(OpCode.Stop);
        }

        var machine = new Machine(program);
        Assert.Equal(RunResult.Success, machine.Run(entry));
        Assert.Equal(RunResult.Failure, machine.Redo());
    }

    [Fact]
    public void CompiledHaltDoesNotTryAnOutstandingAlternative()
    {
        var program = new BytecodeProgram();
        var machine = new Machine(program);
        var compiled = new CompiledProgram([], [], [], 2);
        var alternatives = 0;
        compiled.SetTarget(
            0,
            program.RegisterCompiledBlock(
                (ref Machine.CompiledExecution execution, CompiledProgram _) =>
                {
                    alternatives++;
                    return execution.TrustMe(0);
                },
                compiled
            )
        );
        compiled.SetTarget(
            1,
            program.RegisterCompiledBlock(
                (ref Machine.CompiledExecution execution, CompiledProgram _) =>
                {
                    machine.RequestHalt(23);
                    return execution.Fail();
                },
                compiled
            )
        );
        var entry = program.RegisterCompiledBlock(
            static (ref Machine.CompiledExecution execution, CompiledProgram targets) =>
                execution.TryMeElse(targets.Target(0), targets.Target(1)),
            compiled
        );

        Assert.Equal(RunResult.Halted, machine.Run(entry));
        Assert.Equal(23, machine.ExitCode);
        Assert.Equal(0, alternatives);
    }
}
