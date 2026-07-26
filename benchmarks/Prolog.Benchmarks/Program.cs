using System.Reflection;
using BenchmarkDotNet.Running;

namespace Prolog.Benchmarks;

/// <summary>Entry point for <c>dotnet run -c Release --project benchmarks/Prolog.Benchmarks</c>.</summary>
internal static class Program
{
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
