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
            var token = lexer.Next();
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
        var tokens = Tokenize("foo(Bar, baz).", out var diagnostics);

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
        var tokens = Tokenize("ǅelta.", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("ǅelta", tokens[0].Text);
    }

    [Fact]
    public void RejectsNonAsciiDigitsAsNumericStartsWithoutThrowing()
    {
        var tokens = Tokenize("١ next.", out var diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
        Assert.Equal(["next", ".", string.Empty], tokens.Select(token => token.Text));
    }

    [Fact]
    public void MarksLayoutBeforeOpeningParenthesis()
    {
        var attached = Tokenize("foo(a)", out _);
        var detached = Tokenize("foo (a)", out _);

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
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var conversions = new CharacterConversionTable();
            conversions.Set('#', punctuation);
            conversions.Set(punctuation, 'z');
            var flags = new BytecodeProgram(mode).Flags;
            flags.SetCharConversion(true);
            List<Diagnostic> diagnostics = [];
            var lexer = new Lexer(
                $" \n#{punctuation} '{punctuation}' \"{punctuation}\" `{punctuation}`",
                "punctuation.pl",
                diagnostics,
                conversions,
                flags
            );

            var token = lexer.Next();
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

        var token = lexer.Next();
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
        var tokens = Tokenize(@"'Hello!\nIt''s here'", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("Hello!\nIt's here", tokens[0].Text);
        Assert.True(tokens[0].Quoted);
    }

    [Fact]
    public void ReadsBackquotedAtomWithEscapesAndDoubledQuote()
    {
        var tokens = Tokenize(@"`Hello\n``world`", out var diagnostics);

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
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            foreach (var value in new[] { string.Empty, "plain", "two words", "λ😀" + new string('a', 128) })
            {
                var conversions = new CharacterConversionTable();
                conversions.Set('a', 'z');
                var flags = new BytecodeProgram(mode).Flags;
                flags.SetCharConversion(true);
                List<Diagnostic> diagnostics = [];
                var lexer = new Lexer($"\n {quote}{value}{quote} next", "quoted.pl", diagnostics, conversions, flags);

                var token = lexer.Next();
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
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var source = $"{quote}{prefix}\\nq{quote}{quote}r\\x41\\s{quote} next";
            var tokens = Tokenize(source, new BytecodeProgram(mode).Flags, out var diagnostics);

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
        var tokens = Tokenize(source, out var diagnostics);

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
            var tokens = Tokenize(new string(quote, length), out var diagnostics);

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
        foreach (var doubled in new[] { false, true })
        {
            var content = doubled ? $"head{quote}{quote}tail" : "head";
            var expected = doubled ? $"head{quote}tail" : "head";
            var tokens = Tokenize($"{quote}{content}{quote}next", out var diagnostics);

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
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var tokens = Tokenize(
                $"{quote}a\nb{quote}{quote}c{quote}next",
                new BytecodeProgram(mode).Flags,
                out var diagnostics
            );

            Assert.Equal($"ab{quote}c", tokens[0].Text);
            var diagnostic = Assert.Single(diagnostics);
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
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var conversions = new CharacterConversionTable();
            conversions.Set('\n', quote);
            var flags = new BytecodeProgram(mode).Flags;
            flags.SetCharConversion(true);
            List<Diagnostic> diagnostics = [];
            var lexer = new Lexer($"\nhead{quote} next", null, diagnostics, conversions, flags);

            Assert.Equal("head", lexer.Next().Text);
            var next = lexer.Next();
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
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            var tokens = Tokenize(
                $"{quote}prefix\\\nsuffix{quote} next",
                new BytecodeProgram(mode).Flags,
                out var diagnostics
            );

            Assert.Empty(diagnostics);
            Assert.Equal("prefixsuffix", tokens[0].Text);
            Assert.Equal("next", tokens[1].Text);
            Assert.Equal(new SourceSpan(17, 4, 2, 9), tokens[1].Span);
        }
    }

    // Neither ISO nor SWI-Prolog has a \d or \o escape; octal is \101\ alone.
    [Theory]
    [InlineData(@"'\d'")]
    [InlineData(@"""\d""")]
    [InlineData(@"`\d`")]
    [InlineData(@"0'\d")]
    [InlineData(@"'\o101'")]
    [InlineData(@"""\o101""")]
    [InlineData(@"0'\o101\")]
    [InlineData(@"0'\o0\")]
    public void RejectsDeleteAndOctalLetterEscapesInBothModes(string text)
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Tokenize(text, new BytecodeProgram(mode).Flags, out var diagnostics);

            Assert.Equal(DiagnosticIds.InvalidEscape, Assert.Single(diagnostics).Id);
        }
    }

    [Theory]
    [InlineData(@"'\e'")]
    [InlineData(@"""\e""")]
    [InlineData(@"0'\e")]
    public void ReadsTheEscapeCharacterOnlyInModern(string text)
    {
        var tokens = Tokenize(
            text,
            new BytecodeProgram(PrologLanguageMode.Modern).Flags,
            out var diagnostics
        );
        Assert.Empty(diagnostics);
        var token = tokens[0];
        Assert.Equal(27L, token.Kind == TokenKind.Integer ? token.Integer : token.Text.Single());

        Tokenize(text, new BytecodeProgram(PrologLanguageMode.StrictIso).Flags, out diagnostics);
        Assert.Equal(DiagnosticIds.InvalidEscape, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("'raw\tlayout'")]
    [InlineData("\"raw\nlayout\"")]
    [InlineData("`raw\u0001control`")]
    public void RejectsUnescapedControlAndLayoutCharactersInsideQuotes(string text)
    {
        Tokenize(text, out var diagnostics);

        Assert.Equal(DiagnosticIds.InvalidQuotedCharacter, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData(@"'\x41\'", false)]
    [InlineData(@"""\101\""", true)]
    [InlineData(@"'\101\'", false)]
    public void ReadsIsoNumericEscapes(string text, bool stringToken)
    {
        var tokens = Tokenize(text, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(stringToken ? TokenKind.String : TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("A", tokens[0].Text);
    }

    [Theory]
    [InlineData(@"0'\0", DiagnosticIds.InvalidEscape, 2, 2)]
    [InlineData(@"'\0'", DiagnosticIds.InvalidEscape, 1, 2)]
    [InlineData("\"\\0\"", DiagnosticIds.InvalidEscape, 1, 2)]
    [InlineData(@"`\0`", DiagnosticIds.InvalidEscape, 1, 2)]
    public void RejectsUndoubledCharacterCodeQuotesAndUnterminatedZeroEscapes(string text, string id, int start, int length)
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Tokenize(text, new BytecodeProgram(mode).Flags, out var diagnostics);
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(id, diagnostic.Id);
            Assert.Equal(new SourceSpan(start, length, 1, start + 1), diagnostic.Span);
        }
    }

    [Theory]
    [InlineData(@"0'\x41\")]
    [InlineData(@"0'\101\")]
    public void ReadsIsoNumericEscapesInCharacterCodeLiterals(string text)
    {
        var tokens = Tokenize(text, out var diagnostics);

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
        Tokenize(text, out var diagnostics);

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
        var tokens = Tokenize(text, out var diagnostics);

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
        Tokenize(text, out var diagnostics);

        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticIds.InvalidEscape, diagnostic.Id));
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void NeverJoinsTwoSurrogateEscapes()
    {
        var tokens = Tokenize(@"'\uD83D\uDE00'", out var diagnostics);

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == DiagnosticIds.InvalidEscape));
        Assert.Equal(string.Empty, tokens[0].Text);
    }

    [Fact]
    public void ReadsACharacterCodeLiteralAsOneCodePoint()
    {
        var tokens = Tokenize("0'😀", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(128512, tokens[0].Integer);
    }

    [Theory]
    [InlineData("𝑎bc", false)]
    [InlineData("𐐨x", false)]
    [InlineData("𐐀x", true)]
    public void ClassifiesSupplementaryLettersLikeOtherLetters(string text, bool variable)
    {
        var tokens = Tokenize(text, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(variable ? TokenKind.Variable : TokenKind.Atom, tokens[0].Kind);
        Assert.Equal(text, tokens[0].Text);
    }

    [Fact]
    public void ReportsAnUnexpectedSupplementaryCharacterOnce()
    {
        Tokenize("\U000F0000.", out var diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData(@"'\x10000\'")]
    [InlineData(@"'\u00e9'")]
    public void StrictModeKeepsItsCodeUnitEscapes(string text)
    {
        TokenizeStrict(text, out var diagnostics);

        Assert.Equal(DiagnosticIds.InvalidEscape, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("😀", "😀")]
    [InlineData("€", "€")]
    [InlineData("x\u0301", "x\u0301")]
    [InlineData("Ⅰ", "Ⅰ")]
    public void ReadsUnicodeSymbolsAndIdentifiersAsAtoms(string text, string atom)
    {
        var tokens = Tokenize(text, out var diagnostics);

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
        Tokenize(text, out var diagnostics);

        Assert.Equal(DiagnosticIds.InvalidQuotedCharacter, Assert.Single(diagnostics).Id);
    }

    // Where no single quoted character follows 0' (an undoubled quote, or a line continuation,
    // which denotes none), the token is the integer 0 and the quote starts the next token.
    [Theory]
    [InlineData("0''1", "0", "", "1")]
    [InlineData("0'\\\n+'1", "0", "+", "1")]
    [InlineData("0xor 2", "0", "xor", "2")]
    [InlineData("0bop 2", "0", "bop", "2")]
    [InlineData("0o8", "0", "o8", null)]
    [InlineData("0b2", "0", "b2", null)]
    public void EndsTheIntegerZeroWhereNoCharacterCodeOrRadixDigitFollows(string text, string zero, string name, string? last)
    {
        var tokens = Tokenize(text, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Integer, tokens[0].Kind);
        Assert.Equal(zero, tokens[0].Text);
        Assert.Equal(TokenKind.Atom, tokens[1].Kind);
        Assert.Equal(name, tokens[1].Text);
        if (last is not null)
        {
            Assert.Equal(last, tokens[2].Text);
        }
    }

    [Fact]
    public void ReadsASpaceAfterACharacterCodeQuote()
    {
        var tokens = Tokenize("0' ", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(32, tokens[0].Integer);
    }

    [Fact]
    public void StrictModeRejectsSymbolsOutsideAscii()
    {
        TokenizeStrict("a § b", out var diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void StrictModeReadsASupplementaryLetterAsTwoCodeUnits()
    {
        TokenizeStrict("𝑎bc.", out var diagnostics);

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == DiagnosticIds.UnexpectedCharacter));
    }

    [Theory]
    [InlineData("\u00a0")]
    [InlineData("\u2003")]
    [InlineData("\u0085")]
    public void StrictModeRejectsNonAsciiLayout(string layout)
    {
        TokenizeStrict($"a{layout}b", out var strictDiagnostics);
        Tokenize($"a{layout}b", out var modernDiagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(strictDiagnostics).Id);
        Assert.Empty(modernDiagnostics);
    }

    [Fact]
    public void StrictModeRejectsNonAsciiDecimalDigitsAsIdentifierContinuations()
    {
        var strict = TokenizeStrict("a١", out var strictDiagnostics);
        var modern = Tokenize("a١", out var modernDiagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(strictDiagnostics).Id);
        Assert.Equal("a", strict[0].Text);
        Assert.Empty(modernDiagnostics);
        Assert.Equal("a١", modern[0].Text);
    }

    [Fact]
    public void RejectsNonOctalDigitsInDelimitedOctalEscape()
    {
        Tokenize(@"'\128\'", out var diagnostics);

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
        var tokens = Tokenize(text, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Integer, tokens[0].Kind);
        Assert.Equal(expected, tokens[0].Integer);
    }

    [Theory]
    [InlineData("1_000", 1000L)]
    [InlineData("1_000_000", 1000000L)]
    [InlineData("1_ 000", 1000L)]
    [InlineData("1_ /**/ 000", 1000L)]
    [InlineData("0xFF_FF", 65535L)]
    [InlineData("0o7_7", 63L)]
    [InlineData("0b1010_1010", 170L)]
    public void ReadsDigitSeparatorsBetweenDigitGroups(string text, long expected)
    {
        var tokens = Tokenize(text, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Integer, tokens[0].Kind);
        Assert.Equal(expected, tokens[0].Integer);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void AnUnderscoreNoDigitFollowsStartsTheNextToken()
    {
        var tokens = Tokenize("1__000", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal([TokenKind.Integer, TokenKind.Variable, TokenKind.Eof], tokens.Select(token => token.Kind));
        Assert.Equal(1L, tokens[0].Integer);
    }

    [Fact]
    public void ASeparatedIntegerDoesNotContinueIntoAFloat()
    {
        var tokens = Tokenize("1_5.5", out _);

        Assert.Equal(TokenKind.Integer, tokens[0].Kind);
        Assert.Equal(15L, tokens[0].Integer);
    }

    [Fact]
    public void ReadsDigitSeparatorsInRationalLiterals()
    {
        var tokens = Tokenize("1_0r3", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Integer, tokens[0].Kind);
        Assert.Equal(new System.Numerics.BigInteger(10), tokens[0].Big);
        Assert.Equal(new System.Numerics.BigInteger(3), tokens[0].RationalDenominator);
    }

    [Fact]
    public void RejectsDigitSeparatorsInStrictIso()
    {
        var tokens = TokenizeStrict("1_000", out _);

        Assert.Equal([TokenKind.Integer, TokenKind.Variable, TokenKind.Eof], tokens.Select(token => token.Kind));
        Assert.Equal(1L, tokens[0].Integer);
    }

    [Fact]
    public void ReadsFloatLiteralButNotClauseTerminator()
    {
        var tokens = Tokenize("1.5 2.", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Float, tokens[0].Kind);
        Assert.Equal(1.5, tokens[0].Float);
        Assert.Equal(TokenKind.Integer, tokens[1].Kind);
        Assert.Equal(TokenKind.End, tokens[2].Kind);
    }

    [Fact]
    public void ExponentWithoutFractionIsNotAFloatToken()
    {
        var tokens = Tokenize("1e2", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal([TokenKind.Integer, TokenKind.Atom, TokenKind.Eof], tokens.Select(token => token.Kind));
        Assert.Equal(["1", "e2", string.Empty], tokens.Select(token => token.Text));
    }

    [Fact]
    public void RetainsFloatOverflowForReaderDiagnostics()
    {
        var tokens = Tokenize("1.0e9999", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Float, tokens[0].Kind);
        Assert.True(tokens[0].FloatOverflow);
        Assert.True(double.IsPositiveInfinity(tokens[0].Float));
    }

    [Fact]
    public void SkipsLineAndBlockComments()
    {
        var tokens = Tokenize("a % trailing\n/* block\n   comment */ b.", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["a", "b", ".", string.Empty], tokens.Select(t => t.Text));
    }

    [Fact]
    public void LexesEmptyListAndCurlyAsAtoms()
    {
        var tokens = Tokenize("[] {}", out _);

        Assert.Equal(TokenKind.Atom, tokens[0].Kind);
        Assert.Equal("[]", tokens[0].Text);
        Assert.Equal(TokenKind.Atom, tokens[1].Kind);
        Assert.Equal("{}", tokens[1].Text);
    }

    [Fact]
    public void ReportsUnterminatedQuotedAtom()
    {
        Tokenize("'oops", out var diagnostics);

        Assert.Equal(DiagnosticIds.UnterminatedQuoted, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void ReportsUnexpectedCharacterAndKeepsGoing()
    {
        var tokens = Tokenize("a « b", out var diagnostics);

        Assert.Equal(DiagnosticIds.UnexpectedCharacter, Assert.Single(diagnostics).Id);
        Assert.Equal(["a", "b", string.Empty], tokens.Select(t => t.Text));
    }
}
