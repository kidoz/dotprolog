using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

public sealed class NativePredicateShadowingTests
{
    private const string Definitions =
        """
            msort(Value, user_sort) :- true.
            between(0, 0, zero).
            between(1, 1, one).
            writeln(Value) :- write(user_write(Value)), nl.
            """ + "\n";

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void SameUnitDefinitionsReplaceNativeCallsAndMetaCalls(PrologLanguageMode mode)
    {
        using var output = new StringWriter();
        using var engine = new PrologEngine(mode) { Output = output };

        var loaded = engine.ConsultText(
            Definitions
                + """

                direct :-
                    msort([b, a], user_sort),
                    between(1, 1, one),
                    writeln(direct).
                meta :-
                    call(msort([], user_sort)),
                    Goal = between(0, 0, zero),
                    call(Goal),
                    call(writeln(meta)).
                :- initialization((direct, meta)).
                """
                + "\n"
                + "\n"
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("user_write(direct)\nuser_write(meta)\n", output.ToString());
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void EarlierUnitDefinitionsReplaceNativeCallsAndMetaCalls(PrologLanguageMode mode)
    {
        using var output = new StringWriter();
        using var engine = new PrologEngine(mode) { Output = output };

        var definitions = engine.ConsultText(Definitions + "\n", "definitions.pl");
        var calls = engine.ConsultText(
            """
            direct :-
                msort([b, a], user_sort),
                between(1, 1, one),
                writeln(direct).
            meta :-
                call(msort([], user_sort)),
                Goal = between(0, 0, zero),
                call(Goal),
                call(writeln(meta)).
            :- initialization((direct, meta)).
            """ + "\n",
            "calls.pl"
        );

        Assert.True(definitions.Success, string.Join("; ", definitions.Diagnostics));
        Assert.True(calls.Success, string.Join("; ", calls.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("user_write(direct)\nuser_write(meta)\n", output.ToString());
        Assert.Equal(RunResult.Success, engine.RunGoal("msort([], user_sort), call(between(0, 0, zero))", out _));
    }

    [Fact]
    public void LaterUnitDefinitionReplacesPreviouslyCompiledNativeCalls()
    {
        using var engine = new PrologEngine();

        var calls = engine.ConsultText(
            """
            direct(Sort, Value) :- msort([], Sort), between(1, 1, Value).
            meta(Sort, Value) :- call(msort([], Sort)), call(between(0, 0, Value)).
            """,
            "calls.pl"
        );
        var definitions = engine.ConsultText(Definitions + "\n", "definitions.pl");

        Assert.True(calls.Success, string.Join("; ", calls.Diagnostics));
        Assert.True(definitions.Success, string.Join("; ", definitions.Diagnostics));
        Assert.True(engine.Query("direct(user_sort, one), meta(user_sort, zero)").Prove());
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void UserSoftCutIsAnOrdinaryPredicateForDirectAndMetaCalls(PrologLanguageMode mode)
    {
        using var output = new StringWriter();
        using var engine = new PrologEngine(mode) { Output = output };

        var loaded = engine.ConsultText(
            """
            '*->'(Left, Right) :- write(user_soft_cut).
            direct :- '*->'(1, 2).
            meta :- call('*->'(3, 4)).
            :- initialization((direct, meta)).
            """ + "\n"
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("user_soft_cutuser_soft_cut", output.ToString());
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void NativeReplacementRemainsAStaticProcedure(PrologLanguageMode mode)
    {
        using var engine = new PrologEngine(mode);
        var loaded = engine.ConsultText("msort(a, b).\n");

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(
            RunResult.Success,
            engine.RunGoal(
                "catch(assertz(msort(a, b)), " + "error(permission_error(modify, static_procedure, msort/2), _), true)",
                out _
            )
        );
    }

    [Fact]
    public void StrictModeStillRejectsAnUnreplacedNativeExtension()
    {
        using var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        var loaded = engine.ConsultText("direct :- msort([b, a], []).\n");

        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(CompilerDiagnosticIds.StrictIsoViolation, diagnostic.Id);
        Assert.Contains("msort/2", diagnostic.Message, StringComparison.Ordinal);

        Assert.True(engine.ConsultText("invoke(Goal) :- call(Goal).\n").Success);
        var exception = Assert.Throws<PrologException>(() => engine.RunGoal("invoke(msort([b, a], []))", out _));
        Assert.Contains(
            "permission_error(access, implementation_specific_feature, msort/2)",
            exception.Message,
            StringComparison.Ordinal
        );
    }
}
