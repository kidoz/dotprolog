using BenchmarkDotNet.Attributes;
using Prolog.Compiler;
using Prolog.Runtime;

namespace Prolog.Benchmarks;

/// <summary>
/// What it costs to get an engine ready to run a goal.
/// </summary>
/// <remarks>
/// Construction compiles the bootstrap and standard libraries, so this is the floor under a console
/// application's startup and is paid again for every test, which each get a fresh engine. It is the
/// number to watch when a predicate moves from the runtime into the Prolog-level library.
/// </remarks>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    [Benchmark(Description = "new PrologEngine(), which compiles both libraries")]
    public PrologEngine Construct() => new() { Output = TextWriter.Null };

    [Benchmark(Description = "Construct, then consult and run a goal")]
    public RunResult ConstructAndRun()
    {
        var engine = new PrologEngine { Output = TextWriter.Null };
        engine.ConsultOrThrow("greeting('Hello! World!').", "bench.pl");
        return engine.RunGoal("greeting(_)", out _);
    }
}
