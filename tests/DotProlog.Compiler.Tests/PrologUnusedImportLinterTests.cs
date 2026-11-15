using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologUnusedImportLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-imports-" + Guid.NewGuid().ToString("N")
    );

    public PrologUnusedImportLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReportsOnlyUnusedSelectiveBindingsAtTheirSourceSpans()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/0]).\n:- use_module('other.pl', [used/0, unused/0]).\ngo :- used.\n"),
            ("other.pl", ":- module(other, [used/0, unused/0]). used. unused.")
        );

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
        Assert.Equal(2, warning.Span.Line);
        Assert.Contains("unused/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("from module other", warning.Message, StringComparison.Ordinal);
        Assert.EndsWith("main.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAnUnusedSelectiveNonterminal()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/1]). :- use_module('other.pl', [used//0, unused//1]). go(X) :- phrase(used, X)."),
            ("other.pl", ":- module(other, [used//0, unused//1]). used --> [a]. unused(_) --> [b].")
        );

        AssertUnused(diagnostics, "unused//1");
    }

    [Fact]
    public void EmptySelectiveImportProducesNoFinding()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [])."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void WholeImportIsReportedOnceWhenNoBindingIsUsed()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/0]). :- use_module('other.pl'). go."),
            ("other.pl", ":- module(other, [first/0, second/0]). first. second.")
        );

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
        Assert.Contains("any predicate or nonterminal", warning.Message, StringComparison.Ordinal);
        Assert.Contains("from module other", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WholeImportIsNotReportedWhenAnyBindingIsUsed()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/0]). :- use_module('other.pl'). go :- first."),
            ("other.pl", ":- module(other, [first/0, second/0]). first. second.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void MetaClosureCountsAsAReference()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/2]). :- use_module('other.pl', [prefix/3]). go(X, Y) :- maplist(prefix(a), X, Y)."),
            ("other.pl", ":- module(other, [prefix/3]). prefix(_, _, _).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void GrammarBodyCountsAsANonterminalReference()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go//0]). :- use_module('other.pl', [other_grammar//0]). go --> other_grammar."),
            ("other.pl", ":- module(other, [other_grammar//0]). other_grammar --> [a].")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void InitializationGoalCountsAsAReferenceWithoutExecuting()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). :- initialization(helper)."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void ReferenceInUnreachableCodeStillCounts()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). dead :- helper."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void UsageByAnotherModuleDoesNotCount()
    {
        var diagnostics = Lint(
            ("first.pl", ":- module(first, [go/0]). :- use_module('other.pl', [helper/0]). go :- helper."),
            ("second.pl", ":- module(second, []). :- use_module('other.pl', [helper/0])."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
        Assert.Contains("module second", warning.Message, StringComparison.Ordinal);
        Assert.EndsWith("second.pl", warning.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedCallDoesNotCountAsImportUsage()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/0]). :- use_module('other.pl', [helper/0]). go :- other:helper."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        AssertUnused(diagnostics, "helper/0");
    }

    [Fact]
    public void PredicateNonterminalMismatchDoesNotCountAsUsage()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/2]). :- use_module('other.pl', [p//0]). go(A, B) :- p(A, B)."),
            ("other.pl", ":- module(other, [p//0]). p --> [a].")
        );

        AssertUnused(diagnostics, "p//0");
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.PredicateNonterminalMismatch));
    }

    [Fact]
    public void ReexportsAreNotReported()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main). :- reexport(other, [helper/0])."),
            ("other.pl", ":- module(other). :- export(helper/0). :- end_module(other). :- body(other). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesReportUnusedImports(PrologLanguageMode mode)
    {
        (string Name, string Text) main =
            mode == PrologLanguageMode.Modern
                ? ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]).")
                : ("main.pl", ":- module(main). :- import(other, [helper/0]).");
        (string Name, string Text) other =
            mode == PrologLanguageMode.Modern
                ? ("other.pl", ":- module(other, [helper/0]). helper.")
                : ("other.pl", ":- module(other). :- export(helper/0). :- end_module(other). :- body(other). helper.");

        var diagnostics = Lint(mode, main, other);

        AssertUnused(diagnostics, "helper/0");
    }

    [Fact]
    public void ReaderSideEffectsSuppressImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0])."),
            ("other.pl", ":- module(other, [helper/0]). :- op(200, fy, myop). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void InitializationSideEffectsSuppressImportFindingsWithoutExecuting()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0])."),
            ("other.pl", ":- module(other, [helper/0]). :- initialization(halt(9)). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void TransitiveSideEffectsSuppressImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0])."),
            ("other.pl", ":- module(other, [helper/0]). :- use_module('effects.pl'). helper."),
            ("effects.pl", ":- module(effects, []). :- initialization(halt(9)).")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void UnresolvedImportsSuppressAllUnusedImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('missing.pl'). :- use_module('other.pl', [helper/0])."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void MissingIncludesSuppressAllUnusedImportFindings()
    {
        Source("main.pl", ":- module(main, []). :- include('missing.pl'). :- use_module('other.pl', [helper/0]).\n");
        Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        var diagnostics = Load("main.pl", "other.pl");

        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UnresolvedLintSource));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void IncludedImportRetainsItsPhysicalSourceOwner()
    {
        Source("main.pl", ":- module(main, []). :- include('parts.pl').\n");
        Source("parts.pl", ":- use_module('other.pl', [helper/0]).\n");
        Source("other.pl", ":- module(other, [helper/0]). helper.\n");

        var diagnostics = Load("main.pl", "other.pl");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Fact]
    public void VariableGoalsSuppressUnusedImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). go(G) :- G."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void RuntimeDatabaseMutationSuppressesUnusedImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). go :- assertz(fact)."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void ConditionalCompilationSuppressesUnusedImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). :- if(condition). :- endif."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void SourceExpansionSuppressesUnusedImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). goal_expansion(_, _)."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void UnsupportedMetaClosuresSuppressUnusedImportFindings()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). go(X) :- maplist(42, X)."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void LocalShadowingDoesNotProduceAnImportUsageFinding()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/0]). :- use_module('other.pl', [helper/0]). go :- helper. helper."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    [Fact]
    public void BuiltInCallsDoNotCountAsImportUsage()
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, [go/0]). :- use_module('other.pl'). go :- atom(foo)."),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        AssertUnused(diagnostics, "any predicate or nonterminal");
    }

    [Theory]
    [InlineData("go :- unknown:helper.")]
    [InlineData("go :- consult('other.pl').")]
    public void IncompleteCallsSuppressUnusedImportFindings(string body)
    {
        var diagnostics = Lint(
            ("main.pl", ":- module(main, []). :- use_module('other.pl', [helper/0]). " + body),
            ("other.pl", ":- module(other, [helper/0]). helper.")
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossiblyUnusedImport));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void AssertUnused(IReadOnlyList<Diagnostic> diagnostics, params string[] expected)
    {
        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.PossiblyUnusedImport)];
        Assert.Equal(expected.Length, warnings.Length);
        foreach (var fragment in expected)
        {
            Assert.Contains(warnings, warning => warning.Message.Contains(fragment, StringComparison.Ordinal));
        }
    }

    private IReadOnlyList<Diagnostic> Load(params string[] names)
    {
        List<PrologLintSource> sources = [];
        List<Diagnostic> diagnostics = [];
        foreach (var name in names)
        {
            var path = Path.Combine(_directory, name);
            var loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
            sources.Add(loaded.Source);
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources)];
    }

    private IReadOnlyList<Diagnostic> Lint(params (string Name, string Text)[] inputs) => Lint(PrologLanguageMode.Modern, inputs);

    private IReadOnlyList<Diagnostic> Lint(PrologLanguageMode mode, params (string Name, string Text)[] inputs)
    {
        List<PrologLintSource> sources = [];
        List<Diagnostic> diagnostics = [];
        foreach ((var name, var text) in inputs)
        {
            var loaded = PrologLintSourceLoader.Load(
                Path.Combine(_directory, name),
                text + "\n",
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
