using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologReachabilityLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-reachability-" + Guid.NewGuid().ToString("N")
    );

    public PrologReachabilityLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReportsAnUnreferencedPredicateOnceAtItsFirstHead()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, [go/0]).\ngo.\nunused.\nunused.\n");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
        Assert.Equal(3, warning.Span.Line);
        Assert.Equal(1, warning.Span.Column);
        Assert.Contains("m:unused/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("may be unreferenced", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FollowsDirectAndIndirectCallsFromAnExport()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, [go/0]). go :- middle. middle :- leaf. leaf. unused.");

        AssertUnreferenced(diagnostics, "unused/0");
    }

    [Fact]
    public void RecursiveAndMutuallyRecursivePredicatesCanBeUnreachable()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, [go/0]). go. dead :- dead. left :- right. right :- left.");

        AssertUnreferenced(diagnostics, "dead/0", "left/0", "right/0");
    }

    [Fact]
    public void NonModulePredicatesAreExternalEntryPoints()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint("unused :- helper. helper.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void InitializationGoalsRootTheirCallTreesWithoutExecuting()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, []). :- initialization((go, halt(9))). go :- helper. helper. unused."
        );

        AssertUnreferenced(diagnostics, "unused/0");
    }

    [Fact]
    public void DiscoveredTestPredicatesAreEntryPoints()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). test_example :- helper. helper. test_helper(_). unused.");

        AssertUnreferenced(diagnostics, "test_helper/1", "unused/0");
    }

    [Fact]
    public void DynamicAndMultifilePredicatesAreEntryPoints()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, []). :- dynamic(dynamic_p/0). :- multifile(multi_p/0). "
                + "dynamic_p :- dynamic_helper. multi_p :- multi_helper. dynamic_helper. multi_helper. unused."
        );

        AssertUnreferenced(diagnostics, "unused/0");
    }

    [Fact]
    public void MetaClosuresContributeTheirExpandedArity()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [go/2]). go(X, Y) :- maplist(prefix(a), X, Y). prefix(_, _, _). prefix(_, _). unused."
        );

        AssertUnreferenced(diagnostics, "prefix/2", "unused/0");
    }

    [Fact]
    public void ExportedNonterminalsRootGrammarCallsAndOrdinaryGoals()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [grammar//0]). grammar --> child, { helper }. child --> [a]. helper. dead_grammar --> [a]."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void UnreferencedNonterminalsAreNotFindingsInThisSlice()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). dead_grammar --> [a].");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameReachabilityRules(PrologLanguageMode mode)
    {
        var source =
            mode == PrologLanguageMode.Modern
                ? ":- module(m, [go/0]). go :- helper. helper. unused."
                : ":- module(m). :- export(go/0). :- end_module(m). :- body(m). go :- helper. helper. unused. :- end_body(m).";

        IReadOnlyList<Diagnostic> diagnostics = Lint(mode, source);

        AssertUnreferenced(diagnostics, "unused/0");
    }

    [Fact]
    public void UnresolvedImportsSuppressOnlyReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). :- use_module('missing.pl'). dead.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void MissingIncludesSuppressOnlyReachabilityFindings()
    {
        Source("main.pl", ":- module(m, []). :- include('missing.pl'). dead.\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UnresolvedLintSource));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void IncludedPredicateRetainsItsPhysicalSourceOwner()
    {
        Source("parts.pl", "dead.\n");
        Source("main.pl", ":- module(m, [go/0]). :- include('parts.pl'). go.\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
        Assert.Equal(1, warning.Span.Line);
    }

    [Fact]
    public void VariableGoalsSuppressReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). go(G) :- G. dead.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void UnknownModulesSuppressReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). go :- unknown:helper. dead.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void UndefinedCallsRemainVisibleWhileReachabilityIsSuppressed()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). go :- missing. dead.");

        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void RuntimeDatabaseMutationSuppressesReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). go :- assertz(fact). dead.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void RuntimeSourceLoadingSuppressesReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). go :- consult('other.pl'). dead.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void ConditionalCompilationSuppressesReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). :- if(condition). dead. :- endif.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void SourceExpansionDefinitionsSuppressReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). goal_expansion(_, _). dead.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    [Fact]
    public void UnsupportedMetaClosuresSuppressReachabilityFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(":- module(m, []). go(X) :- maplist(42, X). dead.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleUnreferencedPredicate));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void AssertUnreferenced(IReadOnlyList<Diagnostic> diagnostics, params string[] expected)
    {
        string[] actual =
        [
            .. diagnostics
                .Where(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleUnreferencedPredicate)
                .Select(warning => warning.Message)
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(expected.Length, actual.Length);
        foreach (string indicator in expected)
        {
            Assert.Contains(actual, message => message.Contains(indicator, StringComparison.Ordinal));
        }
    }

    private IReadOnlyList<Diagnostic> Load(string name)
    {
        var path = Path.Combine(_directory, name);
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
        return [.. loaded.Diagnostics, .. PrologLinter.AnalyzeProgram([loaded.Source])];
    }

    private IReadOnlyList<Diagnostic> Lint(string source) => Lint(PrologLanguageMode.Modern, source);

    private IReadOnlyList<Diagnostic> Lint(PrologLanguageMode mode, string source)
    {
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            Path.Combine(_directory, "main.pl"),
            source,
            new BytecodeProgram(mode)
        );
        return [.. loaded.Diagnostics, .. PrologLinter.AnalyzeProgram([loaded.Source])];
    }

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
