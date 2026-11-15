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
    private const string FullMsBuildOptInVariable = "DOTPROLOG_RUN_FULL_MSBUILD_TESTS";

    [Fact]
    public async Task CSharpProjectCallsAPrologProjectThroughAProjectReference()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        var console = Path.Combine(RepositoryLayout.Root, "samples", "PricingConsole");

        // The task assembly is loaded by MSBuild, so it has to exist before the sample builds.
        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (var buildExit, var buildLog) = await Run("dotnet", ["build", "-nodereuse:false", console, "--nologo"]);
        Assert.True(buildExit == 0, $"Building the sample failed:\n{buildLog}");

        // The facade must be generated into obj/, not beside the sources.
        var generated = Path.Combine(RepositoryLayout.Root, "samples", "PricingRules", "obj", "prolog", "PricingModule.g.cs");
        Assert.True(File.Exists(generated), $"No generated facade at {generated}.");
        Assert.Contains(
            "public partial interface IPricingModule",
            await File.ReadAllTextAsync(generated, TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );

        (var runExit, var output) = await Run("dotnet", ["run", "--project", console, "--no-build"]);
        Assert.True(runExit == 0, $"Running the sample failed:\n{output}");

        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

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

    [Fact]
    public async Task VisualStudioMsBuildHostsTheNetFacadeGenerator()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(FullMsBuildOptInVariable) == "1",
            $"Run on Windows with {FullMsBuildOptInVariable}=1 to test full MSBuild task hosting."
        );

        var console = Path.Combine(RepositoryLayout.Root, "samples", "PricingConsole", "PricingConsole.csproj");

        (var buildExit, var buildLog) = await Run(
            "msbuild",
            [
                console,
                "-restore",
                "-target:Rebuild",
                "-property:Configuration=Release",
                "-nodeReuse:false",
                "-verbosity:minimal",
                "-nologo",
            ]
        );

        Assert.True(buildExit == 0, $"Full MSBuild could not host the .NET facade generator:\n{buildLog}");

        var generated = Path.Combine(RepositoryLayout.Root, "samples", "PricingRules", "obj", "prolog", "PricingModule.g.cs");
        Assert.True(File.Exists(generated), $"No generated facade at {generated}.");

        (var runExit, var output) = await Run("dotnet", ["run", "--project", console, "-c", "Release", "--no-build"]);
        Assert.True(runExit == 0, $"Running the full-MSBuild output failed:\n{output}");
        Assert.Contains("100 less 15% = 85", output, StringComparison.Ordinal);
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

        var project = Path.Combine(RepositoryLayout.Root, "samples", sample);

        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (var buildExit, var buildLog) = await Run("dotnet", ["build", "-nodereuse:false", project, "--nologo"]);
        Assert.True(buildExit == 0, $"Building {sample} failed:\n{buildLog}");

        (var runExit, var output) = await Run("dotnet", ["run", "--project", project, "--no-build"]);
        Assert.True(runExit == 0, $"Running {sample} failed:\n{output}");

        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

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

        var project = Path.Combine(RepositoryLayout.Root, "samples", "GreetingApp");

        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (var buildExit, var buildLog) = await Run("dotnet", ["build", "-nodereuse:false", project, "--nologo"]);
        Assert.True(buildExit == 0, $"Building GreetingApp failed:\n{buildLog}");

        (var runExit, var output) = await Run("dotnet", ["run", "--project", project, "--no-build"]);
        Assert.True(runExit == 0, $"Running GreetingApp failed:\n{output}");

        Assert.Equal(
            ["Hello, world!", "Hello, prolog!", "Hello, dotnet!"],
            output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
        );
    }

    [Theory]
    [InlineData(
        "NaturalLanguage",
        new[]
        {
            "statement: every cat chases a mouse",
            "  words:   [every,cat,chases,a,mouse]",
            "  tree:    s(np(det(every),n(cat)),vp(v(chases),np(det(a),n(mouse))))",
            "  meaning: all(x,cat(x),exists(y,and(mouse(y),chases(x,y))))",
            "  holds:   yes",
            "statement: a dog sleeps",
            "  words:   [a,dog,sleeps]",
            "  tree:    s(np(det(a),n(dog)),vp(v(sleeps)))",
            "  meaning: exists(x,and(dog(x),sleeps(x)))",
            "  holds:   yes",
            "statement: every mouse fears a dog",
            "  words:   [every,mouse,fears,a,dog]",
            "  tree:    s(np(det(every),n(mouse)),vp(v(fears),np(det(a),n(dog))))",
            "  meaning: all(x,mouse(x),exists(y,and(dog(y),fears(x,y))))",
            "  holds:   no",
            "statement: tom chases jerry",
            "  words:   [tom,chases,jerry]",
            "  tree:    s(np(name(tom)),vp(v(chases),np(name(jerry))))",
            "  meaning: chases(tom,jerry)",
            "  holds:   yes",
            "question:  who chases a mouse?",
            "  words:   [who,chases,a,mouse]",
            "  tree:    q(who,vp(v(chases),np(det(a),n(mouse))))",
            "  meaning: exists(y,and(mouse(y),chases(x,y)))",
            "  answers: x = [tom,whiskers]",
            "question:  who sleeps?",
            "  words:   [who,sleeps]",
            "  tree:    q(who,vp(v(sleeps)))",
            "  meaning: sleeps(x)",
            "  answers: x = [rex]",
            "statement: every cat chase a mouse",
            "  words:   [every,cat,chase,a,mouse]",
            "  no parse: subject and verb disagree in number",
        }
    )]
    [InlineData(
        "TextGrammar",
        new[]
        {
            "\"abc\" = [First|Rest]  gives  First = a, Rest = [b,c]",
            "sum of 12+34+6 is 52",
            "words of the quick brown fox: [the,quick,brown,fox]",
        }
    )]
    public async Task GeneratedTextApplicationsRunTheirCompleteDemonstrations(string sample, string[] expected)
    {
        var output = await BuildAndRunSample(sample);
        Assert.Equal(expected, OutputLines(output));
    }

    [Theory]
    [InlineData("winning_path.txt", "The empty armor falls apart.", "The village bell is silent.")]
    [InlineData("mercy_path.txt", "The knight kneels:", "Tomas rings the village bell")]
    public async Task GeneratedAdventureApplicationCompletesItsDocumentedJourneys(
        string journey,
        string expectedResolution,
        string expectedEpilogue
    )
    {
        var project = Path.Combine(RepositoryLayout.Root, "samples", "GreymereAdventure");
        var input = await File.ReadAllTextAsync(Path.Combine(project, journey), TestContext.Current.CancellationToken);
        var output = await BuildAndRunSample("GreymereAdventure", input);

        Assert.Contains("Dawn, dusk, then noon: the bells sing of life renewed.", output, StringComparison.Ordinal);
        Assert.Contains(expectedResolution, output, StringComparison.Ordinal);
        Assert.Contains(expectedEpilogue, output, StringComparison.Ordinal);
        Assert.EndsWith("                    *** YOU ARE VICTORIOUS ***", output.TrimEnd(), StringComparison.Ordinal);
        Assert.DoesNotContain("*** THE END ***", output, StringComparison.Ordinal);
        Assert.DoesNotContain("That input is not a valid Prolog command.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APrologTestProjectDiscoversAndRunsItsTests()
    {
        // Exercise the public test contract, not only the generated executable: .NET 10's MTP mode
        // must discover and run a .dplproj alongside the repository's xUnit MTP projects.
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        var project = Path.Combine(RepositoryLayout.Root, "samples", "PricingTests");

        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (var buildExit, var buildLog) = await Run("dotnet", ["build", "-nodereuse:false", project, "--nologo"]);
        Assert.True(buildExit == 0, $"Building PricingTests failed:\n{buildLog}");

        (var listExit, var listLog) = await Run(
            "dotnet",
            ["test", "--project", project, "--no-build", "--no-restore", "--list-tests", "--no-ansi"]
        );
        Assert.True(listExit == 0, $"Listing tests failed:\n{listLog}");
        Assert.Contains("test_discount_reduces_price", listLog, StringComparison.Ordinal);
        Assert.Contains("Discovered 7 tests.", listLog, StringComparison.Ordinal);

        (var runExit, var runLog) = await Run(
            "dotnet",
            ["test", "--project", project, "--no-build", "--no-restore", "--no-ansi"]
        );

        Assert.True(runExit == 0, $"Running the tests failed:\n{runLog}");
        Assert.Contains("total: 7", runLog, StringComparison.Ordinal);
        Assert.Contains("succeeded: 7", runLog, StringComparison.Ordinal);
        Assert.Contains("failed: 0", runLog, StringComparison.Ordinal);
    }

    private static async Task<string> BuildAndRunSample(string sample, string? standardInput = null)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        var project = Path.Combine(RepositoryLayout.Root, "samples", sample);
        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );
        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        (var buildExit, var buildLog) = await Run("dotnet", ["build", "-nodereuse:false", project, "--nologo"]);
        Assert.True(buildExit == 0, $"Building {sample} failed:\n{buildLog}");

        (var runExit, var output) = await ChildProcess.RunAsync(
            "dotnet",
            ["run", "--project", project, "--no-build"],
            RepositoryLayout.Root,
            standardInput: standardInput
        );
        Assert.True(runExit == 0, $"Running {sample} failed:\n{output}");
        return output;
    }

    private static string[] OutputLines(string output) =>
        output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static Task<(int ExitCode, string Log)> Run(string fileName, string[] arguments) =>
        ChildProcess.RunAsync(fileName, arguments, RepositoryLayout.Root);
}
