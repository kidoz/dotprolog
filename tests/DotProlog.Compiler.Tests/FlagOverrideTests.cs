using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>Initial flag overrides layered over a language mode's defaults (ADR 0048).</summary>
public sealed class FlagOverrideTests
{
    private static PrologEngine Engine(PrologLanguageMode languageMode, DoubleQuotesMode doubleQuotes) =>
        new(languageMode, new PrologFlagOverrides { DoubleQuotes = doubleQuotes }) { Output = new StringWriter() };

    [Fact]
    public void OverrideSeedsTheInitialValueInExtendedMode()
    {
        PrologEngine engine = Engine(PrologLanguageMode.Extended, DoubleQuotesMode.Chars);

        Assert.Equal(DoubleQuotesMode.Chars, engine.Program.InitialDoubleQuotes);
        Assert.Equal(RunResult.Success, engine.RunGoal("current_prolog_flag(double_quotes, chars)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("\"hi\" == [h,i]", out _));
    }

    [Fact]
    public void OverrideAppliesInStrictIsoMode()
    {
        PrologEngine engine = Engine(PrologLanguageMode.StrictIso, DoubleQuotesMode.Atom);

        Assert.Equal(RunResult.Success, engine.RunGoal("current_prolog_flag(double_quotes, atom)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("\"hi\" == hi", out _));
    }

    [Fact]
    public void OverrideWinsOverTheModeDefault()
    {
        PrologEngine engine = Engine(PrologLanguageMode.Modern, DoubleQuotesMode.Codes);

        Assert.Equal(DoubleQuotesMode.Codes, engine.Program.InitialDoubleQuotes);
        Assert.Equal(RunResult.Success, engine.RunGoal("\"hi\" == [104,105]", out _));
    }

    [Fact]
    public void DirectiveStillOverridesLocallyAndRestoresToTheOverride()
    {
        PrologEngine engine = Engine(PrologLanguageMode.Extended, DoubleQuotesMode.Chars);

        LoadResult loaded = engine.ConsultText(
            """
            :- set_prolog_flag(double_quotes, atom).
            atom_text("ab").

            chars_text("cd").
            """,
            "override.pl"
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunGoal("atom_text(ab)", out _));
        // The directive governed the rest of its own file only; the next unit re-enters at the
        // project override, not at the mode default.
        Assert.Equal(DoubleQuotesMode.Chars, engine.Program.Flags.DoubleQuotes);
        Assert.Equal(RunResult.Success, engine.RunGoal("\"ef\" == [e,f]", out _));
    }

    [Fact]
    public void RedundantOverrideIsANoOp()
    {
        PrologEngine engine = Engine(PrologLanguageMode.Modern, DoubleQuotesMode.Chars);

        Assert.Equal(RunResult.Success, engine.RunGoal("current_prolog_flag(double_quotes, chars)", out _));
    }

    [Fact]
    public void BundledLibrariesAreUnaffectedByAnOverride()
    {
        // Constructing the engine consults the bootstrap and standard libraries; a misread there
        // fails construction, and library predicates keep working under the override.
        PrologEngine engine = Engine(PrologLanguageMode.Extended, DoubleQuotesMode.Atom);

        Assert.Equal(RunResult.Success, engine.RunGoal("atom_length(abc, 3), msort([b,a], [a,b])", out _));
    }
}
