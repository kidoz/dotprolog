using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp.Tests;

public sealed class PrologGroundRuleContractLinterTests : IDisposable
{
    private static readonly PrologLintOptions GroundRules = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.GroundRuleContractOutputTypeMismatch],
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-ground-rule-contract-lint-" + Guid.NewGuid().ToString("N")
    );

    public PrologGroundRuleContractLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void GroundRuleAnalysisRunsWithoutExplicitEnablement()
    {
        const string source = ":- module(m, [p/1]). p(wrong) :- true.";
        const string contract = ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).";

        Assert.Single(Lint(source, contract), Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
        Assert.Single(
            Lint(source, contract, PrologLintOptions.Covington),
            Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch)
        );
        Assert.Single(Lint(source, contract, GroundRules), Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("a = a")]
    [InlineData("foo(a) = foo(a)")]
    [InlineData("[a,b] = [a,b]")]
    [InlineData("1 = 1r1")]
    [InlineData("1.0 = 1.0")]
    [InlineData("-0.0 = 0.0")]
    [InlineData("1r2 = 1r2")]
    [InlineData("true, a = a")]
    [InlineData("(a = a, true)")]
    public void SuccessfulGroundBodiesExposeRuleOutputs(string body)
    {
        const string sourcePrefix = ":- module(m, [p/1]). p(wrong) :- ";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $"{sourcePrefix}{body}.",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
        Assert.Equal(sourcePrefix.Length - 10, warning.Span.Start);
        Assert.Equal(5, warning.Span.Length);
        Assert.Contains("Output value of m:p/1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("ground shape atom", warning.Message, StringComparison.Ordinal);
        Assert.Contains("contract type integer", warning.Message, StringComparison.Ordinal);
        Assert.Contains("ground rule output", warning.Message, StringComparison.Ordinal);
        Assert.Contains("rules.dpli", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a = b")]
    [InlineData("foo(a) = foo(b)")]
    [InlineData("[a,b] = [a,c]")]
    [InlineData("1 = 1.0")]
    [InlineData("1.0 = 2.0")]
    [InlineData("1r2 = 1r3")]
    [InlineData("9223372036854775808 = 9223372036854775809")]
    [InlineData("true, a = b")]
    [InlineData("a = b, true")]
    public void FailingGroundBodiesSuppressRuleOutputs(string body)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). p(wrong) :- {body}.",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Fact]
    public void RepeatedGroundComparisonsUseIndependentCallState()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/2]). p(1, wrong) :- foo(a) = foo(a). p(2, also_wrong) :- [a,b] = [a,b].",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, integer), out(value, integer)]).",
            GroundRules
        );

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("det", "p(1, 1) :- true. p(1, wrong) :- true.", 0)]
    [InlineData("det", "p(1, 1) :- a = b. p(1, wrong) :- true.", 1)]
    [InlineData("det", "p(1, 1) :- true. p(2, wrong) :- true.", 1)]
    [InlineData("det", "p(1, 1). p(1, wrong) :- true.", 0)]
    [InlineData("det", "p(1, 1). p(2, wrong) :- true.", 1)]
    [InlineData("semidet", "p(1, 1) :- true. p(1, wrong) :- true.", 0)]
    [InlineData("semidet", "p(1, 1) :- a = b. p(1, wrong) :- true.", 1)]
    [InlineData("semidet", "p(1, 1) :- true. p(2, wrong) :- true.", 1)]
    [InlineData("semidet", "p(1, 1). p(1, wrong) :- true.", 0)]
    [InlineData("semidet", "p(1, 1). p(2, wrong) :- true.", 1)]
    public void DeterministicReachabilitySkipsOnlySuccessfulEarlierClauses(string determinism, string clauses, int expected)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). {clauses}",
            $":- clr_module('M'). :- clr_export(p/2, {determinism}, [in(item, integer), out(value, integer)]).",
            GroundRules
        );

        Assert.Equal(
            expected,
            diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.GroundRuleContractOutputTypeMismatch)
        );
    }

    [Theory]
    [InlineData("multi", "p(1, 1) :- true. p(1, wrong) :- true.", 1)]
    [InlineData("multi", "p(1, 1) :- true. p(2, wrong) :- true.", 1)]
    [InlineData("multi", "p(1, 1). p(1, wrong) :- true.", 1)]
    [InlineData("multi", "p(1, 1) :- true. p(1, wrong) :- a = b.", 0)]
    [InlineData("nondet", "p(1, 1) :- true. p(1, wrong) :- true.", 1)]
    [InlineData("nondet", "p(1, 1) :- true. p(2, wrong) :- true.", 1)]
    [InlineData("nondet", "p(1, 1). p(1, wrong) :- true.", 1)]
    [InlineData("nondet", "p(1, 1) :- true. p(1, wrong) :- a = b.", 0)]
    public void StreamingEnumerationReachesEverySuccessfulRule(string determinism, string clauses, int expected)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). {clauses}",
            $":- clr_module('M'). :- clr_export(p/2, {determinism}, [in(item, integer), out(value, integer)]).",
            GroundRules
        );

        Assert.Equal(
            expected,
            diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.GroundRuleContractOutputTypeMismatch)
        );
    }

    [Theory]
    [InlineData("1", "atom")]
    [InlineData("ok", "integer")]
    [InlineData("ok", "float")]
    [InlineData("[a|b]", "list(atom)")]
    [InlineData("[[a],[1]]", "list(list(atom))")]
    public void RuleOutputUsesExistingMarshallerCompatibility(string output, string type)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). p({output}) :- true.",
            $":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, {type})]).",
            GroundRules
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Fact]
    public void GroundTermOutputHasNoIncompatibleRepresentation()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(1) :- true. p(1.5) :- true. p(1r2) :- true. p(foo(bar)) :- true. p([a|b]) :- true.",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, term)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Fact]
    public void FactAndGroundRuleFindingsDoNotOverlap()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(wrong). p(also_wrong) :- true.",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
        Assert.Single(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.LaterContractOutputTypeMismatch));
        Assert.DoesNotContain(
            Lint(
                ":- module(m, [p/1]). p(1). p(wrong).",
                ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
                GroundRules
            ),
            Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch)
        );
    }

    [Fact]
    public void RepeatedEnablementDoesNotDuplicateFindings()
    {
        PrologLintOptions options = PrologLintOptions.SemanticOnly with
        {
            EnabledRules =
            [
                LintDiagnosticIds.GroundRuleContractOutputTypeMismatch,
                LintDiagnosticIds.GroundRuleContractOutputTypeMismatch,
            ],
        };

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(wrong) :- true.",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            options
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("X = a")]
    [InlineData("X")]
    [InlineData("!")]
    [InlineData("(true; true)")]
    [InlineData("\\+ true")]
    [InlineData("(true -> true)")]
    [InlineData("findall(X, true, X)")]
    [InlineData("1 =:= 1")]
    [InlineData("\"a\" = \"a\"")]
    [InlineData("true, q")]
    [InlineData("a = b, X = a")]
    public void UnknownBodiesRejectTheWholeTable(string body)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). p(wrong) :- true. p(1) :- {body}.",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("p(X) :- true. p(wrong) :- true.")]
    [InlineData("p([a|X]) :- true. p(wrong) :- true.")]
    [InlineData("p(\"text\") :- true. p(wrong) :- true.")]
    [InlineData(":- dynamic(p/1). p(wrong) :- true.")]
    [InlineData(":- multifile(p/1). p(wrong) :- true.")]
    [InlineData(":- initialization(halt(9)). p(wrong) :- true.")]
    [InlineData(":- assertz(q(1)). p(wrong) :- true.")]
    [InlineData(":- ensure_loaded('missing.pl'). p(wrong) :- true.")]
    public void UncertainTablesRemainSilent(string clauses)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). {clauses}",
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("p(1.0, 1) :- true. p(2, wrong) :- true.")]
    [InlineData("p(1, 1) :- true. p(2.0, wrong) :- true.")]
    [InlineData("p(1, 1) :- true. p(X, wrong) :- true.")]
    [InlineData("p(1, 1) :- true. p(9223372036854775808, wrong) :- true.")]
    public void IncompatibleOrVariableInputsRejectTheWholeTable(string clauses)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). {clauses}",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, integer), out(value, integer)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Fact]
    public void MixedGrammarIdentityRejectsTheWholeTable()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/2]). p(ok, 1) :- true. p --> []. p(2, wrong) :- true.",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, atom), out(value, integer)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Fact]
    public void ImportedOnlyDefinitionRemainsSilent()
    {
        PrologLintSource imported = Source("a.pl", ":- module(a, [p/1]). p(wrong) :- true.", PrologLanguageMode.Modern);
        PrologLintSource main = Source("rules.pl", ":- module(m, [p/1]). :- reexport(a).", PrologLanguageMode.Modern);

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [imported, main],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Fact]
    public void AmbiguousModulesRemainSilent()
    {
        PrologLintSource a = Source("a.pl", ":- module(a, [p/1]). p(wrong) :- true.", PrologLanguageMode.Modern);
        PrologLintSource b = Source("b.pl", ":- module(b, [p/1]). p(wrong) :- true.", PrologLanguageMode.Modern);

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [a, b],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothModesReportReachableGroundRuleOutputs(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [Source("rules.pl", ":- module(m, [p/1]). p(wrong) :- true, a = a.", mode)],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
    }

    [Fact]
    public void IncludedGroundRuleRetainsPhysicalLocation()
    {
        var mainPath = Path.Combine(_directory, "rules.pl");
        var rulePath = Path.Combine(_directory, "guarded.pl");
        const string rules = "p(wrong) :- true.\n";
        File.WriteAllText(mainPath, ":- module(m, [p/1]). :- include('guarded.pl').\n");
        File.WriteAllText(rulePath, rules);
        PrologLintSource loaded = PrologLintSourceLoader.Load(mainPath, File.ReadAllText(mainPath), new BytecodeProgram()).Source;

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [loaded],
            ":- clr_module('M'). :- clr_export(p/1, nondet, [out(value, integer)]).",
            GroundRules
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.GroundRuleContractOutputTypeMismatch));
        Assert.Equal(rulePath, warning.FileName);
        Assert.Equal(2, warning.Span.Start);
    }

    [Fact]
    public void MultipleRuleOutputsHaveIndependentFindings()
    {
        const string source = ":- module(m, [p/2]). p(wrong, also_wrong) :- true.";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            source,
            ":- clr_module('M'). :- clr_export(p/2, det, [out(first, integer), out(second, integer)]).",
            GroundRules
        );

        Diagnostic[] warnings =
        [
            .. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.GroundRuleContractOutputTypeMismatch),
        ];
        Assert.Equal(2, warnings.Length);
        Assert.Equal(source.IndexOf("wrong", StringComparison.Ordinal), warnings[0].Span.Start);
        Assert.Equal(source.IndexOf("also_wrong", StringComparison.Ordinal), warnings[1].Span.Start);
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
