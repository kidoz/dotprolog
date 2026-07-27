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
            ["build", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
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

    [Theory]
    [InlineData("PricingFSharp", "F#")]
    [InlineData("PricingVisualBasic", "VB")]
    public async Task OtherDotNetLanguagesCallTheSameProjectTheSameWay(string sample, string prefix)
    {
        // The claim that F# and VB need no extra work is only worth making if it is checked. Adding
        // the F# project is also what caught a repository-wide LangVersion that only C# accepts.
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        string project = Path.Combine(RepositoryLayout.Root, "samples", sample);

        (int taskExit, string taskLog) = await Run(
            "dotnet",
            ["build", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (int buildExit, string buildLog) = await Run("dotnet", ["build", project, "--nologo"]);
        Assert.True(buildExit == 0, $"Building {sample} failed:\n{buildLog}");

        (int runExit, string output) = await Run("dotnet", ["run", "--project", project, "--no-build"]);
        Assert.True(runExit == 0, $"Running {sample} failed:\n{output}");

        string[] lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            [
                $"{prefix}: 100 less 15% = 85",
                $"{prefix}: tier of 1200 is gold",
                $"{prefix}: widget in catalogue: {(prefix == "F#" ? "true" : "True")}",
                $"{prefix}: bundles: widget+gadget, widget, gadget, ",
                $"{prefix}: widget stock is 7",
            ],
            lines
        );
    }

    [Fact]
    public async Task APrologProjectCanBeAnApplicationRatherThanALibrary()
    {
        // A .dplproj with OutputType Exe gets a generated entry point that runs its goals, so Prolog
        // is a language you can write a program in and not only a library other languages call.
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        string project = Path.Combine(RepositoryLayout.Root, "samples", "GreetingApp");

        (int taskExit, string taskLog) = await Run(
            "dotnet",
            ["build", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (int buildExit, string buildLog) = await Run("dotnet", ["build", project, "--nologo"]);
        Assert.True(buildExit == 0, $"Building GreetingApp failed:\n{buildLog}");

        (int runExit, string output) = await Run("dotnet", ["run", "--project", project, "--no-build"]);
        Assert.True(runExit == 0, $"Running GreetingApp failed:\n{output}");

        Assert.Equal(
            ["Hello, world!", "Hello, prolog!", "Hello, dotnet!"],
            output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
        );
    }

    [Fact]
    public async Task APrologTestProjectDiscoversAndRunsItsTests()
    {
        // The test host is a Microsoft.Testing.Platform application, so it is exercised by running it.
        // `dotnet test` cannot drive it yet: MTP mode is a repository-wide global.json switch that
        // breaks the xunit projects here, and VSTest mode is gone on the .NET 10 SDK.
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        string project = Path.Combine(RepositoryLayout.Root, "samples", "PricingTests");

        (int taskExit, string taskLog) = await Run(
            "dotnet",
            ["build", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (int buildExit, string buildLog) = await Run("dotnet", ["build", project, "--nologo"]);
        Assert.True(buildExit == 0, $"Building PricingTests failed:\n{buildLog}");

        (int listExit, string listLog) = await Run("dotnet", ["run", "--project", project, "--no-build", "--", "--list-tests"]);
        Assert.True(listExit == 0, $"Listing tests failed:\n{listLog}");
        Assert.Contains("test_discount_reduces_price", listLog, StringComparison.Ordinal);
        Assert.Contains("found 7 test(s)", listLog, StringComparison.Ordinal);

        (int runExit, string runLog) = await Run("dotnet", ["run", "--project", project, "--no-build"]);

        Assert.True(runExit == 0, $"Running the tests failed:\n{runLog}");
        Assert.Contains("succeeded: 7", runLog, StringComparison.Ordinal);
        Assert.Contains("failed: 0", runLog, StringComparison.Ordinal);
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
