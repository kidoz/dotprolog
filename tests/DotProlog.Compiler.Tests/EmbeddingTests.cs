using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The host-facing API: compiling a goal, pulling answers one at a time, and marshalling each
/// binding into plain .NET objects. This is what C#, F#, and VB callers use.
/// </summary>
public sealed class EmbeddingTests
{
    [Fact]
    public void SpanHostCallsPreserveOutputsFailureAndDetachedValuesAcrossReuse()
    {
        using PrologEngine engine = NewEngine("pair(1, a). pair(2, b).");
        var host = new PrologHost(engine.Machine);
        var predicate = host.Bind("pair", 2);
        PrologInput[] inputs = [PrologInput.Integer(1), PrologInput.Output];
        Assert.True(host.Prove(predicate, inputs.AsSpan()));
        var first = Assert.Single(host.CallOnce(predicate, inputs.AsSpan())!);
        inputs[0] = PrologInput.Integer(2);
        Assert.Equal("b", Assert.Single(host.CallOnce(predicate, inputs.AsSpan())!).ToString());
        Assert.Equal("a", first.ToString());
        inputs[0] = PrologInput.Integer(3);
        Assert.False(host.Prove(predicate, inputs.AsSpan()));
        Assert.Null(host.CallOnce(predicate, inputs.AsSpan()));
        Assert.Throws<PrologException>(() => host.Prove(predicate, ReadOnlySpan<PrologInput>.Empty));
        Assert.True(host.Prove(predicate, PrologInput.Integer(1), PrologInput.Atom("a")));
        Assert.Equal(2, host.CallAll(predicate, PrologInput.Output, PrologInput.Output).Count());

        var oversized = new PrologPredicate(0, "oversized", Machine.ArgumentRegisterCount);
        var tooMany = new PrologInput[Machine.ArgumentRegisterCount];
        var error = Assert.Throws<PrologException>(() => host.Prove(oversized, tooMany.AsSpan()));
        Assert.Contains("representation_error(max_arity)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsultRejectsExcessiveSourceDepthAndRemainsUsable()
    {
        using var engine = new PrologEngine();
        LoadResult loaded = engine.ConsultText(new string('(', 20_000) + "a" + new string(')', 20_000) + ".", "deep.pl");
        Assert.False(loaded.Success);
        Assert.Equal("DPL0012", Assert.Single(loaded.Diagnostics).Id);
        Assert.True(engine.Query("true").Prove());
    }

    [Theory]
    [InlineData("X = f(X)")]
    [InlineData("X = [a|X]")]
    [InlineData("X = f(Y), Y = g(X)")]
    public void QueryRejectsCyclicHostValuesAndCanBeReused(string goal)
    {
        using var engine = new PrologEngine();
        PrologException first = Assert.Throws<PrologException>(() => engine.Query(goal).FirstOrDefault());
        Assert.Contains("representation_error(cyclic_term)", first.Message, StringComparison.Ordinal);
        PrologException all = Assert.Throws<PrologException>(() => engine.Query(goal).Solutions().ToArray());
        Assert.Contains("representation_error(cyclic_term)", all.Message, StringComparison.Ordinal);
        Assert.True(engine.Query("true").Prove());
    }

    [Theory]
    [InlineData("cyclic(X) :- X = f(X).")]
    [InlineData("cyclic(X) :- X = [a|X].")]
    [InlineData("cyclic(X) :- X = f(Y), Y = g(X).")]
    public void HostRejectsCyclicOutputs(string source)
    {
        using PrologEngine engine = NewEngine(source);
        var host = new PrologHost(engine.Machine);
        PrologPredicate predicate = host.Bind("cyclic", 1);
        PrologException first = Assert.Throws<PrologException>(() => host.CallOnce(predicate, PrologInput.Output));
        Assert.Contains("representation_error(cyclic_term)", first.Message, StringComparison.Ordinal);
        PrologException all = Assert.Throws<PrologException>(() => host.CallAll(predicate, PrologInput.Output).ToArray());
        Assert.Contains("representation_error(cyclic_term)", all.Message, StringComparison.Ordinal);
        Assert.True(engine.Query("true").Prove());
    }

    [Fact]
    public void MarshallingAcceptsSharedFiniteStructuresAndDeepFiniteValues()
    {
        using var engine = new PrologEngine();
        PrologSolution solution = engine.Query("X = f(a), Y = pair(X,X)").FirstOrDefault()!;
        Assert.Equal("pair(f(a),f(a))", solution["Y"].ToString());
        engine.ConsultOrThrow("nest(0, a) :- !. nest(N, f(X)) :- M is N-1, nest(M, X).", "nest.pl");
        PrologValue value = engine.Query("nest(20000, X)").FirstOrDefault()!["X"];
        for (var i = 0; i < 20_000; i++)
        {
            value = Assert.Single(Assert.IsType<PrologCompound>(value).Arguments);
        }
        Assert.Equal("a", Assert.IsType<PrologAtom>(value).Name);
    }

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
        using PrologEngine engine = NewEngine("colour(red).\ncolour(green).\ncolour(blue).");

        string[] colours = [.. engine.Query("colour(C)").Solutions().Select(s => s["C"].ToString())];

        Assert.Equal(["red", "green", "blue"], colours);
    }

    [Fact]
    public void ReportsEveryVariableOfTheGoal()
    {
        using PrologEngine engine = NewEngine("pair(a, 1).\npair(b, 2).");

        PrologQuery query = engine.Query("pair(Name, Number)");

        Assert.Equal(["Name", "Number"], query.VariableNames);
        Assert.Equal(["a=1", "b=2"], query.Solutions().Select(s => $"{s["Name"]}={s["Number"]}"));
    }

    [Fact]
    public void YieldsOneEmptySolutionForAGroundGoalThatSucceeds()
    {
        using PrologEngine engine = NewEngine();
        PrologSolution solution = Assert.Single(engine.Query("1 < 2").Solutions());

        Assert.Empty(solution.Bindings);
        Assert.Equal("true", solution.ToString());
    }

    [Fact]
    public void YieldsNothingForAGoalThatFails()
    {
        using PrologEngine engine = NewEngine();
        Assert.Empty(engine.Query("1 > 2").Solutions());
    }

    [Fact]
    public void ProveReportsSuccessWithoutMarshalling()
    {
        using PrologEngine engine = NewEngine("ok.");

        Assert.True(engine.Query("ok").Prove());
        Assert.False(engine.Query("fail").Prove());
    }

    [Fact]
    public void FirstOrDefaultStopsAfterOneAnswer()
    {
        using PrologEngine engine = NewEngine("n(1).\nn(2).");

        Assert.Equal("1", engine.Query("n(X)").FirstOrDefault()?["X"].ToString());
        Assert.Null(engine.Query("n(99)").FirstOrDefault());
    }

    [Fact]
    public void SolutionsAreProducedLazilySoAnInfiniteGoalIsUsable()
    {
        using PrologEngine engine = NewEngine();

        long[] first =
        [
            .. engine.Query("between(1, 1000000000, X)").Solutions().Take(4).Select(s => ((PrologInteger)s["X"]).Value),
        ];

        Assert.Equal([1L, 2L, 3L, 4L], first);
    }

    [Fact]
    public void AQueryCanBeRunMoreThanOnce()
    {
        using PrologEngine engine = NewEngine("n(1).\nn(2).");
        PrologQuery query = engine.Query("n(X)");

        Assert.Equal(2, query.Solutions().Count());
        Assert.Equal(2, query.Solutions().Count());
    }

    [Fact]
    public void MarshalsEachTermKind()
    {
        using PrologEngine engine = NewEngine();

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
        using PrologEngine engine = NewEngine();

        PrologSolution solution = Assert.Single(engine.Query("X = [a,b,c]").Solutions());

        Assert.True(solution["X"].TryGetList(out IReadOnlyList<PrologValue> items));
        Assert.Equal(["a", "b", "c"], items.Select(i => i.ToString()));
    }

    [Fact]
    public void APartialListIsNotAProperList()
    {
        using PrologEngine engine = NewEngine();

        PrologSolution solution = Assert.Single(engine.Query("X = [a|_]").Solutions());

        Assert.False(solution["X"].TryGetList(out _));
    }

    [Fact]
    public void SolutionsSurviveThePursuitOfLaterOnes()
    {
        // Bindings are marshalled as each answer is produced, so collecting them all is safe even
        // though backtracking discards the heap they came from.
        using PrologEngine engine = NewEngine("n(1).\nn(2).\nn(3).");

        List<PrologSolution> all = [.. engine.Query("n(X)").Solutions()];

        Assert.Equal(["1", "2", "3"], all.Select(s => s["X"].ToString()));
    }

    [Fact]
    public void AnUncaughtBallReachesTheHost()
    {
        using PrologEngine engine = NewEngine();

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
        using PrologEngine engine = NewEngine();

        Assert.True(engine.Query("assertz(fact(one)), assertz(fact(two))").Prove());
        Assert.Equal(["one", "two"], engine.Query("fact(X)").Solutions().Select(s => s["X"].ToString()));
    }
}
