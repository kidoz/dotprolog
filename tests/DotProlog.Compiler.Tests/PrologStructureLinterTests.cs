using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologStructureLinterTests
{
    [Fact]
    public void ReportsDiscontiguousClauses() =>
        Assert.Contains(Lint("p(a). q(a). p(b)."), Id(LintDiagnosticIds.DiscontiguousPredicate));

    [Fact]
    public void DiscontiguousDeclarationSuppressesTheWarning() =>
        Assert.DoesNotContain(Lint(":- discontiguous(p/1). p(a). q(a). p(b)."), Id(LintDiagnosticIds.DiscontiguousPredicate));

    [Fact]
    public void AcceptsContiguousClauses() =>
        Assert.DoesNotContain(Lint("p(a). p(b). q(a)."), Id(LintDiagnosticIds.DiscontiguousPredicate));

    [Fact]
    public void ReportsAlphaEquivalentDuplicateClauses() =>
        Assert.Contains(Lint("p(X) :- q(X). p(Y) :- q(Y)."), Id(LintDiagnosticIds.DuplicateClause));

    [Fact]
    public void AcceptsDistinctClauses() =>
        Assert.DoesNotContain(Lint("p(X) :- q(X). p(X) :- r(X)."), Id(LintDiagnosticIds.DuplicateClause));

    [Fact]
    public void ReportsADuplicateDeclaration() =>
        Assert.Contains(Lint(":- dynamic(p/1). :- dynamic(p/1). p(a)."), Id(LintDiagnosticIds.DuplicateDirective));

    [Fact]
    public void DifferentMetaPredicateTemplatesAreNotExactDuplicates() =>
        Assert.DoesNotContain(
            Lint(":- meta_predicate(p(0)). :- meta_predicate(p(1)). p(_)."),
            Id(LintDiagnosticIds.DuplicateDirective)
        );

    [Theory]
    [InlineData("p(X) :- p(Y), q(X).")]
    [InlineData("p(X) --> p(Y), [a].")]
    public void ReportsDirectLeftRecursion(string source) =>
        Assert.Contains(Lint(source), Id(LintDiagnosticIds.DirectLeftRecursion));

    [Theory]
    [InlineData("p(X) :- q(X), p(X).")]
    [InlineData("p(a) :- p(b).")]
    public void AcceptsNonLeftmostOrNonVariantRecursion(string source) =>
        Assert.DoesNotContain(Lint(source), Id(LintDiagnosticIds.DirectLeftRecursion));

    [Theory]
    [InlineData(":- discontiguous(p/1).")]
    [InlineData(":- meta_predicate(p(0)).")]
    public void ReportsDeclarationsWithoutDefinitions(string source) =>
        Assert.Contains(Lint(source), Id(LintDiagnosticIds.DeclaredButUndefined));

    [Fact]
    public void DynamicDeclarationMayIntentionallyHaveNoClauses() =>
        Assert.DoesNotContain(Lint(":- dynamic(p/1)."), Id(LintDiagnosticIds.DeclaredButUndefined));

    private static IReadOnlyList<Diagnostic> Lint(string source)
    {
        ParseResult parsed = TermReader.ReadProgram(source, "source.pl");
        Assert.True(parsed.Success, string.Join(Environment.NewLine, parsed.Diagnostics));
        return PrologLinter.Analyze(parsed.Clauses, "source.pl");
    }

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
