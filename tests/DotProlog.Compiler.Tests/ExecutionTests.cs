using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

/// <summary>End-to-end behaviour of compiled clauses: unification, backtracking, cut, and tail calls.</summary>
public sealed class ExecutionTests
{
    [Fact]
    public void UnifiesStructuresAndPropagatesBindings()
    {
        Assert.Equal("f(a,b)\n", PrologTestHost.RunGoal("X = f(a, Y), Y = b, write(X), nl"));
    }

    [Fact]
    public void UnifiesNestedStructuresInAClauseHead()
    {
        string output = PrologTestHost.Run(
            """
            unwrap(box(inner(Value)), Value).

            :- initialization((unwrap(box(inner(hello)), X), write(X), nl)).
            """
        );

        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void RepeatedHeadVariableForcesArgumentsToMatch()
    {
        string output = PrologTestHost.Run(
            """
            same(X, X).

            :- initialization((same(a, a), write(yes), nl)).
            """
        );

        Assert.Equal("yes\n", output);
    }

    [Fact]
    public void FailedGoalIsReportedRatherThanThrowing()
    {
        (RunResult result, string output, _) = PrologTestHost.Execute(
            """
            same(X, X).

            :- initialization(same(a, b)).
            """
        );

        Assert.Equal(RunResult.Success, result);
        Assert.Equal("Warning: initialization goal failed.\n", output);
    }

    [Fact]
    public void BacktracksThroughClauseAlternativesUntilAGoalSucceeds()
    {
        string output = PrologTestHost.Run(
            """
            p(1).
            p(2).
            p(3).

            :- initialization((p(X), X >= 3, write(X), nl)).
            """
        );

        Assert.Equal("3\n", output);
    }

    [Fact]
    public void BacktrackingUndoesBindingsMadeByTheFailedBranch()
    {
        string output = PrologTestHost.Run(
            """
            p(f(1)).
            p(f(2)).

            check(f(2)).

            :- initialization((p(X), check(X), write(X), nl)).
            """
        );

        Assert.Equal("f(2)\n", output);
    }

    [Fact]
    public void CutDiscardsRemainingAlternatives()
    {
        string output = PrologTestHost.Run(
            """
            p(1).
            p(2).
            p(3).

            first(X) :- p(X), !.

            :- initialization((first(X), write(X), nl)).
            """
        );

        Assert.Equal("1\n", output);
    }

    [Fact]
    public void CutIsLocalToItsOwnPredicate()
    {
        // The cut in first/1 must not remove the choice point q/1 created for the caller.
        string output = PrologTestHost.Run(
            """
            p(1).
            p(2).

            q(a).
            q(b).

            first(X) :- p(X), !.

            :- initialization((q(Q), Q = b, first(X), write(Q), write(X), nl)).
            """
        );

        Assert.Equal("b1\n", output);
    }

    [Fact]
    public void TailRecursionRunsAtConstantStackDepth()
    {
        // 200,000 iterations would exhaust the CLR stack if Prolog calls were CLR calls.
        string output = PrologTestHost.Run(
            """
            count(0) :- !.
            count(N) :- M is N - 1, count(M).

            :- initialization((count(200000), write(done), nl)).
            """
        );

        Assert.Equal("done\n", output);
    }

    [Fact]
    public void RecursesOverListsBuiltFromHeadDecomposition()
    {
        string output = PrologTestHost.Run(
            """
            last([X], X).
            last([_|Tail], X) :- last(Tail, X).

            :- initialization((last([a,b,c], X), write(X), nl)).
            """
        );

        Assert.Equal("c\n", output);
    }

    [Fact]
    public void BuildsListsInGoalArguments()
    {
        Assert.Equal("[a,b,c]\n", PrologTestHost.RunGoal("X = [a,b,c], write(X), nl"));
    }

    [Fact]
    public void CallingAnUndefinedPredicateRaisesAnExistenceError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(nowhere(1)).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("existence_error(procedure, nowhere/1)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HaltStopsBeforeLaterGoalsRun()
    {
        (RunResult result, string output, _) = PrologTestHost.Execute(
            """
            :- initialization((write(before), nl, halt(3), write(after))).
            """
        );

        Assert.Equal(RunResult.Halted, result);
        Assert.Equal("before\n", output);
    }

    [Theory]
    [InlineData("halt(_)", "instantiation_error")]
    [InlineData("halt(stopped)", "type_error(integer,stopped)")]
    [InlineData("halt(1.0)", "type_error(integer,1.0)")]
    public void HaltRejectsInvalidExitStatus(string goal, string expected)
    {
        ArgumentNullException.ThrowIfNull(goal);

        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));
    }

    [Fact]
    public void AGoalThatIsNotCallableIsReportedNotIgnored()
    {
        (_, _, IReadOnlyList<Diagnostic> diagnostics) = PrologTestHost.Execute("p :- 42.");

        Assert.Equal(CompilerDiagnosticIds.UnsupportedGoal, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData(":- module(shapes, [square/2]).")]
    [InlineData(":- discontiguous square/2.")]
    public void PortableDeclarationsAreAcceptedRatherThanRun(string declaration)
    {
        // A file written to load in any Prolog system opens with declarations this release does not
        // act on. Running them as goals would raise existence_error and make the file unusable.
        string output = PrologTestHost.Run(
            $"""
            {declaration}

            square(N, S) :- S is N * N.

            :- initialization((square(7, S), write(S), nl)).
            """
        );

        Assert.Equal("49\n", output);
    }

    [Fact]
    public void ReaderDiagnosticsSurfaceThroughConsult()
    {
        (_, _, IReadOnlyList<Diagnostic> diagnostics) = PrologTestHost.Execute("p(a b).");

        Assert.Equal(DiagnosticIds.UnexpectedToken, Assert.Single(diagnostics).Id);
    }
}
