namespace DotProlog.Runtime.Tests;

public sealed class ArityBoundaryTests
{
    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public void StructureConstructionChecksArityBeforeAllocation(int arity)
    {
        var program = new BytecodeProgram();
        using var machine = new Machine(program);
        var functor = program.Symbols.InternFunctor("f", arity);
        var arguments = Enumerable.Repeat(Cell.Atom(program.Symbols.True), arity).ToArray();
        if (arity == 255)
        {
            var term = machine.CreateStructure(functor, arguments);
            Assert.Equal(arguments[^1], machine.HeapAt(term.Index + arity));
        }
        else
        {
            var error = Assert.Throws<PrologException>(() => machine.CreateStructure(functor, arguments));
            Assert.Contains("representation_error(max_arity)", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    public void MetaDispatchDefensivelyRejectsOversizedStructuresFromRawBytecode(int arity)
    {
        var program = new BytecodeProgram();
        var functor = program.Symbols.InternFunctor("f", arity);
        var constant = program.AddConstant(Cell.Atom(program.Symbols.True));
        var entry = program.Emit(OpCode.PutStructureArgument, functor, 0);
        for (var i = 0; i < arity; i++)
            program.Emit(OpCode.UnifyConstant, constant);
        program.Emit(OpCode.MetaCall);
        program.Emit(OpCode.Stop);
        using var machine = new Machine(program);
        var error = Assert.Throws<PrologException>(() => machine.Run(entry));
        Assert.Contains("representation_error(max_arity)", error.Message, StringComparison.Ordinal);
        Assert.False(machine.IsRunning);
        Assert.Equal(RunResult.Failure, machine.Redo());
    }

    [Fact]
    public void StructureArgumentsMustMatchTheFunctor()
    {
        var program = new BytecodeProgram();
        using var machine = new Machine(program);
        var functor = program.Symbols.InternFunctor("f", 2);
        Assert.Throws<ArgumentException>(() => machine.CreateStructure(functor, [Cell.Integer60(1)]));
    }
}
