using Prolog.Runtime;

namespace Prolog.Compiler.Tests;

/// <summary>
/// Predicates whose clauses change while the program runs, and the logical update view that fixes
/// what a goal already in progress can see.
/// </summary>
public sealed class DynamicPredicateTests
{
    private const string Declared = """
        :- dynamic p/1.

        p(1).
        p(2).
        """;

    [Fact]
    public void AssertzAddsAClauseAtTheEnd()
    {
        Assert.Equal(
            "[1,2,3]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((assertz(p(3)), findall(X, p(X), L), write(L), nl)).")
        );
    }

    [Fact]
    public void AssertaAddsAClauseAtTheFront()
    {
        Assert.Equal(
            "[0,1,2]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((asserta(p(0)), findall(X, p(X), L), write(L), nl)).")
        );
    }

    [Fact]
    public void AssertingToAnUndeclaredPredicateCreatesIt()
    {
        Assert.Equal(
            "[a,b]\n",
            PrologTestHost.RunGoal("assertz(fresh(a)), assertz(fresh(b)), findall(X, fresh(X), L), write(L), nl")
        );
    }

    [Fact]
    public void AssertsARuleWithABody()
    {
        Assert.Equal("yes\n", PrologTestHost.RunGoal("assertz((big(N) :- N > 5)), big(9), write(yes), nl"));
    }

    [Fact]
    public void RetractRemovesTheFirstMatchingClause()
    {
        Assert.Equal(
            "[2]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((retract(p(1)), findall(X, p(X), L), write(L), nl)).")
        );
    }

    [Fact]
    public void RetractFailsWhenNothingMatches()
    {
        Assert.Equal("yes", PrologTestHost.Run($"{Declared}\n:- initialization((\\+ retract(p(99)), write(yes))).")); // no clause p(99)
    }

    [Fact]
    public void RetractMatchesARuleByHeadAndBody()
    {
        Assert.Equal(
            "gone\n",
            PrologTestHost.RunGoal("assertz((r(X) :- X > 1)), retract((r(Y) :- Y > 1)), \\+ r(5), write(gone), nl")
        );
    }

    [Fact]
    public void RetractAllRemovesEveryMatchingClause()
    {
        Assert.Equal(
            "[]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((retractall(p(_)), findall(X, p(X), L), write(L), nl)).")
        );
    }

    [Fact]
    public void RetractAllKeepsClausesThatDoNotMatch()
    {
        Assert.Equal(
            "[2]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((retractall(p(1)), findall(X, p(X), L), write(L), nl)).")
        );
    }

    [Fact]
    public void RetractAllOnAnUnknownPredicateSucceedsAndDefinesIt()
    {
        Assert.Equal("[]\n", PrologTestHost.RunGoal("retractall(unheard(_)), findall(X, unheard(X), L), write(L), nl"));
    }

    [Fact]
    public void AbolishRemovesEveryClause()
    {
        Assert.Equal(
            "[]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((abolish(p/1), findall(X, p(X), L), write(L), nl)).")
        );
    }

    [Fact]
    public void ADeclaredPredicateWithNoClausesFailsRatherThanErroring()
    {
        Assert.Equal("yes\n", PrologTestHost.Run(":- dynamic empty/1.\n:- initialization((\\+ empty(_), write(yes), nl))."));
    }

    [Fact]
    public void ModifyingAStaticPredicateIsAPermissionError()
    {
        string output = PrologTestHost.Run(
            """
            fixed(1).

            :- initialization(catch(assertz(fixed(2)), error(E, _), (write(E), nl))).
            """
        );

        Assert.Equal("permission_error(modify,static_procedure,fixed/1)\n", output);
    }

    [Fact]
    public void AGoalDoesNotSeeClausesAssertedAfterItStarted()
    {
        // The logical update view: p(X) is fixed to the clauses that existed when it was called.
        Assert.Equal(
            "[1,2]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((findall(X, (p(X), assertz(p(9))), L), write(L), nl)).")
        );
    }

    [Fact]
    public void ClausesAssertedDuringAGoalAreVisibleToTheNextOne()
    {
        string output = PrologTestHost.Run(
            $"""
            {Declared}

            :- initialization((
                   findall(X, (p(X), assertz(p(9))), _),
                   findall(Y, p(Y), L),
                   write(L), nl
               )).
            """
        );

        Assert.Equal("[1,2,9,9]\n", output);
    }

    [Fact]
    public void AGoalStillSeesClausesRetractedAfterItStarted()
    {
        // p(2) is retracted while p(X) is iterating, but the call began before that, so it is reached.
        Assert.Equal(
            "[1,2]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((findall(X, (p(X), retractall(p(2))), L), write(L), nl)).")
        );
    }

    [Fact]
    public void AssertedClausesBacktrackLikeCompiledOnes()
    {
        Assert.Equal("2\n", PrologTestHost.RunGoal("assertz(q(1)), assertz(q(2)), assertz(q(3)), q(X), X > 1, write(X), nl"));
    }

    [Fact]
    public void CutInsideAnAssertedRuleWorks()
    {
        Assert.Equal(
            "1\n",
            PrologTestHost.RunGoal("assertz(s(1)), assertz(s(2)), assertz((firsts(X) :- s(X), !)), firsts(Y), write(Y), nl")
        );
    }
}
