using BenchmarkDotNet.Attributes;
using DotProlog.CodeGen.IL;
using DotProlog.Compiler;
using DotProlog.Syntax;

namespace DotProlog.Benchmarks;

/// <summary>Separates front-end lowering from direct IL emission; all output stays in memory.</summary>
[MemoryDiagnoser]
public class CompilerPipelineBenchmarks
{
    private string _source = string.Empty;
    private (string Name, string Text)[] _sources = [];
    private CompiledProgramModel _model = null!;

    [Params(1, 20, 100)]
    public int Repetitions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = CompilerBenchmarkSource.Create(Repetitions);
        _sources = [("pipeline.pl", _source)];
        _model = CompilerBenchmarkSource.Lower(_sources);
    }

    [Benchmark]
    public int Parse() => TermReader.ReadProgram(_source).Clauses.Count;

    [Benchmark]
    public int Lower() => CompilerBenchmarkSource.Lower(_sources).Instructions.Count;

    [Benchmark]
    public long EmitIl()
    {
        using var output = new MemoryStream();
        IlAssemblyEmitter.WriteAssembly(_model, "PipelineBenchmark", output);
        return output.Length;
    }

    [Benchmark]
    public long CompileToIl()
    {
        using var output = new MemoryStream();
        CompilerBenchmarkSource.Check(IlAssemblyEmitter.Emit(_sources, "PipelineBenchmark", output));
        return output.Length;
    }
}
