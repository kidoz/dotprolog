using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologExcessiveArityLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-excessive-arity-" + Guid.NewGuid().ToString("N")
    );

    public PrologExcessiveArityLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        var diagnostics = Lint("p(A, B, C, D, E).", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void ReportsDefaultExcessWithIdentityAndExactSpan()
    {
        const string source = "p(A, B, C, D, E) :- true.\n";
        var diagnostics = Lint(source);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.Equal(0, warning.Span.Start);
        Assert.Equal(1, warning.Span.Length);
        Assert.Contains("Predicate user:p/5", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 5 arguments", warning.Message, StringComparison.Ordinal);
        Assert.Contains("maximum of 4", warning.Message, StringComparison.Ordinal);
        Assert.Contains("design is intentional", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("p.")]
    [InlineData("p(A).")]
    [InlineData("p(A, B, C, D).")]
    public void DefinitionsAtOrBelowDefaultLimitAreNotReported(string source)
    {
        var diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void CustomLimitUsesStrictlyGreaterComparison()
    {
        var diagnostics = Lint("p(A, B). p(A, B, C).", EnabledWithMaximum(2));

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.Contains("user:p/3", warning.Message, StringComparison.Ordinal);
        Assert.Contains("maximum of 2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MinimumLimitAllowsFactsAndUnaryPredicates()
    {
        var diagnostics = Lint("p. p(A). p(A, B).", EnabledWithMaximum(1));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidReusableLimitIsRejectedWhenRuleIsEnabled(int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = Lint("p.", EnabledWithMaximum(maximum)));
    }

    [Fact]
    public void InvalidLimitIsInertWhenRuleIsDisabled()
    {
        var options = PrologLintOptions.SemanticOnly with { MaxPredicateArity = 0 };

        var diagnostics = Lint("p(A, B, C, D, E).", options);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void NonterminalUsesSourceRatherThanLoweredArity()
    {
        var diagnostics = Lint("nt(A, B, C, D) --> []. nt(A, B, C, D, E) --> [].");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.Contains("Nonterminal user:nt//5", warning.Message, StringComparison.Ordinal);
        Assert.Contains("has 5 arguments", warning.Message, StringComparison.Ordinal);
        Assert.Equal("nt(A, B, C, D) --> []. ".Length, warning.Span.Start);
    }

    [Fact]
    public void ModuleDirectiveContributesToIdentity()
    {
        var diagnostics = Lint(":- module(m, []). p(A, B, C, D, E).");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.Contains("Predicate m:p/5", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(":- dynamic(p/5).")]
    [InlineData(":- multifile(p/5).")]
    [InlineData(":- module(m, [p/5]).")]
    public void DeclarationOnlyProceduresAreNotReported(string source)
    {
        var diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Theory]
    [InlineData(":- dynamic(p/5). p(A, B, C, D, E).")]
    [InlineData(":- multifile(p/5). p(A, B, C, D, E).")]
    public void DynamicAndMultifileDefinitionsAreEligible(string source)
    {
        var diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void CallsDoNotProduceArityFindings()
    {
        var diagnostics = Lint("p :- q(A, B, C, D, E).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void OnlyFirstClauseOfProcedureProducesFinding()
    {
        var diagnostics = Lint("p(A, B, C, D, E). p(A, B, C, D, E) :- true.");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.Equal(0, warning.Span.Start);
    }

    [Fact]
    public void DistinctAritiesProduceSeparateFindings()
    {
        var diagnostics = Lint("p(A, B, C, D, E). p(A, B, C, D, E, F).");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void SameProcedureInDistinctModulesProducesSeparateFindings()
    {
        var diagnostics = Lint(
            [("one.pl", ":- module(one, []). p(A, B, C, D, E)."), ("two.pl", ":- module(two, []). p(A, B, C, D, E).")],
            Enabled
        );

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Theory]
    [InlineData("p(A, B, C) :- true. p(X) --> [X].", 1)]
    [InlineData("p(X) --> [X]. p(A, B, C) :- true.", 0)]
    public void MixedDefinitionsUseOnlyFirstSuppliedDefinition(string source, int expected)
    {
        var diagnostics = Lint(source, EnabledWithMaximum(1));

        Assert.Equal(expected, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void IncludedFirstDefinitionRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "p(A, B, C, D, E).\n");
        Source("main.pl", ":- include('parts.pl').\np(A, B, C, D, E) :- true.\n");

        var diagnostics = Load("main.pl");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void ExplicitFilesUseFirstSuppliedDefinition()
    {
        var diagnostics = Lint([("first.pl", "p(A, B, C, D, E)."), ("second.pl", "p(A, B, C, D, E) :- true.")], Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.EndsWith("first.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        var diagnostics = Lint("p(A, B, C, D, E).", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        var diagnostics = Lint("p(A, B, C, D, E) :- missing.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void DuplicateEnabledRulesDoNotDuplicateFindings()
    {
        var options = Enabled with
        {
            EnabledRules = [LintDiagnosticIds.ExcessivePredicateArity, LintDiagnosticIds.ExcessivePredicateArity],
        };

        var diagnostics = Lint("p(A, B, C, D, E).", options);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsCustomLimit()
    {
        const string text = "p(A, B).";
        var loaded = PrologLintSourceLoader.Load(Path.Combine(_directory, "single.pl"), text, new BytecodeProgram());

        var diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", EnabledWithMaximum(1));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        var diagnostics = Lint(":- initialization(halt(9)). p(A, B, C, D, E).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ExcessivePredicateArity));
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
            EnabledRules = [LintDiagnosticIds.ExcessivePredicateArity],
        };

    private static PrologLintOptions EnabledWithMaximum(int maximum) => Enabled with { MaxPredicateArity = maximum };

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
