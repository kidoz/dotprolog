using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Integration.Tests;

/// <summary>
/// The NativeAOT acceptance check from the product scope, run end to end: publish the sample as a
/// native executable, confirm the build raised no trimming or AOT warnings, then run the binary and
/// check it consulted a Prolog file, enumerated solutions, and changed its clause database.
/// </summary>
/// <remarks>
/// Publishing takes tens of seconds and needs a native toolchain, so this is opt-in. Set
/// <c>DOTPROLOG_RUN_AOT_TESTS=1</c> to run it; CI does.
/// </remarks>
public sealed class NativeAotAcceptanceTests
{
    private const string OptInVariable = "DOTPROLOG_RUN_AOT_TESTS";

    [Fact]
    public async Task PublishedExecutableConsultsAndUpdatesAtRunTime()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the NativeAOT acceptance test."
        );

        string output = Path.Combine(Path.GetTempPath(), $"dotprolog-aot-{Environment.ProcessId}");
        Directory.CreateDirectory(output);

        try
        {
            (int exitCode, string log) = await RunAsync(
                "dotnet",
                [
                    "publish",
                    "-nodereuse:false",
                    Path.Combine(RepositoryLayout.Root, "samples", "AotAcceptance", "AotAcceptance.csproj"),
                    "-c",
                    "Release",
                    "-r",
                    RuntimeInformation.RuntimeIdentifier,
                    "-p:PublishAot=true",
                    "-o",
                    output,
                    "--nologo",
                ],
                RepositoryLayout.Root
            );

            Assert.True(exitCode == 0, $"Publish failed:\n{log}");

            // The scope requires no unresolved trimming or AOT warnings, not merely a successful build.
            Assert.DoesNotContain("warning IL", log, StringComparison.Ordinal);
            Assert.DoesNotContain("AOT analysis", log, StringComparison.Ordinal);

            string executable = Path.Combine(output, OperatingSystem.IsWindows() ? "AotAcceptance.exe" : "AotAcceptance");
            Assert.True(File.Exists(executable), $"No published executable at {executable}.");

            // No managed assemblies should ship beside it; only the native image and debug artefacts.
            Assert.Empty(Directory.GetFiles(output, "*.dll"));

            (int runExit, string runLog) = await RunAsync(executable, [], output);

            Assert.True(runExit == 0, $"Published executable failed:\n{runLog}");
            Assert.Equal(
                [
                    "Hello from NativeAOT!",
                    "colours(3)",
                    "[red,green,blue]",
                    "[first]",
                    "[first,second,third]",
                    "[first,third]",
                    "caught",
                    "[a,b,c]",
                    "[A,B,C]",
                    "sum=6",
                    "row     7",
                    "arithmetic=-1,2.0,0.0",
                    "arithmetic_error",
                    "occurs_check",
                    "repeat_control",
                    "compiled_goal_errors",
                    "control_error_audit",
                    "predicate_info",
                    "prolog_flags",
                    "character_conversion",
                    "numeric_escape_syntax",
                    "quoted_token_syntax",
                    "extended_character_syntax",
                    "read_options",
                    "read_option_validation",
                    "integer_representation_errors",
                    "max_arity_representation_error",
                    "float_read_limits",
                    "halt_status_errors",
                    "compare_order_errors",
                    "arg_errors",
                    "term_construction_errors",
                    "current_op_filter_errors",
                    "current_op_snapshot",
                    "quoted_operator_syntax",
                    "database_permission_errors",
                    "stream_properties",
                    "current_stream_domains",
                    "code_io",
                    "stream_eof_actions",
                    "stream_open_errors",
                    "source_sink_domains",
                    "invalid_source_sink_paths",
                    "stream_option_error_priority",
                    "stream_error_terms",
                    "character_input_modes",
                    "primitive_io_error_priority",
                    "stream_system_errors",
                    "term_write_options",
                    "byte_io",
                    "stream_position",
                    "alice likes bob",
                    "+(1,*(2,3))",
                    "427",
                    "captured(1+2)",
                    "done",
                ],
                runLog.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            );
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static Task<(int ExitCode, string Log)> RunAsync(string fileName, string[] arguments, string workingDirectory) =>
        ChildProcess.RunAsync(fileName, arguments, workingDirectory);
}
