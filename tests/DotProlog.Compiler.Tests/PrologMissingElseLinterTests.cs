using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologMissingElseLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-missing-else-" + Guid.NewGuid().ToString("N")
    );

    public PrologMissingElseLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- (true -> true).", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void ReportsConditionalWithIdentityAndPosition()
    {
        const string source = "p :- (true -> true).\n";
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
        Assert.Equal(source.IndexOf("true -> true", StringComparison.Ordinal), warning.Span.Start);
        Assert.Contains("predicate p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("no explicit else branch", warning.Message, StringComparison.Ordinal);
        Assert.Contains("condition-failure behavior", warning.Message, StringComparison.Ordinal);
        Assert.Contains("intentional", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("->")]
    [InlineData("*->")]
    public void BareHardAndSoftConditionalsAreReported(string conditionalOperator)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint($"p :- (a {conditionalOperator} b).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Theory]
    [InlineData("p :- (a -> b ; true).")]
    [InlineData("p :- (a -> b ; fail).")]
    [InlineData("p :- (a -> b ; writeln(no)).")]
    [InlineData("p :- (a *-> b ; true).")]
    [InlineData("p :- (a *-> b ; fail).")]
    [InlineData("p :- (a *-> b ; writeln(no)).")]
    public void AnyExplicitElseBranchSuppressesTheOuterFinding(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Theory]
    [InlineData("p :- true, (a -> b).")]
    [InlineData("p :- (true ; (a -> b)).")]
    [InlineData("p :- ((a -> b) -> c ; d).")]
    [InlineData("p :- (a -> (b -> c) ; d).")]
    [InlineData("p :- (a -> b ; (c -> d)).")]
    [InlineData("p :- ((a *-> b) *-> c ; d).")]
    [InlineData("p :- (a *-> (b *-> c) ; d).")]
    [InlineData("p :- (a *-> b ; (c *-> d)).")]
    public void DirectControlFlowAndNestedConditionalsAreInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void NestedBareConditionalsProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- ((a -> b) -> (c -> d)).");

        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleMissingElse));
    }

    [Theory]
    [InlineData("p :- \\+ (a -> b).")]
    [InlineData("p :- findall(X, (a -> b), X).")]
    [InlineData("p :- setup_call_cleanup(true, (a -> b), true).")]
    [InlineData("p :- call((a -> b)).")]
    [InlineData("p :- call((a -> b), X).")]
    [InlineData("p :- once((a -> b)).")]
    [InlineData("p :- custom_meta((a -> b)).")]
    [InlineData("p :- system:(a -> b).")]
    [InlineData("p :- X = f(a -> b).")]
    public void OpaqueMetaCalledQualifiedAndDataGoalsAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void ReportsBareConditionalInNonterminal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> (a -> b).");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
        Assert.Contains("nonterminal nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsBareConditionalInsideDcgOrdinaryGoal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> { (a -> b) }.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Theory]
    [InlineData("nt --> call((a -> b)).")]
    [InlineData("nt --> { call((a -> b)) }.")]
    [InlineData("nt --> [f(a -> b)].")]
    [InlineData("nt --> { X = f(a -> b) }.")]
    public void DcgMetaCallsAndTermDataAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Theory]
    [InlineData(":- dynamic(p/0). p :- (a -> b).")]
    [InlineData(":- multifile(p/0). p :- (a -> b).")]
    public void DynamicAndMultifileClausesAreEligible(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void MultipleMatchingClausesProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- (a -> b). p :- (a *-> b). q :- (a -> b ; true).");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void IncludedClauseRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "p :- (a -> b).\n");
        Source("main.pl", ":- include('parts.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- (a *-> b).", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- missing, (a -> b).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void DuplicateEnabledRulesDoNotDuplicateFindings()
    {
        PrologLintOptions options = PrologLintOptions.SemanticOnly with
        {
            EnabledRules = [LintDiagnosticIds.PossibleMissingElse, LintDiagnosticIds.PossibleMissingElse],
        };

        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- (a -> b).", options);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = "p :- (a -> b).";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- initialization(halt(9)). p :- (a -> b).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMissingElse));
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
            EnabledRules = [LintDiagnosticIds.PossibleMissingElse],
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
