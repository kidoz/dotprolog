using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

/// <summary>
/// The detached term copy that <c>catch/3</c> and <c>findall/3</c> share. These assert the two
/// properties both depend on: a copy survives the heap being truncated, and variables are shared
/// within one copy but never across two.
/// </summary>
public sealed class TermBufferTests
{
    private static Machine NewMachine()
    {
        var program = new BytecodeProgram();
        CoreBuiltins.RegisterAll(program);
        return new Machine(program);
    }

    private static Cell Rebuild(Machine machine, TermBuffer buffer, int root) =>
        machine.HeapAt(buffer.Materialize(machine) + root);

    [Fact]
    public void CopiesAConstant()
    {
        Machine machine = NewMachine();
        var buffer = new TermBuffer();

        int root = buffer.Copy(machine, Cell.Integer60(42));

        Assert.Equal("42", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void CopiesANestedStructure()
    {
        Machine machine = NewMachine();
        int f = machine.Symbols.InternFunctor("f", 2);
        int g = machine.Symbols.InternFunctor("g", 1);
        Cell inner = machine.CreateStructure(g, [Cell.Atom(machine.Symbols.InternAtom("a"))]);
        Cell term = machine.CreateStructure(f, [inner, Cell.Integer60(7)]);

        var buffer = new TermBuffer();
        int root = buffer.Copy(machine, term);

        Assert.Equal("f(g(a),7)", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void KeepsVariablesSharedWithinOneCopy()
    {
        Machine machine = NewMachine();
        int f = machine.Symbols.InternFunctor("f", 2);
        Cell variable = machine.CreateVariable();
        Cell term = machine.CreateStructure(f, [variable, variable]);

        var buffer = new TermBuffer();
        int root = buffer.Copy(machine, term);
        Cell rebuilt = Rebuild(machine, buffer, root);

        Cell left = machine.Dereference(machine.HeapAt(rebuilt.Index + 1));
        Cell right = machine.Dereference(machine.HeapAt(rebuilt.Index + 2));
        Assert.Equal(CellTag.Reference, left.Tag);
        Assert.Equal(left, right);
    }

    [Fact]
    public void RenamesVariablesApartBetweenCopies()
    {
        Machine machine = NewMachine();
        Cell variable = machine.CreateVariable();

        var buffer = new TermBuffer();
        int first = buffer.Copy(machine, variable);
        int second = buffer.Copy(machine, variable);

        int origin = buffer.Materialize(machine);
        Cell left = machine.Dereference(machine.HeapAt(origin + first));
        Cell right = machine.Dereference(machine.HeapAt(origin + second));

        Assert.Equal(CellTag.Reference, left.Tag);
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void CopyFollowsBindingsRatherThanRecordingTheReference()
    {
        Machine machine = NewMachine();
        Cell variable = machine.CreateVariable();
        Assert.True(machine.Unify(variable, Cell.Atom(machine.Symbols.InternAtom("bound"))));

        var buffer = new TermBuffer();
        int root = buffer.Copy(machine, variable);

        Assert.Equal("bound", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void CopyingACyclicTermRaisesACatchableRepresentationError()
    {
        Machine machine = NewMachine();
        int f = machine.Symbols.InternFunctor("f", 1);
        Cell variable = machine.CreateVariable();
        Cell term = machine.CreateStructure(f, [variable]);
        Assert.True(machine.Unify(variable, term));

        var buffer = new TermBuffer();
        PrologException error = Assert.Throws<PrologException>(() => buffer.Copy(machine, term));

        Assert.Contains("representation_error(cyclic_term)", error.Message);
    }

    [Fact]
    public void CopyingASharedSubtermIsNotMistakenForACycle()
    {
        Machine machine = NewMachine();
        int f = machine.Symbols.InternFunctor("f", 2);
        int g = machine.Symbols.InternFunctor("g", 1);
        Cell shared = machine.CreateStructure(g, [Cell.Integer60(1)]);
        Cell term = machine.CreateStructure(f, [shared, shared]);

        var buffer = new TermBuffer();
        int root = buffer.Copy(machine, term);

        Assert.Equal("f(g(1),g(1))", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void ClearDiscardsEverythingCopiedSoFar()
    {
        Machine machine = NewMachine();
        var buffer = new TermBuffer();
        buffer.Copy(machine, Cell.Integer60(1));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
    }
}
