using BenchmarkDotNet.Attributes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>Scaling of successive runtime term reads from an in-memory text stream.</summary>
[MemoryDiagnoser]
public class StreamTermReadingBenchmarks : IDisposable
{
    private PrologEngine _engine = null!;
    private string _input = string.Empty;
    private int _goal;

    [Params(16, 256, 4096)]
    public int Terms { get; set; }

    [Params("Atoms", "Compounds")]
    public string TermShape { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _input = string.Join(
            '\n',
            Enumerable
                .Range(0, Terms)
                .Select(index => TermShape == "Atoms" ? "payload." : $"item({index}, payload, [{index}, {index + 1}]).")
        );
        _engine = new PrologEngine { Input = TextReader.Null, Output = TextWriter.Null };
        _engine.ConsultOrThrow(
            """
            read_terms(0).
            read_terms(N) :-
                N > 0,
                read_term(T, []),
                T \== end_of_file,
                M is N - 1,
                read_terms(M).
            """,
            "stream-term-reading.pl"
        );
        _goal = _engine.CompileGoal($"read_terms({Terms})", out var diagnostics);
        if (_goal < 0)
        {
            throw new InvalidOperationException($"Benchmark goal failed to compile: {string.Join("; ", diagnostics)}");
        }

        ResetInput();
        if (ReadTerms() != RunResult.Success)
        {
            throw new InvalidOperationException("Stream term reading did not consume the complete workload.");
        }
    }

    [IterationSetup]
    public void ResetInput() => _engine.Input = new StringReader(_input);

    [Benchmark]
    public RunResult ReadTerms() => _engine.Machine.Run(_goal);

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
