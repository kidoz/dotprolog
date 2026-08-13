using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// setup_call_cleanup/3 and call_cleanup/2: cleanup runs exactly once on a
/// deterministic exit, on the redo that exhausts the alternatives, on failure, and on a thrown
/// ball, with the SWI ball-precedence rules probed against SWI-Prolog 10.
/// </summary>
public sealed class CleanupGoalTests
{
    // A counting cleanup pins "exactly once": it bumps a non-backtrackable global.
    private const string Counting = "nb_setval(n, 0), Bump = ( nb_getval(n, K), K1 is K + 1, nb_setval(n, K1) )";

    [Fact]
    public void DeterministicSuccessRunsCleanupBeforeTheCallReturns() =>
        Assert.Equal("gck", PrologTestHost.RunGoal("setup_call_cleanup(true, write(g), write(c)), write(k)"));

    [Fact]
    public void FailureRunsCleanupOnce() =>
        Assert.Equal(
            "1-failed",
            PrologTestHost.RunGoal(
                $"{Counting}, ( setup_call_cleanup(true, fail, Bump) -> true ; true ), nb_getval(n, N), write(N-failed)"
            )
        );

    [Fact]
    public void ExhaustingTheAlternativesRunsCleanupWithTheLastSolution() =>
        Assert.Equal(
            "c[1,2]",
            PrologTestHost.RunGoal("findall(X, setup_call_cleanup(true, member(X, [1, 2]), write(c)), L), write(L)")
        );

    [Fact]
    public void ANondeterministicFirstSolutionDefersCleanup() =>
        Assert.Equal(
            "0",
            PrologTestHost.Run(
                """
                two(1). two(2).
                :- initialization((
                    nb_setval(n, 0),
                    Bump = ( nb_getval(n, K), K1 is K + 1, nb_setval(n, K1) ),
                    call_cleanup(two(_), Bump),
                    nb_getval(n, N),
                    write(N)
                )).
                """
            )
        );

    [Fact]
    public void ExhaustionThroughBacktrackingCleansExactlyOnce() =>
        Assert.Equal(
            "solutions([1,2])-1",
            PrologTestHost.RunGoal(
                $"{Counting}, findall(X, setup_call_cleanup(true, member(X, [1, 2]), Bump), L),"
                    + " nb_getval(n, N), write(solutions(L)-N)"
            )
        );

    [Fact]
    public void AGoalBallOutranksTheCleanupBall() =>
        Assert.Equal("a", PrologTestHost.RunGoal("catch(setup_call_cleanup(true, throw(a), throw(b)), E, true), write(E)"));

    [Fact]
    public void ACleanupBallPropagatesWhenNothingElseIsPending() =>
        Assert.Equal("c", PrologTestHost.RunGoal("catch(setup_call_cleanup(true, true, throw(c)), E, true), write(E)"));

    [Fact]
    public void ACleanupBallDoesNotRunTheCleanupTwice() =>
        Assert.Equal(
            "1",
            PrologTestHost.RunGoal(
                $"{Counting}, catch(setup_call_cleanup(true, true, ( Bump, throw(c) )), c, true), nb_getval(n, N), write(N)"
            )
        );

    [Fact]
    public void AThrownBallStillRunsCleanupOnce() =>
        Assert.Equal(
            "1-ball",
            PrologTestHost.RunGoal(
                $"{Counting}, catch(setup_call_cleanup(true, throw(ball), Bump), B, true), nb_getval(n, N), write(N-B)"
            )
        );

    [Fact]
    public void CleanupFailureIsIgnored() =>
        Assert.Equal("ok", PrologTestHost.RunGoal("setup_call_cleanup(true, true, fail), write(ok)"));

    [Fact]
    public void AFailingSetupRunsNothing() =>
        Assert.Equal(
            "0-no",
            PrologTestHost.RunGoal(
                $"{Counting}, ( setup_call_cleanup(fail, true, Bump) -> write(yes) ; true ), nb_getval(n, N), write(N-no)"
            )
        );

    [Fact]
    public void OnceInsideTheGoalGivesCommitSemantics() =>
        Assert.Equal("c1", PrologTestHost.RunGoal("setup_call_cleanup(true, once(member(X, [1, 2])), write(c)), write(X)"));

    [Fact]
    public void NestedCleanupsFireInnerFirst() =>
        Assert.Equal(
            "[inner,outer]",
            PrologTestHost.RunGoal(
                "nb_setval(order, []),"
                    + " setup_call_cleanup(true,"
                    + "   setup_call_cleanup(true, true, ( nb_getval(order, A), append(A, [inner], A1), nb_setval(order, A1) )),"
                    + "   ( nb_getval(order, B), append(B, [outer], B1), nb_setval(order, B1) )),"
                    + " nb_getval(order, Order), write(Order)"
            )
        );

    [Fact]
    public void CallCleanupIsTheTwoArgumentForm() =>
        Assert.Equal("gc", PrologTestHost.RunGoal("call_cleanup(write(g), write(c))"));

    [Fact]
    public void StrictModeRejectsCleanupGoals()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        LoadResult loaded = engine.ConsultText("p :- call_cleanup(true, true).", "strict.pl");

        DotProlog.Syntax.Diagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("call_cleanup/2", diagnostic.Message, StringComparison.Ordinal);
    }
}
