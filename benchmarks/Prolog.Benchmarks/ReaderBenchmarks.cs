using BenchmarkDotNet.Attributes;
using Prolog.Compiler;
using Prolog.Syntax;

namespace Prolog.Benchmarks;

/// <summary>Front-end cost: reading source into terms, and lowering those terms to bytecode.</summary>
[MemoryDiagnoser]
public class ReaderBenchmarks
{
    private string _source = string.Empty;

    [Params(1, 20)]
    public int Repetitions { get; set; }

    [GlobalSetup]
    public void Setup() => _source = string.Join("\n", Enumerable.Range(0, Repetitions).Select(CopyWithSuffix));

    /// <summary>Renames the predicates so each copy is a distinct predicate rather than extra clauses.</summary>
    private static string CopyWithSuffix(int index) =>
        BenchmarkPrograms
            .NaiveReverse.Replace("app(", $"app{index}(", StringComparison.Ordinal)
            .Replace("nrev(", $"nrev{index}(", StringComparison.Ordinal)
            .Replace("mklist(", $"mklist{index}(", StringComparison.Ordinal);

    [Benchmark]
    public int ReadProgram() => TermReader.ReadProgram(_source).Clauses.Count;

    [Benchmark]
    public bool Consult() => new PrologEngine().ConsultText(_source).Success;
}
