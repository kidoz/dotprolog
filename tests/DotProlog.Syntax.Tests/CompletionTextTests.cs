namespace DotProlog.Syntax.Tests;

public sealed class CompletionTextTests
{
    [Fact]
    public void SupplementaryLettersAndCombiningMarksKeepUtf16Spans()
    {
        const string text = "run(𐐀Value) :- pair(𐐀Va";
        var site = Assert.IsType<PrologCompletionPrefix>(
            PrologCompletionText.Create(text, TestContext.Current.CancellationToken).At(text.Length)
        );
        Assert.Equal(["𐐀Value"], site.Variables);
        Assert.Equal(4, site.Range.Length);
        const string atom = "run :- café";
        Assert.Equal("café", PrologCompletionText.Create(atom, TestContext.Current.CancellationToken).At(atom.Length)!.Text);
    }

    [Fact]
    public void IncompleteClauseKeepsVariablesAndFullReplacementSpan()
    {
        const string text = "before(Other). run(Value, _Private, _) :- pair(Value, VaLong";
        var snapshot = PrologCompletionText.Create(text, TestContext.Current.CancellationToken);
        var prefix = Assert.IsType<PrologCompletionPrefix>(snapshot.At(text.Length - 4));
        Assert.Equal("Va", prefix.Text);
        Assert.True(prefix.IsVariable);
        Assert.Equal("VaLong", text.Substring(prefix.Range.Start, prefix.Range.Length));
        Assert.Equal(["Value", "_Private"], prefix.Variables);
    }

    [Theory]
    [InlineData("run :- % mem")]
    [InlineData("run :- /* mem")]
    [InlineData("run :- 'mem")]
    [InlineData("run :- \"mem")]
    [InlineData("run :- `mem")]
    [InlineData("run :- 12")]
    [InlineData("run :- M:mem")]
    public void UnsupportedSitesReturnNoPrefix(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Assert.Null(PrologCompletionText.Create(text, TestContext.Current.CancellationToken).At(text.Length));
    }

    [Fact]
    public void LexerSeparatesCommentsDecimalsAndQuotedFullStopsFromClauseBoundaries()
    {
        const string text = "run(Value) :- f(3.14, 'a.b'), /* period. */ pair(Va";
        var prefix = Assert.IsType<PrologCompletionPrefix>(
            PrologCompletionText.Create(text, TestContext.Current.CancellationToken).At(text.Length)
        );
        Assert.Equal(["Value"], prefix.Variables);
        var afterComment = PrologCompletionText.Create("% comment\nrun :- lists:", TestContext.Current.CancellationToken).At(23);
        Assert.NotNull(afterComment);
        Assert.Equal("lists", afterComment.Module);
        Assert.Empty(afterComment.Text);
    }

    [Fact]
    public void ClosedCommentAllowsCompletionAtFollowingCursor()
    {
        const string text = "run :- /* hidden */";
        Assert.NotNull(PrologCompletionText.Create(text, TestContext.Current.CancellationToken).At(text.Length));
        Assert.Null(PrologCompletionText.Create(text, TestContext.Current.CancellationToken).At(text.Length - 1));
    }
}
