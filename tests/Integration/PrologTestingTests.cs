namespace Integration.Tests;

/// <summary>Exercises failing and module-local Prolog tests through the public dotnet test command.</summary>
public sealed class PrologTestingTests : IDisposable
{
    private const string OptInVariable = "DOTPROLOG_RUN_AOT_TESTS";
    private readonly string _project = CreateProjectDirectory();

    [Fact]
    public async Task DotnetTestReportsARealFailureAndDistinguishesModuleLocalNames()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        (var taskExit, var taskLog) = await ChildProcess.RunAsync(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"],
            RepositoryLayout.Root
        );
        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        WriteProject();
        await File.WriteAllTextAsync(
            Path.Combine(_project, "a.pl"),
            ":- module(a, []). test_shared :- write(a_passed).\n",
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(_project, "b.pl"),
            ":- module(b, []). test_shared :- write(b_failed), fail.\n",
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(Path.Combine(_project, "user.pl"), "test_user.\n", TestContext.Current.CancellationToken);

        (var listExit, var listLog) = await ChildProcess.RunAsync(
            "dotnet",
            ["test", "--project", _project, "--list-tests", "--no-ansi"],
            RepositoryLayout.Root
        );
        Assert.True(listExit == 0, $"Listing tests failed:\n{listLog}");
        Assert.Contains("a:test_shared", listLog, StringComparison.Ordinal);
        Assert.Contains("b:test_shared", listLog, StringComparison.Ordinal);
        Assert.Contains("test_user", listLog, StringComparison.Ordinal);
        Assert.Contains("Discovered 3 tests.", listLog, StringComparison.Ordinal);

        (var runExit, var runLog) = await ChildProcess.RunAsync(
            "dotnet",
            ["test", "--project", _project, "--no-build", "--no-restore", "--no-ansi"],
            RepositoryLayout.Root
        );
        Assert.True(runExit != 0, $"The intentionally failing test did not fail dotnet test:\n{runLog}");
        Assert.Contains("b:test_shared failed.", runLog, StringComparison.Ordinal);
        Assert.Contains("b_failed", runLog, StringComparison.Ordinal);
        Assert.Contains("total: 3", runLog, StringComparison.Ordinal);
        Assert.Contains("succeeded: 2", runLog, StringComparison.Ordinal);
        Assert.Contains("failed: 1", runLog, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fail", "Initialization failed.")]
    [InlineData("halt(7)", "Initialization halted with exit code 7.")]
    public async Task DotnetTestReportsGeneratedInitializationFailure(string goal, string expected)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        (var taskExit, var taskLog) = await ChildProcess.RunAsync(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"],
            RepositoryLayout.Root
        );
        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        WriteProject();
        await File.WriteAllTextAsync(
            Path.Combine(_project, "initialization.pl"),
            $":- initialization({goal}). test_never_runs.\n",
            TestContext.Current.CancellationToken
        );

        (var runExit, var runLog) = await ChildProcess.RunAsync(
            "dotnet",
            ["test", "--project", _project, "--no-ansi"],
            RepositoryLayout.Root
        );

        Assert.True(runExit != 0, $"Initialization {goal} did not fail dotnet test:\n{runLog}");
        Assert.Contains(expected, runLog, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_project, recursive: true);
        }
        catch (IOException) { }
    }

    private void WriteProject()
    {
        var source = Path.Combine(RepositoryLayout.Root, "src");
        var contents = $"""
                        <Project Sdk="Microsoft.NET.Sdk">
                            <Import Project="{Path.Combine(source, "DotProlog.Sdk", "Sdk", "Sdk.props")}" />
                            <PropertyGroup>
                                <OutputType>Exe</OutputType>
                                <DotPrologTestProject>true</DotPrologTestProject>
                                <TargetFramework>net10.0</TargetFramework>
                                <IsPackable>false</IsPackable>
                                <NoWarn>$(NoWarn);CS1591</NoWarn>
                                <DotPrologTasksAssembly>{Path.Combine(
                                    source,
                                    "DotProlog.Build.Tasks",
                                    "bin",
                                    "$(Configuration)",
                                    "net10.0",
                                    "DotProlog.Build.Tasks.dll"
                                )}</DotPrologTasksAssembly>
                            </PropertyGroup>
                            <ItemGroup>
                                <ProjectReference Include="{Path.Combine(source, "DotProlog.Testing", "DotProlog.Testing.csproj")}" />
                                <ProjectReference Include="{Path.Combine(
                                    source,
                                    "DotProlog.Build.Tasks",
                                    "DotProlog.Build.Tasks.csproj"
                                )}" ReferenceOutputAssembly="false" />
                            </ItemGroup>
                            <Import Project="{Path.Combine(source, "DotProlog.Sdk", "Sdk", "Sdk.targets")}" />
                        </Project>
                        """;
        File.WriteAllText(Path.Combine(_project, "FailingTests.dplproj"), contents);
    }

    private static string CreateProjectDirectory()
    {
        var path = Directory.CreateTempSubdirectory("dotprolog-testing-").FullName;
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + path : path;
    }
}
