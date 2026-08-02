using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologLayoutLinterTests
{
    [Fact]
    public void SemanticProfileDoesNotImposeLayout()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("pair(a,b).\n", PrologLintOptions.SemanticOnly);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CovingtonProfileAcceptsItsRecommendedClauseLayout()
    {
        const string source = """
            same_length([], []).
            same_length([_|L1], [_|L2]) :-
                same_length(L1, L2).

            """;

        Assert.Empty(Lint(source, PrologLintOptions.Covington));
    }

    [Fact]
    public void ReportsTabsAndTrailingWhitespaceAtTheirExactLocation()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("fact.\t\n", PrologLintOptions.Covington);

        Assert.Collection(
            diagnostics,
            diagnostic => AssertDiagnostic(diagnostic, LintDiagnosticIds.TabCharacter, new SourceSpan(5, 1, 1, 6)),
            diagnostic => AssertDiagnostic(diagnostic, LintDiagnosticIds.TrailingWhitespace, new SourceSpan(5, 1, 1, 6))
        );
    }

    [Fact]
    public void ReportsInconsistentClauseIndentation()
    {
        const string source = "rule :-\n  first,\n    second.\n";

        Diagnostic diagnostic = Assert.Single(Lint(source, PrologLintOptions.Covington));

        AssertDiagnostic(diagnostic, LintDiagnosticIds.InconsistentIndentation, new SourceSpan(10, 1, 2, 3));
    }

    [Fact]
    public void ReportsThePartOfALinePastTheConfiguredLimit()
    {
        const string source = "abcdefghij.\n";
        var options = PrologLintOptions.SemanticOnly with { MaxLineLength = 8 };

        Diagnostic diagnostic = Assert.Single(Lint(source, options));

        AssertDiagnostic(diagnostic, LintDiagnosticIds.LineTooLong, new SourceSpan(8, 3, 1, 9));
    }

    [Fact]
    public void ReportsTheFirstClauseLinePastTheConfiguredLimit()
    {
        const string source = "rule :-\n    first,\n    second.\n";
        var options = PrologLintOptions.SemanticOnly with { MaxClauseLines = 2 };

        Diagnostic diagnostic = Assert.Single(Lint(source, options));

        AssertDiagnostic(diagnostic, LintDiagnosticIds.ClauseTooLong, new SourceSpan(19, 1, 3, 1));
    }

    [Fact]
    public void CommaSpacingIgnoresQuotedTextAndComments()
    {
        const string source = "value('a,b', /* x,y */ [a,b]).\n";
        int comma = source.IndexOf("[a,b]", StringComparison.Ordinal) + 2;

        Diagnostic diagnostic = Assert.Single(Lint(source, PrologLintOptions.Covington));

        AssertDiagnostic(diagnostic, LintDiagnosticIds.MissingSpaceAfterComma, new SourceSpan(comma, 1, 1, comma + 1));
    }

    [Theory]
    // A character-code literal is not a quote, so commas after one stay checked. Each form the
    // lexer accepts is covered: plain, escaped, and the doubled quote that denotes ' itself.
    [InlineData("c(0'a).\nd(X,Y).\n")]
    [InlineData("c(0'\\n).\nd(X,Y).\n")]
    [InlineData("c(0'').\nd(X,Y).\n")]
    [InlineData("c(0''').\nd(X,Y).\n")]
    [InlineData("c(0'\\').\nd(X,Y).\n")]
    public void CommaSpacingSurvivesCharacterCodeLiterals(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source, PrologLintOptions.Covington);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == LintDiagnosticIds.MissingSpaceAfterComma);
    }

    [Fact]
    public void OneCharacterCodeLiteralDoesNotSilenceTheRestOfTheFile()
    {
        // The defect this covers suppressed every comma diagnostic between one 0'c and the next.
        const string source = "c(0'a).\nd(X,Y).\ne(P,Q).\nf(R,S).\n";

        Assert.Equal(
            3,
            Lint(source, PrologLintOptions.Covington)
                .Count(diagnostic => diagnostic.Id == LintDiagnosticIds.MissingSpaceAfterComma)
        );
    }

    [Fact]
    public void QuotedAtomsStayShieldedAfterACharacterCodeLiteral()
    {
        // Both halves at once: the 0'a must not open a quote, and the quoted atom following it must
        // still shield its own comma. Only the comma between them is a violation.
        const string source = "c(0'a,'x,y').\n";

        Diagnostic diagnostic = Assert.Single(
            Lint(source, PrologLintOptions.Covington),
            diagnostic => diagnostic.Id == LintDiagnosticIds.MissingSpaceAfterComma
        );

        Assert.Equal(5, diagnostic.Span.Start);
    }

    [Theory]
    [InlineData("first. second.\n", 7, 8)]
    [InlineData("rule :- body.\n", 8, 9)]
    public void ReportsClauseBoundaryViolations(string source, int offset, int column)
    {
        Diagnostic diagnostic = Assert.Single(Lint(source, PrologLintOptions.Covington));

        AssertDiagnostic(diagnostic, LintDiagnosticIds.ClauseLayout, new SourceSpan(offset, 1, 1, column));
    }

    [Fact]
    public void ReportsConjunctionSubgoalsSharingALine()
    {
        const string source = "rule :-\n    first, second.\n";

        Diagnostic diagnostic = Assert.Single(Lint(source, PrologLintOptions.Covington));

        AssertDiagnostic(diagnostic, LintDiagnosticIds.SubgoalLayout, new SourceSpan(19, 1, 2, 12));
    }

    [Fact]
    public void DoesNotTreatACommaTermInFactDataAsSubgoals()
    {
        const string source = "fact(','(a, b)).\n";

        Assert.DoesNotContain(
            Lint(source, PrologLintOptions.Covington),
            diagnostic => diagnostic.Id == LintDiagnosticIds.SubgoalLayout
        );
    }

    [Fact]
    public void CrLfSourceRetainsReaderCompatibleLocations()
    {
        Diagnostic diagnostic = Assert.Single(Lint("fact. \r\n", PrologLintOptions.Covington));

        AssertDiagnostic(diagnostic, LintDiagnosticIds.TrailingWhitespace, new SourceSpan(5, 1, 1, 6));
    }

    [Fact]
    public void RejectsNonPositiveThresholds()
    {
        var options = PrologLintOptions.SemanticOnly with { IndentSize = 0 };
        ParseResult parsed = TermReader.ReadProgram("fact.\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => PrologLinter.AnalyzeSource("fact.\n", parsed.Clauses, options: options));
    }

    private static IReadOnlyList<Diagnostic> Lint(string source, PrologLintOptions options)
    {
        ParseResult parsed = TermReader.ReadProgram(source, "source.pl");
        Assert.True(parsed.Success, string.Join(Environment.NewLine, parsed.Diagnostics));
        return PrologLinter.AnalyzeSource(source, parsed.Clauses, "source.pl", options);
    }

    private static void AssertDiagnostic(Diagnostic actual, string id, SourceSpan span)
    {
        Assert.Equal(id, actual.Id);
        Assert.Equal(DiagnosticSeverity.Warning, actual.Severity);
        Assert.Equal(span, actual.Span);
        Assert.Equal("source.pl", actual.FileName);
    }
}
