using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologMissingExportLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-missing-export-" + Guid.NewGuid().ToString("N")
    );

    public PrologMissingExportLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReportsMissingSelectiveImportAtIndicatorByDefault()
    {
        const string main = ":- module(m, []). :- import(a,[p/0]).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(("a.pl", ":- module(a, [])."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Equal(main.LastIndexOf("p/0", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal(3, warning.Span.Length);
        Assert.EndsWith("main.pl", warning.FileName, StringComparison.Ordinal);
        Assert.Equal(
            "Import of p/0 into module m from module a is not exported by source module a; the binding is not resolved.",
            warning.Message
        );
    }

    [Fact]
    public void RepeatedRequestsFromOneSourceProduceOneFindingAtTheFirstBinding()
    {
        const string main = ":- module(m, []). :- import(a,[p/0]). :- use_module('a.pl',[p/0]). :- import(a,[p/0]).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(("a.pl", ":- module(a, [])."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Equal(main.IndexOf("p/0", StringComparison.Ordinal), warning.Span.Start);
    }

    [Fact]
    public void DistinctIdentitiesAndSourcesProduceSeparateFindings()
    {
        const string main = ":- module(m, []). :- import(a,[p/0,q/0]). :- import(b,[p/0]).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [])."),
            ("b.pl", ":- module(b, [])."),
            ("main.pl", main)
        );

        Diagnostic[] warnings = Warnings(diagnostics);
        Assert.Equal(3, warnings.Length);
        Assert.Equal(main.IndexOf("p/0", StringComparison.Ordinal), warnings[0].Span.Start);
        Assert.Equal(main.IndexOf("q/0", StringComparison.Ordinal), warnings[1].Span.Start);
        Assert.Equal(main.LastIndexOf("p/0", StringComparison.Ordinal), warnings[2].Span.Start);
        Assert.Contains(warnings, warning => warning.Message.Contains("from module b", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("use_module('a.pl',[p/0])")]
    [InlineData("import(a,[p/0])")]
    [InlineData("reexport(a,[p/0])")]
    public void EverySelectiveImportFormIsCovered(string directive)
    {
        string main = $":- module(m, []). :- {directive}.";
        IReadOnlyList<Diagnostic> diagnostics = Lint(("a.pl", ":- module(a, [])."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Equal(main.LastIndexOf("p/0", StringComparison.Ordinal), warning.Span.Start);
    }

    [Theory]
    [InlineData("use_module('a.pl')")]
    [InlineData("import(a)")]
    [InlineData("reexport(a)")]
    public void WholeImportFormsDoNotProduceMissingExportFindings(string directive)
    {
        string main = $":- module(m, []). :- {directive}.";
        IReadOnlyList<Diagnostic> diagnostics = Lint(("a.pl", ":- module(a, [p/0]). p."), ("main.pl", main));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    [Fact]
    public void MissingNonterminalUsesLoweredIdentityAndSourceNotation()
    {
        const string main = ":- module(m, []). :- import(a,[nt//0]).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(("a.pl", ":- module(a, [])."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Equal(main.LastIndexOf("nt//0", StringComparison.Ordinal), warning.Span.Start);
        Assert.Contains("Import of nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryExportWithTheSameLoweredIdentityIsNotMissing()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [nt/2]). nt(_, _)."),
            ("main.pl", ":- module(m, []). :- import(a,[nt//0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("multifile")]
    public void ExplicitDynamicAndMultifileExportsAreEligible(string declaration)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", $":- module(a, [p/0]). :- {declaration}(p/0)."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    [Fact]
    public void DynamicDefinitionWithoutExportIsStillMissing()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, []). :- dynamic(p/0). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    [Fact]
    public void UnknownSourcesDoNotHideIndependentMissingBindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [])."),
            ("main.pl", ":- module(m, []). :- import(missing,[p/0]). :- use_module('absent.pl',[q/0]). :- import(a,[x/0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Contains("Import of x/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("from module a", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedExportSurfaceSuppressesOnlyItsOwnAbsenceClaims()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [q/0,broken]). q."),
            ("b.pl", ":- module(b, [])."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[x/0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Contains("Import of x/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("from module b", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidBindingsInAMalformedSelectiveRequestRemainIndependent()
    {
        const string main = ":- module(m, []). :- import(a,[p/0,broken]).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(("a.pl", ":- module(a, [])."), ("main.pl", main));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Equal(main.IndexOf("p/0", StringComparison.Ordinal), warning.Span.Start);
    }

    [Fact]
    public void ValidAndMissingBindingsInOneDirectiveResolveIndependently()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0,q/0]). go :- p.")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Contains("Import of q/0", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic =>
                diagnostic.Id == LintDiagnosticIds.UndefinedPredicate
                && diagnostic.Message.Contains("p/0", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void FailedReexportAndDownstreamImportAreBothReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [])."),
            ("r.pl", ":- module(r, []). :- reexport(a,[p/0])."),
            ("main.pl", ":- module(m, []). :- import(r,[p/0]).")
        );

        Diagnostic[] warnings = Warnings(diagnostics);
        Assert.Equal(2, warnings.Length);
        Assert.Contains(warnings, warning => warning.Message.Contains("into module r from module a", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Message.Contains("into module m from module r", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("reexport(missing)")]
    [InlineData("reexport(missing,[p/0])")]
    public void UnresolvedReexportsMakeDownstreamExportsUnknown(string directive)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("r.pl", $":- module(r, []). :- {directive}."),
            ("main.pl", ":- module(m, []). :- import(r,[q/0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    [Fact]
    public void SelectiveIncompleteReexportTaintsOnlyTheRequestedIdentity()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [broken])."),
            ("r.pl", ":- module(r, []). :- reexport(a,[p/0])."),
            ("main.pl", ":- module(m, []). :- import(r,[p/0,q/0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Contains("Import of q/0 into module m from module r", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedReexportSuppressesDownstreamAbsenceButKeepsIndependentFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [])."),
            ("r.pl", ":- module(r, []). :- reexport(a,[p/0,broken])."),
            ("main.pl", ":- module(m, []). :- import(r,[q/0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Contains("into module r from module a", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludedImportRetainsPhysicalSourceAndSpan()
    {
        Source("a.pl", ":- module(a, []).\n");
        Source("imports.pl", ":- import(a,[p/0]).\n");
        Source("main.pl", ":- module(m, []). :- include('imports.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl", "a.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.Equal(Path.Combine(_directory, "imports.pl"), warning.FileName);
        Assert.Equal(
            File.ReadAllText(Path.Combine(_directory, "imports.pl")).IndexOf("p/0", StringComparison.Ordinal),
            warning.Span.Start
        );
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            mode,
            ("a.pl", ":- module(a, [])."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    [Fact]
    public void ProgramDiagnosticsRemainOrderedByFileAndSourcePosition()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("main.pl", ":- module(m, []). :- import(a,[p/0,q/0])."),
            ("a.pl", ":- module(a, [undefined/0]).")
        );

        Diagnostic[] warnings = Warnings(diagnostics);
        Assert.Equal(2, warnings.Length);
        Assert.True(warnings[0].Span.Start < warnings[1].Span.Start);
        Diagnostic[] ordered =
        [
            .. diagnostics
                .OrderBy(diagnostic => diagnostic.FileName, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Span.Start)
                .ThenBy(diagnostic => diagnostic.Id),
        ];
        Assert.Equal(ordered, diagnostics);
    }

    [Fact]
    public void SingleFileEntryPointDoesNotInventExportKnowledge()
    {
        const string text = ":- module(m, []). :- import(a,[p/0]).";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    [Fact]
    public void MissingBindingsDoNotProduceImportConflicts()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [])."),
            ("b.pl", ":- module(b, [])."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.Equal(2, Warnings(diagnostics).Length);
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void MissingBindingsDoNotProduceLocalImportShadows()
    {
        PrologLintOptions options = PrologLintOptions.SemanticOnly with { EnabledRules = [LintDiagnosticIds.LocalImportShadow] };
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            PrologLanguageMode.Modern,
            options,
            ("a.pl", ":- module(a, [])."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). p.")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [])."),
            ("main.pl", ":- module(m, []). :- initialization(halt(9)). :- import(a,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.MissingExport));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private IReadOnlyList<Diagnostic> Load(params string[] names)
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

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources)];
    }

    private static Diagnostic[] Warnings(IReadOnlyList<Diagnostic> diagnostics) =>
        [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.MissingExport)];

    private static IReadOnlyList<Diagnostic> Lint(params (string Name, string Text)[] inputs) =>
        Lint(PrologLanguageMode.Modern, options: null, inputs);

    private static IReadOnlyList<Diagnostic> Lint(PrologLanguageMode mode, params (string Name, string Text)[] inputs) =>
        Lint(mode, options: null, inputs);

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
