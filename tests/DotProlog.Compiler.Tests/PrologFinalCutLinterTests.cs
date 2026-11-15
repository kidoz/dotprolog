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
        var diagnostics = Lint("p :- !.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void ReportsFinalCutWithIdentityAndPosition()
    {
        const string source = "p :- true, !.\n";
        var diagnostics = Lint(source);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Equal(source.IndexOf('!'), warning.Span.Start);
        Assert.Contains("predicate p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("last supplied clause", warning.Message, StringComparison.Ordinal);
        Assert.Contains("intentional", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheLastRuleIsEligible()
    {
        var diagnostics = Lint("p :- !. p :- true. p :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void LaterRuleWithoutFinalCutSuppressesAnEarlierFinalCut()
    {
        var diagnostics = Lint("p :- !. p :- true.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void LaterFactSuppressesAnEarlierFinalCut()
    {
        var diagnostics = Lint("p :- !. p.");

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
        var diagnostics = Lint(source);

        Assert.Equal(expected, diagnostics.Any(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void IndependentFinalBranchCutsProduceSeparateFindings()
    {
        var diagnostics = Lint("p :- (! ; !).");

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
        var diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void ReportsFinalCutInNonterminal()
    {
        var diagnostics = Lint("nt --> !.");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Contains("nonterminal nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsFinalCutInsideDcgOrdinaryGoal()
    {
        var diagnostics = Lint("nt --> { ! }.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("nt --> !, [a].")]
    [InlineData("nt --> call(!).")]
    [InlineData("nt --> { call(!) }.")]
    public void NonfinalAndMetaCalledDcgCutsAreNotInspected(string source)
    {
        var diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("p --> !. p(_, _) :- true.")]
    [InlineData("p(_, _) :- !. p --> true.")]
    public void MixedDefinitionsUseTheirSharedLoweredIdentity(string source)
    {
        var diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData("p --> true. p(_, _) :- !.", "predicate p/2")]
    [InlineData("p(_, _) :- true. p --> !.", "nonterminal p//0")]
    public void LastMixedClauseDeterminesTheReportedSourceKind(string source, string identity)
    {
        var diagnostics = Lint(source);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Contains(identity, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(":- dynamic(p/0). p :- !.")]
    [InlineData(":- multifile(p/0). p :- !.")]
    public void DynamicAndMultifileProceduresAreExcluded(string source)
    {
        var diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void DeclarationInAnotherNamedFileExcludesTheProcedure()
    {
        var diagnostics = Lint(
            [("declarations.pl", ":- module(m, []). :- multifile(p/0)."), ("definitions.pl", ":- module(m, []). p :- !.")],
            Enabled
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void StaticDiscontiguousProcedureRemainsEligible()
    {
        var diagnostics = Lint(":- discontiguous(p/0). p :- !. q. p :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void LastClauseIsSelectedAcrossNamedFiles()
    {
        var diagnostics = Lint([("first.pl", "p :- !."), ("second.pl", "p :- true.")], Enabled);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void FinalCutInLastNamedFileRetainsItsSource()
    {
        var diagnostics = Lint([("first.pl", "p :- true."), ("second.pl", "p :- !.")], Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.EndsWith("second.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void ModulesHaveIndependentLastClauses()
    {
        var diagnostics = Lint(
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

        var diagnostics = Load("main.pl");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void IncludedLaterClauseSuppressesAnEarlierFinalCut()
    {
        Source("parts.pl", "p :- true.\n");
        Source("main.pl", "p :- !.\n:- include('parts.pl').\n");

        var diagnostics = Load("main.pl");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        var diagnostics = Lint("p :- !.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        var diagnostics = Lint("p :- missing, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRulesAndLaterFacts()
    {
        const string text = "p :- !. p. q :- !.";
        var loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        var diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleFinalCut));
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
