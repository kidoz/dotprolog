namespace DotProlog.Compiler.Tests;

/// <summary>
/// Rational terms. Unification builds them, but detaching one from the heap — for
/// <c>copy_term/2</c>, <c>findall/3</c>, or <c>assertz/1</c> — raises a catchable
/// <c>representation_error(cyclic_term)</c>. Thrown balls preserve cycles, and writing a cycle
/// cuts it off with an ellipsis.
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
    public void ThrowOfACyclicBallPreservesTheCycleAndVariableSharingAfterUnwinding()
    {
        Assert.Equal(
            "caught",
            PrologTestHost.RunGoal(
                "catch((X = f(V, X), throw(g(X, V))), g(C, W), true), "
                    + "var(X), var(V), C = f(A, Tail), C == Tail, A == W, var(W), W = kept, "
                    + "arg(1, C, kept), write(caught)"
            )
        );
    }

    [Fact]
    public void CyclicBallSurvivesAMismatchingCatcherAndRethrow()
    {
        Assert.Equal(
            "caught",
            PrologTestHost.RunGoal(
                "catch(catch(catch((X = f(X), throw(g(X))), g(h(_)), fail), "
                    + "g(C), throw(again(C))), again(D), true), "
                    + "var(X), var(C), D = f(Tail), D == Tail, write(caught)"
            )
        );
    }

    [Fact]
    public void BacktrackingDiscardsACaughtCycleAndRestoresBindings()
    {
        Assert.Equal(
            "caughtrestored",
            PrologTestHost.RunGoal(
                "(catch((X = f(X), throw(X)), C, true), C = f(Tail), C == Tail, "
                    + "write(caught), fail ; var(X), var(C), var(Tail), write(restored))"
            )
        );
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
