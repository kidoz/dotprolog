namespace DotProlog.Compiler.Tests;

/// <summary>
/// Rational terms. Unification builds them, but detaching one from the heap — for
/// <c>copy_term/2</c>, <c>findall/3</c>, or <c>assertz/1</c> — raises a catchable
/// <c>representation_error(cyclic_term)</c>. Thrown balls preserve cycles, writing cuts one off
/// with an ellipsis, and inspection and list consumers terminate with their ordinary answers or errors.
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

    [Theory]
    [InlineData("L = [a|L], \\+ is_list(L)")]
    [InlineData("L = [a|L], \\+ is_list([b,c|L])")]
    public void IsListFailsForDirectAndFinitePrefixCycles(string goal) =>
        Assert.Equal("yes", PrologTestHost.RunGoal($"{goal}, write(yes)"));

    [Theory]
    [InlineData("X = f(X), ground(X)")]
    [InlineData("X = [a|X], ground(X)")]
    [InlineData("X = f(Y,X), \\+ ground(X), var(Y)")]
    [InlineData("S = g(a), ground(f(S,S))")]
    public void GroundTerminatesOnCyclesAndSharedSubterms(string goal) =>
        Assert.Equal("yes", PrologTestHost.RunGoal($"{goal}, write(yes)"));

    [Theory]
    [InlineData("X = f(X), term_variables(X, [])")]
    [InlineData("X = f(Y,X,Z,Y), term_variables(X, [Y,Z])")]
    [InlineData("S = g(V), term_variables(f(S,S), [V])")]
    [InlineData("L = [a,X|L], term_variables(L, [X])")]
    public void TermVariablesTerminatesAndKeepsLeftToRightDistinctVariables(string goal) =>
        Assert.Equal("yes", PrologTestHost.RunGoal($"{goal}, write(yes)"));

    [Theory]
    [InlineData("term_variables(a, R)")]
    [InlineData("_ =.. R")]
    [InlineData("sort(R, _)")]
    [InlineData("msort(R, _)")]
    [InlineData("keysort(R, _)")]
    [InlineData("sort(0, @<, R, _)")]
    [InlineData("sort([], R)")]
    [InlineData("msort([], R)")]
    [InlineData("keysort([], R)")]
    [InlineData("sort(0, @<, [], R)")]
    public void ListReadersRejectFinitePrefixCyclesWithTheWholeCyclicCulprit(string goal) =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                $"catch((R = [a,b|R], {goal}), error(type_error(list,C), _), true), "
                    + "C = [a,b|Tail], C == Tail, var(R), write(yes)"
            )
        );

    [Theory]
    [InlineData("X = f(X), Y = f(Y), msort([X,Y], [X,Y])")]
    [InlineData("X = f(X), Y = f(Y), sort([X,Y], [X])")]
    [InlineData("X = f(X), Y = f(Y), sort(0, @=<, [X,Y], [X,Y])")]
    [InlineData("X = f(X), Y = f(Y), keysort([X-1,Y-2], [X-1,Y-2])")]
    public void SortsTerminateWhenElementsContainCyclicCompounds(string goal) =>
        Assert.Equal("yes", PrologTestHost.RunGoal($"{goal}, write(yes)"));

    [Theory]
    [InlineData("string_length(R, _)", "text")]
    [InlineData("atom_string(_, R)", "string")]
    [InlineData("string_chars(_, R)", "list")]
    [InlineData("format(R, [])", "text")]
    public void TextReadersRejectFinitePrefixCyclesWithPredicateSpecificErrors(string goal, string expected) =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                $"catch((R = [a,b|R], {goal}), error(type_error({expected},C), _), true), "
                    + "C = [a,b|Tail], C == Tail, var(R), write(yes)"
            )
        );

    [Fact]
    public async Task CyclicInspectionTerminatesWithinTheCancellationWindow()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        var run = Task.Run(
            () =>
                PrologTestHost.RunGoal(
                    "X = f(V,X), \\+ ground(X), term_variables(X, [V]), "
                        + "L = [a|L], \\+ is_list(L), catch(sort(L, _), error(type_error(list,_), _), true), write(yes)"
                ),
            cancellation.Token
        );

        Assert.Equal("yes", await run.WaitAsync(cancellation.Token));
    }
}
