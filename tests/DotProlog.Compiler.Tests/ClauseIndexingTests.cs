using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// First-argument clause indexing must never change which solutions appear or their order — it may
/// only skip clauses head unification would reject. These tests pin hits, misses, variable
/// fallback, ordering across mixed keys, and dynamic-predicate behaviour under the logical update
/// view.
/// </summary>
public sealed class ClauseIndexingTests
{
    [Fact]
    public void BoundFirstArgumentSelectsTheMatchingClause()
    {
        Assert.Equal("g", Run("colour(red, r). colour(green, g). colour(blue, b).", "colour(green, X), write(X)"));
    }

    [Fact]
    public void UnboundFirstArgumentEnumeratesEveryClauseInOrder()
    {
        Assert.Equal(
            "[r,g,b]",
            Run("colour(red, r). colour(green, g). colour(blue, b).", "findall(X, colour(_, X), Xs), write(Xs)")
        );
    }

    [Fact]
    public void UnmatchedKeyFailsWithoutSolutions()
    {
        Assert.Equal("[]", Run("colour(red, r). colour(green, g).", "findall(X, colour(yellow, X), Xs), write(Xs)"));
    }

    [Fact]
    public void VariableHeadedClausesMatchEveryKey()
    {
        const string source = "p(a, 1). p(_, 2). p(b, 3).";
        Assert.Equal("[1,2]", Run(source, "findall(V, p(a, V), Vs), write(Vs)"));
        Assert.Equal("[2,3]", Run(source, "findall(V, p(b, V), Vs), write(Vs)"));
        Assert.Equal("[2]", Run(source, "findall(V, p(c, V), Vs), write(Vs)"));
        Assert.Equal("[1,2,3]", Run(source, "findall(V, p(_, V), Vs), write(Vs)"));
    }

    [Fact]
    public void ClausesSharingAKeyKeepTheirOrder()
    {
        Assert.Equal("[1,2]", Run("m(a, 1). m(b, 9). m(a, 2).", "findall(V, m(a, V), Vs), write(Vs)"));
    }

    [Fact]
    public void IntegerAndFloatKeysStayDistinct()
    {
        const string source = "q(1, int). q(1.0, float).";
        Assert.Equal("[int]", Run(source, "findall(X, q(1, X), Xs), write(Xs)"));
        Assert.Equal("[float]", Run(source, "findall(X, q(1.0, X), Xs), write(Xs)"));
    }

    [Fact]
    public void CompoundKeysDispatchOnTheirFunctor()
    {
        const string source = "s(f(_), ff). s(g(_), gg). s(f(x), fx).";
        Assert.Equal("[ff,fx]", Run(source, "findall(X, s(f(x), X), Xs), write(Xs)"));
        Assert.Equal("[gg]", Run(source, "findall(X, s(g(9), X), Xs), write(Xs)"));
        Assert.Equal("[]", Run(source, "findall(X, s(h(1), X), Xs), write(Xs)"));
    }

    [Fact]
    public void ListAndAtomKeysAreDistinguished()
    {
        const string source = "kind([], empty). kind([_|_], cons). kind(atom, plain).";
        Assert.Equal("[empty]", Run(source, "findall(X, kind([], X), Xs), write(Xs)"));
        Assert.Equal("[cons]", Run(source, "findall(X, kind([1,2], X), Xs), write(Xs)"));
        Assert.Equal("[plain]", Run(source, "findall(X, kind(atom, X), Xs), write(Xs)"));
    }

    [Fact]
    public void CutInsideAnIndexedClauseStillPrunesAlternatives()
    {
        const string source = "first(a, 1) :- !. first(a, 2). first(b, 3).";
        Assert.Equal("[1]", Run(source, "findall(V, first(a, V), Vs), write(Vs)"));
    }

    [Fact]
    public void AssertedClausesAreIndexedByFirstArgument()
    {
        Assert.Equal(
            "[2]",
            Run(":- dynamic(d/2).", "assertz(d(a, 1)), assertz(d(b, 2)), assertz(d(c, 3)), findall(V, d(b, V), Vs), write(Vs)")
        );
    }

    [Fact]
    public void AssertaPrependsAcrossIndexedDispatch()
    {
        Assert.Equal("[0,1]", Run(":- dynamic(d/2).", "assertz(d(a, 1)), asserta(d(a, 0)), findall(V, d(a, V), Vs), write(Vs)"));
    }

    [Fact]
    public void RetractRemovesAnIndexedClause()
    {
        Assert.Equal(
            "[3]",
            Run(":- dynamic(d/2).", "assertz(d(a, 1)), assertz(d(a, 3)), retract(d(a, 1)), findall(V, d(a, V), Vs), write(Vs)")
        );
    }

    [Fact]
    public void LogicalUpdateViewIsPreservedUnderIndexing()
    {
        // A goal must see exactly the clauses that existed when it started, so asserting more
        // matching clauses while enumerating terminates instead of chasing its own additions.
        Assert.Equal(
            "[1,2]",
            Run(":- dynamic(d/2).", "assertz(d(a, 1)), assertz(d(a, 2)), findall(V, (d(a, V), assertz(d(a, 9))), Vs), write(Vs)")
        );
    }

    [Fact]
    public void RuntimeConsultedPredicatesAreIndexed()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = TextReader.Null };

        Assert.Empty(engine.ConsultText("speed(car, fast). speed(snail, slow).", "facts.pl").Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunGoal("speed(snail, X), write(X)", out _));
        Assert.Equal("slow", output.ToString());
    }

    private static string Run(string source, string goal)
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = TextReader.Null };

        Assert.Empty(engine.ConsultText(source, "indexing.pl").Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunGoal(goal, out _));
        return output.ToString();
    }
}
