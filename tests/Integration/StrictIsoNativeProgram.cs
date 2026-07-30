#if STRICT_ISO_RUNNER
namespace Integration.Tests;

/// <summary>NativeAOT entry point for the strict generated and runtime-consult smoke matrix.</summary>
internal static class StrictIsoNativeProgram
{
    private static int Main() => StrictIsoGenerated.Run();
}
#endif
