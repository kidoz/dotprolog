using BenchmarkDotNet.Attributes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Includes list construction and sorting, with atomic and compound keys.</summary>
[MemoryDiagnoser]
public class SortingBenchmarks : IDisposable
{
    private PrologEngine _engine = null!;
    private int _sort;
    private int _keysort;

    [Params(32, 1024)]
    public int Count { get; set; }

    [Params(false, true)]
    public bool CompoundKeys { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = new PrologEngine { Output = TextWriter.Null };
        _engine.ConsultOrThrow(
            """
            items(0, [], []) :- !.
            items(N, [K|Xs], [K-N|Ps]) :-
                N > 0, M is N-1, V is (N*7919) mod 257,
                key(V, K), items(M, Xs, Ps).
            """
                + "\n"
                + (CompoundKeys ? "key(V, f(V))." : "key(V, V)."),
            "sorting.pl"
        );
        _sort = _engine.CompileGoal($"items({Count}, Xs, _), sort(Xs, _)", out var diagnostics);
        CompilerBenchmarkSource.Check(diagnostics);
        _keysort = _engine.CompileGoal($"items({Count}, _, Ps), keysort(Ps, _)", out diagnostics);
        CompilerBenchmarkSource.Check(diagnostics);
        if (Sort() != RunResult.Success || KeySort() != RunResult.Success)
            throw new InvalidOperationException("Sorting fixture must succeed.");
    }

    [Benchmark]
    public RunResult Sort() => _engine.Machine.Run(_sort);

    [Benchmark]
    public RunResult KeySort() => _engine.Machine.Run(_keysort);

    [GlobalCleanup]
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            _engine?.Dispose();
    }
}
