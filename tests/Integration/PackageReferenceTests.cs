using System.IO.Compression;

namespace Integration.Tests;

public sealed class PackageReferenceTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net11.0")]
    public async Task PackedPrologLibraryIsConsumedThroughPackageReference(string targetFramework)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DOTPROLOG_RUN_AOT_TESTS") == "1",
            "Set DOTPROLOG_RUN_AOT_TESTS=1 to run the packed SDK integration tests."
        );
        if (targetFramework == "net11.0")
            await RequireNet11Sdk();

        var root = CreateTempDirectory();
        try
        {
            var feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
            var work = Directory.CreateDirectory(Path.Combine(root, "consumer with spaces")).FullName;
            var environment = IsolatedEnvironment(root);
            var version = "0.0.0-acceptance." + Guid.NewGuid().ToString("N");

            foreach (
                var project in new[]
                {
                    "DotProlog.Runtime",
                    "DotProlog.Syntax",
                    "DotProlog.Compiler",
                    "DotProlog.Sdk",
                    "DotProlog.Templates",
                }
            )
            {
                var result = await Run(
                    work,
                    environment,
                    "pack",
                    Path.Combine(RepositoryLayout.Root, "src", project),
                    "-c",
                    "Release",
                    "-o",
                    feed,
                    "-p:Version=" + version,
                    "-nodereuse:false"
                );
                Assert.True(result.ExitCode == 0, $"Packing {project} failed:\n{result.Log}");
            }

            await Write(
                Path.Combine(work, "NuGet.Config"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                    <packageSources>
                        <clear />
                        <add key="dotprolog-local" value="{feed}" />
                    </packageSources>
                </configuration>
                """
            );

            var templates = Path.Combine(feed, $"DotProlog.Templates.{version}.nupkg");
            var install = await Run(work, environment, "new", "install", templates);
            Assert.True(install.ExitCode == 0, $"Installing packed templates failed:\n{install.Log}");
            var create = await Run(
                work,
                environment,
                "new",
                "prolog-lib",
                "-n",
                "UserRules",
                "--DotPrologVersion",
                version,
                "--Framework",
                targetFramework
            );
            Assert.True(create.ExitCode == 0, $"Creating the prolog-lib consumer failed:\n{create.Log}");

            var library = Path.Combine(work, "UserRules");
            var libraryProject = Path.Combine(library, "UserRules.dplproj");
            Assert.Contains(
                $"<TargetFramework>{targetFramework}</TargetFramework>",
                await File.ReadAllTextAsync(libraryProject, TestContext.Current.CancellationToken),
                StringComparison.Ordinal
            );
            var pack = await Run(
                work,
                environment,
                "pack",
                libraryProject,
                "-c",
                "Release",
                "-o",
                feed,
                "-p:Version=1.2.3",
                "-nodereuse:false"
            );
            Assert.True(pack.ExitCode == 0, $"Packing the user prolog-lib failed:\n{pack.Log}");

            var packagePath = Path.Combine(feed, "UserRules.1.2.3.nupkg");
            await using (var package = await ZipFile.OpenReadAsync(packagePath, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(package.GetEntry($"lib/{targetFramework}/UserRules.dll"));
            }

            Directory.Delete(library, recursive: true);
            var consumer = Directory.CreateDirectory(Path.Combine(work, "Consumer")).FullName;
            await Write(
                Path.Combine(consumer, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>{targetFramework}</TargetFramework>
                    </PropertyGroup>
                    <ItemGroup>
                        <PackageReference Include="UserRules" Version="1.2.3" />
                    </ItemGroup>
                </Project>
                """
            );
            await Write(
                Path.Combine(consumer, "Program.cs"),
                """
                var rules = UserRules.RulesModule.Create();
                System.Console.WriteLine(rules.Double(21));
                System.Console.WriteLine(rules.Positive(21));
                System.Console.WriteLine(string.Join(",", rules.Upto(3)));
                """
            );

            var run = await Run(work, environment, "run", "--project", consumer, "-c", "Release");
            Assert.True(run.ExitCode == 0, $"Running the PackageReference consumer failed:\n{run.Log}");
            Assert.Equal(
                ["42", "True", "1,2,3"],
                run.Log.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Directory.CreateTempSubdirectory("dotprolog package reference ").FullName;
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + path : path;
    }

    private static Dictionary<string, string?> IsolatedEnvironment(string root) =>
        new()
        {
            ["DOTNET_CLI_HOME"] = Path.Combine(root, "cli-home"),
            ["NUGET_PACKAGES"] = Path.Combine(root, "packages"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
            ["DOTNET_NOLOGO"] = "true",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "true",
            ["MSBUILDDISABLENODEREUSE"] = "1",
        };

    private static async Task RequireNet11Sdk()
    {
        var result = await ChildProcess.RunAsync("dotnet", ["--list-sdks"], RepositoryLayout.Root);
        Assert.True(result.ExitCode == 0, result.Log);
        var available = result
            .Log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ', 2)[0])
            .Any(text => Version.TryParse(text, out var version) && version.Major >= 11);
        Assert.SkipUnless(available, "The .NET 11 SDK is not installed; the net11.0 package consumer was not run.");
    }

    private static Task Write(string path, string text) =>
        File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

    private static Task<(int ExitCode, string Log)> Run(
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments
    ) => ChildProcess.RunAsync("dotnet", arguments, workingDirectory, environment);
}
