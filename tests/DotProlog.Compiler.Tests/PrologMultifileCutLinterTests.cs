using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologMultifileCutLinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-multifile-cuts-" + Guid.NewGuid().ToString("N")
    );

    public PrologMultifileCutLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledByDefault()
    {
        var diagnostics = Lint(":- multifile(p/0). p :- !.", PrologLintOptions.SemanticOnly);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void ReportsMultifileClauseAtItsFirstCut()
    {
        var diagnostics = Lint(":- multifile(p/0). p :- !, !.\n");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
        Assert.Equal(1, warning.Span.Line);
        Assert.Contains("multifile predicate p/0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("other source units", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClauseWithoutMultifileDeclarationIsNotReported()
    {
        var diagnostics = Lint("p :- !.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void MultifileClauseWithoutCutIsNotReported()
    {
        var diagnostics = Lint(":- multifile(p/0). p :- true.");

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void MultipleMatchingClausesProduceSeparateFindings()
    {
        var diagnostics = Lint(":- multifile(p/0). p :- !. p :- !.");

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void ReportsCutInMultifileNonterminal()
    {
        var diagnostics = Lint(":- multifile(nt//2). nt(A, A) --> [A], { ! }.");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
        Assert.Contains("multifile nonterminal nt//2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicMultifileClauseIsEligible()
    {
        var diagnostics = Lint(":- dynamic(p/0). :- multifile(p/0). p :- !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void MultifileDeclarationAppliesAcrossNamedFiles()
    {
        var diagnostics = Lint(
            [("declarations.pl", ":- module(m, []). :- multifile(p/0)."), ("definitions.pl", ":- module(m, []). p :- !.")],
            Enabled
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void PhysicalSeparationWithoutDeclarationIsNotEnough()
    {
        var diagnostics = Lint([("first.pl", ":- module(m, []). p :- !."), ("second.pl", ":- module(m, []). p :- !.")], Enabled);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void IncludedClauseRetainsOwnershipWithoutDuplicateFindings()
    {
        Source("parts.pl", ":- multifile(p/0). p :- !.\n");
        Source("main.pl", ":- include('parts.pl').\n");

        var diagnostics = Load("main.pl");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
        Assert.Equal(Path.Combine(_directory, "parts.pl"), warning.FileName);
    }

    [Theory]
    [InlineData("(true ; !)", true)]
    [InlineData("(true -> ! ; true)", true)]
    [InlineData("(true -> true ; !)", true)]
    [InlineData("(! *-> true)", true)]
    [InlineData("(! -> true ; true)", false)]
    [InlineData("\\+ !", false)]
    [InlineData("call(!)", false)]
    [InlineData("findall(Y, (Y = 1 ; !), _)", false)]
    [InlineData("once(!)", false)]
    [InlineData("custom_meta(!)", false)]
    public void CutScopeDeterminesEligibility(string body, bool expected)
    {
        var diagnostics = Lint($":- multifile(p/0). p :- {body}.");

        Assert.Equal(expected, diagnostics.Any(diagnostic => diagnostic.Id == LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void AnOpaqueConditionCutDoesNotHideALaterClauseCut()
    {
        var diagnostics = Lint(":- multifile(p/0). p :- ((! -> true ; true), !).");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameRule(PrologLanguageMode mode)
    {
        var diagnostics = Lint(":- multifile(p/0). p :- !.", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
    }

    [Fact]
    public void UnrelatedUndefinedCallsDoNotSuppressTheRule()
    {
        var diagnostics = Lint(":- multifile(p/0). p :- missing, !.");

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
        Assert.Contains(diagnostics, Id(LintDiagnosticIds.UndefinedPredicate));
    }

    [Fact]
    public void ReusableSingleFileApiHonorsEnabledRules()
    {
        const string text = ":- multifile(p/0). p :- !.";
        var loaded = PrologLintSourceLoader.Load(Path.Combine(_directory, "single.pl"), text, new BytecodeProgram());

        var diagnostics = PrologLinter.AnalyzeSource(text, loaded.Source.Clauses, "single.pl", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.PossibleMultifileCut));
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
            EnabledRules = [LintDiagnosticIds.PossibleMultifileCut],
        };

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
