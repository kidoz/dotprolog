using BenchmarkDotNet.Attributes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>
/// Allocation evidence for C# 14 collection-expression-to-span calls and the typed embedding
/// surface that builds equivalent argument cells.
/// </summary>
[MemoryDiagnoser]
public class HostCallBenchmarks : IDisposable
{
    private PrologEngine _engine = null!;
    private Machine Machine => _engine.Machine;
    private PrologHost _host = null!;
    private PrologPredicate _predicate;
    private int _pairFunctor;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new PrologEngine { Output = TextWriter.Null };
        _engine.ConsultOrThrow("pair(1, 2).", "host-call-benchmark.pl");

        _host = new PrologHost(Machine);
        _predicate = _host.Bind("pair", 2);
        _pairFunctor = _predicate.FunctorId;
    }

    [Benchmark(Description = "Machine.Call with a two-cell collection expression")]
    public RunResult MachineSpanCall()
    {
        Machine.BeginCall();
        return Machine.Call(_pairFunctor, [Cell.Integer60(1), Cell.Integer60(2)]);
    }

    [Benchmark(Description = "PrologHost.Prove with two typed inputs")]
    public bool TypedHostCall() => _host.Prove(_predicate, PrologInput.Integer(1), PrologInput.Integer(2));

    [Benchmark(Description = "CreateStructure with a two-cell collection expression")]
    public Cell SmallStructureConstruction()
    {
        Machine.BeginCall();
        return Machine.CreateStructure(_pairFunctor, [Cell.Integer60(1), Cell.Integer60(2)]);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _engine?.Dispose();
    }
}
