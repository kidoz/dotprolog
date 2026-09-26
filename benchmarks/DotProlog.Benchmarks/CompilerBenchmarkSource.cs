using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Benchmarks;

internal static class CompilerBenchmarkSource
{
    internal static string Create(int repetitions) =>
        string.Join(
            "\n",
            Enumerable
                .Range(0, repetitions)
                .Select(index =>
                    BenchmarkPrograms
                        .NaiveReverse.Replace("app(", $"app{index}(", StringComparison.Ordinal)
                        .Replace("nrev(", $"nrev{index}(", StringComparison.Ordinal)
                        .Replace("mklist(", $"mklist{index}(", StringComparison.Ordinal)
                )
        );

    internal static CompiledProgramModel Lower(IReadOnlyList<(string Name, string Text)> sources)
    {
        var model = CompiledProgramBuilder.Compile(
            sources,
            [],
            PrologLanguageMode.Modern,
            PrologFlagOverrides.None,
            out var diagnostics,
            indexFirstArgument: true
        );
        Check(diagnostics);
        return model ?? throw new InvalidOperationException("Benchmark source produced no model.");
    }

    internal static void Check(IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException($"Benchmark compilation failed: {string.Join("; ", diagnostics)}");
        }
    }
}
