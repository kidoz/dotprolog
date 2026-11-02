using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologNonTailRecursionLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-tail-recursion-" + Guid.NewGuid().ToString("N")
    );

    public PrologNonTailRecursionLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- p, true.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void TailSelfCallIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- p.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void ReportsSelfCallWithRemainingWorkAtTheCallSite()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p(N) :- N > 0, p(N), writeln(N).\n");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
        Assert.Equal(1, warning.Span.Line);
        Assert.Contains("predicate p/1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("prevents last-call optimization", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FollowingCutPreventsTailPosition()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- p, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Theory]
    [InlineData("p", false)]
    [InlineData("p, true", true)]
    [InlineData("(p ; p)", false)]
    [InlineData("(p, true ; p)", true)]
    [InlineData("(true -> p ; true)", false)]
    [InlineData("(true -> true ; p)", false)]
    [InlineData("(p -> true ; true)", true)]
    [InlineData("(p *-> true)", true)]
    [InlineData("(true *-> p ; true)", false)]
    [InlineData("\\+ p", false)]
    [InlineData("call(p)", false)]
    [InlineData("findall(X, p, _)", false)]
    [InlineData("once(p)", false)]
    public void ControlStructureDeterminesTailPosition(string body, bool expected)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint($"p :- {body}.");

        Assert.Equal(expected, diagnostics.Any(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void MultipleNonTailBranchesReportIndependentCallSites()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- (p, true ; p, true).");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void SameModuleQualificationCountsAsSelfCall()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). p :- m:p, true.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void CrossModuleCallIsNotSelfRecursion()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). p :- other:p, true.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void IndirectAndMutualRecursionAreOutsideTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- q, true. q :- p.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void ReportsNonTerminalRecursionWithRemainingWork()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p --> p, [a].");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
        Assert.Contains("nonterminal p//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TailNonTerminalRecursionIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p --> p.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void DcgMetaCallIsExcluded()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p --> call(p).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("multifile")]
    public void DynamicAndMultifileSourceClausesAreEligible(string declaration)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint($":- {declaration}(p/0). p :- p, true.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- p, true.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- missing, p, true.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void IncludedClauseRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "p :- p, true.\n");
        Source("main.pl", ":- include('parts.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = "p :- p, true.";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleNonTailRecursion));
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
            EnabledRules = [LintDiagnosticIds.PossibleNonTailRecursion],
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
    )
    {
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(Path.GetTempPath(), "tail-recursion.pl"),
            source,
            new BytecodeProgram(mode)
        );
        return [.. loaded.Diagnostics, .. PrologLinter.AnalyzeProgram([loaded.Source], options ?? Enabled)];
    }

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
