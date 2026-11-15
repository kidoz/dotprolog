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
        var diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void ReportsDefaultExcessWithIdentityAndExactSpan()
    {
        const string source = "p :- a,b,c,d,e,f,g,h,i.\n";
        var diagnostics = Lint(source);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
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
        var diagnostics = Lint("p :- a,b,c,d,e,f,g,h.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void CustomLimitUsesStrictlyGreaterComparison()
    {
        var diagnostics = Lint("p :- a,b. q :- a,b,c.", EnabledWithMaximum(2));

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("user:q/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 3 direct subgoals", warning.Message, StringComparison.Ordinal);
        Assert.Contains("maximum of 2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FactsProduceNoCountEvenAtMinimumLimit()
    {
        var diagnostics = Lint("p. p(A, B, C).", EnabledWithMaximum(1));

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
        var options = PrologLintOptions.SemanticOnly with { MaxClauseSubgoals = 0 };

        var diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", options);

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
        var diagnostics = Lint(source, EnabledWithMaximum(2));

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
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
        var diagnostics = Lint(source, EnabledWithMaximum(1));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void OpaqueGoalContributesOneToOuterCount()
    {
        var diagnostics = Lint("p :- findall(X,(a,b,c),X),d.", EnabledWithMaximum(1));

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("has 2 direct subgoals", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nt --> {a,b,c}.")]
    [InlineData("nt --> [a],b,[c].")]
    public void DcgOrdinaryGoalsAndTerminalsFollowTheSameCount(string source)
    {
        var diagnostics = Lint(source, EnabledWithMaximum(2));

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("Grammar rule for nonterminal user:nt//0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 3 direct subgoals", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nt --> call((a,b,c)).")]
    [InlineData("nt --> {call((a,b,c))}.")]
    public void DcgMetaCallsRemainOpaque(string source)
    {
        var diagnostics = Lint(source, EnabledWithMaximum(1));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void DcgSemicontextIsNotCounted()
    {
        var diagnostics = Lint("nt, [X] --> {a,b}.", EnabledWithMaximum(1));

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("user:nt//0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 2 direct subgoals", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleAndDuplicateClausesProduceIndependentFindings()
    {
        var diagnostics = Lint("p :- a,b. p :- a,b.", EnabledWithMaximum(1));

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Theory]
    [InlineData(":- dynamic(p/0). p :- a,b.")]
    [InlineData(":- multifile(p/0). p :- a,b.")]
    public void DynamicAndMultifileRulesAreEligible(string source)
    {
        var diagnostics = Lint(source, EnabledWithMaximum(1));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void ModuleDirectiveContributesToIdentity()
    {
        var diagnostics = Lint(":- module(m, []). p :- a,b,c,d,e,f,g,h,i.");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains("predicate m:p/0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludedRuleRetainsPhysicalOwnership()
    {
        Source("parts.pl", "p :- a,b,c,d,e,f,g,h,i.\n");
        Source("main.pl", ":- include('parts.pl').\n");

        var diagnostics = Load("main.pl");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void UnrecognizedHeadIsNotReported()
    {
        var diagnostics = Lint("X :- a,b.", EnabledWithMaximum(1));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        var diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        var diagnostics = Lint("p :- missing,a,b,c,d,e,f,g,h,i.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void DuplicateEnabledRulesDoNotDuplicateFindings()
    {
        var options = Enabled with
        {
            EnabledRules = [LintDiagnosticIds.ExcessiveSubgoalCount, LintDiagnosticIds.ExcessiveSubgoalCount],
        };

        var diagnostics = Lint("p :- a,b,c,d,e,f,g,h,i.", options);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsCustomLimit()
    {
        const string text = "p :- a,b.";
        var loaded = PrologLintSourceLoader.Load(Path.Combine(_directory, "single.pl"), text, new BytecodeProgram());

        var diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", EnabledWithMaximum(1));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessiveSubgoalCount));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecutedOrCounted()
    {
        var diagnostics = Lint(":- initialization(halt(9)). p :- a,b,c,d,e,f,g,h,i.");

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
