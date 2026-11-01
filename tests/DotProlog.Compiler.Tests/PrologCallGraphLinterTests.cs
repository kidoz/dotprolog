using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologCallGraphLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-program-" + Guid.NewGuid().ToString("N")
    );

    public PrologCallGraphLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ResolvesAnImportedPredicateAcrossNamedFiles()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("main.pl", ":- module(main, [go/0]). :- use_module('other.pl'). go :- helper."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleBuiltinArityMismatch));
    }

    [Fact]
    public void ReportsWrongArityAfterCrossFileResolution() =>
        Assert.Contains(
            Lint(
                ("main.pl", ":- module(main, []). :- use_module('other.pl'). go :- helper(1)."),
                ("other.pl", ":- module(other, [helper/0]). helper.")
            ),
            Id(LintDiagnosticIds.UndefinedPredicate)
        );

    [Fact]
    public void SelectiveImportsUseOnlyExportedPredicates()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [missing/0]). go :- missing."),
            ("other.pl", ":- module(other, [helper/0]). helper. private.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void MissingImportSourcesAndModulesRemainUnknown()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('missing.pl'). go :- helper, unknown_module:other.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void UserPredicatesRemainVisibleAsGlobalFallbacks()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(("main.pl", ":- module(main, []). go :- helper."), ("user.pl", "helper."));

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ExplicitModuleQualificationResolvesTheTargetModule()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("main.pl", ":- module(main, []). go :- other:helper."),
            ("other.pl", ":- module(other, []). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void DynamicAndRuntimeGoalsAreNotUndefined()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- dynamic(helper/0). go(G) :- helper, G.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void MetaClosureUsesTheAdditionalArgumentCount()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl'). go(X, Y) :- maplist(prefix(a), X, Y)."),
            ("other.pl", ":- module(other, [prefix/3]). prefix(_, _, _).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void WrongMetaClosureArityIsUndefined() =>
        Assert.Contains(
            Lint(
                ("main.pl", ":- module(main, []). :- use_module('other.pl'). go(X, Y) :- maplist(prefix(a), X, Y)."),
                ("other.pl", ":- module(other, [prefix/1]). prefix(_).")
            ),
            Id(LintDiagnosticIds.UndefinedPredicate)
        );

    [Fact]
    public void FileLocalMetaDeclarationsGuideGoalAnalysis()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- meta_predicate(run(0)). run(Goal) :- Goal. go :- run(helper). helper.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReportsAnExportedPredicateWithoutADefinition() =>
        Assert.Contains(Lint(":- module(other, [helper/0])."), Id(LintDiagnosticIds.MissingPredicateDefinition));

    [Fact]
    public void DeclarationsCanBeDefinedInAnotherNamedFile()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("declarations.pl", ":- module(m, []). :- discontiguous(p/1)."),
            ("definitions.pl", ":- module(m, []). p(a).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.MissingPredicateDefinition));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.DeclaredButUndefined));
    }

    [Fact]
    public void ReportsAnOrdinaryPredicateUsedAsANonterminal() =>
        Assert.Contains(
            Lint(
                ("main.pl", ":- module(main, []). :- use_module('other.pl'). go(X) :- phrase(p(X), [a])."),
                ("other.pl", ":- module(other, [p/1]). p(_).")
            ),
            Id(LintDiagnosticIds.PredicateNonterminalMismatch)
        );

    [Fact]
    public void ReportsANonterminalUsedAsAnOrdinaryPredicate() =>
        Assert.Contains(
            Lint(
                ("main.pl", ":- module(main, []). :- use_module('other.pl'). go(X) :- p(X, [a], [])."),
                ("other.pl", ":- module(other, [p//1]). p(_) --> [a].")
            ),
            Id(LintDiagnosticIds.PredicateNonterminalMismatch)
        );

    [Fact]
    public void AcceptsAValidNonterminalCall()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl'). go(X) :- phrase(p(X), [a])."),
            ("other.pl", ":- module(other, [p//1]). p(_) --> [a].")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PredicateNonterminalMismatch));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReportsAnUndefinedNonterminalInAGrammarBody() =>
        Assert.Contains(Lint("go --> missing."), Id(LintDiagnosticIds.UndefinedPredicate));

    [Fact]
    public void IsoModuleExportsAndImportsResolve()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            (
                "main.pl",
                ":- module(main). :- export(go/0). :- end_module(main). :- body(main). :- import(other). go :- helper. :- end_body(main)."
            ),
            (
                "other.pl",
                ":- module(other). :- export(helper/0). :- end_module(other). :- body(other). helper. :- end_body(other)."
            )
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void IncludeExpandsWithoutExecutingInitializationDirectives()
    {
        Source("parts.pl", ":- initialization(halt(9)). helper. singleton(X).\n");
        Source("main.pl", ":- include('parts.pl'). go :- helper.\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
        Assert.Contains(diagnostics, diagnostic => diagnostic.FileName?.EndsWith("parts.pl", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void MissingIncludeProducesASourceResolutionWarning()
    {
        Source("main.pl", ":- include('missing.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UnresolvedLintSource));
    }

    [Fact]
    public void IncludeCycleIsBoundedAndReported()
    {
        Source("a.pl", ":- include('b.pl').\n");
        Source("b.pl", ":- include('a.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("a.pl");

        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UnresolvedLintSource));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private IReadOnlyList<Diagnostic> Load(string name)
    {
        var path = Path.Combine(_directory, name);
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
        return [.. loaded.Diagnostics, .. PrologLinter.AnalyzeProgram([loaded.Source])];
    }

    private IReadOnlyList<Diagnostic> Lint(params (string Name, string Text)[] inputs) => LintSources(inputs);

    private IReadOnlyList<Diagnostic> LintSources(IEnumerable<(string Name, string Text)> inputs)
    {
        List<PrologLintSource> sources = [];
        List<Diagnostic> readerDiagnostics = [];
        foreach ((string name, string text) in inputs)
        {
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
                Path.Combine(_directory, name),
                text,
                new BytecodeProgram()
            );
            sources.Add(loaded.Source);
            readerDiagnostics.AddRange(loaded.Diagnostics);
        }

        return [.. readerDiagnostics, .. PrologLinter.AnalyzeProgram(sources)];
    }

    private IReadOnlyList<Diagnostic> Lint(string source) => Lint(("main.pl", source));

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
