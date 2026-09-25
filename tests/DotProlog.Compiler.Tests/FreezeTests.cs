using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// <c>freeze/2</c> and <c>frozen/2</c>: a goal frozen on a variable runs when the variable is bound,
/// at the next call, return, builtin, cut, disjunction, or catch frame, in the order SWI-Prolog runs
/// it. Each expectation was checked against SWI-Prolog 10.
/// </summary>
public sealed class FreezeTests
{
    [Theory]
    [InlineData("freeze(X, write(a)), X = 1, write(b)", "ab")]
    [InlineData("freeze(1, write(now))", "now")]
    [InlineData("freeze(X, write(a)), freeze(X, write(b)), X = 1", "ab")]
    [InlineData("freeze(X, write(x)), freeze(Y, write(y)), f(X, Y) = f(1, 2)", "xy")]
    [InlineData("freeze(X, write(a)), atom_length(abc, X), write(b)", "ab")]
    [InlineData("freeze(X, write(w)), X = f(_), write(c)", "wc")]
    [InlineData("freeze(X, (Y = 1)), freeze(Y, write(y)), X = 0, write(end)", "yend")]
    public void AGoalRunsWhenItsVariableIsBound(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("freeze(X, write(a)), X = Y, write(b), Y = 1", "ba")]
    [InlineData("freeze(X, write(x)), freeze(Y, write(y)), X = Y, X = 1", "xy")]
    [InlineData("freeze(X, write(g)), X = Y, Y = Z, Z = 1", "g")]
    [InlineData("freeze(X, write(w)), X = Y, frozen(Y, freeze(V, G)), V == Y, write(G)", "write(w)")]
    public void AliasingMovesTheGoalWithoutRunningIt(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("( freeze(X, fail), X = 1 -> write(yes) ; write(no) )", "no")]
    [InlineData("freeze(X, write(w)), X = 1, ( write(a), fail ; write(b) )", "wab")]
    [InlineData("freeze(X, write(w)), ( X = 1, fail ; write(c) )", "wc")]
    [InlineData("freeze(X, write(w)), ( X = 1 ; X = 2 ), write(X), X == 2", "w1w2")]
    [InlineData("findall(Z, (freeze(X, member(Z, [a, b, c])), X = go), L), write(L)", "[a,b,c]")]
    [InlineData("( ( freeze(X, write(a)), fail ; true ), X = 1, write(unfrozen) )", "unfrozen")]
    [InlineData("freeze(X, write(w)), \\+ \\+ X = 1, var(X), write(still_var)", "wstill_var")]
    public void BacktrackingRestoresFrozenAndWokenGoals(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void HeadUnificationWakesBeforeTheClauseCommits() =>
        Assert.Equal(
            "other",
            PrologTestHost.Run("r(1) :- !.\nr(_) :- write(other).\n:- initialization((freeze(X, fail), r(X))).\n")
        );

    [Fact]
    public void ABuiltinInABodyWakesBeforeTheNextGoal() =>
        Assert.Equal(
            "wokein_q",
            PrologTestHost.Run("q(X) :- X = 1, write(in_q).\n:- initialization((freeze(X, write(woke)), q(X))).\n")
        );

    [Theory]
    [InlineData("catch((freeze(X, throw(bad)), X = 1, write(no)), bad, write(caught))", "caught")]
    [InlineData("freeze(X, throw(bad)), catch(X = 1, bad, write(caught_inside))", "caught_inside")]
    [InlineData("freeze(X, (write(k), nl)), setup_call_cleanup(true, X = 1, write(cleanup))", "k\ncleanup")]
    public void AWokenGoalRunsInsideTheScopeThatBoundIt(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Theory]
    [InlineData("frozen(_, G), write(G)", "true")]
    [InlineData("freeze(X, true), frozen(X, G), G = freeze(V, true), V == X, write(yes)", "yes")]
    [InlineData("freeze(X, a), freeze(X, b), frozen(X, (freeze(_, A), freeze(_, B))), write(A/B)", "a/b")]
    [InlineData("freeze(X, (a, b)), frozen(X, freeze(_, G)), write(G)", "a,b")]
    [InlineData("freeze(X, write(x)), copy_term(X, Y), frozen(Y, freeze(V, G)), V == Y, write(G)", "write(x)")]
    [InlineData("freeze(X, write(x)), X = 1, frozen(X, G), write(G)", "xtrue")]
    public void FrozenReportsTheGoalsStillWaiting(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void AHostEnumeratesSolutionsThroughAWokenGoal()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText("wake(Z) :- freeze(X, member(Z, [1, 2, 3])), X = go.\n").Success);

        var solutions = engine.Query("wake(Z)").Solutions().Select(solution => solution["Z"].ToString()).ToArray();

        Assert.Equal(["1", "2", "3"], solutions);
    }

    [Fact]
    public void StrictIsoRejectsFreeze()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };

        LoadResult loaded = engine.ConsultText(":- initialization(freeze(_, true)).\n");

        Assert.False(loaded.Success);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == "DPL1018");
    }
}
