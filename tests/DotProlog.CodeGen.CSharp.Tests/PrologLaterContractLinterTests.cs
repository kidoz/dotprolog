using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp.Tests;

public sealed class PrologLaterContractLinterTests : IDisposable
{
    private static readonly PrologLintOptions Later = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.LaterContractOutputTypeMismatch],
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-later-contract-lint-" + Guid.NewGuid().ToString("N")
    );

    public PrologLaterContractLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void LaterRuleIsOptIn()
    {
        const string source = ":- module(m, [p/1]). p(1). p(wrong).";
        const string contract = ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).";

        Assert.DoesNotContain(Lint(source, contract), Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
        Assert.Single(Lint(source, contract, Later), Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("det")]
    [InlineData("semidet")]
    public void DifferentInputsMakeLaterFactReachable(string determinism)
    {
        const string source = ":- module(m, [p/2]). p(1, 1). p(2, wrong).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            source,
            $":- clr_module('M'). :- clr_export(p/2, {determinism}, [in(item, integer), out(value, integer)]).",
            Later
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
        Assert.Equal(source.LastIndexOf("wrong", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal(5, warning.Span.Length);
        Assert.Contains("Output value of m:p/2", warning.Message, StringComparison.Ordinal);
        Assert.Contains("ground shape atom", warning.Message, StringComparison.Ordinal);
        Assert.Contains("contract type integer", warning.Message, StringComparison.Ordinal);
        Assert.Contains("later fact output", warning.Message, StringComparison.Ordinal);
        Assert.Contains("rules.dpli", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("det")]
    [InlineData("semidet")]
    public void EarlierUnifiableInputHidesLaterFact(string determinism)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/2]). p(1, 1). p(1, wrong).",
            $":- clr_module('M'). :- clr_export(p/2, {determinism}, [in(item, integer), out(value, integer)]).",
            Later
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("multi", "p(1, 1). p(1, wrong).", 1)]
    [InlineData("multi", "p(1, 1). p(2, wrong).", 1)]
    [InlineData("nondet", "p(1, 1). p(1, wrong).", 1)]
    [InlineData("nondet", "p(1, 1). p(2, wrong).", 1)]
    public void StreamingEnumerationReachesLaterFacts(string determinism, string clauses, int expected)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). {clauses}",
            $":- clr_module('M'). :- clr_export(p/2, {determinism}, [in(item, integer), out(value, integer)]).",
            Later
        );

        Assert.Equal(
            expected,
            diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.LaterContractOutputTypeMismatch)
        );
    }

    [Theory]
    [InlineData("1", "atom")]
    [InlineData("ok", "integer")]
    [InlineData("ok", "float")]
    [InlineData("[a|b]", "list(atom)")]
    [InlineData("[[a],[1]]", "list(list(atom))")]
    public void LaterOutputUsesExistingMarshallerCompatibility(string output, string type)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). p(ok). p({output}).",
            $":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, {type})]).",
            Later
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Fact]
    public void GroundTermOutputHasNoIncompatibleRepresentation()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(ok). p(1). p(1.5). p(1r2). p(foo(bar)). p([a|b]).",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, term)]).",
            Later
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Fact]
    public void FirstAndLaterFindingsDoNotOverlap()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(wrong). p(also_wrong).",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            Later
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
        Assert.Single(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
        Assert.DoesNotContain(
            Lint(
                ":- module(m, [p/1]). p(wrong).",
                ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
                Later
            ),
            Id(LintDiagnosticIds.LaterContractOutputTypeMismatch)
        );
    }

    [Theory]
    [InlineData("p(1, 1). p(1r1, wrong).", 0)]
    [InlineData("p(1, 1). p(1.0, wrong).", 1)]
    [InlineData("p(foo(a), 1). p(foo(a), wrong).", 0)]
    [InlineData("p(foo(a), 1). p(foo(b), wrong).", 1)]
    [InlineData("p([a,b], 1). p([a,b], wrong).", 0)]
    [InlineData("p([a,b], 1). p([a,c], wrong).", 1)]
    public void DeterministicReachabilityUsesRuntimeUnification(string clauses, int expected)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). {clauses}",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, term), out(value, integer)]).",
            Later
        );

        Assert.Equal(
            expected,
            diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.LaterContractOutputTypeMismatch)
        );
    }

    [Theory]
    [InlineData("p(1) :- true. p(wrong).")]
    [InlineData("p(1). p(wrong) :- true.")]
    [InlineData("p(1) :- !. p(wrong).")]
    [InlineData("p(X). p(wrong).")]
    [InlineData("p([a|X]). p(wrong).")]
    [InlineData("p(\"text\"). p(wrong).")]
    [InlineData(":- dynamic(p/1). p(1). p(wrong).")]
    [InlineData(":- multifile(p/1). p(1). p(wrong).")]
    [InlineData(":- initialization(halt(9)). p(1). p(wrong).")]
    [InlineData(":- assertz(q(1)). p(1). p(wrong).")]
    [InlineData(":- ensure_loaded('missing.pl'). p(1). p(wrong).")]
    public void UncertainTablesRemainSilent(string clauses)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). {clauses}",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            Later
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("p(1.0, 1). p(2, wrong).")]
    [InlineData("p(1, 1). p(2.0, wrong).")]
    [InlineData("p(1, 1). p(X, wrong).")]
    [InlineData("p(1, 1). p(9223372036854775808, wrong).")]
    public void IncompatibleOrVariableInputsRejectTheWholeTable(string clauses)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). {clauses}",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, integer), out(value, integer)]).",
            Later
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Fact]
    public void MixedGrammarIdentityRejectsTheWholeTable()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/2]). p(ok, 1). p --> []. p(2, wrong).",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, atom), out(value, integer)]).",
            Later
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Fact]
    public void ImportedOnlyDefinitionRemainsSilent()
    {
        PrologLintSource imported = Source("a.pl", ":- module(a, [p/1]). p(1). p(wrong).", PrologLanguageMode.Modern);
        PrologLintSource main = Source("rules.pl", ":- module(m, [p/1]). :- reexport(a).", PrologLanguageMode.Modern);

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [imported, main],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            Later
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Fact]
    public void AmbiguousModulesRemainSilent()
    {
        PrologLintSource a = Source("a.pl", ":- module(a, [p/1]). p(1). p(wrong).", PrologLanguageMode.Modern);
        PrologLintSource b = Source("b.pl", ":- module(b, [p/1]). p(1). p(wrong).", PrologLanguageMode.Modern);

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [a, b],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            Later
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothModesReportReachableLaterOutputs(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [Source("rules.pl", ":- module(m, [p/1]). p(1). p(wrong).", mode)],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            Later
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    [Fact]
    public void IncludedLaterFactRetainsPhysicalLocation()
    {
        var mainPath = Path.Combine(_directory, "rules.pl");
        var factPath = Path.Combine(_directory, "facts.pl");
        const string facts = "p(1). p(wrong).\n";
        File.WriteAllText(mainPath, ":- module(m, [p/1]). :- include('facts.pl').\n");
        File.WriteAllText(factPath, facts);
        PrologLintSource loaded = PrologLintSourceLoader.Load(mainPath, File.ReadAllText(mainPath), new BytecodeProgram()).Source;

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [loaded],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            Later
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
        Assert.Equal(factPath, warning.FileName);
        Assert.Equal(facts.LastIndexOf("wrong", StringComparison.Ordinal), warning.Span.Start);
    }

    [Fact]
    public void MultipleLaterOutputsHaveIndependentFindings()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/3]). p(1, 1, 1). p(2, wrong, also_wrong).",
            ":- clr_module('M'). :- clr_export(p/3, det, [in(item, integer), out(first, integer), out(second, integer)]).",
            Later
        );

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.LaterContractOutputTypeMismatch));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static IReadOnlyList<Diagnostic> Lint(string source, string contract, PrologLintOptions? options = null) =>
        Lint([Source("rules.pl", source, PrologLanguageMode.Modern)], contract, options);

    private static IReadOnlyList<Diagnostic> Lint(
        IReadOnlyList<PrologLintSource> sources,
        string contract,
        PrologLintOptions? options = null
    )
    {
        var path = Path.Combine(Path.GetTempPath(), "rules.dpli");
        ContractReadResult result = ContractReader.Read(contract, "Tests", path);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return PrologContractLinter.Analyze(sources, result.Contract!, path, options);
    }

    private static PrologLintSource Source(string name, string text, PrologLanguageMode mode) =>
        PrologLintSourceLoader.Load(Path.Combine(Path.GetTempPath(), name), text, new BytecodeProgram(mode)).Source;

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
