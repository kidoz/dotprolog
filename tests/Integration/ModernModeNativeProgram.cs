#if MODERN_MODE_RUNNER
namespace Integration.Tests;

/// <summary>NativeAOT entry point for the Modern-mode generated and runtime-consult smoke matrix.</summary>
internal static class ModernModeNativeProgram
{
    private static int Main() => ModernModeGenerated.Run();
}
#endif
