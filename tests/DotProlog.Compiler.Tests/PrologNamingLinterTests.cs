using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologNamingLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-naming-" + Guid.NewGuid().ToString("N")
    );

    public PrologNamingLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("badName.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void ReportsNameWithModuleIdentityAndExactSpan()
    {
        const string source = "badName(X) :- true.\n";
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.Equal(0, warning.Span.Start);
        Assert.Equal("badName".Length, warning.Span.Length);
        Assert.Contains("predicate user:badName/1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("lower snake_case", warning.Message, StringComparison.Ordinal);
        Assert.Contains("deviation is intentional", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("p.")]
    [InlineData("parse.")]
    [InlineData("parse_term.")]
    [InlineData("parse_term2.")]
    [InlineData("parse_2.")]
    [InlineData("p2(X).")]
    [InlineData("'quoted_name'.")]
    public void ConformingNamesAreNotReported(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Theory]
    [InlineData("'ParseTerm'.")]
    [InlineData("parseTerm.")]
    [InlineData("parse__term.")]
    [InlineData("'_parse'.")]
    [InlineData("parse_.")]
    [InlineData("'2parse'.")]
    [InlineData("'parse term'.")]
    [InlineData("'é'.")]
    public void NonconformingNamesAreReported(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Theory]
    [InlineData("'+'.")]
    [InlineData("'=..'.")]
    [InlineData("'___'.")]
    public void SymbolicNamesAreExcluded(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Theory]
    [InlineData("'$helper'.")]
    [InlineData("'$BadName'.")]
    public void ImplementationPrivateNamesAreExcluded(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void UnquotedUppercaseHeadIsNotAStaticallyRecognizedPredicate()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("BadName.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void ReportsNonterminalWithSourceArity()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("badName(X) --> [X].");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.Contains("nonterminal user:badName//1", warning.Message, StringComparison.Ordinal);
        Assert.Equal("badName".Length, warning.Span.Length);
    }

    [Fact]
    public void SemicontextDoesNotChangeNonterminalIdentity()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("badName, [X] --> [X].");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.Contains("nonterminal user:badName//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleDirectiveContributesToIdentity()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). badName.");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.Contains("predicate m:badName/0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleNamesAreNotChecked()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module('BadModule', []). p.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Theory]
    [InlineData(":- dynamic(badName/0).")]
    [InlineData(":- multifile(badName/0).")]
    [InlineData(":- module(m, [badName/0]).")]
    public void DeclarationOnlyProceduresAreNotReported(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Theory]
    [InlineData(":- dynamic(badName/0). badName.")]
    [InlineData(":- multifile(badName/0). badName.")]
    public void DynamicAndMultifileDefinitionsAreEligible(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void CallsDoNotProduceNamingFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("p :- badName.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void OnlyFirstClauseOfProcedureProducesFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("badName. badName :- true.");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.Equal(0, warning.Span.Start);
    }

    [Fact]
    public void DistinctAritiesProduceSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("badName(X). badName(X, Y).");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void SameNameInDistinctModulesProducesSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [("one.pl", ":- module(one, []). badName."), ("two.pl", ":- module(two, []). badName.")],
            Enabled
        );

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Theory]
    [InlineData("badName(A, B) :- true. badName --> [].")]
    [InlineData("badName --> []. badName(A, B) :- true.")]
    public void MixedDefinitionsSharingLoweredIdentityProduceOneFinding(string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(source);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void IncludedFirstDefinitionRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", "badName.\n");
        Source("main.pl", ":- include('parts.pl').\nbadName :- true.\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void ExplicitFilesUseFirstSuppliedDefinition()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint([("first.pl", "badName."), ("second.pl", "badName :- true.")], Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.EndsWith("first.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("'BadName'.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("badName :- missing.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void DuplicateEnabledRulesDoNotDuplicateFindings()
    {
        PrologLintOptions options = PrologLintOptions.SemanticOnly with
        {
            EnabledRules = [LintDiagnosticIds.NonSnakeCasePredicate, LintDiagnosticIds.NonSnakeCasePredicate],
        };

        IReadOnlyList<Diagnostic> diagnostics = Lint("badName.", options);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = "badName.";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- initialization(halt(9)). badName.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.NonSnakeCasePredicate));
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
            EnabledRules = [LintDiagnosticIds.NonSnakeCasePredicate],
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
