using BenchmarkDotNet.Attributes;
using Prolog.Compiler;
using Prolog.Runtime;

namespace Prolog.Benchmarks;

/// <summary>
/// Engine throughput: structure building and deep recursion (naive reverse), deterministic tail
/// calls (countdown), and choice-point churn (fact-table scan).
/// </summary>
[MemoryDiagnoser]
public class EngineBenchmarks
{
    private PrologEngine _reverseEngine = null!;
    private PrologEngine _countdownEngine = null!;
    private PrologEngine _backtrackEngine = null!;
    private int _reverseGoal;
    private int _countdownGoal;
    private int _backtrackGoal;

    [Params(30)]
    public int ListLength { get; set; }

    [Params(10_000)]
    public int CountdownIterations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _reverseEngine = NewEngine(BenchmarkPrograms.NaiveReverse);
        _reverseGoal = CompileGoal(_reverseEngine, $"mklist({ListLength}, L), nrev(L, _)");

        _countdownEngine = NewEngine(BenchmarkPrograms.Countdown);
        _countdownGoal = CompileGoal(_countdownEngine, $"count({CountdownIterations})");

        _backtrackEngine = NewEngine(BenchmarkPrograms.FactTable);
        _backtrackGoal = CompileGoal(_backtrackEngine, "find(_)");
    }

    [Benchmark]
    public RunResult NaiveReverse() => _reverseEngine.Machine.Run(_reverseGoal);

    [Benchmark]
    public RunResult Countdown() => _countdownEngine.Machine.Run(_countdownGoal);

    [Benchmark]
    public RunResult BacktrackThroughFacts() => _backtrackEngine.Machine.Run(_backtrackGoal);

    private static PrologEngine NewEngine(string source)
    {
        var engine = new PrologEngine { Output = TextWriter.Null };
        LoadResult loaded = engine.ConsultText(source);
        if (!loaded.Success)
        {
            throw new InvalidOperationException($"Benchmark program failed to compile: {string.Join("; ", loaded.Diagnostics)}");
        }

        return engine;
    }

    private static int CompileGoal(PrologEngine engine, string goal)
    {
        int address = engine.CompileGoal(goal, out IReadOnlyList<Syntax.Diagnostic> diagnostics);
        if (address < 0)
        {
            throw new InvalidOperationException($"Benchmark goal failed to compile: {string.Join("; ", diagnostics)}");
        }

        return address;
    }
}
