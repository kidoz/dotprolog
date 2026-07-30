using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary><c>throw/1</c>, <c>catch/3</c>, and the ISO error terms the engine itself raises.</summary>
public sealed class ExceptionTests
{
    private static PrologException Uncaught(string source)
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(source).Success);
        return Assert.Throws<PrologException>(() => engine.RunPendingGoals());
    }

    [Fact]
    public void CatchRunsTheRecoveryGoalForAMatchingBall()
    {
        Assert.Equal("caught\n", PrologTestHost.RunGoal("catch(throw(boom), boom, (write(caught), nl))"));
    }

    [Fact]
    public void CatcherBindsVariablesFromTheBall()
    {
        Assert.Equal("42\n", PrologTestHost.RunGoal("catch(throw(size(42)), size(N), (write(N), nl))"));
    }

    [Fact]
    public void GoalThatSucceedsLeavesTheRecoveryGoalAlone()
    {
        Assert.Equal("fine\n", PrologTestHost.RunGoal("catch(write(fine), _, write(recovered)), nl"));
    }

    [Fact]
    public void CatchIsTransparentToFailure()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("\\+ catch(fail, _, true), write(yes)"));
    }

    [Fact]
    public void ABallThatDoesNotMatchThePatternPassesThrough()
    {
        Assert.Equal(
            "outer\n",
            PrologTestHost.RunGoal("catch(catch(throw(inner), other, write(wrong)), inner, (write(outer), nl))")
        );
    }

    [Fact]
    public void InnermostMatchingCatchWins()
    {
        Assert.Equal("inner\n", PrologTestHost.RunGoal("catch(catch(throw(b), b, (write(inner), nl)), b, write(outer))"));
    }

    [Fact]
    public void BindingsMadeByTheGoalAreUndoneBeforeRecoveryRuns()
    {
        Assert.Equal("unbound\n", PrologTestHost.RunGoal("catch((X = 1, throw(e)), e, (var(X), write(unbound), nl))"));
    }

    [Fact]
    public void CatchDoesNotApplyOnceItsGoalHasSucceeded()
    {
        // The frame must go out of scope when the goal succeeds, or an unrelated later throw would
        // be caught by a catch/3 it never ran inside.
        PrologException error = Uncaught(":- initialization(( catch(true, _, write(wrong)), throw(later) )).");

        Assert.Equal("later", error.Message);
    }

    [Fact]
    public void CatcherStaysActiveWhenExecutionBacktracksIntoTheGoal()
    {
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2) :- throw(oops).

            :- initialization(catch((q(X), X > 1), Ball, (write(caught(Ball)), nl))).
            """
        );

        Assert.Equal("caught(oops)\n", output);
    }

    [Fact]
    public void BacktrackingThroughCatchReachesLaterSolutions()
    {
        string output = PrologTestHost.Run(
            """
            r(1).
            r(2).

            :- initialization(( catch(r(Y), _, fail), Y = 2, write(Y), nl )).
            """
        );

        Assert.Equal("2\n", output);
    }

    [Fact]
    public void UncaughtBallReachesTheHost()
    {
        Assert.Equal("boom(1)", Uncaught(":- initialization(throw(boom(1))).").Message);
    }

    [Fact]
    public void ThrowingAnUnboundVariableIsAnInstantiationError()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("catch(throw(_), error(instantiation_error, _), write(yes))"));
    }

    [Theory]
    [InlineData("catch(_, never, true)", "instantiation_error")]
    [InlineData("catch(4, never, true)", "type_error(callable,4)")]
    [InlineData("catch(throw(ball), ball, 4)", "type_error(callable,4)")]
    [InlineData("catch(true, _, 4)", "success")]
    public void CatchValidatesOnlyTheGoalItExecutes(string goal, string expected)
    {
        Assert.Equal(
            expected,
            PrologTestHost.RunGoal($"catch(({goal}, Result = success), error(E, _), Result = E), write(Result)")
        );
    }

    // write/1 is canonical until the writer learns the operator table, so a predicate indicator
    // comes out as '/(nowhere,1)' rather than 'nowhere/1'.
    [Theory]
    [InlineData("nowhere(1)", "existence_error(procedure,nowhere/1)")]
    [InlineData("X is 1 // 0", "evaluation_error(zero_divisor)")]
    [InlineData("X is Y + 1", "instantiation_error")]
    [InlineData("atom_length(x, y, z)", "existence_error(procedure,atom_length/3)")]
    [InlineData("X is foo + 1", "type_error(evaluable,foo/0)")]
    [InlineData("T =.. []", "domain_error(non_empty_list,[])")]
    public void EngineErrorsAreCatchable(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(({goal}), error(E, _), write(E))"));
    }

    [Fact]
    public void ExistenceErrorCarriesAPredicateIndicator()
    {
        Assert.Equal(
            "nowhere 2\n",
            PrologTestHost.RunGoal(
                "catch(nowhere(a, b), error(existence_error(procedure, N/A), _), (write(N), write(' '), write(A), nl))"
            )
        );
    }

    [Fact]
    public void ErrorTermsCarryAnUnboundContext()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("catch(nowhere, error(_, Context), (var(Context), write(yes)))"));
    }

    [Fact]
    public void RecoveryGoalMayItselfThrow()
    {
        Assert.Equal("second\n", PrologTestHost.RunGoal("catch(catch(throw(a), a, throw(b)), b, (write(second), nl))"));
    }

    [Fact]
    public void ABallIsCopiedSoItSurvivesTheUnwind()
    {
        // The thrown term is built on heap that unwinding discards, so it has to be copied out and
        // rebuilt. A deep term makes a shallow copy obvious.
        Assert.Equal("f(g(h(1)),[a,b])\n", PrologTestHost.RunGoal("catch(throw(f(g(h(1)), [a,b])), B, (write(B), nl))"));
    }

    [Fact]
    public void HostCanTellAPrologBallFromAHostFault()
    {
        Assert.True(Uncaught(":- initialization(throw(anything)).").HasBall);
    }
}
