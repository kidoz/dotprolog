using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Disjunction, if-then-else, soft cut, and negation, compiled in place inside a clause body — plus
/// the cut-scoping rules that make them behave like ISO expects.
/// </summary>
public sealed class ControlConstructTests
{
    [Fact]
    public void DisjunctionTakesTheFirstBranchThatSucceeds()
    {
        Assert.Equal("1\n", PrologTestHost.RunGoal("( X = 1 ; X = 2 ), write(X), nl"));
    }

    [Fact]
    public void DisjunctionBacktracksIntoItsSecondBranch()
    {
        Assert.Equal("2\n", PrologTestHost.RunGoal("( X = 1 ; X = 2 ), X = 2, write(X), nl"));
    }

    [Fact]
    public void DisjunctionChainsMoreThanTwoBranches()
    {
        Assert.Equal("3\n", PrologTestHost.RunGoal("( X = 1 ; X = 2 ; X = 3 ), X = 3, write(X), nl"));
    }

    [Theory]
    [InlineData(10, "big")]
    [InlineData(1, "small")]
    public void IfThenElseCommitsToTheBranchTheConditionSelects(int input, string expected)
    {
        string output = PrologTestHost.Run(
            $"""
            classify(X, R) :- ( X > 5 -> R = big ; R = small ).

            :- initialization((classify({input}, R), write(R), nl)).
            """
        );

        Assert.Equal($"{expected}\n", output);
    }

    [Fact]
    public void IfThenElseCommitsToTheFirstSolutionOfItsCondition()
    {
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            pick(R) :- ( q(R) -> true ; R = none ).

            :- initialization((pick(R), write(R), nl)).
            """
        );

        Assert.Equal("1\n", output);
    }

    [Fact]
    public void IfThenWithoutAnElseFailsWhenItsConditionFails()
    {
        (RunResult result, string output, _) = PrologTestHost.Execute(":- initialization(( fail -> write(then) )).");

        Assert.Equal(RunResult.Success, result);
        Assert.Equal("Warning: initialization goal failed.\n", output);
    }

    [Fact]
    public void CutInsideAConditionIsLocalToThatCondition()
    {
        // ISO 7.8.7: '->' is opaque to a cut in its condition. The cut must prune only what the
        // condition created, so the else branch is still reachable when the condition then fails.
        Assert.Equal("else\n", PrologTestHost.RunGoal("( ( !, fail ) -> write(then) ; write(else) ), nl"));
    }

    [Fact]
    public void ACutInTheLeftBranchOfADisjunctionPrunesTheRightOne()
    {
        // Written in a clause body the cut is transparent to ;/2, so it removes the alternative and
        // the whole goal fails. Reached through call/1 it is local and the alternative survives,
        // which is the deviation COMPATIBILITY.md records and the conformance suite pins.
        string output = PrologTestHost.Run(
            """
            p :- ( !, fail ; true ).

            :- initialization(( p -> write(succeeded) ; write(failed) )).
            """
        );

        Assert.Equal("failed", output);
    }

    [Fact]
    public void TheSameDisjunctionSucceedsWhenItIsMetaCalled() =>
        Assert.Equal("succeeded", PrologTestHost.RunGoal("( call(( !, fail ; true )) -> write(succeeded) ; write(failed) )"));

    [Fact]
    public void CutInsideAConditionStillPrunesTheConditionsOwnAlternatives()
    {
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            pick(R) :- ( q(R), ! -> true ; R = none ).

            :- initialization((pick(R), write(R), nl)).
            """
        );

        Assert.Equal("1\n", output);
    }

    [Fact]
    public void CutInsideABranchIsTransparentAndPrunesTheWholeClause()
    {
        string output = PrologTestHost.Run(
            """
            p(1).
            p(2).

            committed(R) :- p(R), ( true -> ! ; true ).
            open(R) :- p(R), ( true -> true ; true ).

            :- initialization(( \+ ( committed(R), R = 2 ), open(S), S = 2, write(S), nl )).
            """
        );

        Assert.Equal("2\n", output);
    }

    [Fact]
    public void SoftCutKeepsEverySolutionOfItsCondition()
    {
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            pick(R) :- ( q(R) *-> true ; R = none ).

            :- initialization((pick(R), R = 2, write(R), nl)).
            """
        );

        Assert.Equal("2\n", output);
    }

    [Fact]
    public void SoftCutRunsItsElseBranchWhenTheConditionHasNoSolution()
    {
        Assert.Equal("none\n", PrologTestHost.RunGoal("( fail *-> R = some ; R = none ), write(R), nl"));
    }

    [Fact]
    public void SoftCutDoesNotRunItsElseBranchAfterTheConditionSucceeded()
    {
        // Backtracking past both solutions of q/1 must not fall through to the else branch.
        string output = PrologTestHost.Run(
            """
            q(1).
            q(2).

            pick(R) :- ( q(R) *-> true ; R = none ).

            :- initialization(( \+ ( pick(R), R = none ), write(ok), nl )).
            """
        );

        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void NegationSucceedsWhenItsGoalFails()
    {
        Assert.Equal("yes\n", PrologTestHost.RunGoal("\\+ (1 = 2), write(yes), nl"));
    }

    [Fact]
    public void NegationFailsWhenItsGoalSucceeds()
    {
        (RunResult result, string output, _) = PrologTestHost.Execute(":- initialization(( \\+ (1 = 1), write(no) )).");

        Assert.Equal(RunResult.Success, result);
        Assert.Equal("Warning: initialization goal failed.\n", output);
    }

    [Fact]
    public void NegationUndoesTheBindingsItsGoalMade()
    {
        Assert.Equal("unbound\n", PrologTestHost.RunGoal("\\+ \\+ (X = 1), var(X), write(unbound), nl"));
    }

    [Fact]
    public void ControlConstructsNestAndStillReturnFromTheClause()
    {
        string output = PrologTestHost.Run(
            """
            sign(N, R) :- ( N > 0 -> R = positive ; N < 0 -> R = negative ; R = zero ).

            :- initialization(( sign(3, A), sign(-3, B), sign(0, C), write(A/B/C), nl )).
            """
        );

        Assert.Equal("positive/negative/zero\n", output);
    }
}
