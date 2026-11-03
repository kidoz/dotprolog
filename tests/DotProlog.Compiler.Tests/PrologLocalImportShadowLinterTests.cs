using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologLocalImportShadowLinterTests : IDisposable
{
    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.LocalImportShadow],
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-local-import-shadow-" + Guid.NewGuid().ToString("N")
    );

    public PrologLocalImportShadowLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsOptInAndReportsSelectiveShadowAtLocalName()
    {
        const string main = ":- module(m, []). :- import(a,[p/0]). p.";
        IReadOnlyList<Diagnostic> defaultDiagnostics = Lint(options: null, ("a.pl", ":- module(a,[p/0]). p."), ("main.pl", main));
        IReadOnlyList<Diagnostic> enabledDiagnostics = Lint(Enabled, ("a.pl", ":- module(a,[p/0]). p."), ("main.pl", main));

        Assert.DoesNotContain(defaultDiagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Diagnostic warning = Assert.Single(enabledDiagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Equal(main.LastIndexOf("p.", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal(1, warning.Span.Length);
        Assert.EndsWith("main.pl", warning.FileName, StringComparison.Ordinal);
        Assert.Contains("Local procedure p/0 in module m", warning.Message, StringComparison.Ordinal);
        Assert.Contains("shadows the import from module a", warning.Message, StringComparison.Ordinal);
        Assert.Contains("imported binding is not resolved", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalDefinitionBeforeImportHasTheSameEffect()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). p. :- import(a,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void RepeatedDirectivesFromOneSourceProduceOneFinding()
    {
        PrologLintOptions duplicateOptions = Enabled with { EnabledRules = [LintDiagnosticIds.LocalImportShadow, "dpl3048"] };
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            duplicateOptions,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). p. :- import(a,[p/0]). :- use_module('a.pl').")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void DistinctSourcesProduceSeparateFindingsAtTheSameLocalName()
    {
        const string main = ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]). p.";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", main)
        );

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.LocalImportShadow)];
        Assert.Equal(2, warnings.Length);
        Assert.Contains(warnings, warning => warning.Message.Contains("from module a", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Message.Contains("from module b", StringComparison.Ordinal));
        Assert.All(warnings, warning => Assert.Equal(main.LastIndexOf("p.", StringComparison.Ordinal), warning.Span.Start));
    }

    [Fact]
    public void DistinctAritiesDoNotShadow()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/1]). p(_)."),
            ("main.pl", ":- module(m, []). :- import(a,[p/1]). p.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void WholeImportReportsEveryShadowedExport()
    {
        const string main = ":- module(m, []). :- import(a). p. q.";
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("a.pl", ":- module(a,[p/0,q/0]). p. q."), ("main.pl", main));

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.LocalImportShadow)];
        Assert.Equal(2, warnings.Length);
        Assert.Equal(main.IndexOf("p.", StringComparison.Ordinal), warnings[0].Span.Start);
        Assert.Equal(main.IndexOf("q.", StringComparison.Ordinal), warnings[1].Span.Start);
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("multifile")]
    public void DeclarationOnlyProcedureUsesIndicatorSpan(string declaration)
    {
        string main = $":- module(m, []). :- import(a,[p/0]). :- {declaration}(p/0).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("a.pl", ":- module(a,[p/0]). p."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Equal(main.LastIndexOf("p/0", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal(3, warning.Span.Length);
    }

    [Fact]
    public void FirstLocalClauseTakesPrecedenceOverEarlierDeclarationForLocation()
    {
        const string main = ":- module(m, []). :- dynamic(p/0). :- import(a,[p/0]). p.";
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("a.pl", ":- module(a,[p/0]). p."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Equal(main.LastIndexOf("p.", StringComparison.Ordinal), warning.Span.Start);
    }

    [Fact]
    public void CompoundHeadUsesOnlyTheFunctorToken()
    {
        const string main = ":- module(m, []). :- import(a,[p/1]). local :- p(value).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("a.pl", ":- module(a,[p/1]). p(_)."), ("main.pl", main));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));

        const string shadowing = ":- module(m, []). :- import(a,[p/1]). p(value) :- true.";
        diagnostics = Lint(Enabled, ("a.pl", ":- module(a,[p/1]). p(_)."), ("main.pl", shadowing));
        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Equal(shadowing.LastIndexOf("p(value)", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal(1, warning.Span.Length);
    }

    [Fact]
    public void GrammarShadowUsesSourceNonterminalAndNameSpan()
    {
        const string main = ":- module(m, []). :- import(a,[nt//0]). nt --> [].";
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("a.pl", ":- module(a,[nt//0]). nt --> []."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Equal(main.LastIndexOf("nt -->", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal(2, warning.Span.Length);
        Assert.Contains("nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(":- module(a,[p//0]). p --> [].", "p(_, _).", "p//0")]
    [InlineData(":- module(a,[p/2]). p(_, _).", "p --> [].", "p/2")]
    public void OrdinaryAndGrammarDefinitionsShareLoweredIdentity(string source, string local, string display)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", source),
            ("main.pl", $":- module(m, []). :- import(a). {local}")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Contains(display, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(":- use_module('a.pl').")]
    [InlineData(":- import(a).")]
    [InlineData(":- reexport(a).")]
    public void WholeImportFormsReportLocalShadow(string directive)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", $":- module(m, []). {directive} p.")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void RejectedReexportReportsShadowAndDoesNotExportBinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("r.pl", ":- module(r,[]). :- reexport(a,[p/0]). p."),
            ("main.pl", ":- module(m,[]). :- import(r,[p/0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Contains("in module r", warning.Message, StringComparison.Ordinal);
        Assert.Contains("from module a", warning.Message, StringComparison.Ordinal);
        Assert.EndsWith("r.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFallbackIsNotLocalImportShadowing()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("user.pl", "p."),
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void UnresolvedSourcesAndExportsDoNotSuppressIndependentFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0,q/0]). p. q."),
            (
                "main.pl",
                ":- module(m, []). :- import(missing,[x/0]). :- import(a,[missing/0,p/0]). :- use_module('missing.pl'). p."
            )
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Contains("p/0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltinNamesAndQualifiedCallsDoNotCreateFindingsWithoutRejectedImports()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). member(_, _). go :- a:p.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void IncludedDefinitionRetainsPhysicalOwnership()
    {
        Source("a.pl", ":- module(a,[p/1]). p(_).\n");
        Source("local.pl", "p(value).\n");
        Source("imports.pl", ":- import(a,[p/1]).\n");
        Source("main.pl", ":- module(m, []). :- include('local.pl'). :- include('imports.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load(Enabled, "main.pl", "a.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Equal(Path.Combine(_directory, "local.pl"), warning.FileName);
        Assert.Equal(0, warning.Span.Start);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            mode,
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). p.")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void LocalRejectionDoesNotAlsoProduceImportConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). p.")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void ImportConflictsRemainEnabledAlongsideOptInLocalShadowing()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void OtherOptInRulesComposeWithLocalShadowing()
    {
        PrologLintOptions options = Enabled with
        {
            EnabledRules = [LintDiagnosticIds.LocalImportShadow, LintDiagnosticIds.NonSnakeCasePredicate],
        };
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            options,
            ("a.pl", ":- module(a,[badName/0]). badName."),
            ("main.pl", ":- module(m, []). :- import(a,[badName/0]). badName.")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.NonSnakeCasePredicate));
    }

    [Fact]
    public void FindingsUsePhysicalSourceOrder()
    {
        const string main = ":- module(m, []). :- import(a,[q/0,p/0]). p. q.";
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("a.pl", ":- module(a,[p/0,q/0]). p. q."), ("main.pl", main));

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.LocalImportShadow)];
        Assert.Equal(2, warnings.Length);
        Assert.True(warnings[0].Span.Start < warnings[1].Span.Start);
        Assert.Contains("p/0", warnings[0].Message, StringComparison.Ordinal);
        Assert.Contains("q/0", warnings[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleFileEntryPointDoesNotInventCrossFileShadowing()
    {
        const string text = ":- module(m, []). :- import(a,[p/0]). p.";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- initialization(halt(9)). :- import(a,[p/0]). p.")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private IReadOnlyList<Diagnostic> Load(PrologLintOptions? options, params string[] names)
    {
        List<PrologLintSource> sources = [];
        List<Diagnostic> diagnostics = [];
        foreach (string name in names)
        {
            var path = Path.Combine(_directory, name);
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
            sources.Add(loaded.Source);
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources, options)];
    }

    private static IReadOnlyList<Diagnostic> Lint(PrologLintOptions? options, params (string Name, string Text)[] inputs) =>
        Lint(PrologLanguageMode.Modern, options, inputs);

    private static IReadOnlyList<Diagnostic> Lint(
        PrologLanguageMode mode,
        PrologLintOptions? options,
        params (string Name, string Text)[] inputs
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
