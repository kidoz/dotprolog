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

    [Theory]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData(',')]
    [InlineData('|')]
    public void ConvertedPunctuationKeepsSourcePositionAndQuotedText(char punctuation)
    {
        foreach (PrologLanguageMode mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var conversions = new CharacterConversionTable();
            conversions.Set('#', punctuation);
            conversions.Set(punctuation, 'z');
            PrologFlags flags = new BytecodeProgram(mode).Flags;
            flags.SetCharConversion(true);
            List<Diagnostic> diagnostics = [];
            var lexer = new Lexer(
                $" \n#{punctuation} '{punctuation}' \"{punctuation}\" `{punctuation}`",
                "punctuation.pl",
                diagnostics,
                conversions,
                flags
            );

            Token token = lexer.Next();
            Assert.Equal(TokenKind.Punctuation, token.Kind);
            Assert.Equal(punctuation.ToString(), token.Text);
            Assert.Equal(new SourceSpan(2, 1, 2, 1), token.Span);
            Assert.True(token.PrecededByLayout);
            Assert.False(token.Quoted);

            token = lexer.Next();
            Assert.Equal(TokenKind.Atom, token.Kind);
            Assert.Equal("z", token.Text);
            Assert.Equal(new SourceSpan(3, 1, 2, 2), token.Span);
            Assert.False(token.PrecededByLayout);
            Assert.Equal((TokenKind.Atom, punctuation.ToString(), true), Quoted(lexer.Next()));
            Assert.Equal((TokenKind.String, punctuation.ToString(), false), Quoted(lexer.Next()));
            Assert.Equal((TokenKind.Atom, punctuation.ToString(), true), Quoted(lexer.Next()));
            Assert.Equal(TokenKind.Eof, lexer.Next().Kind);
            Assert.Empty(diagnostics);
        }

        static (TokenKind Kind, string Text, bool Quoted) Quoted(Token token) => (token.Kind, token.Text, token.Quoted);
    }

    [Theory]
    [InlineData('[', ']', "[]")]
    [InlineData('{', '}', "{}")]
    public void ConvertedEmptyDelimitersRemainAtomsOnlyWhenAdjacent(char open, char close, string empty)
    {
        var conversions = new CharacterConversionTable();
        conversions.Set('a', open);
        conversions.Set('b', close);
        var flags = new PrologFlags();
        flags.SetCharConversion(true);
        List<Diagnostic> diagnostics = [];
        var lexer = new Lexer("ab a b", null, diagnostics, conversions, flags);

        Token token = lexer.Next();
        Assert.Equal(TokenKind.Atom, token.Kind);
        Assert.Equal(empty, token.Text);
        Assert.Equal(new SourceSpan(0, 2, 1, 1), token.Span);
        token = lexer.Next();
        Assert.Equal(TokenKind.Punctuation, token.Kind);
        Assert.Equal(open.ToString(), token.Text);
        token = lexer.Next();
        Assert.Equal(TokenKind.Punctuation, token.Kind);
        Assert.Equal(close.ToString(), token.Text);
        Assert.Equal(TokenKind.Eof, lexer.Next().Kind);
        Assert.Empty(diagnostics);
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
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void PlainQuotedTextKeepsContentAndSourceSpan(char quote)
    {
        foreach (PrologLanguageMode mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            foreach (string value in new[] { string.Empty, "plain", "two words", "λ😀" + new string('a', 128) })
            {
                var conversions = new CharacterConversionTable();
                conversions.Set('a', 'z');
                PrologFlags flags = new BytecodeProgram(mode).Flags;
                flags.SetCharConversion(true);
                List<Diagnostic> diagnostics = [];
                var lexer = new Lexer($"\n {quote}{value}{quote} next", "quoted.pl", diagnostics, conversions, flags);

                Token token = lexer.Next();
                Assert.Equal(quote == '"' ? TokenKind.String : TokenKind.Atom, token.Kind);
                Assert.Equal(value, token.Text);
                Assert.Equal(quote != '"', token.Quoted);
                Assert.True(token.PrecededByLayout);
                Assert.Equal(new SourceSpan(2, value.Length + 2, 2, 2), token.Span);
                token = lexer.Next();
                Assert.Equal("next", token.Text);
                Assert.Equal(new SourceSpan(value.Length + 5, 4, 2, value.Length + 5), token.Span);
                Assert.Equal(TokenKind.Eof, lexer.Next().Kind);
                Assert.Empty(diagnostics);
            }
        }
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void QuotedTextKeepsPrefixWhenDecodingStartsLate(char quote)
    {
        string prefix = new('p', 80);
        foreach (PrologLanguageMode mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            string source = $"{quote}{prefix}\\nq{quote}{quote}r\\x41\\s{quote} next";
            List<Token> tokens = Tokenize(source, new BytecodeProgram(mode).Flags, out List<Diagnostic> diagnostics);

            Assert.Empty(diagnostics);
            Assert.Equal($"{prefix}\nq{quote}rAs", tokens[0].Text);
            Assert.Equal(new SourceSpan(0, source.Length - 5, 1, 1), tokens[0].Span);
            Assert.Equal("next", tokens[1].Text);
            Assert.Equal(TokenKind.Eof, tokens[2].Kind);
        }
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void InvalidQuotedCharactersPreservePrefixAndRecovery(char quote)
    {
        List<Diagnostic> diagnostics = [];
        var lexer = new Lexer($"{quote}prefix\tbad\\qtail{quote} next", "quoted.pl", diagnostics);

        Assert.Equal("prefixbadtail", lexer.Next().Text);
        Assert.Equal("next", lexer.Next().Text);
        Assert.Equal(TokenKind.Eof, lexer.Next().Kind);
        Assert.Equal([DiagnosticIds.InvalidQuotedCharacter, DiagnosticIds.InvalidEscape], diagnostics.Select(d => d.Id));
        Assert.Equal(new SourceSpan(7, 1, 1, 8), diagnostics[0].Span);
        Assert.Equal(new SourceSpan(11, 2, 1, 12), diagnostics[1].Span);
        Assert.All(diagnostics, diagnostic => Assert.Equal("quoted.pl", diagnostic.FileName));
    }

    [Theory]
    [InlineData("'prefix", "prefix", DiagnosticIds.UnterminatedQuoted)]
    [InlineData("'prefix\\", "prefix", DiagnosticIds.InvalidEscape)]
    [InlineData("'prefix''", "prefix'", DiagnosticIds.UnterminatedQuoted)]
    public void UnterminatedQuotedTextRetainsItsDecodedPrefix(string source, string expected, string firstDiagnostic)
    {
        List<Token> tokens = Tokenize(source, out List<Diagnostic> diagnostics);

        Assert.Equal(expected, tokens[0].Text);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
        Assert.Equal(firstDiagnostic, diagnostics[0].Id);
        Assert.Equal(DiagnosticIds.UnterminatedQuoted, diagnostics[^1].Id);
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void RunsOfQuotesDistinguishDecodedDelimitersFromTheClosingDelimiter(char quote)
    {
        for (var length = 2; length <= 8; length++)
        {
            List<Token> tokens = Tokenize(new string(quote, length), out List<Diagnostic> diagnostics);

            Assert.Equal(new string(quote, (length - 1) / 2), tokens[0].Text);
            Assert.Equal(new SourceSpan(0, length, 1, 1), tokens[0].Span);
            Assert.Equal(TokenKind.Eof, tokens[1].Kind);
            if (length % 2 == 0)
            {
                Assert.Empty(diagnostics);
            }
            else
            {
                Assert.Equal(DiagnosticIds.UnterminatedQuoted, Assert.Single(diagnostics).Id);
            }
        }
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void ClosingQuoteDoesNotConsumeTheAdjacentToken(char quote)
    {
        foreach (bool doubled in new[] { false, true })
        {
            string content = doubled ? $"head{quote}{quote}tail" : "head";
            string expected = doubled ? $"head{quote}tail" : "head";
            List<Token> tokens = Tokenize($"{quote}{content}{quote}next", out List<Diagnostic> diagnostics);

            Assert.Empty(diagnostics);
            Assert.Equal(expected, tokens[0].Text);
            Assert.Equal(new SourceSpan(0, content.Length + 2, 1, 1), tokens[0].Span);
            Assert.Equal("next", tokens[1].Text);
            Assert.Equal(new SourceSpan(content.Length + 2, 4, 1, content.Length + 3), tokens[1].Span);
            Assert.False(tokens[1].PrecededByLayout);
            Assert.Equal(TokenKind.Eof, tokens[2].Kind);
        }
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void QuotedNewlineRecoveryKeepsTheFollowingTokenPosition(char quote)
    {
        foreach (PrologLanguageMode mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            List<Token> tokens = Tokenize(
                $"{quote}a\nb{quote}{quote}c{quote}next",
                new BytecodeProgram(mode).Flags,
                out List<Diagnostic> diagnostics
            );

            Assert.Equal($"ab{quote}c", tokens[0].Text);
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal(DiagnosticIds.InvalidQuotedCharacter, diagnostic.Id);
            Assert.Equal(new SourceSpan(2, 1, 1, 3), diagnostic.Span);
            Assert.Equal("next", tokens[1].Text);
            Assert.Equal(new SourceSpan(8, 4, 2, 6), tokens[1].Span);
            Assert.Equal(TokenKind.Eof, tokens[2].Kind);
        }
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void NewlineConvertedToOpeningQuoteStillAdvancesTheSourceLine(char quote)
    {
        foreach (PrologLanguageMode mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var conversions = new CharacterConversionTable();
            conversions.Set('\n', quote);
            PrologFlags flags = new BytecodeProgram(mode).Flags;
            flags.SetCharConversion(true);
            List<Diagnostic> diagnostics = [];
            var lexer = new Lexer($"\nhead{quote} next", null, diagnostics, conversions, flags);

            Assert.Equal("head", lexer.Next().Text);
            Token next = lexer.Next();
            Assert.Equal("next", next.Text);
            Assert.Equal(new SourceSpan(7, 4, 2, 7), next.Span);
            Assert.Equal(TokenKind.Eof, lexer.Next().Kind);
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void QuotedLineContinuationKeepsPrefixAndFollowingPosition(char quote)
    {
        foreach (PrologLanguageMode mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            List<Token> tokens = Tokenize(
                $"{quote}prefix\\\nsuffix{quote} next",
                new BytecodeProgram(mode).Flags,
                out List<Diagnostic> diagnostics
            );

            Assert.Empty(diagnostics);
            Assert.Equal("prefixsuffix", tokens[0].Text);
            Assert.Equal("next", tokens[1].Text);
            Assert.Equal(new SourceSpan(17, 4, 2, 9), tokens[1].Span);
        }
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
    [InlineData("0''", DiagnosticIds.InvalidQuotedCharacter, 2, 1)]
    [InlineData(@"0'\0", DiagnosticIds.InvalidEscape, 2, 2)]
    [InlineData(@"'\0'", DiagnosticIds.InvalidEscape, 1, 2)]
    [InlineData("\"\\0\"", DiagnosticIds.InvalidEscape, 1, 2)]
    [InlineData(@"`\0`", DiagnosticIds.InvalidEscape, 1, 2)]
    public void RejectsUndoubledCharacterCodeQuotesAndUnterminatedZeroEscapes(string text, string id, int start, int length)
    {
        foreach (PrologLanguageMode mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Tokenize(text, new BytecodeProgram(mode).Flags, out List<Diagnostic> diagnostics);
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal(id, diagnostic.Id);
            Assert.Equal(new SourceSpan(start, length, 1, start + 1), diagnostic.Span);
        }
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
        Tokenize("\U000F0000.", out List<Diagnostic> diagnostics);

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

    [Theory]
    [InlineData("😀", "😀")]
    [InlineData("€", "€")]
    [InlineData("x\u0301", "x\u0301")]
    [InlineData("Ⅰ", "Ⅰ")]
    public void ReadsUnicodeSymbolsAndIdentifiersAsAtoms(string text, string atom)
    {
        List<Token> tokens = Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal(atom, tokens[0].Text);
    }

    [Theory]
    [InlineData("0'\t")]
    [InlineData("0'\n")]
    [InlineData("0'\u0007")]
    public void RejectsARawControlOrLayoutCharacterAfterACharacterCodeQuote(string text)
    {
        Tokenize(text, out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.InvalidQuotedCharacter, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void RejectsALineContinuationAsTheCharacterOfACharacterCode()
    {
        Tokenize("0'\\\nx", out List<Diagnostic> diagnostics);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.InvalidNumber, diagnostic.Id);
        Assert.Equal(1, diagnostic.Span.Line);
        Assert.Equal(1, diagnostic.Span.Column);
    }

    [Fact]
    public void ReadsASpaceAfterACharacterCodeQuote()
    {
        List<Token> tokens = Tokenize("0' ", out List<Diagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(32, tokens[0].Integer);
    }

    [Fact]
    public void StrictModeRejectsSymbolsOutsideAscii()
    {
        TokenizeStrict("a § b", out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
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
        List<Token> tokens = Tokenize("a « b", out List<Diagnostic> diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
        Assert.Equal(["a", "b", string.Empty], tokens.Select(t => t.Text));
    }
}
