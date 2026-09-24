using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// The lint rules that catch double-quoted text read one way and used another: conversions that
/// need the other list kind, and grammars comparing codes over text read as characters.
/// </summary>
public sealed class DoubleQuotesLinterTests
{
    private const string CodeGrammar = """
        digits([D|T]) --> digit(D), digits(T).
        digits([D])   --> digit(D).
        digit(D)      --> [D], { D >= 0'0, D =< 0'9 }.
        number(N)     --> digits(Ds), { number_codes(N, Ds) }.

        """;

    [Fact]
    public void CharactersPassedToACodeConversionAreReported()
    {
        Diagnostic diagnostic = Assert.Single(Lint("p(N) :- number_codes(N, \"42\")."));

        Assert.Equal(LintDiagnosticIds.DoubleQuotedListKindMismatch, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal((1, 25), (diagnostic.Span.Line, diagnostic.Span.Column));
        Assert.Contains("double_quotes=chars", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("use number_chars/2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodesPassedToACharacterConversionAreReported()
    {
        Diagnostic diagnostic = Assert.Single(Lint("p(A) :- atom_chars(A, \"ab\").", DoubleQuotesMode.Codes));

        Assert.Equal(LintDiagnosticIds.DoubleQuotedListKindMismatch, diagnostic.Id);
        Assert.Contains("double_quotes=codes", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("use atom_codes/2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("atom_codes")]
    [InlineData("number_chars")]
    [InlineData("string_codes")]
    public void AtomTextIsReportedForEveryListConversion(string conversion)
    {
        Diagnostic diagnostic = Assert.Single(Lint($"p(X) :- {conversion}(X, \"12\").", DoubleQuotesMode.Atom));

        Assert.Equal(LintDiagnosticIds.DoubleQuotedListKindMismatch, diagnostic.Id);
        Assert.Contains("reads as an atom", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("p(A) :- atom_chars(A, \"ab\"), number_chars(_, \"12\").", DoubleQuotesMode.Chars)]
    [InlineData("p(A) :- atom_codes(A, \"ab\"), number_codes(_, \"12\").", DoubleQuotesMode.Codes)]
    [InlineData("p(S) :- string_codes(S, \"ab\"), string_chars(S, \"ab\").", DoubleQuotesMode.Chars)]
    [InlineData("p(S) :- string_codes(S, \"ab\"), string_chars(S, \"ab\").", DoubleQuotesMode.Codes)]
    [InlineData("p(A) :- atom_codes(A, \"\").", DoubleQuotesMode.Chars)]
    [InlineData("p(A) :- atom_codes(A, [0'a]).", DoubleQuotesMode.Chars)]
    public void ConversionsMatchingTheReadingAreAccepted(string source, DoubleQuotesMode initial)
    {
        Assert.Empty(Lint(source, initial));
    }

    [Fact]
    public void ADirectiveMovesTheReadingForLaterClausesOnly()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            """
            before :- number_codes(_, "1").
            :- set_prolog_flag(double_quotes, codes).
            after :- number_codes(_, "2").
            """
        );

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Span.Line);
    }

    [Fact]
    public void PhraseOverCharactersIntoACodeGrammarIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(CodeGrammar + "main(N) :- phrase(number(N), \"427\").");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(LintDiagnosticIds.CodeGrammarOverCharacters, diagnostic.Id);
        Assert.Equal((5, 30), (diagnostic.Span.Line, diagnostic.Span.Column));
        // The message points at the code test, two nonterminals away from the one phrase/2 names.
        Assert.Contains("at line 3, column 26", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PhraseThreeAndQualifiedNonterminalsAreFollowed()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(CodeGrammar + "main(N, R) :- phrase(user:number(N), \"427\", R).");

        Assert.Equal(LintDiagnosticIds.CodeGrammarOverCharacters, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("alt --> ( [x] ; inner ).\ninner --> \\+ deep, [_].\ndeep --> [C], { code_type(C, digit(_)) }.")]
    [InlineData("alt --> [C], { between(0'a, 0'z, C) }.")]
    [InlineData("alt --> [0'a].")]
    public void CodeTestsAreFoundThroughGrammarControls(string grammar)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(grammar + "\nmain :- phrase(alt, \"a\").");

        Assert.Equal(LintDiagnosticIds.CodeGrammarOverCharacters, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void ARuleMixingTextAndCodeTerminalsIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("greeting --> \"hi\", [0' ], name.\nname --> [w].");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(LintDiagnosticIds.CodeGrammarOverCharacters, diagnostic.Id);
        Assert.Equal((1, 14), (diagnostic.Span.Line, diagnostic.Span.Column));
    }

    [Fact]
    public void ACharacterGrammarIsAccepted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            """
            digits([D|T]) --> digit(D), digits(T).
            digits([D])   --> digit(D).
            digit(D)      --> [D], { char_type(D, digit(_)) }.
            number(N)     --> digits(Ds), { number_chars(N, Ds) }.
            count(N)      --> [_], { N > 0 }.
            spaced        --> " ", number(_).
            main(N) :- phrase(number(N), "427"), phrase(spaced, " 1"), phrase(count(1), "x").
            """
        );

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ACodeGrammarReadingCodesIsAccepted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- set_prolog_flag(double_quotes, codes).\n" + CodeGrammar + "main(N) :- phrase(number(N), \"427\")."
        );

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AGrammarDefinedOutsideTheFileIsNotJudged()
    {
        Assert.Empty(Lint("main :- phrase(elsewhere, \"427\")."));
    }

    [Fact]
    public void DiagnosticsFromEveryRuleFollowSourceOrder()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- number_codes(N, \"1\").\nq(X) :- atom_codes(X, \"a\").");

        Assert.Collection(
            diagnostics,
            first => Assert.Equal(LintDiagnosticIds.SingletonVariable, first.Id),
            second => Assert.Equal(LintDiagnosticIds.DoubleQuotedListKindMismatch, second.Id),
            third =>
            {
                Assert.Equal(LintDiagnosticIds.DoubleQuotedListKindMismatch, third.Id);
                Assert.Equal(2, third.Span.Line);
            }
        );
    }

    private static IReadOnlyList<Diagnostic> Lint(string source, DoubleQuotesMode initial = DoubleQuotesMode.Chars)
    {
        ParseResult parsed = TermReader.ReadProgram(source, "source.pl");
        Assert.True(parsed.Success, string.Join(Environment.NewLine, parsed.Diagnostics));
        return PrologLinter.Analyze(parsed.Clauses, "source.pl", initial);
    }
}
