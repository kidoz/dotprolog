using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

public sealed class PrologBuiltinShadowLinterTests : IDisposable
{
    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.BuiltinShadow],
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-lint-builtin-shadow-" + Guid.NewGuid().ToString("N")
    );

    public PrologBuiltinShadowLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void SingleFileRuleIsOptInAndReportsNativeIdentityAtName()
    {
        const string text = "writeln(Value).";
        var defaultDiagnostics = LintSource(text, options: null);
        var enabledDiagnostics = LintSource(text, Enabled);

        Assert.DoesNotContain(defaultDiagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        var warning = Assert.Single(enabledDiagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(0, warning.Span.Start);
        Assert.Equal("writeln".Length, warning.Span.Length);
        Assert.Equal("single.pl", warning.FileName);
        Assert.Contains("Local procedure writeln/1 in module user", warning.Message, StringComparison.Ordinal);
        Assert.Contains("predefined predicate writeln/1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("review which definition calls resolve to", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not resolved", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledLibraryIdentityIsReported()
    {
        var diagnostics = Lint(Enabled, ("main.pl", "member(X, X)."));

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Contains("predefined predicate member/2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentArityDoesNotCollide()
    {
        var diagnostics = LintSource("member(X, X, X).", Enabled);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void RepeatedClausesProduceOneFinding()
    {
        var diagnostics = LintSource("member(X, [X|_]). member(X, [_|T]) :- member(X, T).", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Theory]
    [InlineData(":- module(m, []).")]
    [InlineData(":- module(m).")]
    public void ModuleFormsUseModuleQualifiedIdentity(string directive)
    {
        var diagnostics = LintSource($"{directive} member(_, _).", Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Contains("in module m", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctModulesReceiveSeparateFindings()
    {
        var diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a, []). member(_, _)."),
            ("b.pl", ":- module(b, []). member(_, _).")
        );

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.BuiltinShadow)];
        Assert.Equal(2, warnings.Length);
        Assert.Contains(warnings, warning => warning.Message.Contains("in module a", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Message.Contains("in module b", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("multifile")]
    public void DeclarationOnlyProcedureUsesIndicatorSpan(string declaration)
    {
        var text = $":- {declaration}(member/2).";
        var diagnostics = LintSource(text, Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(text.IndexOf("member/2", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal("member/2".Length, warning.Span.Length);
    }

    [Fact]
    public void FirstClauseTakesPrecedenceOverEarlierDeclarationForLocation()
    {
        const string text = ":- dynamic(member/2). member(a, b).";
        var diagnostics = LintSource(text, Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(text.LastIndexOf("member(a, b)", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal("member".Length, warning.Span.Length);
    }

    [Fact]
    public void GrammarCollisionUsesBothSourceAndLoweredIndicators()
    {
        const string text = "member --> [a].";
        var diagnostics = LintSource(text, Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(0, warning.Span.Start);
        Assert.Equal("member".Length, warning.Span.Length);
        Assert.Contains("Local procedure member//0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("predefined predicate member/2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryAndGrammarDefinitionsSharingIdentityProduceOneFinding()
    {
        var diagnostics = LintSource("member --> [a]. member(_, _).", Enabled);

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Contains("member//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludedDefinitionRetainsPhysicalOwnershipWithoutDuplication()
    {
        Source("local.pl", "member(_, _).\n");
        Source("main.pl", ":- module(m, []). :- include('local.pl').\n");

        var diagnostics = Load("main.pl");

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(Path.Combine(_directory, "local.pl"), warning.FileName);
        Assert.Equal(0, warning.Span.Start);
    }

    [Fact]
    public void ImportsAndReexportsDoNotDuplicateSourceDefinitionFinding()
    {
        var diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a, [member/2]). member(_, _)."),
            ("r.pl", ":- module(r, []). :- reexport(a, [member/2])."),
            ("main.pl", ":- module(m, []). :- import(r, [member/2]).")
        );

        var warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.EndsWith("a.pl", warning.FileName, StringComparison.Ordinal);
        Assert.Contains("in module a", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonDefiningDeclarationsDoNotCollide()
    {
        var diagnostics = LintSource(
            ":- module(m, [member/2]). :- discontiguous(member/2). :- meta_predicate(member(?, ?)).",
            Enabled
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void UnresolvedImportsDoNotSuppressIndependentCollision()
    {
        var diagnostics = Lint(
            Enabled,
            ("main.pl", ":- module(m, []). :- import(missing, [x/0]). member(_, _).")
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameCatalog(PrologLanguageMode mode)
    {
        var diagnostics = LintSource("member(_, _).", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void WholeProgramDoesNotDuplicateExplicitSourceFinding()
    {
        var diagnostics = Lint(Enabled, ("main.pl", "member(_, _)."));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void DuplicateEnabledRulesDoNotDuplicateFindings()
    {
        var options = Enabled with { EnabledRules = [LintDiagnosticIds.BuiltinShadow, "dpl3049"] };

        var diagnostics = LintSource("member(_, _).", options);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void LocalImportAndBuiltinShadowRulesCompose()
    {
        var options = Enabled with
        {
            EnabledRules = [LintDiagnosticIds.BuiltinShadow, LintDiagnosticIds.LocalImportShadow],
        };
        var diagnostics = Lint(
            options,
            ("a.pl", ":- module(a, [member/2]). member(_, _)."),
            ("main.pl", ":- module(m, []). :- import(a, [member/2]). member(_, _).")
        );

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.BuiltinShadow));
        Assert.Single(diagnostics, Id(LintDiagnosticIds.LocalImportShadow));
    }

    [Fact]
    public void FindingsUseSourceOrder()
    {
        var diagnostics = LintSource("writeln(_). member(_, _).", Enabled);

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.BuiltinShadow)];
        Assert.Equal(2, warnings.Length);
        Assert.Contains("writeln/1", warnings[0].Message, StringComparison.Ordinal);
        Assert.Contains("member/2", warnings[1].Message, StringComparison.Ordinal);
        Assert.True(warnings[0].Span.Start < warnings[1].Span.Start);
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        var diagnostics = LintSource(":- initialization(halt(9)). member(_, _).", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
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
        foreach (var name in names)
        {
            var path = Path.Combine(_directory, name);
            var loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
            sources.Add(loaded.Source);
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources, Enabled)];
    }

    private static IReadOnlyList<Diagnostic> LintSource(
        string text,
        PrologLintOptions? options,
        PrologLanguageMode mode = PrologLanguageMode.Modern
    )
    {
        var loaded = PrologLintSourceLoader.Load("single.pl", text + "\n", new BytecodeProgram(mode));
        return
        [
            .. loaded.Diagnostics,
            .. PrologLinter.AnalyzeSource(
                text,
                loaded.Source.Clauses,
                "single.pl",
                options,
                loaded.Source.InitialDoubleQuotes,
                loaded.Source.CodePoints
            ),
        ];
    }

    private static IReadOnlyList<Diagnostic> Lint(PrologLintOptions? options, params (string Name, string Text)[] inputs)
    {
        List<PrologLintSource> sources = [];
        List<Diagnostic> diagnostics = [];
        foreach ((var name, var text) in inputs)
        {
            var loaded = PrologLintSourceLoader.Load(
                Path.Combine(Path.GetTempPath(), name),
                text,
                new BytecodeProgram()
            );
            sources.Add(loaded.Source);
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources, options)];
    }

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
