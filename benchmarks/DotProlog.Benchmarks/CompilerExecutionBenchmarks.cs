using System.Runtime.Loader;
using BenchmarkDotNet.Attributes;
using DotProlog.CodeGen.IL;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Compares warmed direct-IL predicates with consulted bytecode using identical goals.</summary>
/// <remarks>Assembly loading is JIT benchmark setup only; it is excluded from timing and production AOT paths.</remarks>
[MemoryDiagnoser]
public class CompilerExecutionBenchmarks
{
    private AssemblyLoadContext _context = null!;
    private PrologEngine _bytecode = null!;
    private PrologEngine _compiled = null!;
    private int _bytecodeGoal;
    private int _compiledGoal;

    [Params("Reverse30", "Countdown10000", "FactScan20")]
    public string Workload { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var (source, goal) = Workload switch
        {
            "Reverse30" => (BenchmarkPrograms.NaiveReverse, "mklist(30, L), nrev(L, _)"),
            "Countdown10000" => (BenchmarkPrograms.Countdown, "count(10000)"),
            "FactScan20" => (BenchmarkPrograms.FactTable, "find(_)"),
            _ => throw new InvalidOperationException("Unknown benchmark workload."),
        };
        _bytecode = new PrologEngine { Output = TextWriter.Null };
        _bytecode.ConsultOrThrow(source, "execution.pl");
        _compiled = new PrologEngine { Output = TextWriter.Null };
        using var output = new MemoryStream();
        CompilerBenchmarkSource.Check(IlAssemblyEmitter.Emit([("execution.pl", source)], "ExecutionBenchmark", output));
        output.Position = 0;
        _context = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = _context.LoadFromStream(output);
        var install = assembly
            .GetType(IlAssemblyEmitter.ProgramTypeName)!
            .GetMethod("Install")!
            .CreateDelegate<Func<PrologEngine, int[]>>();
        install(_compiled);
        _bytecodeGoal = CompileGoal(_bytecode, goal);
        _compiledGoal = CompileGoal(_compiled, goal);
        if (Bytecode() != RunResult.Success || DirectIl() != RunResult.Success)
        {
            throw new InvalidOperationException("Benchmark goal must succeed on both execution paths.");
        }
    }

    [Benchmark(Baseline = true)]
    public RunResult Bytecode() => _bytecode.Machine.Run(_bytecodeGoal);

    [Benchmark]
    public RunResult DirectIl() => _compiled.Machine.Run(_compiledGoal);

    [GlobalCleanup]
    public void Cleanup() => _context.Unload();

    private static int CompileGoal(PrologEngine engine, string goal)
    {
        var address = engine.CompileGoal(goal, out var diagnostics);
        CompilerBenchmarkSource.Check(diagnostics);
        if (address < 0)
        {
            throw new InvalidOperationException("Benchmark goal produced no entry point.");
        }
        return address;
    }
}
