using System.Diagnostics;

namespace Integration.Tests;

/// <summary>
/// Builds the C# console sample, which reaches a Prolog rule set through an ordinary
/// <c>ProjectReference</c> to a <c>.dplproj</c>, and runs it.
/// </summary>
/// <remarks>
/// MSBuild behaviour can only really be checked by running MSBuild. This proves the whole chain: the
/// SDK targets fire, the task generates the facade before <c>CoreCompile</c>, the generated C#
/// compiles into the Prolog project's assembly, and a plain C# program calls it.
/// </remarks>
public sealed class ProjectReferenceTests
{
    private const string OptInVariable = "DOTPROLOG_RUN_AOT_TESTS";

    [Fact]
    public async Task CSharpProjectCallsAPrologProjectThroughAProjectReference()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        string console = Path.Combine(RepositoryLayout.Root, "samples", "PricingConsole");

        // The task assembly is loaded by MSBuild, so it has to exist before the sample builds.
        (int taskExit, string taskLog) = await Run(
            "dotnet",
            ["build", Path.Combine(RepositoryLayout.Root, "src", "Prolog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (int buildExit, string buildLog) = await Run("dotnet", ["build", console, "--nologo"]);
        Assert.True(buildExit == 0, $"Building the sample failed:\n{buildLog}");

        // The facade must be generated into obj/, not beside the sources.
        string generated = Path.Combine(RepositoryLayout.Root, "samples", "PricingRules", "obj", "prolog", "PricingModule.g.cs");
        Assert.True(File.Exists(generated), $"No generated facade at {generated}.");
        Assert.Contains(
            "public partial interface IPricingModule",
            await File.ReadAllTextAsync(generated, TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );

        (int runExit, string output) = await Run("dotnet", ["run", "--project", console, "--no-build"]);
        Assert.True(runExit == 0, $"Running the sample failed:\n{output}");

        string[] lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                "100 less 15% = 85",
                "total 1200 is gold",
                "total 700 is silver",
                "total 100 is bronze",
                "widget in catalogue: True",
                "anvil in catalogue:  False",
                "bundles of [widget, gadget]:",
                "  [widget, gadget]",
                "  [widget]",
                "  [gadget]",
                "  []",
            ],
            lines
        );
    }

    private static async Task<(int ExitCode, string Log)> Run(string fileName, string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepositoryLayout.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await standardOutput + await standardError);
    }
}
