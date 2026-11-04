using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp.Tests;

public sealed class PrologContractLinterTests : IDisposable
{
    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.ContractOutputTypeMismatch],
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "dotprolog-contract-lint-" + Guid.NewGuid().ToString("N")
    );

    public PrologContractLinterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RuleIsDisabledUnlessExplicitlyEnabled()
    {
        const string source = ":- module(m, [p/1]). p(wrong).";
        const string contract = ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)]).";

        Assert.Empty(Lint(source, contract, options: PrologLintOptions.SemanticOnly));
        Assert.Empty(Lint(source, contract, options: PrologLintOptions.Covington));
        Assert.Single(Lint(source, contract), Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void ReportsExactOutputSpanAndMessage()
    {
        const string source = ":- module(m, [p/1]). p(wrong).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            source,
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
        Assert.Equal(source.LastIndexOf("wrong", StringComparison.Ordinal), warning.Span.Start);
        Assert.Equal(5, warning.Span.Length);
        Assert.EndsWith("rules.pl", warning.FileName, StringComparison.Ordinal);
        Assert.Contains("Output value of m:p/1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("ground shape atom", warning.Message, StringComparison.Ordinal);
        Assert.Contains("contract type integer", warning.Message, StringComparison.Ordinal);
        Assert.Contains("rules.dpli", warning.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be marshalled", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ok", "atom")]
    [InlineData("42", "integer")]
    [InlineData("1.5", "float")]
    [InlineData("42", "float")]
    [InlineData("9223372036854775808", "float")]
    [InlineData("1r2", "float")]
    [InlineData("foo(bar)", "term")]
    [InlineData("[a,b]", "list(atom)")]
    [InlineData("[[a],[b]]", "list(list(atom))")]
    [InlineData("2r1", "integer")]
    public void CompatibleGroundOutputsProduceNoFinding(string output, string type)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). p({output}).",
            $":- clr_module('M'). :- clr_export(p/1, det, [out(value, {type})])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("ok", "integer", "atom")]
    [InlineData("1", "atom", "integer")]
    [InlineData("576460752303423488", "integer", "big_integer")]
    [InlineData("9223372036854775808", "integer", "big_integer")]
    [InlineData("1r2", "integer", "rational")]
    [InlineData("ok", "float", "atom")]
    [InlineData("[a|b]", "list(atom)", "compound(./2)")]
    [InlineData("[a,1]", "list(atom)", "list")]
    [InlineData("[[a],[1]]", "list(list(atom))", "list")]
    public void IncompatibleGroundOutputsReportTheirRuntimeShape(string output, string type, string shape)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). p({output}).",
            $":- clr_module('M'). :- clr_export(p/1, det, [out(value, {type})])."
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
        Assert.Contains($"ground shape {shape}", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ok", "atom")]
    [InlineData("42", "integer")]
    [InlineData("1.5", "float")]
    [InlineData("[a,b]", "list(atom)")]
    [InlineData("foo(bar)", "term")]
    public void GroundCompatibleInputsAllowLeadingFactAnalysis(string input, string type)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). p({input}, wrong).",
            $":- clr_module('M'). :- clr_export(p/2, det, [in(item, {type}), out(value, integer)])."
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("1", "atom")]
    [InlineData("1.0", "integer")]
    [InlineData("1", "float")]
    [InlineData("9223372036854775808", "integer")]
    [InlineData("[a|b]", "list(atom)")]
    [InlineData("[1]", "list(atom)")]
    [InlineData("X", "atom")]
    public void IncompatibleOrVariableInputsMakeTheResultNotEstablished(string input, string type)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/2]). p({input}, wrong).",
            $":- clr_module('M'). :- clr_export(p/2, det, [in(item, {type}), out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void AliasedInputsAreNotSolved()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/3]). p(X, X, wrong).",
            ":- clr_module('M'). :- clr_export(p/3, det, [in(a, atom), in(b, integer), out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("[a|X]")]
    [InlineData("\"text\"")]
    public void VariableOpenOrReaderDependentOutputsAreNotEstablished(string output)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). p({output}).",
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void LeadingRuleIsNotEstablished()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(wrong) :- true.",
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void LaterFactIsNotEstablished()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(1). p(wrong).",
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("multifile")]
    public void DynamicAndMultifileTargetsAreNotEstablished(string declaration)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            $":- module(m, [p/1]). :- {declaration}(p/1). p(wrong).",
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void EffectfulModuleIsSkippedWithoutExecutingInitialization()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). :- initialization(halt(9)). p(wrong).",
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void MixedOrdinaryAndGrammarDefinitionsAreNotEstablished()
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/2]). p(ok, 1). p --> [].",
            ":- clr_module('M'). :- clr_export(p/2, det, [in(item, atom), out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void ImportedOnlyDefinitionsAreNotEstablished()
    {
        PrologLintSource imported = Source("a.pl", ":- module(a, [p/1]). p(wrong).", PrologLanguageMode.Modern);
        PrologLintSource main = Source("rules.pl", ":- module(m, [p/1]). :- reexport(a).", PrologLanguageMode.Modern);

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [imported, main],
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void AmbiguousExportModulesAreNotEstablished()
    {
        PrologLintSource a = Source("a.pl", ":- module(a, [p/1]). p(wrong).", PrologLanguageMode.Modern);
        PrologLintSource b = Source("b.pl", ":- module(b, [p/1]). p(wrong).", PrologLanguageMode.Modern);

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [a, b],
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Assert.DoesNotContain(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void IncludedLeadingFactRetainsItsPhysicalLocation()
    {
        var mainPath = Path.Combine(_directory, "rules.pl");
        var factPath = Path.Combine(_directory, "fact.pl");
        File.WriteAllText(mainPath, ":- module(m, [p/1]). :- include('fact.pl').\n");
        File.WriteAllText(factPath, "p(wrong).\n");
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            mainPath,
            File.ReadAllText(mainPath),
            new BytecodeProgram()
        );

        IReadOnlyList<Diagnostic> diagnostics = Lint(
            [loaded.Source],
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)])."
        );

        Diagnostic warning = Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
        Assert.Equal(factPath, warning.FileName);
        Assert.Equal(2, warning.Span.Start);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void BothLanguageModesUseTheSameContractCompatibility(PrologLanguageMode mode)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(wrong).",
            ":- clr_module('M'). :- clr_export(p/1, det, [out(value, integer)]).",
            mode
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Theory]
    [InlineData("det")]
    [InlineData("semidet")]
    [InlineData("multi")]
    [InlineData("nondet")]
    public void EveryDeterminismDeclarationUsesTheSameOutputContract(string determinism)
    {
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            ":- module(m, [p/1]). p(wrong).",
            $":- clr_module('M'). :- clr_export(p/1, {determinism}, [out(value, integer)])."
        );

        Assert.Single(diagnostics, Id(LintDiagnosticIds.ContractOutputTypeMismatch));
    }

    [Fact]
    public void MultipleOutputsHaveIndependentFindings()
    {
        const string source = ":- module(m, [p/2]). p(wrong, also_wrong).";
        IReadOnlyList<Diagnostic> diagnostics = Lint(
            source,
            ":- clr_module('M'). :- clr_export(p/2, det, [out(first, integer), out(second, integer)])."
        );

        Diagnostic[] warnings =
        [
            .. diagnostics.Where(diagnostic => diagnostic.Id == LintDiagnosticIds.ContractOutputTypeMismatch),
        ];
        Assert.Equal(2, warnings.Length);
        Assert.Equal(source.IndexOf("wrong", StringComparison.Ordinal), warnings[0].Span.Start);
        Assert.Equal(source.IndexOf("also_wrong", StringComparison.Ordinal), warnings[1].Span.Start);
    }

    [Fact]
    public void CompatibilityAgreesWithRuntimeMarshallerForRepresentativeValues()
    {
        PrologValue[] values =
        [
            new PrologAtom("a"),
            new PrologInteger(1),
            new PrologBigInteger(System.Numerics.BigInteger.One),
            new PrologFloat(1),
            new PrologRational(1, 2),
            new PrologString("a"),
            new PrologCompound("f", [new PrologAtom("a")]),
            new PrologCompound(".", [new PrologAtom("a"), new PrologAtom("[]")]),
        ];
        ContractType[] types =
        [
            new(ContractTypeKind.Atom),
            new(ContractTypeKind.Integer),
            new(ContractTypeKind.Float),
            new(ContractTypeKind.Term),
            new(ContractTypeKind.List, new ContractType(ContractTypeKind.Atom)),
        ];

        foreach (ContractType type in types)
        {
            foreach (PrologValue value in values)
            {
                Assert.Equal(Marshals(type, value), type.CanMarshalOutput(value));
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static bool Marshals(ContractType type, PrologValue value)
    {
        try
        {
            switch (type.Kind)
            {
                case ContractTypeKind.Atom:
                    _ = PrologMarshal.ToAtom(value);
                    break;
                case ContractTypeKind.Integer:
                    _ = PrologMarshal.ToInteger(value);
                    break;
                case ContractTypeKind.Float:
                    _ = PrologMarshal.ToFloat(value);
                    break;
                case ContractTypeKind.Term:
                    break;
                case ContractTypeKind.List:
                    var compatible = true;
                    _ = PrologMarshal.ToList(
                        value,
                        item =>
                        {
                            compatible &= Marshals(type.Element!, item);
                            return item;
                        }
                    );
                    if (!compatible)
                    {
                        return false;
                    }

                    break;
            }

            return true;
        }
        catch (PrologException)
        {
            return false;
        }
    }

    private static IReadOnlyList<Diagnostic> Lint(
        string source,
        string contract,
        PrologLanguageMode mode = PrologLanguageMode.Modern,
        PrologLintOptions? options = null
    ) => Lint([Source("rules.pl", source, mode)], contract, options);

    private static IReadOnlyList<Diagnostic> Lint(
        IReadOnlyList<PrologLintSource> sources,
        string contract,
        PrologLintOptions? options = null
    )
    {
        var path = Path.Combine(Path.GetTempPath(), "rules.dpli");
        ContractReadResult result = ContractReader.Read(contract, "Tests", path);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return PrologContractLinter.Analyze(sources, result.Contract!, path, options ?? Enabled);
    }

    private static PrologLintSource Source(string name, string text, PrologLanguageMode mode) =>
        PrologLintSourceLoader.Load(Path.Combine(Path.GetTempPath(), name), text, new BytecodeProgram(mode)).Source;

    private static Predicate<Diagnostic> Id(string id) => diagnostic => diagnostic.Id == id;
}
