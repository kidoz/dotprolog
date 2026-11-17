#if NATIVE_CONFORMANCE_RUNNER || COMPILED_CONFORMANCE_RUNNER
using System.Text.Json;

namespace Integration.Tests;

/// <summary>Direct NativeAOT entry point for the pinned independent ISO corpus.</summary>
internal static class NativeConformanceProgram
{
#if COMPILED_CONFORMANCE_RUNNER
    private static int Main() => CompiledConformanceGenerated.Run();
#else
    private static async Task<int> Main(string[] args)
    {
        var reportPath = args.Length > 0 ? args[0] : Path.GetTempFileName();
        try
        {
            await LogtalkConformanceTests.RunPinnedIsoCorpusAsync(selectedId: null, reportPath, CancellationToken.None);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            var summary = report.RootElement.GetProperty("summary");
            var passed = summary.GetProperty("passed").GetInt32();
            var auditOnly = summary.GetProperty("audit_only").GetInt32();
            var executable = summary.GetProperty("applicable").GetInt32() - auditOnly;
            await Console.Out.WriteLineAsync(
                $"NativeAOT independent ISO conformance: {passed}/{executable} executable cases passed; {auditOnly} audit-only cases not executed."
            );
            return 0;
        }
        finally
        {
            if (args.Length == 0)
                File.Delete(reportPath);
        }
    }
#endif
}
#endif
