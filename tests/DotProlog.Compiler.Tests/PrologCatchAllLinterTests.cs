using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologCatchAllLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-catch-all-" + Guid.NewGuid().ToString("N")
    );

    public PrologCatchAllLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- catch(true, _, true).", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Fact]
    public void ReportsAnonymousCatcherWithIdentityAndPosition()
    {
        const string source = "p :- catch(true, _, true).\n";
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
        Assert.Equal(source.IndexOf('_'), warning.Span.Start);
        Assert.Contains("predicate p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("may match every thrown term", warning.Message, StringComparison.Ordinal);
        Assert.Contains("intentional", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("p :- catch(true, Error, true).")]
    [InlineData("p :- catch(true, _Error, true).")]
    [InlineData("p :- catch(Error, Error, Error).")]
    public void NamedAndSharedCatchersAreEligible(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
        Assert.Equal(1, warning.Span.Line);
    }

    [Fact]
    public void PreviouslyAssignedCatcherRemainsAnAdvisoryFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- Error = expected, catch(true, Error, true).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Theory]
    [InlineData("p :- catch(true, expected, true).")]
    [InlineData("p :- catch(true, error(Formal, _), true).")]
    [InlineData("p :- catch(true, error(_, _), true).")]
    public void NonVariableCatcherPatternsAreNotReported(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Theory]
    [InlineData("p :- true, catch(true, _, true).")]
    [InlineData("p :- (true ; catch(true, _, true)).")]
    [InlineData("p :- (catch(true, _, true) -> true ; true).")]
    [InlineData("p :- (true -> catch(true, _, true) ; true).")]
    [InlineData("p :- (true -> true ; catch(true, _, true)).")]
    [InlineData("p :- (catch(true, _, true) *-> true).")]
    [InlineData("p :- (true *-> catch(true, _, true)).")]
    public void DirectControlFlowIsInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Theory]
    [InlineData("p :- catch(catch(true, _, true), error(_), true).")]
    [InlineData("p :- catch(true, error(_), catch(true, _, true)).")]
    public void NestedCatchGoalAndRecoveryAreInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Fact]
    public void NestedVariableCatchersProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- catch(catch(true, Inner, true), Outer, catch(true, Recovery, true)).");

        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleCatchAll));
    }

    [Theory]
    [InlineData("p :- call(catch(true, _, true)).")]
    [InlineData("p :- once(catch(true, _, true)).")]
    [InlineData("p :- \\+ catch(true, _, true).")]
    [InlineData("p :- findall(X, catch(true, _, true), X).")]
    [InlineData("p :- setup_call_cleanup(true, catch(true, _, true), true).")]
    [InlineData("p :- custom_meta(catch(true, _, true)).")]
    [InlineData("p :- system:catch(true, _, true).")]
    [InlineData("p :- X = f(catch(true, _, true)).")]
    public void OpaqueMetaCalledQualifiedAndDataGoalsAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Fact]
    public void ReportsVariableCatcherInNonterminal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> catch(true, _, true).");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
        Assert.Contains("nonterminal nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsVariableCatcherInsideDcgOrdinaryGoal()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt --> { catch(true, _, true) }.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Theory]
    [InlineData("nt --> call(catch(true, _, true)).")]
    [InlineData("nt --> { call(catch(true, _, true)) }.")]
    [InlineData("nt --> [catch(true, _, true)].")]
    public void DcgMetaCallsAndTerminalsAreNotInspected(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Theory]
    [InlineData(":- dynamic(p/0). p :- catch(true, _, true).")]
    [InlineData(":- multifile(p/0). p :- catch(true, _, true).")]
    public void DynamicAndMultifileClausesAreEligible(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Fact]
    public void MultipleMatchingClausesProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            "p :- catch(true, _, true). p :- catch(true, Error, true). q :- catch(true, expected, true)."
        );

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleCatchAll));
    }

    [Fact]
    public void IncludedClauseRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "p :- catch(true, _, true).\n");
        Source("main.pl", ":- include('parts.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- catch(true, _, true).", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- missing, catch(true, _, true).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = "p :- catch(true, _, true).";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleCatchAll));
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
            EnabledRules = [LintDiagnosticIds.PossibleCatchAll],
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
