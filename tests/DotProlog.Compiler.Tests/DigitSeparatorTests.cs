using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Digit separators (issue #14): in <c>Modern</c> an underscore and following layout text — white
/// space and comments — separate digit groups of an integer, radix, or rational literal, in source
/// and in <c>number_chars/2</c> and <c>number_codes/2</c> alike, as SWI-Prolog reads them. Strict
/// ISO mode has no such token, and floats never take separators.
/// </summary>
public sealed class DigitSeparatorTests
{
    [Theory]
    [InlineData("X = 1_000", "X == 1000")]
    [InlineData("X = 1_ 000", "X == 1000")]
    [InlineData("X = 1_ /**/ 000", "X == 1000")]
    [InlineData("X = 1_\n 000", "X == 1000")]
    [InlineData("X = 1_000_000_000_000_000_000_000", "X =:= 1000000000000000000000")]
    [InlineData("X = 0xFF_FF", "X == 65535")]
    [InlineData("X = 0o7_7", "X == 63")]
    [InlineData("X = 0b1010_1010", "X == 170")]
    [InlineData("X = 1_0r3", "X == 10r3")]
    [InlineData("X = 1r3_0", "X == 1r30")]
    [InlineData("X = -1_000", "X == -1000")]
    [InlineData("X = - 1_000", "X == -(1000)")]
    [InlineData("X = 0'_", "X == 95")]
    public void SourceReadsSeparatedIntegersInModern(string body, string check)
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };
        Assert.True(engine.ConsultText($"t(X) :- {body}.\n", "test.pl").Success);
        Assert.True(engine.Query($"t(X), {check}").Prove());
    }

    [Theory]
    [InlineData("X = 1__000.\n")]
    [InlineData("X = 1000_.\n")]
    [InlineData("X = 1_5e2.\n")]
    [InlineData("X = 1.5_5.\n")]
    public void MisplacedAndFloatSeparatorsAreSourceErrorsInModern(string text)
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };
        Assert.False(engine.ConsultText(text, "test.pl").Success);
    }

    [Fact]
    public void StrictIsoSourceHasNoDigitSeparators()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };
        Assert.False(engine.ConsultText("X = 1_000.\n", "test.pl").Success);
    }

    [Fact]
    public void NumericListsReadSeparatedIntegersInModern()
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };
        Assert.True(engine.Query("number_codes(N, [49,95,48,48,48]), N == 1000").Prove());
        Assert.True(engine.Query("atom_codes(A, [49,95,48,48,48]), atom_chars(A, C), number_chars(M, C), M == 1000").Prove());
        Assert.True(engine.Query("number_codes(R, [49,95,48,114,51]), R == 10r3").Prove());
    }

    [Fact]
    public void NumericListsRejectSeparatorsInStrictIso()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = new StringWriter() };
        Assert.True(
            engine.Query("catch(number_codes(_, [49,95,48,48,48]), error(syntax_error(illegal_number), _), true)").Prove()
        );
        Assert.True(
            engine
                .Query(
                    "atom_codes(A, [49,95,48,48,48]), atom_chars(A, C), "
                        + "catch(number_chars(_, C), error(syntax_error(illegal_number), _), true)"
                )
                .Prove()
        );
    }

    [Fact]
    public void NumericListsRejectFloatSeparatorsInModern()
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };
        Assert.True(
            engine.Query("catch(number_codes(_, [49,95,53,46,53]), error(syntax_error(illegal_number), _), true)").Prove()
        );
        Assert.True(
            engine.Query("catch(number_codes(_, [49,46,53,95,53]), error(syntax_error(illegal_number), _), true)").Prove()
        );
    }

    [Fact]
    public void AtomNumberReadsSeparatedIntegers()
    {
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = new StringWriter() };
        Assert.True(engine.Query("atom_number('1_000', N), N == 1000").Prove());
        Assert.True(engine.Query("atom_number('0xFF_FF', H), H == 65535").Prove());
        Assert.True(engine.Query("atom_number('1_0r3', R), R == 10r3").Prove());
        Assert.True(engine.Query("atom_number('0''_', C), C == 95").Prove());
        Assert.True(engine.Query(@"\+ atom_number('1_5.5', _)").Prove());
        Assert.True(engine.Query(@"\+ atom_number('1__000', _)").Prove());
    }

    [Fact]
    public void WritingHasNoSeparators()
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.Modern) { Output = output };
        Assert.True(engine.Query("writeq(1_000_000)").Prove());
        Assert.Equal("1000000", output.ToString());
        Assert.True(engine.Query("number_chars(1000, C), C == ['1','0','0','0']").Prove());
    }
}
