using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using DotProlog.CodeGen.IL;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>
/// What it costs to get an engine ready to run a goal.
/// </summary>
/// <remarks>
/// Construction compiles the bootstrap and standard libraries, so this is the floor under a console
/// application's startup and is paid again for every test, which each get a fresh engine. It is the
/// number to watch when a predicate moves from the runtime into the Prolog-level library.
/// <para>
/// BenchmarkDotNet measures warmed construction. For first use, launch this executable in a fresh
/// process with <c>--measure-startup Engine 1 32 128</c> or
/// <c>--measure-startup CompileToIl 20 32 128</c>. These collect one first operation, then 32 unmeasured
/// warmups and 128 measured repetitions. CSV timing excludes process launch, source preparation and
/// output, includes operation-specific JIT work, and uses the process's existing runtime settings.
/// Managed bytes are current-thread allocations; collection counts are process-wide. No collection
/// is forced between operations. These observations complement the steady-state benchmarks.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    // Run once per fresh process. Source preparation and CSV output are outside timing;
    // the first operation includes its JIT/static initialization, but not process launch.
    internal static int Measure(ReadOnlySpan<string> args)
    {
        if (
            args.Length != 4
            || args[0] is not ("Engine" or "CompileToIl")
            || !int.TryParse(args[1], CultureInfo.InvariantCulture, out var repetitions)
            || repetitions is not (1 or 20 or 100)
            || (args[0] == "Engine" && repetitions != 1)
            || !int.TryParse(args[2], CultureInfo.InvariantCulture, out var warmups)
            || warmups < 0
            || !int.TryParse(args[3], CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0
            || iterations == int.MaxValue
        )
        {
            Console.Error.WriteLine("Usage: --measure-startup Engine|CompileToIl repetitions(1|20|100) warmups iterations");
            Console.Error.WriteLine(
                "Engine requires repetitions=1. Run each case in a fresh process for first-use measurements."
            );
            return 2;
        }

        var compile = args[0] == "CompileToIl";
        (string Name, string Text)[] sources = compile ? [("pipeline.pl", CompilerBenchmarkSource.Create(repetitions))] : [];
        var samples = new (long Ticks, long Bytes, int Gen0, int Gen1, int Gen2, long Output)[iterations + 1];
        _ = Stopwatch.GetTimestamp();
        _ = GC.GetAllocatedBytesForCurrentThread();
        samples[0] = Sample(compile, sources);
        for (var iteration = 0; iteration < warmups; iteration++)
        {
            Execute(compile, sources);
        }
        for (var iteration = 1; iteration < samples.Length; iteration++)
        {
            samples[iteration] = Sample(compile, sources);
            if (samples[iteration].Output != samples[0].Output)
            {
                throw new InvalidOperationException("Startup workload output changed between invocations.");
            }
        }

        Console.WriteLine("Workload,Repetitions,Phase,Iteration,Microseconds,ManagedBytes,Gen0,Gen1,Gen2,OutputUnits");
        for (var iteration = 0; iteration < samples.Length; iteration++)
        {
            var sample = samples[iteration];
            string phase = iteration == 0 ? "First" : "Warm";
            double microseconds = sample.Ticks * 1_000_000.0 / Stopwatch.Frequency;
            Console.WriteLine(
                FormattableString.Invariant(
                    $"{args[0]},{repetitions},{phase},{iteration},{microseconds:F3},{sample.Bytes},{sample.Gen0},{sample.Gen1},{sample.Gen2},{sample.Output}"
                )
            );
        }
        return 0;
    }

    private static (long Ticks, long Bytes, int Gen0, int Gen1, int Gen2, long Output) Sample(
        bool compile,
        (string Name, string Text)[] sources
    )
    {
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        long output = Execute(compile, sources);
        long elapsed = Stopwatch.GetTimestamp() - start;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return (elapsed, bytes, GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, output);
    }

    // Keep first-use JIT work inside Sample's clock. Output units are bytecode instruction
    // count for Engine and PE byte count for CompileToIl; these check workload consistency.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Execute(bool compile, (string Name, string Text)[] sources)
    {
        if (!compile)
        {
            return new PrologEngine().Program.CodeLength;
        }
        using var output = new MemoryStream();
        CompilerBenchmarkSource.Check(IlAssemblyEmitter.Emit(sources, "PipelineBenchmark", output));
        return output.Length;
    }

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
