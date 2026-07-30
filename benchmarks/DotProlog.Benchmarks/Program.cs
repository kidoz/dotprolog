using System.Reflection;
using BenchmarkDotNet.Running;

namespace DotProlog.Benchmarks;

/// <summary>Entry point for <c>dotnet run -c Release --project benchmarks/DotProlog.Benchmarks</c>.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--aot-linq")
        {
            return AotLinqComparison.Run(args.AsSpan(1));
        }

        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
        return 0;
    }
}
