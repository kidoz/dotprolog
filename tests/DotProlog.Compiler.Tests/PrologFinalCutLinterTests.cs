using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologFinalCutLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-final-cuts-" + Guid.NewGuid().ToString("N")
    );

    public PrologFinalCutLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- !.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void ReportsFinalCutWithIdentityAndPosition()
    {
        const string source = "p :- true, !.\n";
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Equal(source.IndexOf('!'), warning.Span.Start);
        Assert.Contains("predicate p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("last supplied clause", warning.Message, StringComparison.Ordinal);
        Assert.Contains("intentional", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheLastRuleIsEligible()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- !. p :- true. p :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void LaterRuleWithoutFinalCutSuppressesAnEarlierFinalCut()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- !. p :- true.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void LaterFactSuppressesAnEarlierFinalCut()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- !. p.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("p :- !.", true)]
    [InlineData("p :- true, !.", true)]
    [InlineData("p :- !, true.", false)]
    [InlineData("p :- (true ; !).", true)]
    [InlineData("p :- (! ; true).", true)]
    [InlineData("p :- (true -> ! ; true).", true)]
    [InlineData("p :- (true -> true ; !).", true)]
    [InlineData("p :- (! -> true ; true).", false)]
    [InlineData("p :- (true *-> !).", true)]
    [InlineData("p :- (! *-> true).", false)]
    public void FinalControlFlowPositionDeterminesEligibility(string source, bool expected)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Equal(expected, diagnostics.Any(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void IndependentFinalBranchCutsProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- (! ; !).");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("p :- \\+ !.")]
    [InlineData("p :- call(!).")]
    [InlineData("p :- once(!).")]
    [InlineData("p :- findall(X, (X = 1 ; !), _).")]
    [InlineData("p :- setup_call_cleanup(true, true, !).")]
    [InlineData("p :- custom_meta(!).")]
    [InlineData("p :- X = f(!).")]
    public void OpaqueMetaCalledAndDataCutsAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void ReportsFinalCutInNonterminal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> !.");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Contains("nonterminal nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsFinalCutInsideDcgOrdinaryGoal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> { ! }.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("nt --> !, [a].")]
    [InlineData("nt --> call(!).")]
    [InlineData("nt --> { call(!) }.")]
    public void NonfinalAndMetaCalledDcgCutsAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("p --> !. p(_, _) :- true.")]
    [InlineData("p(_, _) :- !. p --> true.")]
    public void MixedDefinitionsUseTheirSharedLoweredIdentity(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("p --> true. p(_, _) :- !.", "predicate p/2")]
    [InlineData("p(_, _) :- true. p --> !.", "nonterminal p//0")]
    public void LastMixedClauseDeterminesTheReportedSourceKind(string source, string identity)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Contains(identity, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(":- dynamic(p/0). p :- !.")]
    [InlineData(":- multifile(p/0). p :- !.")]
    public void DynamicAndMultifileProceduresAreExcluded(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void DeclarationInAnotherNamedFileExcludesTheProcedure()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [("declarations.pl", ":- module(m, []). :- multifile(p/0)."), ("definitions.pl", ":- module(m, []). p :- !.")],
            Enabled
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void StaticDiscontiguousProcedureRemainsEligible()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- discontiguous(p/0). p :- !. q. p :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void LastClauseIsSelectedAcrossNamedFiles()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint([("first.pl", "p :- !."), ("second.pl", "p :- true.")], Enabled);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void FinalCutInLastNamedFileRetainsItsSource()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint([("first.pl", "p :- true."), ("second.pl", "p :- !.")], Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.EndsWith("second.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void ModulesHaveIndependentLastClauses()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [("m.pl", ":- module(m, []). p :- !."), ("n.pl", ":- module(n, []). p :- !.")],
            Enabled
        );

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void IncludedLastClauseRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "p :- !.\n");
        Source("main.pl", ":- include('parts.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void IncludedLaterClauseSuppressesAnEarlierFinalCut()
    {
        Source("parts.pl", "p :- true.\n");
        Source("main.pl", "p :- !.\n:- include('parts.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- !.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- missing, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRulesAndLaterFacts()
    {
        const string text = "p :- !. p. q :- !.";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Contains("q/0", warning.Message, StringComparison.Ordinal);
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
            EnabledRules = [LintDiagnosticIds.PossibleFinalCut],
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
