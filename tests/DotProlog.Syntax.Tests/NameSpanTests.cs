namespace DotProlog.Syntax.Tests;

public sealed class NameSpanTests
{
    [Theory]
    [InlineData("'two words'(a).", "two words", "'two words'")]
    [InlineData("'a\\'b'(x).", "a'b", "'a\\'b'")]
    [InlineData(":- op(500, xfy, likes). a likes b.", "likes", "likes")]
    [InlineData(":- op(500, fy, before). before a.", "before", "before")]
    [InlineData(":- op(500, yf, after). a after.", "after", "after")]
    [InlineData("{value}.", "{}", "{")]
    public void ReaderRetainsOriginalFunctorToken(string source, string name, string token)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parsed = TermReader.ReadProgram(source);
        Assert.Empty(parsed.Diagnostics);
        var term = Assert.IsType<CompoundTerm>(parsed.Clauses[^1]);
        Assert.Equal(name, term.Name);
        var span = Assert.IsType<SourceSpan>(term.NameSpan);
        Assert.Equal(token, source.Substring(span.Start, span.Length));
        Assert.InRange(span.Start, term.Span.Start, term.Span.Start + term.Span.Length);
        Assert.True(span.Start + span.Length <= term.Span.Start + term.Span.Length);
    }
}
