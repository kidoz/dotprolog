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
        IReadOnlyList<Diagnostic> diagnostics = Lint("max(X, Y, X) :- X >= Y, !.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void ReportsCrossArgumentAliasingAtTheFirstCut()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("max(X, Y, X) :- X >= Y, !.\nmax(_, Y, Y).");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Equal(1, warning.Span.Line);
        Assert.Contains("predicate max/3", warning.Message, StringComparison.Ordinal);
        Assert.Contains("may be non-steadfast", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindsAliasesNestedInDistinctHeadArguments()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(f(X), g(X)) :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void RepetitionWithinOneArgumentIsNotCrossArgumentAliasing()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(f(X, X)) :- !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void AnonymousVariablesDoNotEstablishAliasing()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(_, _) :- !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void AliasingWithoutACutIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("max(X, Y, X) :- X >= Y.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultipleCutsProduceOneFindingPerClause()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(X, X) :- !, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultipleMatchingClausesProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(X, X) :- !. p(X, X) :- !.");

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
        IReadOnlyList<Diagnostic> diagnostics = Lint($"p(X, X) :- {body}.");

        Assert.Equal(expected, diagnostics.Any(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void AnOpaqueConditionCutDoesNotHideALaterClauseCut()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(X, X) :- ((! -> true ; true), !).");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Equal(1, warning.Span.Line);
    }

    [Fact]
    public void ReportsAliasingInAGrammarRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt(A, A) --> [A], { A >= 65 }, !.");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Contains("nonterminal nt//2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneGrammarArgumentAndDifferenceListsAreNotAliasing()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt(A) --> [A], !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultifileClausesAreExcluded()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- multifile(p/2). p(X, X) :- !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void DynamicClausesAreEligible()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- dynamic(p/2). p(X, X) :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void MultifileDeclarationsApplyAcrossNamedFiles()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
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

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("max(X, Y, X) :- X >= Y, !.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(X, X) :- missing, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonSteadfastClause));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = "max(X, Y, X) :- X >= Y, !.";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

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
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
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
        foreach ((string name, string text) in inputs)
        {
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
                Path.Combine(Path.GetTempPath(), name),
                text,
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
