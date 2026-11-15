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
        using var machine = NewMachine();
        var buffer = new TermBuffer();

        var root = buffer.Copy(machine, Cell.Integer60(42));

        Assert.Equal("42", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void CopiesANestedStructure()
    {
        using var machine = NewMachine();
        var f = machine.Symbols.InternFunctor("f", 2);
        var g = machine.Symbols.InternFunctor("g", 1);
        var inner = machine.CreateStructure(g, [Cell.Atom(machine.Symbols.InternAtom("a"))]);
        var term = machine.CreateStructure(f, [inner, Cell.Integer60(7)]);

        var buffer = new TermBuffer();
        var root = buffer.Copy(machine, term);

        Assert.Equal("f(g(a),7)", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void KeepsVariablesSharedWithinOneCopy()
    {
        using var machine = NewMachine();
        var f = machine.Symbols.InternFunctor("f", 2);
        var variable = machine.CreateVariable();
        var term = machine.CreateStructure(f, [variable, variable]);

        var buffer = new TermBuffer();
        var root = buffer.Copy(machine, term);
        var rebuilt = Rebuild(machine, buffer, root);

        var left = machine.Dereference(machine.HeapAt(rebuilt.Index + 1));
        var right = machine.Dereference(machine.HeapAt(rebuilt.Index + 2));
        Assert.Equal(CellTag.Reference, left.Tag);
        Assert.Equal(left, right);
    }

    [Fact]
    public void RenamesVariablesApartBetweenCopies()
    {
        using var machine = NewMachine();
        var variable = machine.CreateVariable();

        var buffer = new TermBuffer();
        var first = buffer.Copy(machine, variable);
        var second = buffer.Copy(machine, variable);

        var origin = buffer.Materialize(machine);
        var left = machine.Dereference(machine.HeapAt(origin + first));
        var right = machine.Dereference(machine.HeapAt(origin + second));

        Assert.Equal(CellTag.Reference, left.Tag);
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void CopyFollowsBindingsRatherThanRecordingTheReference()
    {
        using var machine = NewMachine();
        var variable = machine.CreateVariable();
        Assert.True(machine.Unify(variable, Cell.Atom(machine.Symbols.InternAtom("bound"))));

        var buffer = new TermBuffer();
        var root = buffer.Copy(machine, variable);

        Assert.Equal("bound", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void CopyingACyclicTermRaisesACatchableRepresentationError()
    {
        using var machine = NewMachine();
        var f = machine.Symbols.InternFunctor("f", 1);
        var variable = machine.CreateVariable();
        var term = machine.CreateStructure(f, [variable]);
        Assert.True(machine.Unify(variable, term));

        var buffer = new TermBuffer();
        var error = Assert.Throws<PrologException>(() => buffer.Copy(machine, term));

        Assert.Contains("representation_error(cyclic_term)", error.Message);
    }

    [Fact]
    public void CyclePreservingCopiesRelocateBackEdgesAndRemainIndependent()
    {
        using var machine = NewMachine();
        var cycle = machine.CreateVariable();
        var variable = machine.CreateVariable();
        var f = machine.Symbols.InternFunctor("f", 3);
        var term = machine.CreateStructure(f, [variable, cycle, variable]);
        Assert.True(machine.Unify(cycle, term));

        var buffer = new TermBuffer();
        var first = buffer.Copy(machine, term, allowCycles: true);
        var second = buffer.Copy(machine, term, allowCycles: true);
        var origin = buffer.Materialize(machine);
        var left = machine.HeapAt(origin + first);
        var right = machine.HeapAt(origin + second);

        Assert.NotEqual(term, left);
        Assert.NotEqual(left, right);
        Assert.Equal(left, machine.HeapAt(left.Index + 2));
        Assert.Equal(right, machine.HeapAt(right.Index + 2));
        var leftVariable = machine.Dereference(machine.HeapAt(left.Index + 1));
        Assert.Equal(leftVariable, machine.Dereference(machine.HeapAt(left.Index + 3)));
        Assert.NotEqual(variable, leftVariable);
        Assert.True(machine.Unify(leftVariable, Cell.Integer60(42)));
        Assert.Equal(CellTag.Reference, machine.Dereference(machine.HeapAt(right.Index + 1)).Tag);
        Assert.Equal(CellTag.Reference, machine.Dereference(variable).Tag);
    }

    [Fact]
    public void CopyingASharedSubtermIsNotMistakenForACycle()
    {
        using var machine = NewMachine();
        var f = machine.Symbols.InternFunctor("f", 2);
        var g = machine.Symbols.InternFunctor("g", 1);
        var shared = machine.CreateStructure(g, [Cell.Integer60(1)]);
        var term = machine.CreateStructure(f, [shared, shared]);

        var buffer = new TermBuffer();
        var root = buffer.Copy(machine, term);

        Assert.Equal("f(g(1),g(1))", TermWriter.ToDisplayString(machine, Rebuild(machine, buffer, root)));
    }

    [Fact]
    public void ClearDiscardsEverythingCopiedSoFar()
    {
        using var machine = NewMachine();
        var buffer = new TermBuffer();
        buffer.Copy(machine, Cell.Integer60(1));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
    }
}
