using DotProlog.Syntax;

namespace DotProlog.Syntax.Tests;

public sealed class LexerTests
{
    private static List<Token> Tokenize(string text, out List<Diagnostic> diagnostics)
    {
        diagnostics = [];
        var lexer = new Lexer(text, null, diagnostics);
        List<Token> tokens = [];
        while (true)
        {
            Token token = lexer.Next();
            tokens.Add(token);
            if (token.Kind == TokenKind.Eof)
            {
                return tokens;
            }
        }
    }

    [Fact]
    public void SeparatesAtomsVariablesAndPunctuation()
    {
        List<Token> tokens = Tokenize("foo(Bar, baz).", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            [
                TokenKind.Atom,
                TokenKind.Punctuation,
                TokenKind.Variable,
                TokenKind.Punctuation,
                TokenKind.Atom,
                TokenKind.Punctuation,
                TokenKind.End,
                TokenKind.Eof,
            ],
            tokens.Select(t => t.Kind)
        );
    }

    [Fact]
    public void MarksLayoutBeforeOpeningParenthesis()
    {
        List<Token> attached = Tokenize("foo(a)", out _);
        List<Token> detached = Tokenize("foo (a)", out _);

        Assert.False(attached[1].PrecededByLayout);
        Assert.True(detached[1].PrecededByLayout);
    }

    [Fact]
    public void ReadsQuotedAtomWithEscapesAndDoubledQuote()
    {
        List<Token> tokens = Tokenize(@"'Hello!\nIt''s here'", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("Hello!\nIt's here", tokens[0].Text);
        Assert.True(tokens[0].Quoted);
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("0xff", 255L)]
    [InlineData("0o17", 15L)]
    [InlineData("0b1011", 11L)]
    [InlineData("0'a", 97L)]
    public void ReadsIntegerLiterals(string text, long expected)
    {
        List<Token> tokens = Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Integer, tokens[0].Kind);
        Assert.Equal(expected, tokens[0].Integer);
    }

    [Fact]
    public void ReadsFloatLiteralButNotClauseTerminator()
    {
        List<Token> tokens = Tokenize("1.5 2.", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Float, tokens[0].Kind);
        Assert.Equal(1.5, tokens[0].Float);
        Assert.Equal(TokenKind.Integer, tokens[1].Kind);
        Assert.Equal(TokenKind.End, tokens[2].Kind);
    }

    [Fact]
    public void SkipsLineAndBlockComments()
    {
        List<Token> tokens = Tokenize("a % trailing\n/* block\n   comment */ b.", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["a", "b", ".", string.Empty], tokens.Select(t => t.Text));
    }

    [Fact]
    public void LexesEmptyListAndCurlyAsAtoms()
    {
        List<Token> tokens = Tokenize("[] {}", out _);

        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("[]", tokens[0].Text);
        Assert.Equal(TokenKind.Atom, tokens[1].Kind);
        Assert.Equal("{}", tokens[1].Text);
    }

    [Fact]
    public void ReportsUnterminatedQuotedAtom()
    {
        Tokenize("'oops", out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.UnterminatedQuoted, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void ReportsUnexpectedCharacterAndKeepsGoing()
    {
        List<Token> tokens = Tokenize("a § b", out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
        Assert.Equal(["a", "b", string.Empty], tokens.Select(t => t.Text));
    }
}
