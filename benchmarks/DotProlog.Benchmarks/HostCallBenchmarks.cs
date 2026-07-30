using BenchmarkDotNet.Attributes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>
/// Allocation evidence for C# 14 collection-expression-to-span calls and the typed embedding
/// surface that builds equivalent argument cells.
/// </summary>
[MemoryDiagnoser]
public class HostCallBenchmarks
{
    private Machine _machine = null!;
    private PrologHost _host = null!;
    private PrologPredicate _predicate;
    private int _pairFunctor;

    [GlobalSetup]
    public void Setup()
    {
        var engine = new PrologEngine { Output = TextWriter.Null };
        engine.ConsultOrThrow("pair(1, 2).", "host-call-benchmark.pl");

        _machine = engine.Machine;
        _host = new PrologHost(_machine);
        _predicate = _host.Bind("pair", 2);
        _pairFunctor = _predicate.FunctorId;
    }

    [Benchmark(Description = "Machine.Call with a two-cell collection expression")]
    public RunResult MachineSpanCall()
    {
        _machine.BeginCall();
        return _machine.Call(_pairFunctor, [Cell.Integer60(1), Cell.Integer60(2)]);
    }

    [Benchmark(Description = "PrologHost.Prove with two typed inputs")]
    public bool TypedHostCall() => _host.Prove(_predicate, PrologInput.Integer(1), PrologInput.Integer(2));

    [Benchmark(Description = "CreateStructure with a two-cell collection expression")]
    public Cell SmallStructureConstruction()
    {
        _machine.BeginCall();
        return _machine.CreateStructure(_pairFunctor, [Cell.Integer60(1), Cell.Integer60(2)]);
    }
}
