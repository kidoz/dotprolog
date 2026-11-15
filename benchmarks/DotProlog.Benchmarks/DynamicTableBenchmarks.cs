using BenchmarkDotNet.Attributes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Bound lookup and complete redo over a large live dynamic predicate.</summary>
[MemoryDiagnoser]
public class DynamicTableBenchmarks : IDisposable
{
    private PrologEngine _engine = null!;
    private int _hitGoal;
    private int _missGoal;
    private int _redoGoal;

    [Params(20, 4096)]
    public int Clauses { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var source = ":- dynamic item/1.\n" + string.Join('\n', Enumerable.Range(0, Clauses).Select(index => $"item({index})."));
        _engine = new PrologEngine { Input = TextReader.Null, Output = TextWriter.Null };
        _engine.ConsultOrThrow(source, "dynamic-table.pl");
        _hitGoal = CompileGoal($"item({Clauses - 1})");
        _missGoal = CompileGoal($"item({Clauses})");
        _redoGoal = CompileGoal("item(_)");
        if (Hit() != RunResult.Success || Miss() != RunResult.Failure || Redo() != Clauses)
        {
            throw new InvalidOperationException("Dynamic-table workloads returned unexpected results.");
        }
    }

    private int CompileGoal(string goal)
    {
        var address = _engine.CompileGoal(goal, out var diagnostics);
        if (address < 0)
        {
            throw new InvalidOperationException($"Benchmark goal failed to compile: {string.Join("; ", diagnostics)}");
        }

        return address;
    }

    [Benchmark]
    public RunResult Hit() => _engine.Machine.Run(_hitGoal);

    [Benchmark]
    public RunResult Miss() => _engine.Machine.Run(_missGoal);

    [Benchmark]
    public int Redo()
    {
        var machine = _engine.Machine;
        if (machine.Run(_redoGoal) != RunResult.Success)
        {
            throw new InvalidOperationException("Dynamic-table redo must produce at least one solution.");
        }

        var solutions = 1;
        while (machine.Redo() == RunResult.Success)
        {
            solutions++;
        }

        return solutions;
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
        if (disposing)
        {
            _engine?.Dispose();
        }
    }
}
