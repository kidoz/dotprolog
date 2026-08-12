using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The engine-scoped global variables of ADR 0042: nb_setval/2 and nb_getval/2 survive
/// backtracking through a detached copy, while b_setval/2 and b_getval/2 hold the live term and
/// are undone by trail unwinding. The tests pin transitions — set, backtrack, cut, rethrow,
/// reset — rather than only final answers.
/// </summary>
public sealed class GlobalVariableTests
{
    [Theory]
    [InlineData("nb_setval(k, f(1, x)), nb_getval(k, V), write(V)", "f(1,x)")]
    [InlineData("nb_setval(k, 1), nb_setval(k, 2), nb_getval(k, V), write(V)", "2")]
    [InlineData("( nb_setval(k, saved), fail ; nb_getval(k, V) ), write(V)", "saved")]
    [InlineData("b_setval(k, f(a)), b_getval(k, V), write(V)", "f(a)")]
    [InlineData("nb_setval(k, 1), b_getval(k, V), write(V)", "1")]
    [InlineData("b_setval(k, 1), nb_getval(k, V), write(V)", "1")]
    public void SetAndGetShareOneStore(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    // nb_setval stores a copy: binding the original's variables afterwards must not reach the
    // stored value, and reading materializes a fresh copy each time.
    [Fact]
    public void NonBacktrackableValuesAreDetachedCopies() =>
        Assert.Equal(
            "fresh",
            PrologTestHost.RunGoal("nb_setval(k, f(X)), X = bound, nb_getval(k, f(Y)), ( var(Y) -> write(fresh) ; write(Y) )")
        );

    // b_setval stores the live term: bindings made after the assignment are visible through it.
    [Fact]
    public void BacktrackableValuesStayLive() =>
        Assert.Equal("1", PrologTestHost.RunGoal("b_setval(k, f(X)), X = 1, b_getval(k, f(Y)), write(Y)"));

    [Fact]
    public void BacktrackingRestoresThePreviousBacktrackableValue() =>
        Assert.Equal("1", PrologTestHost.RunGoal("b_setval(k, 1), ( b_setval(k, 2), fail ; b_getval(k, V) ), write(V)"));

    [Fact]
    public void BacktrackingRemovesAnAssignmentThatCreatedTheName() =>
        Assert.Equal(
            "existence_error(variable,k)",
            PrologTestHost.RunGoal("( b_setval(k, 1), fail ; true ), catch(b_getval(k, _), error(E, _), true), write(E)")
        );

    // Each solution of the enumeration sees its own assignment, and exhausting the enumeration
    // unwinds every one of them back to the outer value.
    [Fact]
    public void NestedChoicePointsUnwindAssignmentsInOrder() =>
        Assert.Equal(
            "[1,2]-0",
            PrologTestHost.RunGoal(
                "b_setval(k, 0), findall(V, ( member(X, [1, 2]), b_setval(k, X), b_getval(k, V) ), Vs),"
                    + " b_getval(k, End), write(Vs-End)"
            )
        );

    // Cut discards a choice point without unwinding the trail, so the assignment survives the
    // cut and is readable afterwards; the failure that follows backtracks below the sentinel and
    // removes it. call/1 confines the cut so the outer disjunction stays available.
    [Fact]
    public void CutKeepsTheAssignmentAndLaterBacktrackingUndoesIt() =>
        Assert.Equal(
            "2existence_error(variable,k)",
            PrologTestHost.RunGoal(
                "( call(( b_setval(k, 2), ( true ; true ), !, b_getval(k, V), write(V), fail ))"
                    + " ; catch(b_getval(k, _), error(E, _), true), write(E) )"
            )
        );

    [Fact]
    public void CatchUnwindingUndoesBacktrackableAssignments() =>
        Assert.Equal(
            "existence_error(variable,k)",
            PrologTestHost.RunGoal(
                "catch(( b_setval(k, 1), throw(ball) ), ball, true), catch(b_getval(k, _), error(E, _), true), write(E)"
            )
        );

    // The backtrackable undo restores the state before the b_setval even when an nb_setval wrote
    // the same name in between — the same slot semantics SWI has.
    [Fact]
    public void UndoRestoresThePreBacktrackableState() =>
        Assert.Equal(
            "base",
            PrologTestHost.RunGoal("nb_setval(k, base), ( b_setval(k, temp), fail ; true ), nb_getval(k, V), write(V)")
        );

    [Theory]
    [InlineData("nb_setval(_, 1)", "instantiation_error")]
    [InlineData("nb_setval(f(a), 1)", "type_error(atom,f(a))")]
    [InlineData("b_getval(_, _)", "instantiation_error")]
    [InlineData("nb_getval(7, _)", "type_error(atom,7)")]
    [InlineData("nb_getval(unset, _)", "existence_error(variable,unset)")]
    [InlineData("b_getval(unset, _)", "existence_error(variable,unset)")]
    public void GlobalVariableArgumentsAreValidated(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(({goal}), error(E, _), true), write(E)"));

    // Non-backtrackable values survive from one top-level goal to the next on the same engine;
    // backtrackable assignments are unwound when the goal ends.
    [Fact]
    public void GoalBoundariesKeepNonBacktrackableAndDropBacktrackableValues()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };
        Assert.True(engine.ConsultText("").Success);

        Assert.Equal(RunResult.Success, engine.RunGoal("nb_setval(keep, 7), b_setval(drop, 1)", out _));
        Assert.Equal(
            RunResult.Success,
            engine.RunGoal("nb_getval(keep, V), write(V), catch(b_getval(drop, _), error(E, _), true), write(E)", out _)
        );
        Assert.Equal("7existence_error(variable,drop)", output.ToString());
    }

    [Fact]
    public void StrictModeRejectsGlobalVariables()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        LoadResult loaded = engine.ConsultText("p :- nb_setval(k, 1).", "strict.pl");

        DotProlog.Syntax.Diagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("nb_setval/2", diagnostic.Message, StringComparison.Ordinal);
    }
}
