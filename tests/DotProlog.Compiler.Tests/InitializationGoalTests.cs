using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// SWI-Prolog's <c>initialization(Goal, When)</c> in the default mode: <c>now</c> runs Goal where the
/// directive stands, <c>after_load</c> defers it as <c>initialization/1</c> does, and <c>main</c> runs it
/// after everything else and ends the program with SWI's exit status.
/// </summary>
public sealed class InitializationGoalTests
{
    [Fact]
    public void NowRunsInPlaceAndAfterLoadWaitsForTheFile()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };

        Assert.True(
            engine
                .ConsultText(
                    ":- initialization(write(load), after_load).\n:- initialization(write(now), now).\n:- write(directive).\n"
                )
                .Success
        );
        Assert.Equal("nowdirective", output.ToString());
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("nowdirectiveload", output.ToString());
    }

    [Fact]
    public void TheLastMainGoalRunsLastAndHalts()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };

        Assert.True(
            engine
                .ConsultText(
                    ":- initialization(write(a), main).\n:- initialization(write(b), main).\n:- initialization(write(c)).\n"
                )
                .Success
        );
        Assert.Equal(RunResult.Halted, engine.RunPendingGoals());
        Assert.Equal("cb", output.ToString());
        Assert.Equal(0, engine.Machine.ExitCode);
    }

    [Fact]
    public void AMainGoalDefinedInAModuleIsFound()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };

        Assert.True(
            engine
                .ConsultText(":- module(app, [main/0]).\n:- initialization(main, main).\nmain :- helper.\nhelper :- write(ok).\n")
                .Success
        );
        Assert.Equal(RunResult.Halted, engine.RunPendingGoals());
        Assert.Equal("ok", output.ToString());
    }

    [Theory]
    [InlineData(":- initialization(true, foo).", "domain_error(initialization_type,foo)")]
    [InlineData(":- initialization(true, 1).", "type_error(atom,1)")]
    [InlineData(":- initialization(true, _).", "instantiation_error")]
    public void AnUnknownWhenIsAnError(string source, string expected)
    {
        var engine = new PrologEngine { Output = new StringWriter() };

        PrologException exception = Assert.Throws<PrologException>(() => engine.ConsultText(source));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallAtRunTimeRunsTheGoal()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };

        Assert.True(engine.ConsultText(":- initialization(initialization(write(ran), after_load)).").Success);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("ran", output.ToString());
    }

    [Fact]
    public void StrictIsoRejectsIt()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };

        LoadResult loaded = engine.ConsultText(":- initialization(main, main).\nmain.\n");

        Assert.False(loaded.Success);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Id == "DPL1018");
    }
}
