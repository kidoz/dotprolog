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
    public void AGoalBallWithTheOldInternalShapeIsPreservedAndRunsCleanup() =>
        Assert.Equal(
            "1-ok",
            PrologTestHost.RunGoal(
                $"{Counting}, catch(setup_call_cleanup(true, throw('$cleanup_ball'(user)), Bump), B, true), "
                    + "B == '$cleanup_ball'(user), nb_getval(n, N), write(N-ok)"
            )
        );

    [Fact]
    public void ACleanupBallWithTheOldInternalShapeIsPreservedAndRunsCleanupOnce() =>
        Assert.Equal(
            "1-ok",
            PrologTestHost.RunGoal(
                $"{Counting}, catch(setup_call_cleanup(true, true, ( Bump, throw('$cleanup_ball'(cleanup)) )), B, true), "
                    + "B == '$cleanup_ball'(cleanup), nb_getval(n, N), write(N-ok)"
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
    public void SurroundingCutRunsDeferredCleanupExactlyOnce() =>
        Assert.Equal(
            "1-1",
            PrologTestHost.RunGoal($"{Counting}, call_cleanup(member(X, [1, 2]), Bump), !, nb_getval(n, N), write(N-X)")
        );

    [Theory]
    [InlineData("call_cleanup(member(_, [a,b]), Y = done), !, Y == done")]
    [InlineData("call_cleanup(member(X, [a,b]), Y = X), X = b, !, Y == b")]
    [InlineData("call_cleanup(member(_, [a,b]), Cleanup), Cleanup = (Y = done), !, Y == done")]
    [InlineData("freeze(Y, Z = woke), call_cleanup(member(_, [a,b]), Y = done), !, Z == woke")]
    [InlineData("call_cleanup(member(_, [a,b]), Y = done), call_cleanup(member(_, [a,b]), Y = done), !, Y == done")]
    public void CutCleanupSharesCallerVariables(string goal) => Assert.Equal("ok", PrologTestHost.RunGoal(goal + ", write(ok)"));

    [Fact]
    public void CutCleanupObservesBindingsMadeAfterTheGoalReturns() =>
        Assert.Equal("done", PrologTestHost.RunGoal("call_cleanup(member(_, [a,b]), write(Y)), Y = done, !"));

    [Fact]
    public void CleanupArgumentBindingIsRestoredBetweenSolutions() =>
        Assert.Equal(
            "ok",
            PrologTestHost.RunGoal(
                "call_cleanup((Cleanup = write(wrong) ; Cleanup = (Y = done) ; Cleanup = write(wrong)), Cleanup), "
                    + "Cleanup = (Y = done), !, Y == done, write(ok)"
            )
        );

    [Fact]
    public void SurroundingCutRunsNestedCleanupsInnerFirst() =>
        Assert.Equal(
            "[inner,outer]",
            PrologTestHost.RunGoal(
                "nb_setval(order, []),"
                    + " setup_call_cleanup(true,"
                    + "   setup_call_cleanup(true, member(_, [1, 2]), ( nb_getval(order, A), append(A, [inner], A1), nb_setval(order, A1) )),"
                    + "   ( nb_getval(order, B), append(B, [outer], B1), nb_setval(order, B1) )), !,"
                    + " nb_getval(order, Order), write(Order)"
            )
        );

    [Fact]
    public void BacktrackingAfterCutDoesNotRepeatCleanup() =>
        Assert.Equal(
            "1",
            PrologTestHost.RunGoal(
                $"{Counting}, ( call(( call_cleanup(member(_, [1, 2]), Bump), !, fail )) ; nb_getval(n, N), write(N) )"
            )
        );

    [Fact]
    public void CleanupBallAfterCutPropagatesOnce() =>
        Assert.Equal(
            "1-cut_ball",
            PrologTestHost.RunGoal(
                $"{Counting}, catch(( call_cleanup(member(_, [1, 2]), ( Bump, throw(cut_ball) )), ! ), B, true), "
                    + "nb_getval(n, N), write(N-B)"
            )
        );

    [Fact]
    public void InnerCleanupBallOutranksOuterCleanupBallAfterCut() =>
        Assert.Equal(
            "2-inner",
            PrologTestHost.RunGoal(
                $"{Counting}, catch(( setup_call_cleanup(true,"
                    + " setup_call_cleanup(true, member(_, [1, 2]), ( Bump, throw(inner) )),"
                    + " ( Bump, throw(outer) )), ! ), B, true), nb_getval(n, N), write(N-B)"
            )
        );

    [Fact]
    public void HostRedoExhaustionRunsCleanupOnce()
    {
        var output = new StringWriter();
        using var engine = new PrologEngine { Output = output };
        engine.ConsultOrThrow("two(1). two(2).\n", "redo.pl");

        Assert.Equal(RunResult.Success, engine.RunGoal($"{Counting}, setup_call_cleanup(true, two(_), Bump)", out _));
        Assert.Equal(RunResult.Success, engine.Machine.Redo());
        Assert.Equal(RunResult.Failure, engine.Machine.Redo());
        Assert.Equal(RunResult.Success, engine.RunGoal("nb_getval(n, N), write(N)", out _));
        Assert.Equal("1", output.ToString());
    }

    [Fact]
    public void SurroundingCutClosesTheSetupResource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotprolog-cleanup-{Guid.NewGuid():N}.txt");
        try
        {
            Assert.Equal(
                "closed",
                PrologTestHost.RunGoal(
                    $"setup_call_cleanup(open('{path}', write, S), member(_, [a, b]), close(S)), !, "
                        + "\\+ stream_property(S, _), write(closed)"
                )
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StrictModeRejectsCleanupGoals()
    {
        using var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        var loaded = engine.ConsultText("p :- call_cleanup(true, true).\n", "strict.pl");

        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("call_cleanup/2", diagnostic.Message, StringComparison.Ordinal);
    }
}
