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
        IReadOnlyList<Diagnostic> defaultDiagnostics = LintSource(text, options: null);
        IReadOnlyList<Diagnostic> enabledDiagnostics = LintSource(text, Enabled);

        Assert.DoesNotContain(defaultDiagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Diagnostic warning = Assert.Single(enabledDiagnostics, Id(LintDiagnosticIds.BuiltinShadow));
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
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("main.pl", "member(X, X)."));

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Contains("predefined predicate member/2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentArityDoesNotCollide()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource("member(X, X, X).", Enabled);

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void RepeatedClausesProduceOneFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource("member(X, [X|_]). member(X, [_|T]) :- member(X, T).", Enabled);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Theory]
    [InlineData(":- module(m, []).")]
    [InlineData(":- module(m).")]
    public void ModuleFormsUseModuleQualifiedIdentity(string directive)
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource($"{directive} member(_, _).", Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Contains("in module m", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctModulesReceiveSeparateFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
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
        string text = $":- {declaration}(member/2).";
        IReadOnlyList<Diagnostic> diagnostics = LintSource(text, Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(text.IndexOf("member/2", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal("member/2".Length, warning.Span.Length);
    }

    [Fact]
    public void FirstClauseTakesPrecedenceOverEarlierDeclarationForLocation()
    {
        const string text = ":- dynamic(member/2). member(a, b).";
        IReadOnlyList<Diagnostic> diagnostics = LintSource(text, Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(text.LastIndexOf("member(a, b)", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal("member".Length, warning.Span.Length);
    }

    [Fact]
    public void GrammarCollisionUsesBothSourceAndLoweredIndicators()
    {
        const string text = "member --> [a].";
        IReadOnlyList<Diagnostic> diagnostics = LintSource(text, Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(0, warning.Span.Start);
        Assert.Equal("member".Length, warning.Span.Length);
        Assert.Contains("Local procedure member//0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("predefined predicate member/2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryAndGrammarDefinitionsSharingIdentityProduceOneFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource("member --> [a]. member(_, _).", Enabled);

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Contains("member//0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludedDefinitionRetainsPhysicalOwnershipWithoutDuplication()
    {
        Source("local.pl", "member(_, _).\n");
        Source("main.pl", ":- module(m, []). :- include('local.pl').\n");

        IReadOnlyList<Diagnostic> diagnostics = Load("main.pl");

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.Equal(Path.Combine(_directory, "local.pl"), warning.FileName);
        Assert.Equal(0, warning.Span.Start);
    }

    [Fact]
    public void ImportsAndReexportsDoNotDuplicateSourceDefinitionFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            Enabled,
            ("a.pl", ":- module(a, [member/2]). member(_, _)."),
            ("r.pl", ":- module(r, []). :- reexport(a, [member/2])."),
            ("main.pl", ":- module(m, []). :- import(r, [member/2]).")
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
        Assert.EndsWith("a.pl", warning.FileName, StringComparison.Ordinal);
        Assert.Contains("in module a", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonDefiningDeclarationsDoNotCollide()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(
            ":- module(m, [member/2]). :- discontiguous(member/2). :- meta_predicate(member(?, ?)).",
            Enabled
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void UnresolvedImportsDoNotSuppressIndependentCollision()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
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
        IReadOnlyList<Diagnostic> diagnostics = LintSource("member(_, _).", Enabled, mode);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void WholeProgramDoesNotDuplicateExplicitSourceFinding()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(Enabled, ("main.pl", "member(_, _)."));

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void DuplicateEnabledRulesDoNotDuplicateFindings()
    {
        PrologLintOptions options = Enabled with { EnabledRules = [LintDiagnosticIds.BuiltinShadow, "dpl3049"] };

        IReadOnlyList<Diagnostic> diagnostics = LintSource("member(_, _).", options);

        Assert.Single(diagnostics, Id(LintDiagnosticIds.BuiltinShadow));
    }

    [Fact]
    public void LocalImportAndBuiltinShadowRulesCompose()
    {
        PrologLintOptions options = Enabled with
        {
            EnabledRules = [LintDiagnosticIds.BuiltinShadow, LintDiagnosticIds.LocalImportShadow],
        };
        IReadOnlyList<Diagnostic> diagnostics = Lint(
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
        IReadOnlyList<Diagnostic> diagnostics = LintSource("writeln(_). member(_, _).", Enabled);

        Diagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.BuiltinShadow)];
        Assert.Equal(2, warnings.Length);
        Assert.Contains("writeln/1", warnings[0].Message, StringComparison.Ordinal);
        Assert.Contains("member/2", warnings[1].Message, StringComparison.Ordinal);
        Assert.True(warnings[0].Span.Start < warnings[1].Span.Start);
    }

    [Fact]
    public void InitializationDirectivesAreNotExecuted()
    {
        IReadOnlyList<Diagnostic> diagnostics = LintSource(":- initialization(halt(9)). member(_, _).", Enabled);

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
        foreach (string name in names)
        {
            var path = Path.Combine(_directory, name);
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(path, File.ReadAllText(path), new BytecodeProgram());
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
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load("single.pl", text, new BytecodeProgram(mode));
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

        return [.. diagnostics, .. PrologLinter.AnalyzeProgram(sources, options)];
    }

    private void Source(string name, string text) => File.WriteAllText(Path.Combine(_directory, name), text);

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
