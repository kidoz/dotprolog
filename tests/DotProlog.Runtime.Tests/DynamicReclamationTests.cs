using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

public sealed class DynamicReclamationTests
{
    [Fact]
    public void RetractionThroughAnOlderSnapshotKeepsTheOriginalDeathGeneration()
    {
        var program = new BytecodeProgram();
        DatabaseBuiltins.Register(program.Builtins);
        var functor = program.Symbols.InternFunctor("item", 1);
        DynamicPredicate predicate = program.DeclareDynamic(functor);
        Add(program, predicate, 1);
        DynamicClause last = Add(program, predicate, 2);
        Assert.True(program.Builtins.TryGetId(program.Symbols.InternFunctor("retract", 1), out var retract));
        var wrapper = program.Symbols.InternFunctor("erase", 1);
        var entry = program.Emit(OpCode.CallBuiltin, retract, 1);
        program.Emit(OpCode.Proceed);
        program.DefinePredicate(wrapper, entry);
        using var machine = new Machine(program);
        machine.BeginCall();
        Cell pattern = machine.CreateStructure(functor, [machine.CreateVariable()]);
        Assert.Equal(RunResult.Success, machine.Call(wrapper, [pattern]));
        var death = program.NextGeneration();
        last.Death = death;
        program.NoteRetraction();
        Assert.Equal(RunResult.Success, machine.Redo());
        Assert.Equal(death, last.Death);
        machine.BeginCall();
        Assert.Null(predicate.First);
    }

    [Fact]
    public void SuspendedCallOnAnotherMachineProtectsRetractedClausesUntilReset()
    {
        var program = new BytecodeProgram();
        var functor = program.Symbols.InternFunctor("item", 1);
        DynamicPredicate predicate = program.DeclareDynamic(functor);
        DynamicClause first = Add(program, predicate, 1);
        DynamicClause last = Add(program, predicate, 2);
        using var reader = new Machine(program);
        using var writer = new Machine(program);
        reader.BeginCall();
        Assert.Equal(RunResult.Success, reader.Call(functor, [reader.CreateVariable()]));
        first.Death = program.NextGeneration();
        last.Death = program.NextGeneration();
        program.NoteRetraction(2);
        writer.BeginCall();
        Assert.Same(first, predicate.First);
        Assert.Equal(RunResult.Success, reader.Redo());
        Assert.Equal(Cell.Integer60(2), reader.Argument(0));
        Assert.Equal(RunResult.Failure, reader.Redo());
        writer.BeginCall();
        Assert.Null(predicate.First);
        Assert.Null(first.Next);
        Add(program, predicate, 3);
        Assert.Equal(RunResult.Success, writer.Call(functor, [Cell.Integer60(3)]));
    }

    [Fact]
    public void ReclamationKeepsSourceOrderAndRepairsPrependAndAppendAfterRemovingTheTail()
    {
        var program = new BytecodeProgram();
        var functor = program.Symbols.InternFunctor("item", 1);
        DynamicPredicate predicate = program.DeclareDynamic(functor);
        DynamicClause first = Add(program, predicate, 1);
        DynamicClause middle = Add(program, predicate, 2);
        DynamicClause tail = Add(program, predicate, 3);
        first.Death = tail.Death = program.NextGeneration();
        program.NoteRetraction(2);
        program.ReclaimDynamicClauses();
        Assert.Same(middle, predicate.First);
        Assert.Null(middle.Next);
        DynamicClause last = Add(program, predicate, 4);
        Assert.Same(last, middle.Next);
        predicate.Prepend(first);
        first.Death = int.MaxValue;
        Assert.Same(first, predicate.First);
        Assert.Same(middle, first.Next);
    }

    [Fact]
    public void AbolishedAliasesKeepOldEnumerationAndReleaseTheRetiredList()
    {
        var program = new BytecodeProgram();
        var functor = program.Symbols.InternFunctor("item", 1);
        DynamicPredicate predicate = program.DeclareDynamic(functor);
        DynamicClause first = Add(program, predicate, 1);
        Add(program, predicate, 2);
        var alias = program.Symbols.InternFunctor("alias", 1);
        Assert.True(program.AliasPredicate(alias, functor));
        using var machine = new Machine(program);
        machine.BeginCall();
        Assert.Equal(RunResult.Success, machine.Call(alias, [machine.CreateVariable()]));
        Assert.True(program.AbolishDynamic(functor));
        Assert.False(program.IsDefined(alias));
        Assert.Equal(RunResult.Success, machine.Redo());
        Assert.Equal(Cell.Integer60(2), machine.Argument(0));
        machine.BeginCall();
        Assert.Null(predicate.First);
        Assert.Null(first.Next);
    }

    private static DynamicClause Add(BytecodeProgram program, DynamicPredicate predicate, int value)
    {
        var address = program.Emit(OpCode.GetConstant, program.AddConstant(Cell.Integer60(value)), 0);
        program.Emit(OpCode.Proceed);
        var rule = program.Symbols.InternFunctor(":-", 2);
        var clause = new DynamicClause
        {
            CodeAddress = address,
            Birth = program.NextGeneration(),
            Term = TermBuffer.FromCells([
                Cell.Structure(1),
                Cell.Functor(rule),
                Cell.Structure(4),
                Cell.Atom(program.Symbols.True),
                Cell.Functor(predicate.FunctorId),
                Cell.Integer60(value),
            ]),
        };
        predicate.Append(clause);
        return clause;
    }
}
