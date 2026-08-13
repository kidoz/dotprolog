namespace DotProlog.Runtime.Tests;

/// <summary>The shared name=value spelling of initial flag overrides.</summary>
public sealed class PrologFlagOverridesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    public void BlankTextParsesToNone(string? text)
    {
        Assert.True(PrologFlagOverrides.TryParse(text, out PrologFlagOverrides overrides, out var error));
        Assert.Same(PrologFlagOverrides.None, overrides);
        Assert.True(overrides.IsEmpty);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("double_quotes=codes", DoubleQuotesMode.Codes)]
    [InlineData("double_quotes=chars", DoubleQuotesMode.Chars)]
    [InlineData("double_quotes=atom", DoubleQuotesMode.Atom)]
    [InlineData("double_quotes=string", DoubleQuotesMode.String)]
    [InlineData(" Double_Quotes = ATOM ;", DoubleQuotesMode.Atom)]
    public void DoubleQuotesEntryParses(string text, DoubleQuotesMode expected)
    {
        Assert.True(PrologFlagOverrides.TryParse(text, out PrologFlagOverrides overrides, out _));
        Assert.Equal(expected, overrides.DoubleQuotes);
        Assert.False(overrides.IsEmpty);
    }

    [Theory]
    [InlineData("double_quotes", "not a name=value pair")]
    [InlineData("double_quotes=strings", "not a double_quotes value")]
    [InlineData("occurs_check=true", "not an overridable flag")]
    [InlineData("double_quotes=codes;double_quotes=chars", "more than once")]
    public void InvalidTextIsRejectedWithTheReason(string text, string reason)
    {
        Assert.False(PrologFlagOverrides.TryParse(text, out PrologFlagOverrides overrides, out var error));
        Assert.Same(PrologFlagOverrides.None, overrides);
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorsSpellTheAcceptedEntries()
    {
        Assert.False(PrologFlagOverrides.TryParse("unknown=1", out _, out var error));
        Assert.Contains(PrologFlagOverrides.Entries, error, StringComparison.Ordinal);
    }
}
