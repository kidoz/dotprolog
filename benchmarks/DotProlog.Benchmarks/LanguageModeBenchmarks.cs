using BenchmarkDotNet.Attributes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Benchmarks;

/// <summary>
/// What selecting a language mode costs, and what the load-unit scope of <c>double_quotes</c> costs.
/// </summary>
/// <remarks>
/// <para>
/// Modern mode differs from Extended only in the value <c>double_quotes</c> starts at, so these
/// benchmarks are watching for two things: that seeding the flag does not change what engine
/// construction costs, and that reading text as chars is not slower than reading it as codes.
/// Both paths go through <c>TermNormalizer</c>, which builds a list either way — a list of atoms
/// rather than a list of integers.
/// </para>
/// <para>
/// A term compiled from a string literal is the same shape in both modes, so a difference here
/// would be in interning one-character atoms rather than in list construction.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class LanguageModeBenchmarks
{
    private const string StringHeavySource = """
        greet("hello, world").
        digits("0123456789").
        letters("abcdefghijklmnopqrstuvwxyz").
        sentence("the quick brown fox jumps over the lazy dog").
        punctuation(".,;:!?").
        """;

    /// <summary>Twenty separate load units, which is what the scope restore is paid per.</summary>
    private static readonly string[] ManyUnits = [.. Enumerable.Range(0, 20).Select(index => $"unit{index}(\"text {index}\").")];

    [Params(PrologLanguageMode.Extended, PrologLanguageMode.Modern)]
    public PrologLanguageMode Mode { get; set; }

    [Benchmark(Description = "Construct an engine in the mode")]
    public PrologEngine Construct() => new(Mode) { Output = TextWriter.Null };

    [Benchmark(Description = "Consult a string-heavy source")]
    public bool ConsultStringHeavy()
    {
        var engine = new PrologEngine(Mode) { Output = TextWriter.Null };
        return engine.ConsultText(StringHeavySource, "bench.pl").Success;
    }

    [Benchmark(Description = "Consult 20 separate load units")]
    public bool ConsultManyUnits()
    {
        var engine = new PrologEngine(Mode) { Output = TextWriter.Null };
        bool success = true;
        for (int index = 0; index < ManyUnits.Length; index++)
        {
            success &= engine.ConsultText(ManyUnits[index], $"unit{index}.pl").Success;
        }

        return success;
    }
}
