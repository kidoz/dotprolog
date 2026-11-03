using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologMetaPredicateDeclarationLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-meta-declarations-" + Guid.NewGuid().ToString("N")
    );

    public PrologMetaPredicateDeclarationLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ModernConflictIsReportedByDefaultAtLaterTemplate()
    {
        const string text = ":- meta_predicate(p(0)). :- meta_predicate(p(1)). p(_).";
        IReadOnlyList<Diagnostic> diagnostics = LintSource(text);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
        Assert.Equal(text.LastIndexOf("p(1)", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal("p(1)".Length, warning.Span.Length);
        Assert.Equal("single.pl", warning.FileName);
        Assert.Contains("declaration p(1) for user:p/1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("earlier template p(0)", warning.Message, StringComparison.Ordinal);
        Assert.Contains("argument modes disagree", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("rejected", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EquivalentOrdinaryModesDoNotConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(
            ":- meta_predicate(p(?)). :- meta_predicate(p(+)). :- meta_predicate(p(-))."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void GoalZeroAndModuleSensitiveModesConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- meta_predicate(p(:)). :- meta_predicate(p(0)).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void DifferentPositiveClosureCountsConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- meta_predicate(p(1)). :- meta_predicate(p(2)).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void ExactDuplicatesKeepExistingDiagnosticWithoutConsistencyWarning()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- meta_predicate(p(0)). :- meta_predicate(p(0)).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
        Assert.Single(diagnostics, Id(LintDiagnosticIds.DuplicateDirective));
    }

    [Fact]
    public void IsoConflictUsesModuleIdentityAndLaterTemplateSpan()
    {
        const string text = ":- module(m). :- metapredicate(p(:)). :- metapredicate(p(*)).";
        IReadOnlyList<Diagnostic> diagnostics = LintSource(text, PrologLanguageMode.StrictIso);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
        Assert.Equal(text.LastIndexOf("p(*)", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal("p(*)".Length, warning.Span.Length);
        Assert.Contains("for m:p/1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("earlier template p(:)", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidIsoModesDoNotCreateConsistencyFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(
            ":- module(m). :- metapredicate(p(0)). :- metapredicate(p(:)).",
            PrologLanguageMode.StrictIso
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void NonCompoundModernTemplateIsSkipped()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- meta_predicate(p). :- meta_predicate(p(0)).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void OnlyFirstDifferingLaterDeclarationIsReported()
    {
        const string text = ":- meta_predicate(p(0)). :- meta_predicate(p(1)). :- meta_predicate(p(2)).";
        IReadOnlyList<Diagnostic> diagnostics = LintSource(text);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
        Assert.Equal(text.IndexOf("p(1)", StringComparison.Ordinal), warning.Span.Start);
    }

    [Fact]
    public void ReturningToFirstSignatureDoesNotCreateAnotherFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(
            ":- meta_predicate(p(0)). :- meta_predicate(p(1)). :- meta_predicate(p(0))."
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void SeparateModulesDoNotShareDeclarationHistory()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, []). :- meta_predicate(p(0))."),
            ("b.pl", ":- module(b, []). :- meta_predicate(p(1)).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void SeparateAritiesDoNotShareDeclarationHistory()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- meta_predicate(p(0)). :- meta_predicate(p(0, ?)).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void SameModuleConflictAcrossNamedFilesUsesLaterPhysicalSource()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(m, []). :- meta_predicate(p(0))."),
            ("b.pl", ":- module(m, []). :- meta_predicate(p(1)).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
        Assert.EndsWith("b.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludedLaterDeclarationRetainsPhysicalOwnership()
    {
        Source("declarations.pl", ":- meta_predicate(p(1)).\n");
        Source("main.pl", ":- module(m, []). :- meta_predicate(p(0)). :- include('declarations.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
        Assert.Equal(Path.Combine(_directory, "declarations.pl"), warning.FileName);
        Assert.Equal(18, warning.Span.Start);
    }

    [Fact]
    public void ImportsAndReexportsDoNotMergeDeclarationHistories()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("a.pl", ":- module(a, [p/1]). :- meta_predicate(p(0)). p(_)."),
            ("r.pl", ":- module(r, []). :- reexport(a, [p/1]). :- meta_predicate(p(1))."),
            ("main.pl", ":- module(m, []). :- import(r, [p/1]). :- meta_predicate(p(2)).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void PredefinedTemplateAloneDoesNotCreateConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- meta_predicate(maplist(0, ?)).");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void DynamicMultifileAndUnresolvedImportsDoNotSuppressConflict()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            (
                "main.pl",
                ":- module(m, []). :- dynamic(p/1). :- multifile(p/1). :- import(missing, [x/0]). :- meta_predicate(p(0)). :- meta_predicate(p(1))."
            )
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void ModernConflictUsesSameDefaultRuleInBothModes(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- meta_predicate(p(0)). :- meta_predicate(p(1)).", mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void WholeProgramDoesNotDuplicateFileLocalFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(("main.pl", ":- meta_predicate(p(0)). :- meta_predicate(p(1))."));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
    }

    [Fact]
    public void FindingsUseSourceOrder()
    {
        const string text = ":- meta_predicate(p(0)). :- meta_predicate(q(0)). :- meta_predicate(p(1)). :- meta_predicate(q(1)).";
        IReadOnlyList<Diagnostic> diagnostics = LintSource(text);

        Diagnostic[] warnings =
        [
            .. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.InconsistentMetaPredicate),
        ];
        Assert.Equal(2, warnings.Length);
        Assert.Contains("for user:p/1", warnings[0].Message, StringComparison.Ordinal);
        Assert.Contains("for user:q/1", warnings[1].Message, StringComparison.Ordinal);
        Assert.True(warnings[0].Span.Start < warnings[1].Span.Start);
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(
            ":- initialization(halt(9)). :- meta_predicate(p(0)). :- meta_predicate(p(1))."
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.InconsistentMetaPredicate));
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

    private static IReadOnlyList<Diagnostic> LintSource(string text, PrologLanguageMode mode = PrologLanguageMode.Modern)
    {
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load("single.pl", text, new BytecodeProgram(mode));
        return
        [
            .. loaded.Diagnostics,
            .. PrologLinter.AnalyzeSource(
                text,
                loaded.Source.Clauses,
                "single.pl",
                initialDoubleQuotes: loaded.Source.InitialDoubleQuotes,
                codePoints: loaded.Source.CodePoints
            ),
        ];
    }

    private static IReadOnlyList<Diagnostic> Lint(params (string Name, string Text)[] inputs)
    {
        List<PrologLintSource> sources = [];
        List<Diagnostic> diagnostics = [];
        foreach ((string name, string text) in inputs)
        {
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
                Path.Combine(Path.GetTempPath(), name),
                text,
                new BytecodeProgram()
            );
            sources.Add(loaded.Source);
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources)];
    }

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
