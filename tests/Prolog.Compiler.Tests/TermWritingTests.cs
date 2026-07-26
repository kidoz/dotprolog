namespace Prolog.Compiler.Tests;

/// <summary>How terms come back out as text, exercised through <c>write/1</c> and <c>writeq/1</c>.</summary>
public sealed class TermWritingTests
{
    [Theory]
    [InlineData("X = hello", "hello")]
    [InlineData("X = 'Hello! World!'", "Hello! World!")]
    [InlineData("X = 42", "42")]
    [InlineData("X = -42", "-42")]
    [InlineData("X = 1.5", "1.5")]
    [InlineData("X = f(a, g(b))", "f(a,g(b))")]
    [InlineData("X = []", "[]")]
    [InlineData("X = [a]", "[a]")]
    [InlineData("X = [a,b,c]", "[a,b,c]")]
    [InlineData("X = [a,[b,c],d]", "[a,[b,c],d]")]
    public void WriteRendersTermsUnquoted(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, write(X)"));
    }

    [Fact]
    public void WriteRendersAPartialListWithABar()
    {
        string output = PrologTestHost.RunGoal("X = [a,b|Tail], write(X)");

        Assert.StartsWith("[a,b|_G", output, StringComparison.Ordinal);
        Assert.EndsWith("]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteRendersAnUnboundVariable()
    {
        Assert.StartsWith("_G", PrologTestHost.RunGoal("write(X)"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("X = 'Hello! World!'", "'Hello! World!'")]
    [InlineData("X = hello", "hello")]
    [InlineData("X = []", "[]")]
    [InlineData("X = 'it''s'", @"'it\'s'")]
    [InlineData("X = f('A b', c)", "f('A b',c)")]
    public void WriteqQuotesAtomsThatNeedIt(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"{goal}, writeq(X)"));
    }

    [Fact]
    public void WritelnAppendsANewline()
    {
        Assert.Equal("hi\n", PrologTestHost.RunGoal("writeln(hi)"));
    }

    [Fact]
    public void DoubleQuotedTextBecomesAListOfCharacterCodes()
    {
        Assert.Equal("[104,105]", PrologTestHost.RunGoal(@"X = ""hi"", write(X)"));
    }
}
