using System.Text.RegularExpressions;
using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

public sealed partial class GreymereAdventureTests
{
    private const string ReachCourtyard = "talk(reeve).\ngo(north).\ngo(north).\ngo(north).\n";
    private const string ClearGatehouse = "go(east).\nattack.\nattack.\ntake(lantern).\ntake(rope).\ngo(west).\n";

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
    [InlineData("off", "winning_path.txt")]
    [InlineData("ascii", "winning_path.txt")]
    [InlineData("on", "winning_path.txt")]
    [InlineData("off", "mercy_path.txt")]
    [InlineData("ascii", "mercy_path.txt")]
    [InlineData("on", "mercy_path.txt")]
    public void WinningPathPreservesGameStateInEveryMode(string mode, string path)
    {
        var commands = $"graphics({mode}).\n" + File.ReadAllText(SamplePath(path));
        (PrologEngine engine, var output) = Play(commands);
        Assert.EndsWith("*** YOU ARE VICTORIOUS ***\n", StripColors(output));
        Assert.True(engine.Query("greymere_adventure:flag(victory), greymere_adventure:holding(ember_crown)").Prove());
        Assert.False(engine.Query("greymere_adventure:alive(_)").Prove());
        Assert.True(engine.Query("greymere_adventure:flag(bells_awakened)").Prove());
        Assert.Equal(path == "mercy_path.txt", engine.Query("greymere_adventure:flag(knight_redeemed)").Prove());
        Assert.Equal(path == "mercy_path.txt", engine.Query("greymere_adventure:flag(bellkeeper_rescued)").Prove());
        Assert.Equal(path == "mercy_path.txt", engine.Query("greymere_adventure:flag(tonic_brewed)").Prove());
        Assert.Contains(
            path == "mercy_path.txt" ? "Tomas rings the village bell" : "The village bell is silent",
            output,
            StringComparison.Ordinal
        );
        Assert.Equal(mode == "on", output.Contains('\u001b'));
        Assert.Equal(mode != "off", output.Contains("G L O A M W A T C H", StringComparison.Ordinal));
        Assert.Equal(mode != "off", StripColors(output).Contains("HP [", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("winning_path.txt")]
    [InlineData("mercy_path.txt")]
    public void AsciiAndAnsiModesRenderTheSameVisibleContent(string filename)
    {
        var path = File.ReadAllText(SamplePath(filename));
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
    public void CorrectedCommandAfterTypoEnablesGraphics()
    {
        var output = Play("graphivs(on).\ngraphics(on).\nquit.\n").Output;
        Assert.Contains("The tale does not understand graphivs(on)", output, StringComparison.Ordinal);
        Assert.Contains("Graphics: on.", output, StringComparison.Ordinal);
        Assert.Contains("G L O A M W A T C H", output, StringComparison.Ordinal);
        Assert.EndsWith("Farewell.\n", output);
    }

    [Theory]
    [InlineData("graphics(,).\n")]
    [InlineData("\u001b[Agraphics(on).\n")]
    [InlineData("\u200bgraphics(on).\n")]
    [InlineData("graphics(,). graphics(,).\n")]
    public void MalformedCommandPreservesProgressAndAllowsRetry(string malformed)
    {
        (PrologEngine engine, var output) = Play("talk(reeve).\n" + malformed + "graphics(on).\nstatus.\nquit.\n");
        Assert.True(
            engine
                .Query(
                    "greymere_adventure:flag(quest_begun), greymere_adventure:holding(keep_key), greymere_adventure:here(village_square), greymere_adventure:player_hp(14)"
                )
                .Prove()
        );
        Assert.Contains("That input is not a valid Prolog command", output, StringComparison.Ordinal);
        Assert.Contains("Graphics: on.", output, StringComparison.Ordinal);
        Assert.Contains("Health: 14/14.", output, StringComparison.Ordinal);
        Assert.EndsWith("Farewell.\n", output);
    }

    [Fact]
    public void IncompleteCommandAtEndOfInputExitsCleanlyAfterReportingError()
    {
        var output = Play("graphics(").Output;
        Assert.Contains("That input is not a valid Prolog command", output, StringComparison.Ordinal);
        Assert.EndsWith("Farewell.\n", output);
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

    [Fact]
    public void WrongBellSequenceResetsAndCompletedRitualStaysOpen()
    {
        const string reachTower = ReachCourtyard + "go(north).\ngo(north).\ngo(up).\n";
        const string wrongNotes = "ring(dawn).\nring(noon).\nring(dusk).\nring(noon).\n";
        (PrologEngine failed, var failedOutput) = Play(reachTower + wrongNotes);
        Assert.False(failed.Query("greymere_adventure:flag(bells_awakened)").Prove());
        Assert.Contains("Begin again", failedOutput, StringComparison.Ordinal);

        (PrologEngine solved, var output) = Play(
            reachTower + wrongNotes + "ring(dawn).\nring(dusk).\nring(noon).\nring(dusk).\njournal.\n"
        );
        Assert.True(solved.Query("greymere_adventure:flag(bells_awakened)").Prove());
        Assert.False(solved.Query("greymere_adventure:flag(bell_dawn); greymere_adventure:flag(bell_dusk)").Prove());
        Assert.Contains("The ritual is complete", output, StringComparison.Ordinal);
        Assert.Contains("[done] The bell ritual", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ring(noon).", "sun seal burns")]
    [InlineData("take(bone_key).", "no ordinary lock")]
    public void SanctumRequiresBothRitualAndKey(string omittedCommand, string explanation)
    {
        var path = File.ReadAllText(SamplePath("winning_path.txt"));
        path = path[..path.IndexOf("take(ember_crown).", StringComparison.Ordinal)];
        (PrologEngine engine, var output) = Play(path.Replace(omittedCommand, "look.", StringComparison.Ordinal));
        Assert.True(engine.Query("greymere_adventure:here(ossuary), greymere_adventure:alive(morvane)").Prove());
        Assert.False(engine.Query("greymere_adventure:flag(sanctum_unlocked)").Prove());
        Assert.Contains(explanation, output, StringComparison.Ordinal);
    }

    [Fact]
    public void RescueNeedsRopeAndCannotDuplicateItsReward()
    {
        const string blocked = ReachCourtyard + "go(west).\ngo(down).\ntalk(bellkeeper).\n";
        (PrologEngine trapped, var blockedOutput) = Play(blocked);
        Assert.True(trapped.Query("greymere_adventure:here(well_house)").Prove());
        Assert.False(trapped.Query("greymere_adventure:holding(ash_ward)").Prove());
        Assert.Contains("Use a rope", blockedOutput, StringComparison.Ordinal);

        const string rescue =
            ReachCourtyard
            + ClearGatehouse
            + "go(west).\nuse(rope).\ngo(down).\ntalk(bellkeeper).\ndrop(ash_ward).\ntalk(bellkeeper).\nlook.\ngo(up).\n";
        (PrologEngine freed, var output) = Play(rescue);
        Assert.True(
            freed
                .Query(
                    "greymere_adventure:flag(bellkeeper_rescued), greymere_adventure:here(well_house), greymere_adventure:at(ash_ward, prison)"
                )
                .Prove()
        );
        Assert.False(freed.Query("greymere_adventure:holding(ash_ward); greymere_adventure:holding(rope)").Prove());
        Assert.Contains("The cell is empty", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CraftingRequiresRecipeAndIngredientsAndConsumesOneDose()
    {
        const string ingredients = "go(north).\ngo(east).\ntake(moonleaf).\ngo(west).\ngo(south).\n";
        const string commands =
            ingredients + ReachCourtyard + ClearGatehouse + "go(west).\ntake(spring_water).\nbrew(herbal_tonic).\n";
        (PrologEngine unknownRecipe, var failedOutput) = Play(commands);
        Assert.True(
            unknownRecipe.Query("greymere_adventure:holding(moonleaf), greymere_adventure:holding(spring_water)").Prove()
        );
        Assert.False(unknownRecipe.Query("greymere_adventure:holding(herbal_tonic)").Prove());
        Assert.Contains("do not know this recipe", failedOutput, StringComparison.Ordinal);

        (PrologEngine brewed, var output) = Play(
            "talk(herbalist).\nbrew(herbal_tonic).\n" + commands + "brew(herbal_tonic).\nuse(herbal_tonic).\nuse(herbal_tonic).\n"
        );
        Assert.True(brewed.Query("greymere_adventure:flag(tonic_brewed), greymere_adventure:player_hp(14)").Prove());
        Assert.False(
            brewed
                .Query(
                    "greymere_adventure:holding(moonleaf); greymere_adventure:holding(spring_water); greymere_adventure:holding(herbal_tonic)"
                )
                .Prove()
        );
        Assert.Contains("need both moonleaf and spring_water", output, StringComparison.Ordinal);
        Assert.Contains("not carrying herbal_tonic", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardAndRescueWardReduceChargedBossAttack()
    {
        var path = File.ReadAllText(SamplePath("winning_path.txt"));
        path = path[..path.IndexOf("take(ember_crown).", StringComparison.Ordinal)];
        (PrologEngine guarded, var output) = Play(path);
        (PrologEngine exposed, _) = Play(path.Replace("guard.", "look.", StringComparison.Ordinal));
        Assert.True(guarded.Query("greymere_adventure:player_hp(12)").Prove());
        Assert.True(exposed.Query("greymere_adventure:player_hp(8)").Prove());
        Assert.Contains("guard now!", output, StringComparison.Ordinal);
        Assert.False(guarded.Query("greymere_adventure:flag(counter_ready); greymere_adventure:enemy_ready(_)").Prove());

        var mercy = File.ReadAllText(SamplePath("mercy_path.txt"));
        (PrologEngine warded, _) = Play(mercy);
        Assert.True(warded.Query("greymere_adventure:player_hp(13)").Prove());
    }

    [Fact]
    public void RestOnlyWorksInVillageAndMapTracksVisitedRooms()
    {
        const string commands = ReachCourtyard + ClearGatehouse + "rest.\nmap.\n";
        (PrologEngine outside, var output) = Play(commands);
        Assert.True(outside.Query("greymere_adventure:player_hp(12)").Prove());
        Assert.Contains("rest safely only in Greymere", output, StringComparison.Ordinal);
        Assert.Contains("east -> The Fallen Gatehouse", output, StringComparison.Ordinal);
        Assert.Contains("west -> unexplored", output, StringComparison.Ordinal);
        Assert.False(outside.Query("greymere_adventure:visited(well_house)").Prove());

        (PrologEngine rested, _) = Play(commands + "go(south).\ngo(south).\ngo(south).\nrest.\n");
        Assert.True(rested.Query("greymere_adventure:player_hp(14)").Prove());
    }

    [Fact]
    public void IncompleteCommandsCannotSelectItemsOrQuestRewards()
    {
        (PrologEngine engine, var output) = Play("talk(X).\ntake(X).\nuse(X).\ngo(X).\nring(X).\nbrew(X).\n");
        Assert.True(engine.Query("greymere_adventure:here(village_square), greymere_adventure:player_hp(14)").Prove());
        Assert.False(engine.Query("greymere_adventure:flag(_)").Prove());
        Assert.Contains("Use a complete command", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FullHealthDoesNotWasteMedicine()
    {
        var path = File.ReadAllText(SamplePath("mercy_path.txt"));
        path = path[..path.IndexOf("use(herbal_tonic).", StringComparison.Ordinal)];
        (PrologEngine engine, var output) = Play(
            path + "go(up).\ngo(up).\ngo(south).\ngo(south).\ngo(south).\ngo(south).\nrest.\nuse(herbal_tonic).\n"
        );
        Assert.True(engine.Query("greymere_adventure:player_hp(14), greymere_adventure:holding(herbal_tonic)").Prove());
        Assert.Contains("Save the medicine for later", output, StringComparison.Ordinal);
    }

    [Fact]
    public void BrewingInCombatLeavesIngredientsIntact()
    {
        const string garden = "talk(herbalist).\ngo(north).\ngo(east).\ntake(moonleaf).\ngo(west).\ngo(south).\n";
        (PrologEngine engine, var output) = Play(
            garden + ReachCourtyard + "go(west).\ntake(spring_water).\ngo(east).\ngo(east).\nbrew(herbal_tonic).\n"
        );
        Assert.True(
            engine
                .Query(
                    "greymere_adventure:holding(moonleaf), greymere_adventure:holding(spring_water), greymere_adventure:player_hp(14)"
                )
                .Prove()
        );
        Assert.False(engine.Query("greymere_adventure:holding(herbal_tonic)").Prove());
        Assert.Contains("cannot brew medicine with an enemy", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardBonusDoesNotStackAndRetreatLosesIt()
    {
        const string guardTwice = ReachCourtyard + "go(east).\nguard.\nguard.\n";
        (PrologEngine countered, var output) = Play(guardTwice + "attack.\n");
        Assert.True(countered.Query("greymere_adventure:player_hp(12)").Prove());
        Assert.False(countered.Query("greymere_adventure:alive(mire_goblin); greymere_adventure:flag(counter_ready)").Prove());
        Assert.Contains("for 4 damage", output, StringComparison.Ordinal);

        (PrologEngine retreated, _) = Play(guardTwice + "go(west).\ngo(east).\nattack.\n");
        Assert.True(retreated.Query("greymere_adventure:enemy_hp(mire_goblin, 2), greymere_adventure:player_hp(10)").Prove());
    }

    private static string StripColors(string output) => AnsiSgr().Replace(output, string.Empty);

    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex AnsiSgr();
}
