using System.Text.RegularExpressions;
using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

public sealed partial class GreymereAdventureTests
{
    private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "GreymereAdventure", name);

    private static (PrologEngine Engine, string Output) Play(string commands)
    {
        using var input = new StringReader(commands);
        using var output = new StringWriter();
        var engine = new PrologEngine { Input = input, Output = output };
        LoadResult loaded = engine.ConsultFile(SamplePath("greymere_adventure.pl"));
        Assert.Empty(loaded.Diagnostics);
        Assert.True(loaded.Success);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        // Queries below inspect state only, after the captured session has finished.
        engine.Input = TextReader.Null;
        engine.Output = TextWriter.Null;
        return (engine, output.ToString());
    }

    [Theory]
    [InlineData("off")]
    [InlineData("ascii")]
    [InlineData("on")]
    public void WinningPathPreservesGameStateInEveryMode(string mode)
    {
        var commands = $"graphics({mode}).\n" + File.ReadAllText(SamplePath("winning_path.txt"));
        (PrologEngine engine, var output) = Play(commands);
        Assert.EndsWith("*** YOU ARE VICTORIOUS ***\n", StripColors(output));
        Assert.True(engine.Query("greymere_adventure:flag(victory), greymere_adventure:holding(ember_crown)").Prove());
        Assert.False(engine.Query("greymere_adventure:alive(_)").Prove());
        Assert.Equal(mode == "on", output.Contains('\u001b'));
        Assert.Equal(mode != "off", output.Contains("G L O A M W A T C H", StringComparison.Ordinal));
        Assert.Equal(mode != "off", StripColors(output).Contains("HP [", StringComparison.Ordinal));
    }

    [Fact]
    public void AsciiAndAnsiModesRenderTheSameVisibleContent()
    {
        var path = File.ReadAllText(SamplePath("winning_path.txt"));
        var ascii = Play("graphics(ascii).\n" + path).Output;
        var ansi = Play("graphics(on).\n" + path).Output;
        Assert.Equal(ascii.Replace("Graphics: ascii.", "Graphics: on.", StringComparison.Ordinal), StripColors(ansi));
        // Every frame and art row fits the declared 60-column layout.
        foreach (var line in ascii.Split('\n').Where(line => line.StartsWith('|') || line.StartsWith('+')))
        {
            Assert.Equal(60, line.Length);
        }
    }

    [Fact]
    public void DefaultAndDisabledOutputContainNoTerminalControls()
    {
        var plain = Play("status.\nquit.\n").Output;
        Assert.DoesNotContain('\u001b', plain);
        Assert.DoesNotContain("HP [", plain);
        var switched = Play("graphics(on).\ngraphics(on).\ngraphics(off).\nstatus.\nquit.\n").Output;
        var disabled = switched[(switched.IndexOf("Graphics: off.", StringComparison.Ordinal))..];
        Assert.DoesNotContain('\u001b', disabled);
        Assert.DoesNotContain("HP [", disabled);
        Assert.Contains("Health: 14/14.", disabled, StringComparison.Ordinal);
        Assert.Contains("\u001b[0m", switched, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("purple")]
    [InlineData("X")]
    [InlineData("on(extra)")]
    public void InvalidGraphicsModeLeavesTheGamePlayable(string mode)
    {
        var output = Play($"graphics(ascii).\ngraphics({mode}).\nstatus.\nquit.\n").Output;
        Assert.Contains("Choose graphics(on)., graphics(ascii)., or graphics(off).", output, StringComparison.Ordinal);
        Assert.Contains("HP [##############] 14/14", output, StringComparison.Ordinal);
        Assert.EndsWith("Farewell.\n", output);
        Assert.DoesNotContain('\u001b', output);
    }

    [Fact]
    public void DefeatedEnemyPortraitDisappearsOnLook()
    {
        const string commands =
            "graphics(ascii).\ntalk(reeve).\ngo(north).\ngo(north).\ngo(north).\ngo(east).\nattack.\nattack.\nlook.\nquit.\n";
        var output = Play(commands).Output;
        Assert.Contains("MIRE GOBLIN", output, StringComparison.Ordinal);
        var afterDefeat = output[(output.IndexOf("The goblin drops", StringComparison.Ordinal))..];
        Assert.DoesNotContain("MIRE GOBLIN", afterDefeat, StringComparison.Ordinal);
        Assert.DoesNotContain("DANGER:", afterDefeat, StringComparison.Ordinal);
        Assert.Contains("HP [############--] 12/14", afterDefeat, StringComparison.Ordinal);
    }

    [Fact]
    public void DeathShowsAnEmptyHealthBarAndEnding()
    {
        const string commands =
            "graphics(on).\ntalk(reeve).\ngo(north).\ngo(north).\ngo(north).\ngo(east).\nattack.\nattack.\ntake(lantern).\ngo(west).\ngo(north).\ngo(down).\ngo(down).\nattack.\nattack.\nattack.\nattack.\n";
        // Enter the ossuary with only the knife: the warden kills the hero before its fifth hit.
        var visible = StripColors(Play(commands).Output);
        Assert.Contains("HP [--------------] 0/14", visible, StringComparison.Ordinal);
        Assert.Contains("THE STAR BURNS ON", visible, StringComparison.Ordinal);
        Assert.EndsWith("*** THE END ***\n", visible);
    }

    private static string StripColors(string output) => AnsiSgr().Replace(output, string.Empty);

    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex AnsiSgr();
}
