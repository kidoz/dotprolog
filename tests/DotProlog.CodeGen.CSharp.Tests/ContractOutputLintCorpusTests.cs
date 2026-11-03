using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp.Tests;

public sealed class ContractOutputLintCorpusTests
{
    private const int CorpusVersion = 1;

    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.ContractOutputTypeMismatch],
    };

    private static readonly CorpusCase[] Cases =
    [
        new("atom-consistent", "p(ok).", "atom", 0),
        new("integer-consistent", "p(42).", "integer", 0),
        new("float-integer-coercion", "p(42).", "float", 0),
        new("float-big-integer-coercion", "p(9223372036854775808).", "float", 0),
        new("float-rational-coercion", "p(1r2).", "float", 0),
        new("term-consistent", "p(any(shape)).", "term", 0),
        new("nested-list-consistent", "p([[a],[b]]).", "list(list(atom))", 0),
        new("atom-violation", "p(wrong).", "integer", 1),
        new("fixnum-widening-violation", "p(576460752303423488).", "integer", 1),
        new("big-integer-violation", "p(9223372036854775808).", "integer", 1),
        new("rational-violation", "p(1r2).", "integer", 1),
        new("improper-list-violation", "p([a|b]).", "list(atom)", 1),
        new("nested-list-element-violation", "p([[a],[1]]).", "list(list(atom))", 1),
        new("leading-rule-unknown", "p(wrong) :- true.", "integer", 0),
        new("later-fact-unknown", "p(1). p(wrong).", "integer", 0),
        new("variable-output-unknown", "p(X).", "integer", 0),
        new("open-list-unknown", "p([a|X]).", "list(atom)", 0),
        new("quoted-output-unknown", "p(\"text\").", "integer", 0),
        new("dynamic-target-unknown", ":- dynamic(p/1). p(wrong).", "integer", 0),
        new("multifile-target-unknown", ":- multifile(p/1). p(wrong).", "integer", 0),
        new("initialization-unknown", ":- initialization(halt(9)). p(wrong).", "integer", 0),
    ];

    [Fact]
    public void VersionedCorpusProducesOnlyIndependentlyExpectedFindings()
    {
        Assert.Equal(1, CorpusVersion);
        foreach (CorpusCase test in Cases)
        {
            IReadOnlyList<Diagnostic> diagnostics = Analyze(test.Source, test.Type);
            Assert.Equal(
                test.ExpectedFindings,
                diagnostics.Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ContractOutputTypeMismatch)
            );
        }
    }

    [Fact]
    public void EverySeededViolationIsDetected()
    {
        CorpusCase[] seeded = [.. Cases.Where(test => test.ExpectedFindings > 0)];
        Assert.NotEmpty(seeded);
        Assert.All(
            seeded,
            test =>
                Assert.Equal(
                    test.ExpectedFindings,
                    Analyze(test.Source, test.Type)
                        .Count(diagnostic => diagnostic.Id == LintDiagnosticIds.ContractOutputTypeMismatch)
                )
        );
    }

    [Fact]
    public void EveryRepositoryContractIsFreeOfLeadingFactOutputViolations()
    {
        var root = RepositoryRoot();
        string[] contracts = Directory.GetFiles(root, "*.dpli", SearchOption.AllDirectories);
        Assert.NotEmpty(contracts);
        foreach (var contractPath in contracts)
        {
            var sourcePath = Path.ChangeExtension(contractPath, ".pl");
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            ContractReadResult contract = ContractReader.Read(File.ReadAllText(contractPath), "Corpus", contractPath);
            Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
                sourcePath,
                File.ReadAllText(sourcePath),
                new BytecodeProgram()
            );
            IReadOnlyList<Diagnostic> diagnostics = PrologContractLinter.Analyze(
                [loaded.Source],
                contract.Contract!,
                contractPath,
                Enabled
            );
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == LintDiagnosticIds.ContractOutputTypeMismatch);
        }
    }

    private static IReadOnlyList<Diagnostic> Analyze(string clauses, string type)
    {
        var source = $":- module(m, [p/1]). {clauses}";
        var path = Path.Combine(Path.GetTempPath(), "corpus.pl");
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(path, source, new BytecodeProgram());
        ContractReadResult contract = ContractReader.Read(
            $":- clr_module('M'). :- clr_export(p/1, det, [out(value, {type})]).",
            "Corpus",
            "corpus.dpli"
        );
        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));
        return PrologContractLinter.Analyze([loaded.Source], contract.Contract!, "corpus.dpli", Enabled);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotProlog.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed record CorpusCase(string Name, string Source, string Type, int ExpectedFindings);
}
