using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using DotProlog.CodeGen.CSharp;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Benchmarks;

/// <summary>Measures opt-in guarded-rule analysis against facade generation with default contract linting.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 5, iterationCount: 30)]
public class GroundRuleContractOutputLintBenchmarks
{
    private const string SourceName = "ground-rule-contract-benchmark.pl";
    private const string ContractName = "ground-rule-contract-benchmark.dpli";
    private static readonly PrologLintOptions GroundRulesEnabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.GroundRuleContractOutputTypeMismatch],
    };

    private string _source = string.Empty;
    private ModuleContract _contract = null!;
    private PrologLintSource[] _sources = [];

    [GlobalSetup]
    public void Setup()
    {
        var exports = new StringBuilder();
        var clauses = new StringBuilder();
        var contract = new StringBuilder(":- clr_module('Benchmark').\n");
        string[] bodies = ["true", "a = b", "a = b", "a = a", "true", "true", "a = b", "a = a", "a = b", "true, a = a"];
        int[] inputs = [0, 0, 1, 1, 2, 2, 3, 4, 4, 5];
        for (var predicate = 0; predicate < 1_000; predicate++)
        {
            exports.Append(":- export([p").Append(predicate).Append("/2]).\n");
            contract.Append(":- clr_export(p").Append(predicate).Append("/2, det, [in(item, integer), out(value, integer)]).\n");
            for (var clause = 0; clause < 10; clause++)
            {
                clauses
                    .Append('p')
                    .Append(predicate)
                    .Append('(')
                    .Append(inputs[clause])
                    .Append(", ")
                    .Append(clause)
                    .Append(") :- ")
                    .Append(bodies[clause])
                    .Append(".\n");
            }
        }

        _source = $":- module(benchmark, []).\n{exports}{clauses}";
        ContractReadResult read = ContractReader.Read(contract.ToString(), "Benchmark", ContractName);
        if (!read.Success)
        {
            throw new InvalidOperationException(string.Join("; ", read.Diagnostics));
        }

        _contract = read.Contract!;
        _sources = [PrologLintSourceLoader.Load(SourceName, _source, new BytecodeProgram()).Source];
        _ = FacadeGenerator.Generate(_contract, _source, SourceName);
        if (PrologContractLinter.Analyze(_sources, _contract, ContractName, GroundRulesEnabled).Count != 0)
        {
            throw new InvalidOperationException("The compatible guarded-rule workload produced a diagnostic.");
        }

        string validationText = _source
            .Replace("p0(0, 0) :- true.", "p0(0, wrong) :- true.", StringComparison.Ordinal)
            .Replace("p0(5, 9) :- true, a = a.", "p0(5, wrong) :- true, a = a.", StringComparison.Ordinal);
        PrologLintSource[] validationSources =
        [
            PrologLintSourceLoader.Load(SourceName, validationText, new BytecodeProgram()).Source,
        ];
        IReadOnlyList<Diagnostic> validation = PrologContractLinter.Analyze(
            validationSources,
            _contract,
            ContractName,
            GroundRulesEnabled
        );
        if (
            validation.Count != 2
            || validation.Any(diagnostic => diagnostic.Id != LintDiagnosticIds.GroundRuleContractOutputTypeMismatch)
        )
        {
            throw new InvalidOperationException(
                $"Expected two guarded-rule validation findings, got {validation.Count}: "
                    + string.Join("; ", validation.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"))
            );
        }
    }

    [Benchmark(Baseline = true)]
    public int FacadeAndDefaultContractLint()
    {
        var length = FacadeGenerator.Generate(_contract, _source, SourceName).Length;
        return length + PrologContractLinter.Analyze(_sources, _contract, ContractName, PrologLintOptions.SemanticOnly).Count;
    }

    [Benchmark]
    public int FacadeAndGroundRuleContractLint()
    {
        var length = FacadeGenerator.Generate(_contract, _source, SourceName).Length;
        return length + PrologContractLinter.Analyze(_sources, _contract, ContractName, GroundRulesEnabled).Count;
    }
}
