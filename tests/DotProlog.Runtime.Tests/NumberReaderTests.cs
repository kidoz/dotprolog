using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

/// <summary>
/// The runtime reads <c>number_chars/2</c> and <c>number_codes/2</c> lists by itself, so a program
/// with no compiler attached answers exactly as one with a compiler does.
/// </summary>
public sealed class NumberReaderTests
{
    [Theory]
    [InlineData("- 1", -1)]
    [InlineData("/**/1", 1)]
    [InlineData("'-'1", -1)]
    [InlineData("- /**/1", -1)]
    [InlineData("0'\\n", 10)]
    public void ReadsLayoutCommentsAndAMinusBeforeTheNumberInStrictIso(string text, long expected)
    {
        Assert.Equal(
            NumberReader.Outcome.Number,
            NumberReader.Read(
                text,
                codePoints: true,
                rationals: true,
                isoReading: true,
                digitSeparators: false,
                out PrologNumber number
            )
        );
        Assert.Equal(expected, number.Integer);
    }

    [Theory]
    [InlineData("-1", -1)]
    [InlineData(" -1", -1)]
    [InlineData("/**/1", 1)]
    [InlineData("0'\\n", 10)]
    public void ReadsAMinusOnlyDirectlyBeforeTheDigitsOtherwise(string text, long expected)
    {
        Assert.Equal(
            NumberReader.Outcome.Number,
            NumberReader.Read(
                text,
                codePoints: true,
                rationals: true,
                isoReading: false,
                digitSeparators: true,
                out PrologNumber number
            )
        );
        Assert.Equal(expected, number.Integer);
    }

    [Theory]
    [InlineData("- 1")]
    [InlineData("'-'1")]
    [InlineData("- /**/1")]
    [InlineData("-%\n0")]
    public void RejectsASeparatedOrQuotedMinusOutsideStrictIso(string text) =>
        Assert.Equal(
            NumberReader.Outcome.NotANumber,
            NumberReader.Read(text, codePoints: true, rationals: true, isoReading: false, digitSeparators: true, out _)
        );

    [Theory]
    [InlineData("1_000", 1000)]
    [InlineData("1_000_000", 1000000)]
    [InlineData("1_ 000", 1000)]
    [InlineData("1_ /**/ 000", 1000)]
    [InlineData("1_%\n000", 1000)]
    [InlineData("0xFF_FF", 65535)]
    [InlineData("0o7_7", 63)]
    [InlineData("0b1010_1010", 170)]
    [InlineData("-1_000", -1000)]
    public void ReadsDigitSeparatorsBetweenDigitGroups(string text, long expected)
    {
        Assert.Equal(
            NumberReader.Outcome.Number,
            NumberReader.Read(
                text,
                codePoints: true,
                rationals: true,
                isoReading: false,
                digitSeparators: true,
                out PrologNumber number
            )
        );
        Assert.Equal(expected, number.Integer);
    }

    [Theory]
    [InlineData("1_000")]
    [InlineData("0xFF_FF")]
    public void RejectsDigitSeparatorsInStrictIso(string text) =>
        Assert.Equal(
            NumberReader.Outcome.NotANumber,
            NumberReader.Read(text, codePoints: true, rationals: true, isoReading: true, digitSeparators: false, out _)
        );

    [Theory]
    [InlineData("1__000")]
    [InlineData("_1000")]
    [InlineData("1000_")]
    [InlineData("1_")]
    [InlineData("1_5.5")]
    [InlineData("1.5_5")]
    [InlineData("1_5e2")]
    [InlineData("10_00.5")]
    [InlineData("1.0e1_0")]
    public void RejectsMisplacedAndFloatSeparators(string text) =>
        Assert.Equal(
            NumberReader.Outcome.NotANumber,
            NumberReader.Read(text, codePoints: true, rationals: true, isoReading: false, digitSeparators: true, out _)
        );

    [Theory]
    [InlineData("+1")]
    [InlineData("1 ")]
    [InlineData("0X1")]
    [InlineData("0'\\\n")]
    public void RejectsWhatTheReaderRejects(string text)
    {
        Assert.Equal(
            NumberReader.Outcome.NotANumber,
            NumberReader.Read(text, codePoints: true, rationals: true, isoReading: true, digitSeparators: false, out _)
        );
        Assert.Equal(
            NumberReader.Outcome.NotANumber,
            NumberReader.Read(text, codePoints: true, rationals: true, isoReading: false, digitSeparators: true, out _)
        );
    }

    [Fact]
    public void AFloatBeyondTheFiniteRangeOverflows() =>
        Assert.Equal(
            NumberReader.Outcome.FloatOverflow,
            NumberReader.Read("9.9e999", codePoints: true, rationals: true, isoReading: true, digitSeparators: false, out _)
        );
}
