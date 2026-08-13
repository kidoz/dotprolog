using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// setarg/3 and nb_setarg/3: destructive slot assignment restored by the value-undo
/// stack interleaved with trail unwinding. The tests pin transitions — assign, backtrack,
/// reassign, bind-then-assign on one slot — rather than only final answers.
/// </summary>
public sealed class SetArgTests
{
    [Theory]
    [InlineData("T = f(a, b), setarg(1, T, x), write(T)", "f(x,b)")]
    [InlineData("T = f(a, b), setarg(2, T, g(1)), write(T)", "f(a,g(1))")]
    [InlineData("T = f(a), setarg(1, T, X), X = bound, write(T)", "f(bound)")]
    public void SetargAssignsTheSlot(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void BacktrackingRestoresTheAssignedSlot() =>
        Assert.Equal("f(a)", PrologTestHost.RunGoal("T = f(a), ( setarg(1, T, x), fail ; true ), write(T)"));

    // Two assignments to one slot unwind in reverse order: the inner one is undone by the
    // backtrack, the outer one survives it.
    [Fact]
    public void NestedAssignmentsUnwindInReverseOrder() =>
        Assert.Equal("f(b)", PrologTestHost.RunGoal("T = f(a), setarg(1, T, b), ( setarg(1, T, c), fail ; true ), write(T)"));

    // A slot that was bound by unification and then assigned must restore through both stacks in
    // exact reverse chronology: assignment back to the binding, binding back to unbound.
    [Fact]
    public void ABoundThenAssignedSlotRestoresToUnbound() =>
        Assert.Equal(
            "restored",
            PrologTestHost.RunGoal(
                "T = f(X), ( arg(1, T, one), setarg(1, T, y), fail ; true ), ( var(X) -> write(restored) ; write(X) )"
            )
        );

    [Fact]
    public void SolutionsObserveTheAssignmentPerBranch() =>
        Assert.Equal(
            "[f(1),f(2)]-f(a)",
            PrologTestHost.RunGoal(
                "T = f(a), findall(C, ( member(X, [1, 2]), setarg(1, T, X), copy_term(T, C) ), Cs), write(Cs-T)"
            )
        );

    [Theory]
    [InlineData("setarg(2, f(a), x)")]
    [InlineData("setarg(0, f(a), x)")]
    [InlineData("nb_setarg(3, f(a, b), x)")]
    public void AnOutOfRangeIndexFails(string goal) =>
        Assert.Equal("no", PrologTestHost.RunGoal($"( {goal} -> write(yes) ; write(no) )"));

    [Theory]
    [InlineData("setarg(a, f(x), v)", "type_error(integer,a)")]
    [InlineData("setarg(1, atom, v)", "type_error(compound,atom)")]
    [InlineData("setarg(_, f(x), v)", "instantiation_error")]
    [InlineData("setarg(1, _, v)", "instantiation_error")]
    [InlineData("nb_setarg(1, f(a), g(1))", "type_error(atomic,g(1))")]
    public void ArgumentsAreValidated(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(({goal}), error(E, _), true), write(E)"));

    [Fact]
    public void NbSetargRejectsAnUnboundValue() =>
        Assert.Equal(
            "atomic",
            PrologTestHost.RunGoal("catch(nb_setarg(1, f(a), _), error(type_error(T, _), _), true), write(T)")
        );

    // The canonical nb_setarg use: a counter mutated through a failure-driven loop survives every
    // backtrack.
    [Fact]
    public void NbSetargCountsThroughAFailureDrivenLoop() =>
        Assert.Equal(
            "3",
            PrologTestHost.RunGoal(
                "T = counter(0), forall(member(_, [a, b, c]), ( arg(1, T, N), N1 is N + 1, nb_setarg(1, T, N1) )),"
                    + " arg(1, T, C), write(C)"
            )
        );

    [Fact]
    public void NbSetargSurvivesBacktracking() =>
        Assert.Equal("f(x)", PrologTestHost.RunGoal("T = f(a), ( nb_setarg(1, T, x), fail ; true ), write(T)"));

    [Fact]
    public void StrictModeRejectsDestructiveAssignment()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        LoadResult loaded = engine.ConsultText("p :- setarg(1, f(a), x).", "strict.pl");

        DotProlog.Syntax.Diagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("setarg/3", diagnostic.Message, StringComparison.Ordinal);
    }
}
