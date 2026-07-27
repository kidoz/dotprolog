using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The host-facing API: compiling a goal, pulling answers one at a time, and marshalling each
/// binding into plain .NET objects. This is what C#, F#, and VB callers use.
/// </summary>
public sealed class EmbeddingTests
{
    private static PrologEngine NewEngine(string? source = null)
    {
        var engine = new PrologEngine { Output = TextWriter.Null };
        if (source is not null)
        {
            LoadResult loaded = engine.ConsultText(source, "test.pl");
            Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        }

        return engine;
    }

    [Fact]
    public void EnumeratesEverySolutionOfAPredicate()
    {
        PrologEngine engine = NewEngine("colour(red).\ncolour(green).\ncolour(blue).");

        string[] colours = [.. engine.Query("colour(C)").Solutions().Select(s => s["C"].ToString())];

        Assert.Equal(["red", "green", "blue"], colours);
    }

    [Fact]
    public void ReportsEveryVariableOfTheGoal()
    {
        PrologEngine engine = NewEngine("pair(a, 1).\npair(b, 2).");

        PrologQuery query = engine.Query("pair(Name, Number)");

        Assert.Equal(["Name", "Number"], query.VariableNames);
        Assert.Equal(["a=1", "b=2"], query.Solutions().Select(s => $"{s["Name"]}={s["Number"]}"));
    }

    [Fact]
    public void YieldsOneEmptySolutionForAGroundGoalThatSucceeds()
    {
        PrologSolution solution = Assert.Single(NewEngine().Query("1 < 2").Solutions());

        Assert.Empty(solution.Bindings);
        Assert.Equal("true", solution.ToString());
    }

    [Fact]
    public void YieldsNothingForAGoalThatFails()
    {
        Assert.Empty(NewEngine().Query("1 > 2").Solutions());
    }

    [Fact]
    public void ProveReportsSuccessWithoutMarshalling()
    {
        PrologEngine engine = NewEngine("ok.");

        Assert.True(engine.Query("ok").Prove());
        Assert.False(engine.Query("fail").Prove());
    }

    [Fact]
    public void FirstOrDefaultStopsAfterOneAnswer()
    {
        PrologEngine engine = NewEngine("n(1).\nn(2).");

        Assert.Equal("1", engine.Query("n(X)").FirstOrDefault()?["X"].ToString());
        Assert.Null(engine.Query("n(99)").FirstOrDefault());
    }

    [Fact]
    public void SolutionsAreProducedLazilySoAnInfiniteGoalIsUsable()
    {
        PrologEngine engine = NewEngine();

        long[] first =
        [
            .. engine.Query("between(1, 1000000000, X)").Solutions().Take(4).Select(s => ((PrologInteger)s["X"]).Value),
        ];

        Assert.Equal([1L, 2L, 3L, 4L], first);
    }

    [Fact]
    public void AQueryCanBeRunMoreThanOnce()
    {
        PrologEngine engine = NewEngine("n(1).\nn(2).");
        PrologQuery query = engine.Query("n(X)");

        Assert.Equal(2, query.Solutions().Count());
        Assert.Equal(2, query.Solutions().Count());
    }

    [Fact]
    public void MarshalsEachTermKind()
    {
        PrologEngine engine = NewEngine();

        PrologSolution solution = Assert.Single(engine.Query("X = f(atom, 42, 1.5, Y, [a,b])").Solutions());
        var term = Assert.IsType<PrologCompound>(solution["X"]);

        Assert.Equal("f", term.Name);
        Assert.Equal(new PrologAtom("atom"), term.Arguments[0]);
        Assert.Equal(new PrologInteger(42), term.Arguments[1]);
        Assert.Equal(new PrologFloat(1.5), term.Arguments[2]);
        Assert.IsType<PrologVariable>(term.Arguments[3]);
    }

    [Fact]
    public void ReadsAProperListBackAsAList()
    {
        PrologEngine engine = NewEngine();

        PrologSolution solution = Assert.Single(engine.Query("X = [a,b,c]").Solutions());

        Assert.True(solution["X"].TryGetList(out IReadOnlyList<PrologValue> items));
        Assert.Equal(["a", "b", "c"], items.Select(i => i.ToString()));
    }

    [Fact]
    public void APartialListIsNotAProperList()
    {
        PrologEngine engine = NewEngine();

        PrologSolution solution = Assert.Single(engine.Query("X = [a|_]").Solutions());

        Assert.False(solution["X"].TryGetList(out _));
    }

    [Fact]
    public void SolutionsSurviveThePursuitOfLaterOnes()
    {
        // Bindings are marshalled as each answer is produced, so collecting them all is safe even
        // though backtracking discards the heap they came from.
        PrologEngine engine = NewEngine("n(1).\nn(2).\nn(3).");

        List<PrologSolution> all = [.. engine.Query("n(X)").Solutions()];

        Assert.Equal(["1", "2", "3"], all.Select(s => s["X"].ToString()));
    }

    [Fact]
    public void AnUncaughtBallReachesTheHost()
    {
        PrologEngine engine = NewEngine();

        PrologException error = Assert.Throws<PrologException>(() => engine.Query("throw(oops)").Prove());

        Assert.Equal("oops", error.Message);
    }

    [Fact]
    public void AGoalThatDoesNotParseIsRejectedAtCompileTime()
    {
        Assert.Throws<PrologException>(() => NewEngine().Query("foo("));
    }

    [Fact]
    public void QueriesSeeClausesAssertedThroughAnEarlierQuery()
    {
        PrologEngine engine = NewEngine();

        Assert.True(engine.Query("assertz(fact(one)), assertz(fact(two))").Prove());
        Assert.Equal(["one", "two"], engine.Query("fact(X)").Solutions().Select(s => s["X"].ToString()));
    }
}
