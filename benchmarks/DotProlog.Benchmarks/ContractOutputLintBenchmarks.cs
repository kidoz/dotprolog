using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using DotProlog.CodeGen.CSharp;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Benchmarks;

/// <summary>Measures the future SDK promotion candidate against the unchanged facade-generation path.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 5, iterationCount: 30)]
public class ContractOutputLintBenchmarks
{
    private const string SourceName = "contract-benchmark.pl";
    private const string ContractName = "contract-benchmark.dpli";
    private static readonly PrologLintOptions Enabled = PrologLintOptions.SemanticOnly with
    {
        EnabledRules = [LintDiagnosticIds.ContractOutputTypeMismatch],
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
        for (var predicate = 0; predicate < 1_000; predicate++)
        {
            exports.Append(":- export([p").Append(predicate).Append("/1]).\n");
            contract.Append(":- clr_export(p").Append(predicate).Append("/1, det, [out(value, integer)]).\n");
            for (var clause = 0; clause < 10; clause++)
            {
                clauses.Append('p').Append(predicate).Append('(').Append(clause).Append(").\n");
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
    }

    [Benchmark(Baseline = true)]
    public int FacadeOnly() => FacadeGenerator.Generate(_contract, _source, SourceName).Length;

    [Benchmark]
    public int FacadeAndContractLint()
    {
        var length = FacadeGenerator.Generate(_contract, _source, SourceName).Length;
        return length + PrologContractLinter.Analyze(_sources, _contract, ContractName, Enabled).Count;
    }
}
