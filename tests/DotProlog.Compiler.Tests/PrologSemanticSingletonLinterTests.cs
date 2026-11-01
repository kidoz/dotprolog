using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologSemanticSingletonLinterTests
{
    [Theory]
    [InlineData("run :- (first(X) ; second(X)).")]
    [InlineData("run :- (first(X) ; second(X) ; third(X)).")]
    [InlineData("run :- (true -> first(X) ; second(X)).")]
    public void ReportsVariablesThatCommunicateNothingBetweenAlternatives(string source) =>
        Assert.Contains(Lint(source), Id(LintDiagnosticIds.SemanticSingletonVariable));

    [Theory]
    [InlineData("run(X) :- (first(X) ; second(X)).")]
    [InlineData("run :- (first(X, X) ; second(X)).")]
    [InlineData("run :- (first(X) -> true ; second(X)).")]
    [InlineData("run :- (first(_X) ; second(_X)).")]
    public void AcceptsSharedRepeatedConditionalAndMarkedVariables(string source) =>
        Assert.DoesNotContain(Lint(source), Id(LintDiagnosticIds.SemanticSingletonVariable));

    private static IReadOnlyList<Diagnostic> Lint(string source)
    {
        ParseResult parsed = TermReader.ReadProgram(source, "source.pl");
        Assert.True(parsed.Success, string.Join(Environment.NewLine, parsed.Diagnostics));
        return PrologLinter.Analyze(parsed.Clauses, "source.pl");
    }

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
