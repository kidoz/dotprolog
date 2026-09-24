using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Syntax.Tests;

public sealed class LexerTests
{
    private static List<Token> Tokenize(string text, out List<Diagnostic> diagnostics) =>
        Tokenize(text, flags: null, out diagnostics);

    /// <summary>Tokenizes as strict ISO mode does, where a character is a UTF-16 code unit.</summary>
    private static List<Token> TokenizeStrict(string text, out List<Diagnostic> diagnostics) =>
        Tokenize(text, new BytecodeProgram(PrologLanguageMode.StrictIso).Flags, out diagnostics);

    private static List<Token> Tokenize(string text, PrologFlags? flags, out List<Diagnostic> diagnostics)
    {
        diagnostics = [];
        var lexer = new Lexer(text, null, diagnostics, flags: flags);
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
    public void TreatsExtendedTitlecaseLettersAsAtomStarts()
    {
        List<Token> tokens = Tokenize("ǅelta.", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("ǅelta", tokens[0].Text);
    }

    [Fact]
    public void RejectsNonAsciiDigitsAsNumericStartsWithoutThrowing()
    {
        List<Token> tokens = Tokenize("١ next.", out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
        Assert.Equal(["next", ".", string.Empty], tokens.Select(token => token.Text));
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

    [Fact]
    public void ReadsBackquotedAtomWithEscapesAndDoubledQuote()
    {
        List<Token> tokens = Tokenize(@"`Hello\n``world`", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("Hello\n`world", tokens[0].Text);
        Assert.True(tokens[0].Quoted);
    }

    [Theory]
    [InlineData(@"'\d'", '\u007f')]
    [InlineData(@"""\d""", '\u007f')]
    [InlineData(@"`\d`", '\u007f')]
    public void ReadsIsoDeleteEscape(string text, char expected)
    {
        List<Token> tokens = Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(expected.ToString(), tokens[0].Text);
    }

    [Theory]
    [InlineData("'raw\tlayout'")]
    [InlineData("\"raw\nlayout\"")]
    [InlineData("`raw\u0001control`")]
    public void RejectsUnescapedControlAndLayoutCharactersInsideQuotes(string text)
    {
        Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.InvalidQuotedCharacter, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData(@"'\x41\'", false)]
    [InlineData(@"""\o101\""", true)]
    [InlineData(@"'\101\'", false)]
    public void ReadsIsoNumericEscapes(string text, bool stringToken)
    {
        List<Token> tokens = Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(stringToken ? TokenKind.String : TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("A", tokens[0].Text);
    }

    [Theory]
    [InlineData(@"0'\x41\")]
    [InlineData(@"0'\o101\")]
    [InlineData(@"0'\101\")]
    public void ReadsIsoNumericEscapesInCharacterCodeLiterals(string text)
    {
        List<Token> tokens = Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Integer, tokens[0].Kind);
        Assert.Equal(65, tokens[0].Integer);
    }

    [Theory]
    [InlineData(@"'\x41'")]
    [InlineData(@"'\o101'")]
    [InlineData(@"'\101'")]
    public void RejectsMalformedIsoNumericEscapes(string text)
    {
        Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.InvalidEscape, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData(@"'\x1F600\'", "😀")]
    [InlineData(@"'\x10000\'", "\U00010000")]
    [InlineData(@"'\u00e9'", "é")]
    [InlineData(@"'\U0001F600'", "😀")]
    [InlineData("'😀'", "😀")]
    public void ReadsSupplementaryCharactersAndUnicodeEscapes(string text, string expected)
    {
        List<Token> tokens = Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(expected, tokens[0].Text);
    }

    [Theory]
    [InlineData(@"'\xD800\'")]
    [InlineData(@"'\x110000\'")]
    [InlineData(@"'\u12'")]
    [InlineData(@"'\U0001F60'")]
    [InlineData(@"'\U00110000'")]
    public void RejectsEscapesThatNameNoCharacter(string text)
    {
        Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticIds.InvalidEscape, diagnostic.Id));
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void NeverJoinsTwoSurrogateEscapes()
    {
        List<Token> tokens = Tokenize(@"'\uD83D\uDE00'", out List<Diagnostic> diagnostics);

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == DiagnosticIds.InvalidEscape));
        Assert.Equal(string.Empty, tokens[0].Text);
    }

    [Fact]
    public void ReadsACharacterCodeLiteralAsOneCodePoint()
    {
        List<Token> tokens = Tokenize("0'😀", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(128512, tokens[0].Integer);
    }

    [Theory]
    [InlineData("𝑎bc", false)]
    [InlineData("𐐨x", false)]
    [InlineData("𐐀x", true)]
    public void ClassifiesSupplementaryLettersLikeOtherLetters(string text, bool variable)
    {
        List<Token> tokens = Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(variable ? TokenKind.Variable : TokenKind.Atom, tokens[0].Kind);
        Assert.Equal(text, tokens[0].Text);
    }

    [Fact]
    public void ReportsAnUnexpectedSupplementaryCharacterOnce()
    {
        Tokenize("😀.", out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData(@"'\x10000\'")]
    [InlineData(@"'\u00e9'")]
    public void StrictModeKeepsItsCodeUnitEscapes(string text)
    {
        TokenizeStrict(text, out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.InvalidEscape, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void StrictModeReadsASupplementaryLetterAsTwoCodeUnits()
    {
        TokenizeStrict("𝑎bc.", out List<Diagnostic> diagnostics);

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == DiagnosticIds.UnexpectedCharacter));
    }

    [Fact]
    public void RejectsNonOctalDigitsInDelimitedOctalEscape()
    {
        Tokenize(@"'\128\'", out List<Diagnostic> diagnostics);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.InvalidEscape);
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
    public void ExponentWithoutFractionIsNotAFloatToken()
    {
        List<Token> tokens = Tokenize("1e2", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal([TokenKind.Integer, TokenKind.Atom, TokenKind.Eof], tokens.Select(token => token.Kind));
        Assert.Equal(["1", "e2", string.Empty], tokens.Select(token => token.Text));
    }

    [Fact]
    public void RetainsFloatOverflowForReaderDiagnostics()
    {
        List<Token> tokens = Tokenize("1.0e9999", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Float, tokens[0].Kind);
        Assert.True(tokens[0].FloatOverflow);
        Assert.True(double.IsPositiveInfinity(tokens[0].Float));
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
