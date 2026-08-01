using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The opt-in mode that starts <c>double_quotes</c> at <c>chars</c>, and the load-unit scoping that
/// keeps one file's choice of convention out of the next one.
/// </summary>
public sealed class ModernModeTests
{
    [Fact]
    public void ModernModeStartsDoubleQuotesAtChars()
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };

        Assert.Equal(DoubleQuotesMode.Chars, engine.Program.InitialDoubleQuotes);
        Assert.Equal(RunResult.Success, engine.RunGoal("current_prolog_flag(double_quotes, chars)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("\"abc\" == [a,b,c]", out _));
    }

    [Fact]
    public void ModernModeDecomposesAStringAsAListOfChars()
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };

        Assert.True(engine.ConsultText("head(L, Ls) :- \"abc\" = [L|Ls].").Success);
        Assert.Equal(RunResult.Success, engine.RunGoal("head(a, [b,c])", out _));
    }

    [Fact]
    public void OtherModesKeepTheIsoInitialValue()
    {
        Assert.Equal(DoubleQuotesMode.Codes, new PrologEngine().Program.InitialDoubleQuotes);
        Assert.Equal(DoubleQuotesMode.Codes, new PrologEngine(PrologLanguageMode.StrictIso).Program.InitialDoubleQuotes);
    }

    [Fact]
    public void ModernModeKeepsTheExtendedPredicateSurface()
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };

        // Modern differs from Extended only in its initial flag values, so a bundled extension that
        // StrictIso would reject stays available here.
        Assert.True(engine.ConsultText("p :- member(a, [a]).").Success);
        Assert.Equal(RunResult.Success, engine.RunGoal("p", out _));
    }

    [Fact]
    public void ModernModeReadsTheBundledLibrariesUnderTheIsoValue()
    {
        // The libraries are processor implementation. If they were re-read as chars the engine would
        // not construct, but assert on behavior rather than on construction alone.
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };

        Assert.Equal(RunResult.Success, engine.RunGoal("append([1], [2], [1,2]), msort([b,a], [a,b])", out _));
    }

    [Fact]
    public void DoubleQuotesDirectiveDoesNotEscapeItsLoadUnit()
    {
        var engine = new PrologEngine { Output = new StringWriter() };

        Assert.True(
            engine
                .ConsultText(
                    """
                    :- set_prolog_flag(double_quotes, chars).
                    declared("ab").
                    """
                )
                .Success
        );

        // The directive governed the rest of its own file...
        Assert.Equal(RunResult.Success, engine.RunGoal("declared([a,b])", out _));

        // ...but the entering value came back, so the next unit reads under the mode's convention.
        Assert.Equal(RunResult.Success, engine.RunGoal("current_prolog_flag(double_quotes, codes)", out _));
        Assert.True(engine.ConsultText("later(\"ab\").").Success);
        Assert.Equal(RunResult.Success, engine.RunGoal("later([0'a, 0'b])", out _));
    }

    [Fact]
    public void ModernModeRestoresCharsAfterALoadUnitOptsOut()
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };

        Assert.True(
            engine
                .ConsultText(
                    """
                    :- set_prolog_flag(double_quotes, codes).
                    iso_style("ab").
                    """
                )
                .Success
        );

        Assert.Equal(RunResult.Success, engine.RunGoal("iso_style([0'a, 0'b])", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("current_prolog_flag(double_quotes, chars)", out _));
    }

    [Theory]
    [InlineData("extended", PrologLanguageMode.Extended)]
    [InlineData("strict-iso", PrologLanguageMode.StrictIso)]
    [InlineData("StrictIso", PrologLanguageMode.StrictIso)]
    [InlineData("modern", PrologLanguageMode.Modern)]
    [InlineData("  Modern  ", PrologLanguageMode.Modern)]
    public void ModeNamesParseCaseInsensitively(string text, PrologLanguageMode expected)
    {
        Assert.True(PrologLanguageModes.TryParse(text, out PrologLanguageMode parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("chars")]
    [InlineData("iso")]
    public void UnknownModeNamesAreRejected(string? text)
    {
        Assert.False(PrologLanguageModes.TryParse(text, out _));
    }
}
