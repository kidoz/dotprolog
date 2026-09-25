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
    [InlineData("0'\\n", 10)]
    public void ReadsLayoutCommentsAndAMinusBeforeTheNumber(string text, long expected)
    {
        Assert.Equal(
            NumberReader.Outcome.Number,
            NumberReader.Read(text, codePoints: true, rationals: true, out PrologNumber number)
        );
        Assert.Equal(expected, number.Integer);
    }

    [Theory]
    [InlineData("+1")]
    [InlineData("1 ")]
    [InlineData("0X1")]
    [InlineData("0'\\\n")]
    public void RejectsWhatTheReaderRejects(string text) =>
        Assert.Equal(NumberReader.Outcome.NotANumber, NumberReader.Read(text, codePoints: true, rationals: true, out _));

    [Fact]
    public void AFloatBeyondTheFiniteRangeOverflows() =>
        Assert.Equal(NumberReader.Outcome.FloatOverflow, NumberReader.Read("9.9e999", codePoints: true, rationals: true, out _));
}
