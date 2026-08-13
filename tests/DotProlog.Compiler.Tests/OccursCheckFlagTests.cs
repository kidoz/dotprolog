using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The occurs_check flag: false is the ISO default, true fails a cycle-creating
/// general unification, and error raises occurs_check(Var, Term) the way SWI-Prolog 10 does.
/// Write-mode head unification is a documented unchecked window, pinned below.
/// </summary>
public sealed class OccursCheckFlagTests
{
    [Theory]
    [InlineData("( X = f(X) -> write(cycled) ; write(failed) )", "cycled")]
    [InlineData("set_prolog_flag(occurs_check, true), ( X = f(X) -> write(cycled) ; write(failed) )", "failed")]
    [InlineData("set_prolog_flag(occurs_check, true), ( p(X, a) = p(f(X), a) -> write(cycled) ; write(failed) )", "failed")]
    [InlineData("set_prolog_flag(occurs_check, true), X = f(Y), Y = ok, write(X)", "f(ok)")]
    [InlineData("set_prolog_flag(occurs_check, true), set_prolog_flag(occurs_check, false), X = f(X), write(cycled)", "cycled")]
    public void TheFlagGovernsGeneralUnification(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void ErrorModeRaisesTheSwiErrorTerm() =>
        Assert.Equal(
            "caught",
            PrologTestHost.RunGoal(
                "set_prolog_flag(occurs_check, error), catch(X = f(X), error(occurs_check(_, _), _), write(caught))"
            )
        );

    [Fact]
    public void ErrorModeReportsTheInnermostPair() =>
        Assert.Equal(
            "one",
            PrologTestHost.RunGoal(
                "set_prolog_flag(occurs_check, error),"
                    + " catch(p(X, a) = p(f(X), b), error(occurs_check(V, T), _), true),"
                    + " ( T == f(V) -> write(one) ; write(T) )"
            )
        );

    [Fact]
    public void ErrorModeStillFailsAPlainMismatch() =>
        Assert.Equal(
            "failed",
            PrologTestHost.RunGoal("set_prolog_flag(occurs_check, error), ( a = b -> write(unified) ; write(failed) )")
        );

    // Head unification through GetValue — a repeated plain variable argument — goes through the
    // general unifier and is checked.
    [Fact]
    public void AliasedHeadArgumentsAreChecked() =>
        Assert.Equal(
            "failed",
            PrologTestHost.Run(
                """
                same(X, X).
                :- initialization((
                    set_prolog_flag(occurs_check, true),
                    ( same(Y, f(Y)) -> write(cycled) ; write(failed) )
                )).
                """
            )
        );

    // The documented divergence: write-mode head unification binds before it fills, so
    // p(W, W) against p(X, f(X)) builds the rational tree SWI would reject. This test pins the
    // current behavior; closing the window needs structure-completion markers in the bytecode.
    [Fact]
    public void WriteModeHeadUnificationIsTheDocumentedUncheckedWindow() =>
        Assert.Equal(
            "cycled",
            PrologTestHost.Run(
                """
                embed(X, f(X)).
                :- initialization((
                    set_prolog_flag(occurs_check, true),
                    ( embed(W, W) -> write(cycled) ; write(failed) )
                )).
                """
            )
        );

    [Fact]
    public void UnifyWithOccursCheckIgnoresTheFlag() =>
        Assert.Equal(
            "failed-cycled",
            PrologTestHost.RunGoal(
                "( unify_with_occurs_check(X, f(X)) -> write(cycled) ; write(failed) ), write(-),"
                    + " ( Y = f(Y) -> write(cycled) ; write(failed) )"
            )
        );

    [Fact]
    public void TheFlagEnumeratesInExtendedMode() =>
        Assert.Equal("false", PrologTestHost.RunGoal("current_prolog_flag(occurs_check, V), write(V)"));

    [Fact]
    public void TheFlagRoundTripsItsValues() =>
        Assert.Equal(
            "true-error-false",
            PrologTestHost.RunGoal(
                "set_prolog_flag(occurs_check, true), current_prolog_flag(occurs_check, A),"
                    + " set_prolog_flag(occurs_check, error), current_prolog_flag(occurs_check, B),"
                    + " set_prolog_flag(occurs_check, false), current_prolog_flag(occurs_check, C),"
                    + " write(A-B-C)"
            )
        );

    [Fact]
    public void ABadValueIsAFlagValueDomainError() =>
        Assert.Equal(
            "domain_error(flag_value,occurs_check+maybe)",
            PrologTestHost.RunGoal("catch(set_prolog_flag(occurs_check, maybe), error(E, _), true), write(E)")
        );

    [Fact]
    public void StrictModeDoesNotHaveTheFlag()
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output };

        Assert.True(
            engine
                .ConsultText(
                    """
                    :- initialization((
                        catch(set_prolog_flag(occurs_check, true), error(E, _), true),
                        write(E),
                        ( current_prolog_flag(occurs_check, _) -> write(enumerated) ; write(absent) )
                    )).
                    """
                )
                .Success
        );
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("domain_error(prolog_flag,occurs_check)absent", output.ToString());
    }
}
