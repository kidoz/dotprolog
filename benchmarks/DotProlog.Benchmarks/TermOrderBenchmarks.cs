using BenchmarkDotNet.Attributes;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Warmed comparison costs, including iterative and cyclic compound traversal.</summary>
[MemoryDiagnoser]
public class TermOrderBenchmarks : IDisposable
{
    private Machine _machine = null!;
    private Cell _left;
    private Cell _right;

    [Params("Integer", "Atom", "Compound", "Cycle")]
    public string Kind { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _machine = new Machine(new BytecodeProgram());
        _left = Cell.Integer60(1);
        _right = Cell.Integer60(2);
        if (Kind == "Atom")
        {
            _left = Cell.Atom(_machine.Symbols.InternAtom("alpha"));
            _right = Cell.Atom(_machine.Symbols.InternAtom("beta"));
        }
        else if (Kind is "Compound" or "Cycle")
        {
            var functor = _machine.Symbols.InternFunctor("f", 2);
            Cell a = _machine.CreateVariable();
            Cell b = _machine.CreateVariable();
            _left = _machine.CreateStructure(functor, [a, _left]);
            _right = _machine.CreateStructure(functor, [b, _right]);
            _machine.Unify(a, Kind == "Cycle" ? _left : Cell.Integer60(0));
            _machine.Unify(b, Kind == "Cycle" ? _right : Cell.Integer60(0));
        }
        if (Compare() >= 0)
            throw new InvalidOperationException("Comparison fixture must be ordered.");
    }

    [Benchmark]
    public int Compare() => TermOrder.Compare(_machine, _left, _right);

    [GlobalCleanup]
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            _machine?.Dispose();
    }
}
