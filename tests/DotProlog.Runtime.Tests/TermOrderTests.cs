using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

public sealed class TermOrderTests
{
    [Fact]
    public void ScratchIsClearedAfterAnEarlyDifferenceAndHeapReuse()
    {
        using var machine = new Machine(new BytecodeProgram());
        var functor = machine.Symbols.InternFunctor("f", 2);
        Cell left = machine.CreateStructure(functor, [Cell.Integer60(1), Cell.Integer60(100)]);
        Cell right = machine.CreateStructure(functor, [Cell.Integer60(2), Cell.Integer60(0)]);
        Assert.True(TermOrder.Compare(machine, left, right) < 0);
        Assert.Empty(machine.ComparisonWork);
        Assert.Empty(machine.ComparisonVisited);

        machine.BeginCall();
        left = machine.CreateStructure(functor, [Cell.Integer60(3), Cell.Integer60(0)]);
        right = machine.CreateStructure(functor, [Cell.Integer60(2), Cell.Integer60(100)]);
        Assert.True(TermOrder.Compare(machine, left, right) > 0);
        Assert.True(TermOrder.Compare(machine, right, left) < 0);
        Assert.Empty(machine.ComparisonWork);
        Assert.Empty(machine.ComparisonVisited);
    }

    [Fact]
    public void DeepAndCyclicTermsRemainIterativeAndRespectLaterArguments()
    {
        using var machine = new Machine(new BytecodeProgram());
        var functor = machine.Symbols.InternFunctor("f", 2);
        Cell a = machine.CreateVariable();
        Cell b = machine.CreateVariable();
        Cell left = machine.CreateStructure(functor, [a, Cell.Integer60(1)]);
        Cell right = machine.CreateStructure(functor, [b, Cell.Integer60(2)]);
        Assert.True(machine.Unify(a, left));
        Assert.True(machine.Unify(b, right));
        Assert.True(TermOrder.Compare(machine, left, right) < 0);
        Assert.True(TermOrder.Compare(machine, right, left) > 0);

        for (var i = 0; i < 20_000; i++)
        {
            left = machine.CreateStructure(functor, [left, Cell.Integer60(0)]);
            right = machine.CreateStructure(functor, [right, Cell.Integer60(0)]);
        }
        Assert.True(TermOrder.Compare(machine, left, right) < 0);
        Assert.Empty(machine.ComparisonWork);
        Assert.Empty(machine.ComparisonVisited);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern, 1)]
    [InlineData(PrologLanguageMode.StrictIso, -1)]
    public void AtomicFastPathRetainsModeSpecificTextOrdering(PrologLanguageMode mode, int sign)
    {
        using var machine = new Machine(new BytecodeProgram(mode));
        var supplementary = machine.Symbols.InternAtom("\U00010000");
        var bmp = machine.Symbols.InternAtom("\uE000");
        Assert.Equal(sign, Math.Sign(TermOrder.Compare(machine, Cell.Atom(supplementary), Cell.Atom(bmp))));
        Assert.Equal(sign, Math.Sign(TermOrder.Compare(machine, Cell.String(supplementary), Cell.String(bmp))));
        Assert.True(TermOrder.Compare(machine, Cell.String(bmp), Cell.Atom(bmp)) < 0);
    }
}
