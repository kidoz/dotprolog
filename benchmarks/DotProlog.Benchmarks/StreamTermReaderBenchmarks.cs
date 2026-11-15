using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Incremental stream input cost for one large multiline quoted term.</summary>
[MemoryDiagnoser]
public class StreamTermReaderBenchmarks
{
    private string _source = string.Empty;

    [Params(1024, 2048, 4096, 8192)]
    public int Lines { get; set; }

    [GlobalSetup]
    public void Setup() => _source = "'" + string.Concat(Enumerable.Repeat("value\\\n", Lines)) + "'. trailing.";

    [Benchmark]
    public int ReadTerm()
    {
        using var engine = new PrologEngine { Input = TextReader.Null };
        return ReadTerm(engine, _source);
    }

    private static int ReadTerm(PrologEngine engine, string source)
    {
        var reader = new StringReader(source);
        var buffer = string.Empty;
        return engine.TryReadTerm(engine.Machine, reader, ref buffer, out var _, out var _, out var _, out var _)
            ? buffer.Length
            : -1;
    }

    internal static void Measure()
    {
        foreach (var lines in (int[])[1024, 2048, 4096, 8192])
        {
            var benchmark = new StreamTermReaderBenchmarks { Lines = lines };
            benchmark.Setup();
            using var engine = new PrologEngine { Input = TextReader.Null };
            for (var warmup = 0; warmup < 3; warmup++)
            {
                _ = ReadTerm(engine, benchmark._source);
            }

            const int iterations = 10;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var elapsed = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                _ = ReadTerm(engine, benchmark._source);
            }

            elapsed.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Console.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} lines: {1:F3} ms/read, {2:N0} allocated bytes/read",
                    lines,
                    elapsed.Elapsed.TotalMilliseconds / iterations,
                    (double)allocated / iterations
                )
            );
        }
    }
}
