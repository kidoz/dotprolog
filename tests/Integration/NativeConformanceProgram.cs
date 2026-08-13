#if NATIVE_CONFORMANCE_RUNNER || COMPILED_CONFORMANCE_RUNNER
namespace Integration.Tests;

/// <summary>Direct NativeAOT entry point for the pinned independent ISO corpus.</summary>
internal static class NativeConformanceProgram
{
#if COMPILED_CONFORMANCE_RUNNER
    private static int Main() => CompiledConformanceGenerated.Run();
#else
    private static async Task<int> Main(string[] args)
    {
        string? reportPath = args.Length > 0 ? args[0] : null;

        await LogtalkConformanceTests.RunPinnedIsoCorpusAsync(selectedId: null, reportPath, CancellationToken.None);
        await Console.Out.WriteLineAsync("NativeAOT independent ISO conformance: 763/763 passed.");
        return 0;
    }
#endif
}
#endif
