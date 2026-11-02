using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologExcessiveSubgoalCountLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-subgoal-count-" + Guid.NewGuid().ToString("N")
    );

    public PrologExcessiveSubgoalCountLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void ReportsDefaultExcessWithIdentityAndExactSpan()
    {
        const string source = "p :- a,b,c,d,e,f,g,h,i.\n";
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Equal(0, warning.Span.Start);
        Assert.Equal(1, warning.Span.Length);
        Assert.Contains("Rule for predicate user:p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 9 direct subgoals", warning.Message, StringComparison.Ordinal);
        Assert.Contains("maximum of 8", warning.Message, StringComparison.Ordinal);
        Assert.Contains("organization is intentional", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CountEqualToDefaultLimitIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- a,b,c,d,e,f,g,h.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void CustomLimitUsesStrictlyGreaterComparison()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- a,b. q :- a,b,c.", EnabledWithMaximum(2));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("user:q/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 3 direct subgoals", warning.Message, StringComparison.Ordinal);
        Assert.Contains("maximum of 2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FactsProduceNoCountEvenAtMinimumLimit()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p. p(A, B, C).", EnabledWithMaximum(1));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidReusableLimitIsRejectedWhenRuleIsEnabled(int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = Lint("p :- a.", EnabledWithMaximum(maximum)));
    }

    [Fact]
    public void InvalidLimitIsInertWhenRuleIsDisabled()
    {
        PrologLintOptions options = PrologLintOptions.SemanticOnly with { MaxClauseSubgoals = 0 };

        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", options);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Theory]
    [InlineData("p :- a,(b,c).")]
    [InlineData("p :- a,(b;c).")]
    [InlineData("p :- (a->b;c).")]
    [InlineData("p :- (a*->b),c.")]
    [InlineData("p :- a,(b->c).")]
    public void TransparentControlFlowCountsLeavesWithoutCountingControlNodes(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source, EnabledWithMaximum(2));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("has 3 direct subgoals", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("p :- \\+ (a,b).")]
    [InlineData("p :- findall(X,(a,b),X).")]
    [InlineData("p :- setup_call_cleanup(a,(b,c),d).")]
    [InlineData("p :- catch((a,b),E,c).")]
    [InlineData("p :- call((a,b)).")]
    [InlineData("p :- call((a,b),X).")]
    [InlineData("p :- custom_meta((a,b)).")]
    [InlineData("p :- system:(a,b).")]
    [InlineData("p :- X = f(a,b).")]
    [InlineData("p :- X.")]
    public void OpaqueGoalsAndTermDataAreNotTraversed(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source, EnabledWithMaximum(1));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void OpaqueGoalContributesOneToOuterCount()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- findall(X,(a,b,c),X),d.", EnabledWithMaximum(1));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("has 2 direct subgoals", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nt --> {a,b,c}.")]
    [InlineData("nt --> [a],b,[c].")]
    public void DcgOrdinaryGoalsAndTerminalsFollowTheSameCount(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source, EnabledWithMaximum(2));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("Grammar rule for nonterminal user:nt//0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 3 direct subgoals", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nt --> call((a,b,c)).")]
    [InlineData("nt --> {call((a,b,c))}.")]
    public void DcgMetaCallsRemainOpaque(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source, EnabledWithMaximum(1));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void DcgSemicontextIsNotCounted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("nt, [X] --> {a,b}.", EnabledWithMaximum(1));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("user:nt//0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 2 direct subgoals", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleAndDuplicateClausesProduceIndependentFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- a,b. p :- a,b.", EnabledWithMaximum(1));

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Theory]
    [InlineData(":- dynamic(p/0). p :- a,b.")]
    [InlineData(":- multifile(p/0). p :- a,b.")]
    public void DynamicAndMultifileRulesAreEligible(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source, EnabledWithMaximum(1));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void ModuleDirectiveContributesToIdentity()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). p :- a,b,c,d,e,f,g,h,i.");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("predicate m:p/0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludedRuleRetainsPhysicalOwnership()
    {
        Source("parts.pl", "p :- a,b,c,d,e,f,g,h,i.\n");
        Source("main.pl", ":- include('parts.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void UnrecognizedHeadIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("X :- a,b.", EnabledWithMaximum(1));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- missing,a,b,c,d,e,f,g,h,i.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void DuplicateEnabledRulesDoNotDuplicateFindings()
    {
        PrologLintOptions options = Enabled with
        {
            EnabledRules = [LintDiagnosticIds.ExcessiveSubgoalCount, LintDiagnosticIds.ExcessiveSubgoalCount],
        };

        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", options);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsCustomLimit()
    {
        const string text = "p :- a,b.";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(
            text,
            loaded.Source.Clauses,
            "single.pl",
            EnabledWithMaximum(1)
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecutedOrCounted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- initialization(halt(9)). p :- a,b,c,d,e,f,g,h,i.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
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
            EnabledRules = [LintDiagnosticIds.ExcessiveSubgoalCount],
        };

    private static PrologLintOptions EnabledWithMaximum(int maximum) => Enabled with { MaxClauseSubgoals = maximum };

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
