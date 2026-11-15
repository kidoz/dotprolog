using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologNonSteadfastLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-steadfastness-" + Guid.NewGuid().ToString("N")
    );

    public PrologNonSteadfastLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        var diagnostics = Lint("max(X, Y, X) :- X >= Y, !.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void ReportsCrossArgumentAliasingAtTheFirstCut()
    {
        var diagnostics = Lint("max(X, Y, X) :- X >= Y, !.\nmax(_, Y, Y).");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Equal(1, warning.Span.Line);
        Assert.Contains("predicate max/3", warning.Message, StringComparison.Ordinal);
        Assert.Contains("may be non-steadfast", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindsAliasesNestedInDistinctHeadArguments()
    {
        var diagnostics = Lint("p(f(X), g(X)) :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void RepetitionWithinOneArgumentIsNotCrossArgumentAliasing()
    {
        var diagnostics = Lint("p(f(X, X)) :- !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void AnonymousVariablesDoNotEstablishAliasing()
    {
        var diagnostics = Lint("p(_, _) :- !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void AliasingWithoutACutIsNotReported()
    {
        var diagnostics = Lint("max(X, Y, X) :- X >= Y.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultipleCutsProduceOneFindingPerClause()
    {
        var diagnostics = Lint("p(X, X) :- !, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultipleMatchingClausesProduceSeparateFindings()
    {
        var diagnostics = Lint("p(X, X) :- !. p(X, X) :- !.");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Theory]
    [InlineData("(true ; !)", true)]
    [InlineData("(true -> ! ; true)", true)]
    [InlineData("(true -> true ; !)", true)]
    [InlineData("(! *-> true)", true)]
    [InlineData("(! -> true ; true)", false)]
    [InlineData("\\+ !", false)]
    [InlineData("call(!)", false)]
    [InlineData("findall(Y, (Y = 1 ; !), _)", false)]
    [InlineData("once(!)", false)]
    [InlineData("custom_meta(!)", false)]
    public void CutScopeDeterminesEligibility(string body, bool expected)
    {
        var diagnostics = Lint($"p(X, X) :- {body}.");

        Assert.Equal(expected, diagnostics.Any(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void AnOpaqueConditionCutDoesNotHideALaterClauseCut()
    {
        var diagnostics = Lint("p(X, X) :- ((! -> true ; true), !).");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Equal(1, warning.Span.Line);
    }

    [Fact]
    public void ReportsAliasingInAGrammarRule()
    {
        var diagnostics = Lint("nt(A, A) --> [A], { A >= 65 }, !.");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Contains("nonterminal nt//2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneGrammarArgumentAndDifferenceListsAreNotAliasing()
    {
        var diagnostics = Lint("nt(A) --> [A], !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultifileClausesAreExcluded()
    {
        var diagnostics = Lint(":- multifile(p/2). p(X, X) :- !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void DynamicClausesAreEligible()
    {
        var diagnostics = Lint(":- dynamic(p/2). p(X, X) :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultifileDeclarationsApplyAcrossNamedFiles()
    {
        var diagnostics = Lint(
            [("declarations.pl", ":- module(m, []). :- multifile(p/2)."), ("definitions.pl", ":- module(m, []). p(X, X) :- !.")],
            Enabled
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void IncludedClauseRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "p(X, X) :- !.\n");
        Source("main.pl", ":- include('parts.pl').\n");

        var diagnostics = Load("main.pl");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        var diagnostics = Lint("max(X, Y, X) :- X >= Y, !.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        var diagnostics = Lint("p(X, X) :- missing, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = "max(X, Y, X) :- X >= Y, !.";
        var loaded = PrologLintSourceLoader.Load(Path.Combine(_directory, "single.pl"), text, new BytecodeProgram());

        var diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static PrologLintOptions Enabled =>
        PrologLintOptions.SemanticOnly with
        {
            EnabledRules = [LintDiagnosticIds.PossibleNonSteadfastClause],
        };

    private IReadOnlyList<Diagnostic> Load(string name)
    {
        var path = Path.Combine(_directory, name);
        var loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
        return [.. loaded.Diagnostics, .. PrologLinter.AnalyzeProgram([loaded.Source], Enabled)];
    }

    private static IReadOnlyList<Diagnostic> Lint(
        string source,
        PrologLintOptions? options = null,
        PrologLanguageMode mode = PrologLanguageMode.Modern
    ) => Lint([("main.pl", source)], options ?? Enabled, mode);

    private static IReadOnlyList<Diagnostic> Lint(
        (string Name, string Text)[] inputs,
        PrologLintOptions options,
        PrologLanguageMode mode = PrologLanguageMode.Modern
    )
    {
        List<PrologLintSource> sources = [];
        List<Diagnostic> diagnostics = [];
        foreach ((var name, var text) in inputs)
        {
            var loaded = PrologLintSourceLoader.Load(
                Path.Combine(Path.GetTempPath(), name),
                text + "\n",
                new BytecodeProgram(mode)
            );
            sources.Add(loaded.Source);
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources, options)];
    }

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
