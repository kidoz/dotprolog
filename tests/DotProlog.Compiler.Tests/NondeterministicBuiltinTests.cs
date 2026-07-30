namespace DotProlog.Compiler.Tests;

/// <summary>
/// Native predicates that yield more than one solution, which the engine gained once a builtin could
/// push a choice point of its own.
/// </summary>
public sealed class NondeterministicBuiltinTests
{
    private const string Declared = """
        :- dynamic p/1.

        p(1).
        p(2).
        p(3).
        """;

    [Fact]
    public void BetweenEnumeratesItsRange()
    {
        Assert.Equal("[1,2,3,4,5]\n", PrologTestHost.RunGoal("findall(X, between(1, 5, X), L), write(L), nl"));
    }

    [Fact]
    public void BetweenYieldsNothingForAnEmptyRange()
    {
        Assert.Equal("[]\n", PrologTestHost.RunGoal("findall(X, between(5, 1, X), L), write(L), nl"));
    }

    [Fact]
    public void BetweenIsARangeCheckWhenItsArgumentIsBound()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("between(1, 10, 4), \\+ between(1, 10, 40), write(yes)"));
    }

    [Fact]
    public void BetweenBacktracksIntoLaterValues()
    {
        Assert.Equal("7\n", PrologTestHost.RunGoal("between(1, 10, X), X > 6, write(X), nl"));
    }

    [Fact]
    public void RetractRemovesAFurtherClauseOnEachRedo()
    {
        // ISO requires retract/1 to be nondeterministic; the whole predicate empties here.
        string output = PrologTestHost.Run(
            $"""
            {Declared}

            :- initialization((
                   findall(X, retract(p(X)), Removed), write(Removed), nl,
                   findall(Y, p(Y), Left), write(Left), nl
               )).
            """
        );

        Assert.Equal("[1,2,3]\n[]\n", output);
    }

    [Fact]
    public void RetractStopsAtTheFirstMatchWhenNotBacktrackedInto()
    {
        Assert.Equal(
            "[2,3]\n",
            PrologTestHost.Run($"{Declared}\n:- initialization((retract(p(_)), findall(Y, p(Y), L), write(L), nl)).")
        );
    }

    [Fact]
    public void ClauseEnumeratesWithoutRemoving()
    {
        string output = PrologTestHost.Run(
            $"""
            {Declared}

            :- initialization((
                   findall(H, clause(p(H), true), Heads), write(Heads), nl,
                   findall(Y, p(Y), Left), write(Left), nl
               )).
            """
        );

        Assert.Equal("[1,2,3]\n[1,2,3]\n", output);
    }

    [Fact]
    public void ClauseMatchesARuleBody()
    {
        Assert.EndsWith(
            ">5",
            PrologTestHost.RunGoal("assertz((big(N) :- N > 5)), clause(big(_), B), write(B)"),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ClauseFailsForAnUnknownPredicate()
    {
        Assert.Equal("yes", PrologTestHost.RunGoal("\\+ clause(nowhere(_), _), write(yes)"));
    }

    [Theory]
    [InlineData("fixed(_)", "fixed/1")]
    [InlineData("write(_)", "write/1")]
    public void ClauseRejectsPrivateProcedures(string head, string indicator)
    {
        string output = PrologTestHost.Run(
            $"""
            fixed(1).
            :- initialization(catch(clause({head}, _), error(E, _), write(E))).
            """
        );

        Assert.Equal($"permission_error(access,private_procedure,{indicator})", output);
    }

    [Theory]
    [InlineData("clause(nowhere(_), 4)")]
    [InlineData("assertz(visible(a)), clause(visible(_), 4)")]
    public void ClauseRejectsANonCallableBody(string goal)
    {
        Assert.Equal("type_error(callable,4)", PrologTestHost.RunGoal($"catch(({goal}), error(E, _), write(E))"));
    }

    [Fact]
    public void NondeterministicBuiltinsUndoTheirBindingsOnBacktracking()
    {
        // Each redo must start from an unbound X, which only happens if the choice point was pushed
        // before the binding rather than after it.
        Assert.Equal("[1,2,3]\n", PrologTestHost.RunGoal("findall(X, between(1, 3, X), L), write(L), nl"));
    }

    [Fact]
    public void NestedNondeterministicBuiltinsEnumerateThePairs()
    {
        Assert.Equal(
            "[1-1,1-2,2-1,2-2]\n",
            PrologTestHost.RunGoal("findall(X-Y, (between(1, 2, X), between(1, 2, Y)), L), write(L), nl")
        );
    }

    [Fact]
    public void CutPrunesANondeterministicBuiltin()
    {
        Assert.Equal("1\n", PrologTestHost.RunGoal("between(1, 5, X), !, write(X), nl"));
    }
}
