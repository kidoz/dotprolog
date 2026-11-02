using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologRepeatLoopLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-repeat-loops-" + Guid.NewGuid().ToString("N")
    );

    public PrologRepeatLoopLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- repeat.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Fact]
    public void ReportsFirstRepeatWithIdentityAndPosition()
    {
        const string source = "p :- repeat, fail, repeat.\n";
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
        Assert.Equal(source.IndexOf("repeat", StringComparison.Ordinal), warning.Span.Start);
        Assert.Contains("predicate p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("no cut", warning.Message, StringComparison.Ordinal);
        Assert.Contains("review", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClauseWithoutRepeatIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- true.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Theory]
    [InlineData("p :- !, repeat.")]
    [InlineData("p :- repeat, !.")]
    [InlineData("p :- (repeat ; !).")]
    [InlineData("p :- (repeat -> ! ; true).")]
    [InlineData("p :- (! *-> repeat).")]
    public void SameScopeCutSuppressesTheFinding(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Theory]
    [InlineData("p :- repeat, (true -> true ; true).")]
    [InlineData("p :- repeat, \\+ !.")]
    [InlineData("p :- repeat, call(!).")]
    [InlineData("p :- repeat, findall(X, (X = 1 ; !), _).")]
    [InlineData("p :- repeat, once(!).")]
    [InlineData("p :- repeat, custom_meta(!).")]
    public void OpaqueCutDoesNotSuppressTheFinding(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Theory]
    [InlineData("p :- true, repeat.")]
    [InlineData("p :- (true ; repeat).")]
    [InlineData("p :- (repeat -> true ; true).")]
    [InlineData("p :- (true -> repeat ; true).")]
    [InlineData("p :- (true -> true ; repeat).")]
    [InlineData("p :- (repeat *-> true).")]
    [InlineData("p :- (true *-> repeat).")]
    public void DirectControlFlowIsInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Theory]
    [InlineData("p :- call(repeat).")]
    [InlineData("p :- once(repeat).")]
    [InlineData("p :- \\+ repeat.")]
    [InlineData("p :- findall(X, repeat, X).")]
    [InlineData("p :- setup_call_cleanup(true, repeat, true).")]
    [InlineData("p :- custom_meta(repeat).")]
    [InlineData("p :- X = f(repeat).")]
    [InlineData("p :- system:repeat.")]
    public void OpaqueMetaCalledQualifiedAndDataGoalsAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Fact]
    public void ReportsDirectRepeatInNonterminal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> repeat.");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
        Assert.Contains("nonterminal nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsRepeatInsideDcgOrdinaryGoal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> { repeat }.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Theory]
    [InlineData("nt --> call(repeat).")]
    [InlineData("nt --> { call(repeat) }.")]
    [InlineData("nt --> [repeat].")]
    public void DcgMetaCallsAndTerminalsAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Theory]
    [InlineData(":- dynamic(p/0). p :- repeat.")]
    [InlineData(":- multifile(p/0). p :- repeat.")]
    public void DynamicAndMultifileClausesAreEligible(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Fact]
    public void MultipleMatchingClausesProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- repeat. p :- repeat. q :- repeat, !.");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Fact]
    public void IncludedClauseRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "p :- repeat.\n");
        Source("main.pl", ":- include('parts.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- repeat.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- missing, repeat.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = "p :- repeat.";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleRepeatWithoutCut));
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
            EnabledRules = [LintDiagnosticIds.PossibleRepeatWithoutCut],
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
