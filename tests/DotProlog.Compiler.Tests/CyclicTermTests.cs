namespace DotProlog.Compiler.Tests;

/// <summary>
/// Rational terms. Unification builds them, but detaching one from the heap — for
/// <c>copy_term/2</c>, <c>findall/3</c>, <c>assertz/1</c>, or a thrown ball — raises a catchable
/// <c>representation_error(cyclic_term)</c>, and writing one cuts the cycle off with an ellipsis.
/// </summary>
public sealed class CyclicTermTests
{
    private const string Catcher = "error(representation_error(cyclic_term), _)";

    [Fact]
    public void CopyTermOfACyclicTermRaisesACatchableError()
    {
        Assert.Equal("caught\n", PrologTestHost.RunGoal($"X = f(X), catch(copy_term(X, _), {Catcher}, write(caught)), nl"));
    }

    [Fact]
    public void FindallOfACyclicSolutionRaisesACatchableError()
    {
        Assert.Equal("caught\n", PrologTestHost.RunGoal($"X = f(X), catch(findall(Y, Y = X, _), {Catcher}, write(caught)), nl"));
    }

    [Fact]
    public void AssertzOfACyclicClauseRaisesACatchableError()
    {
        Assert.Equal("caught\n", PrologTestHost.RunGoal($"X = f(X), catch(assertz(cyc(X)), {Catcher}, write(caught)), nl"));
    }

    [Fact]
    public void ThrowOfACyclicBallRaisesACatchableError()
    {
        Assert.Equal("caught\n", PrologTestHost.RunGoal($"X = f(X), catch(throw(g(X)), {Catcher}, write(caught)), nl"));
    }

    [Fact]
    public void MetaCallOfACyclicControlTermRaisesACatchableError()
    {
        Assert.Equal("caught\n", PrologTestHost.RunGoal($"X = (true, X), catch(call(X), {Catcher}, write(caught)), nl"));
    }

    [Fact]
    public void WritingACyclicTermTerminatesWithAnEllipsis()
    {
        Assert.Equal("f(a,...)\n", PrologTestHost.RunGoal("X = f(a, X), write(X), nl"));
    }

    [Fact]
    public void WritingACyclicListTerminatesWithAnEllipsis()
    {
        Assert.Equal("[1|...]\n", PrologTestHost.RunGoal("X = [1|X], write(X), nl"));
    }

    [Fact]
    public void WritingASharedSubtermIsNotCutOff()
    {
        Assert.Equal("f(g(1),g(1))\n", PrologTestHost.RunGoal("T = g(1), X = f(T, T), write(X), nl"));
    }
}
