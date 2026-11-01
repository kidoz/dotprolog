using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologTrivialFailureLinterTests
{
    [Fact]
    public void ReportsAGroundCallWithNoMatchingClauseHead() =>
        Assert.Contains(Lint("p(a). go :- p(b)."), Id(LintDiagnosticIds.TrivialCallFailure));

    [Fact]
    public void ReportsAcrossNamedFiles() =>
        Assert.Contains(
            Lint(
                ("main.pl", ":- module(main, []). :- use_module('other.pl'). go :- p(b)."),
                ("other.pl", ":- module(other, [p/1]). p(a).")
            ),
            Id(LintDiagnosticIds.TrivialCallFailure)
        );

    [Fact]
    public void ReportsInconsistentRepeatedHeadVariables() =>
        Assert.Contains(Lint("p(X, X). go :- p(a, b)."), Id(LintDiagnosticIds.TrivialCallFailure));

    [Theory]
    [InlineData("p(a). go :- p(a).")]
    [InlineData("p(X). go :- p(b).")]
    [InlineData("p(X, X). go :- p(a, a).")]
    public void AcceptsAMatchingClauseHead(string source) =>
        Assert.DoesNotContain(Lint(source), Id(LintDiagnosticIds.TrivialCallFailure));

    [Fact]
    public void MatchesCompoundTermsAndListsRecursively()
    {
        Assert.Contains(Lint("p(f([a])). go :- p(f([b]))."), Id(LintDiagnosticIds.TrivialCallFailure));
        Assert.DoesNotContain(Lint("p(f([a])). go :- p(f([a]))."), Id(LintDiagnosticIds.TrivialCallFailure));
    }

    [Fact]
    public void MatchingHeadCountsEvenWhenBodyWouldFailOrHalt()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(a) :- halt(9). go :- p(a).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.TrivialCallFailure));
    }

    [Theory]
    [InlineData(":- dynamic(p/1). p(a). go :- p(b).")]
    [InlineData(":- multifile(p/1). p(a). go :- p(b).")]
    public void SuppressesDynamicAndMultifilePredicates(string source) =>
        Assert.DoesNotContain(Lint(source), Id(LintDiagnosticIds.TrivialCallFailure));

    [Fact]
    public void SuppressesVariableContainingCalls() =>
        Assert.DoesNotContain(Lint("p(a). go(X) :- p(X)."), Id(LintDiagnosticIds.TrivialCallFailure));

    [Fact]
    public void SuppressesReaderDependentHeadText() =>
        Assert.DoesNotContain(Lint("p(\"a\"). go :- p(\"b\")."), Id(LintDiagnosticIds.TrivialCallFailure));

    [Fact]
    public void SuppressesUnknownImportSources() =>
        Assert.DoesNotContain(
            Lint(":- module(main, []). :- use_module('missing.pl'). go :- p(b)."),
            Id(LintDiagnosticIds.TrivialCallFailure)
        );

    [Fact]
    public void BuiltinsAreNotCheckedAsUserClauses() =>
        Assert.DoesNotContain(Lint("go :- atom(42)."), Id(LintDiagnosticIds.TrivialCallFailure));

    [Fact]
    public void MetaClosureArgumentsAreNotTreatedAsDirectGroundCalls() =>
        Assert.DoesNotContain(Lint("p(a). go(X) :- maplist(p, X)."), Id(LintDiagnosticIds.TrivialCallFailure));

    [Fact]
    public void InitializationGoalsAreCheckedWithoutExecution() =>
        Assert.Contains(Lint("p(a). :- initialization(p(b))."), Id(LintDiagnosticIds.TrivialCallFailure));

    private static IReadOnlyList<Diagnostic> Lint(params (string Name, string Text)[] inputs)
    {
        List<PrologLintSource> sources = [];
        foreach ((string name, string text) in inputs)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), name);
            sources.Add(PrologLintSourceLoader.Load(path, text, new BytecodeProgram()).Source);
        }

        return PrologLinter.AnalyzeProgram(sources);
    }

    private static IReadOnlyList<Diagnostic> Lint(string source) => Lint(("main.pl", source));

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
