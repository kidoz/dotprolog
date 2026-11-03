using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologConflictingImportLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-conflicting-import-" + Guid.NewGuid().ToString("N")
    );

    public PrologConflictingImportLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReportsSelectiveConflictAtLaterBindingByDefault()
    {
        const string main = ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", main)
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.Equal(main.LastIndexOf("p/0", StringComparison.Ordinal), warning.Span.Start);
        Assert.EndsWith("main.pl", warning.FileName, StringComparison.Ordinal);
        Assert.Contains("p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("module m", warning.Message, StringComparison.Ordinal);
        Assert.Contains("from module b", warning.Message, StringComparison.Ordinal);
        Assert.Contains("earlier import from module a", warning.Message, StringComparison.Ordinal);
        Assert.Contains("later binding is not resolved", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceOrderReversalChangesEarlierAndLaterSources()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(b,[p/0]). :- import(a,[p/0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.Contains("from module a", warning.Message, StringComparison.Ordinal);
        Assert.Contains("earlier import from module b", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedImportsFromSameSourceDoNotConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(a,[p/0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void DistinctAritiesDoNotConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/1]). p(_)."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/1]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void WholeModuleImportReportsEachConflictingExportAtSourceArgument()
    {
        const string main = ":- module(m, []). :- import(a). :- import(b).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0,q/0]). p. q."),
            ("b.pl", ":- module(b,[p/0,q/0]). p. q."),
            ("main.pl", main)
        );

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.ConflictingImport)];
        Assert.Equal(2, warnings.Length);
        int expected = main.LastIndexOf("import(b)", StringComparison.Ordinal) + "import(".Length;
        Assert.All(warnings, warning => Assert.Equal(expected, warning.Span.Start));
    }

    [Fact]
    public void ThirdSourceProducesAnotherLaterBindingFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("c.pl", ":- module(c,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a). :- import(b). :- import(c).")
        );

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.ConflictingImport)];
        Assert.Equal(2, warnings.Length);
        Assert.Contains(warnings, warning => warning.Message.Contains("from module b", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Message.Contains("from module c", StringComparison.Ordinal));
        Assert.All(
            warnings,
            warning => Assert.Contains("earlier import from module a", warning.Message, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void NonterminalConflictUsesSourceIndicator()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[nt//0]). nt --> []."),
            ("b.pl", ":- module(b,[nt//0]). nt --> []."),
            ("main.pl", ":- module(m, []). :- import(a,[nt//0]). :- import(b,[nt//0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.Contains("nt//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryAndGrammarDefinitionsConflictOnLoweredIdentity()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/2]). p(_, _)."),
            ("b.pl", ":- module(b,[p//0]). p --> []."),
            ("main.pl", ":- module(m, []). :- import(a,[p/2]). :- import(b,[p//0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.Contains("p//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileImportsProduceWholeModuleConflict()
    {
        const string main = ":- module(m, []). :- use_module('a.pl'). :- use_module('b.pl').";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", main)
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.Equal(main.LastIndexOf("'b.pl'", StringComparison.Ordinal), warning.Span.Start);
    }

    [Fact]
    public void SuccessfulReexportDoesNotProduceFalseConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("r.pl", ":- module(r,[]). :- reexport(a,[p/0])."),
            ("main.pl", ":- module(m, []). :- import(r,[p/0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void RejectedReexportProducesConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("r.pl", ":- module(r,[]). :- reexport(a,[p/0]). :- reexport(b,[p/0]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.Contains("into module r", warning.Message, StringComparison.Ordinal);
        Assert.Contains("from module b", warning.Message, StringComparison.Ordinal);
        Assert.Contains("earlier import from module a", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalDefinitionOverrideIsNotImportConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). p.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void UserFallbackIsNotImportConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("user.pl", "p."),
            ("a.pl", ":- module(a,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void DynamicAndMultifileDeclarationsDoNotEraseConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). :- dynamic(p/0). p."),
            ("b.pl", ":- module(b,[p/0]). :- multifile(p/0). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void MissingExportDoesNotCreateConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[q/0]). q."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void UnknownModuleDoesNotSuppressIndependentConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(missing,[x/0]). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void IncludedImportRetainsPhysicalOwnership()
    {
        Source("a.pl", ":- module(a,[p/0]). p.\n");
        Source("b.pl", ":- module(b,[p/0]). p.\n");
        Source("imports.pl", ":- import(a,[p/0]). :- import(b,[p/0]).\n");
        Source("main.pl", ":- module(m, []). :- include('imports.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl", "a.pl", "b.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.Equal(Path.Combine(_directory, "imports.pl"), warning.FileName);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            mode,
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void ConflictDoesNotProduceDuplicateUnusedImportFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void SingleFileEntryPointDoesNotInventCrossFileConflict()
    {
        const string text = ":- module(m, []). :- import(a,[p/0]). :- import(b,[p/0]).";
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "single.pl"),
            text,
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a,[p/0]). p."),
            ("b.pl", ":- module(b,[p/0]). p."),
            ("main.pl", ":- module(m, []). :- initialization(halt(9)). :- import(a,[p/0]). :- import(b,[p/0]).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ConflictingImport));
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

    private static IReadOnlyList<Diagnostic> Lint(params (string Name, string Text)[] inputs) =>
        Lint(PrologLanguageMode.Modern, inputs);

    private static IReadOnlyList<Diagnostic> Lint(PrologLanguageMode mode, params (string Name, string Text)[] inputs)
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

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources)];
    }

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
