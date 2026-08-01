using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologLinterTests
{
    [Fact]
    public void ReportsAnOrdinarySingletonAtItsOccurrence()
    {
        Diagnostic diagnostic = Assert.Single(Lint("value(X).", "source.pl"));

        Assert.Equal(LintDiagnosticIds.SingletonVariable, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("source.pl", diagnostic.FileName);
        Assert.Equal(new SourceSpan(6, 1, 1, 7), diagnostic.Span);
    }

    [Fact]
    public void RepeatedVariablesAndIntentionalSingletonsAreAccepted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("same(X, X). ignored(_, _Value).");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReportsARepeatedSingletonMarkerAtItsSecondOccurrence()
    {
        Diagnostic diagnostic = Assert.Single(Lint("same(_Value, _Value)."));

        Assert.Equal(LintDiagnosticIds.RepeatedSingletonMarker, diagnostic.Id);
        Assert.Equal(new SourceSpan(13, 6, 1, 14), diagnostic.Span);
    }

    [Fact]
    public void VariableScopeIsOneClause()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("first(X).\nsecond(X).");

        Assert.Collection(
            diagnostics,
            diagnostic =>
            {
                Assert.Equal(LintDiagnosticIds.SingletonVariable, diagnostic.Id);
                Assert.Equal(1, diagnostic.Span.Line);
            },
            diagnostic =>
            {
                Assert.Equal(LintDiagnosticIds.SingletonVariable, diagnostic.Id);
                Assert.Equal(2, diagnostic.Span.Line);
            }
        );
    }

    [Fact]
    public void DiagnosticsWithinAClauseFollowSourceOrder()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("pair(Z, A).");

        Assert.Collection(
            diagnostics,
            diagnostic =>
                Assert.Equal(
                    "Singleton variable 'Z'; prefix it with '_' when the single occurrence is intentional.",
                    diagnostic.Message
                ),
            diagnostic =>
                Assert.Equal(
                    "Singleton variable 'A'; prefix it with '_' when the single occurrence is intentional.",
                    diagnostic.Message
                )
        );
    }

    [Fact]
    public void DirectivesAreInspectedWithoutSpecialVariableScope()
    {
        Diagnostic diagnostic = Assert.Single(Lint(":- initialization(writeln(X))."));

        Assert.Equal(LintDiagnosticIds.SingletonVariable, diagnostic.Id);
        Assert.Equal(1, diagnostic.Span.Line);
    }

    private static IReadOnlyList<Diagnostic> Lint(string source, string? fileName = null)
    {
        ParseResult parsed = TermReader.ReadProgram(source, fileName);
        Assert.True(parsed.Success, string.Join(Environment.NewLine, parsed.Diagnostics));
        return PrologLinter.Analyze(parsed.Clauses, fileName);
    }
}
