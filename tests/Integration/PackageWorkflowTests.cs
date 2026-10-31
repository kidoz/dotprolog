namespace Integration.Tests;

public sealed class PackageWorkflowTests : IDisposable
{
    private readonly string _directory = CreateDirectory();

    private static string CreateDirectory()
    {
        var path = Directory.CreateTempSubdirectory("propack sdk ").FullName;
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + path : path;
    }

    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task LockedPackagesBuildIncrementallyAndUpdateOnlyExplicitly()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DOTPROLOG_RUN_AOT_TESTS") == "1",
            "Set DOTPROLOG_RUN_AOT_TESTS=1 for SDK package tests."
        );
        var source = Path.Combine(RepositoryLayout.Root, "src");
        var app = Path.Combine(_directory, "app");
        var package = Path.Combine(_directory, "words");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(package);
        await Write(
            Path.Combine(app, "propack.json"),
            """{"name":"example/app","dependencies":{"example/words":{"path":"../words"}}}"""
        );
        await Write(Path.Combine(package, "propack.json"), """{"name":"example/words","modules":{"words":"words.pl"}}""");
        await Write(Path.Combine(package, "words.pl"), ":- module(words, [word/1]).\nword(first).\n");
        await Write(
            Path.Combine(app, "main.pl"),
            ":- use_module(library(words)).\n:- initialization(main).\nmain :- word(X), writeln(X).\n"
        );
        var project = Path.Combine(app, "App.dplproj");
        await Write(
            project,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
                <Import Project="{source}/DotProlog.Sdk/Sdk/Sdk.props" />
                <PropertyGroup>
                    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
                    <RootNamespace>PackageApp</RootNamespace>
                    <DotPrologTasksAssembly>{source}/DotProlog.Build.Tasks/bin/Debug/net10.0/DotProlog.Build.Tasks.dll</DotPrologTasksAssembly>
                </PropertyGroup>
                <ItemGroup><ProjectReference Include="{source}/DotProlog.Compiler/DotProlog.Compiler.csproj" /></ItemGroup>
                <Import Project="{source}/DotProlog.Sdk/Sdk/Sdk.targets" />
            </Project>
            """
        );
        await Success("build", "-nodereuse:false", Path.Combine(source, "DotProlog.Build.Tasks"));
        await Success("build", "-nodereuse:false", Path.Combine(source, "DotProlog.Packages.Cli"));
        var tool = Path.Combine(source, "DotProlog.Packages.Cli", "bin", "Debug", "net10.0", "propack.dll");
        var cache = Path.Combine(_directory, "cache");
        await Success(tool, "restore", "--project", app, "--cache", cache);
        // The SDK inherits PROPACK_CACHE from its child-process environment only.
        var environment = new Dictionary<string, string?> { ["PROPACK_CACHE"] = cache };
        async Task<(int ExitCode, string Log)> Build() =>
            await ChildProcess.RunAsync("dotnet", ["build", "-nodereuse:false", project], RepositoryLayout.Root, environment);
        var first = await Build();
        Assert.True(first.ExitCode == 0, first.Log);
        var stamp = Path.Combine(app, "obj", "prolog", ".generated");
        var firstStamp = File.GetLastWriteTimeUtc(stamp);
        var second = await Build();
        Assert.True(second.ExitCode == 0, second.Log);
        Assert.Equal(firstStamp, File.GetLastWriteTimeUtc(stamp));
        await Write(Path.Combine(package, "words.pl"), ":- module(words, [word/1]).\nword(second).\n");
        var stale = await Build();
        Assert.NotEqual(0, stale.ExitCode);
        Assert.Contains("Local package", stale.Log, StringComparison.Ordinal);
        await Success(tool, "update", "--project", app, "--cache", cache);
        var updated = await Build();
        Assert.True(updated.ExitCode == 0, updated.Log);
        Assert.NotEqual(firstStamp, File.GetLastWriteTimeUtc(stamp));
        Directory.Delete(cache, true);
        Directory.Delete(package, true);
        var run = await ChildProcess.RunAsync(
            "dotnet",
            [Path.Combine(app, "bin", "Debug", "net10.0", "App.dll")],
            RepositoryLayout.Root
        );
        Assert.True(run.ExitCode == 0, run.Log);
        Assert.Equal("second", run.Log.Trim());
    }

    private static Task Write(string path, string text) =>
        File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

    private static async Task Success(params string[] args)
    {
        var result = await ChildProcess.RunAsync("dotnet", args, RepositoryLayout.Root);
        Assert.True(result.ExitCode == 0, result.Log);
    }
}
