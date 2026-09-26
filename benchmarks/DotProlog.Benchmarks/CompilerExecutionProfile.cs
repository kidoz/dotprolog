using System.Globalization;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Runs a fixed workload for an external sampling profiler, separately from benchmark timings.</summary>
internal static class CompilerExecutionProfile
{
    internal static int Run(ReadOnlySpan<string> args)
    {
        if (args.Length != 3 || !int.TryParse(args[2], CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
        {
            Console.Error.WriteLine(
                "Usage: --profile-execution Bytecode|LinearBytecode|DirectIl|InstructionIl|LinearIl|TableIl Reverse30|Countdown10000|FactScan20|FactHit20|FactMiss20 iterations"
            );
            return 2;
        }
        var benchmark = new CompilerExecutionBenchmarks { Workload = args[1] };
        Func<RunResult>? execute = args[0] switch
        {
            "Bytecode" => benchmark.Bytecode,
            "LinearBytecode" => benchmark.LinearBytecode,
            "DirectIl" => benchmark.DirectIl,
            "InstructionIl" => benchmark.InstructionIl,
            "LinearIl" => benchmark.LinearIl,
            "TableIl" => benchmark.TableIl,
            _ => null,
        };
        if (execute is null)
        {
            Console.Error.WriteLine("Unknown execution path.");
            return 2;
        }
        benchmark.Setup();
        try
        {
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                if (execute() != RunResult.Success)
                {
                    throw new InvalidOperationException("Profiled goal failed.");
                }
            }
            Console.WriteLine(FormattableString.Invariant($"{args[0]} {args[1]}: {iterations} successful runs"));
            return 0;
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
