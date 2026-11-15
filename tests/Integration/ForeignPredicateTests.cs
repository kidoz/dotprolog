using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Integration.Tests;

public sealed class ForeignPredicateTests
{
    [Fact]
    public async Task PackedSdkRunsForeignPredicatesFromApplicationFacadeAndConsultedCode()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DOTPROLOG_RUN_AOT_TESTS") == "1",
            "Set DOTPROLOG_RUN_AOT_TESTS=1 to run the packed SDK foreign predicate test."
        );

        var root = CreateTempDirectory();
        try
        {
            var feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
            var work = Directory.CreateDirectory(Path.Combine(root, "foreign consumer with spaces")).FullName;
            var environment = IsolatedEnvironment(root);
            var version = "0.0.0-foreign." + Guid.NewGuid().ToString("N");

            foreach (var project in new[] { "DotProlog.Runtime", "DotProlog.Syntax", "DotProlog.Compiler", "DotProlog.Sdk" })
            {
                var packed = await Run(
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
                Assert.True(packed.ExitCode == 0, $"Packing {project} failed:\n{packed.Log}");
            }

            var sdkPackage = Path.Combine(feed, $"DotProlog.Sdk.{version}.nupkg");
            await using (var package = await ZipFile.OpenReadAsync(sdkPackage, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(package.GetEntry("analyzers/dotnet/cs/DotProlog.Foreign.SourceGenerator.dll"));
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
            await Write(
                Path.Combine(work, "global.json"),
                $$"""
                {
                    "msbuild-sdks": { "DotProlog.Sdk": "{{version}}" }
                }
                """
            );

            var rules = Directory.CreateDirectory(Path.Combine(work, "ForeignRules")).FullName;
            await Write(
                Path.Combine(rules, "ForeignRules.dplproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <Sdk Name="DotProlog.Sdk" Version="{version}" />
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net10.0</TargetFramework>
                        <RootNamespace>ForeignRules</RootNamespace>
                    </PropertyGroup>
                    <ItemGroup>
                        <PackageReference Include="DotProlog.Compiler" Version="{version}" />
                    </ItemGroup>
                </Project>
                """
            );
            await Write(
                Path.Combine(rules, "rules.pl"),
                """
                :- initialization(main).
                main :- foreign_startup, foreign_double(21, X), foreign_write(X).
                double(X, Y) :- foreign_double(X, Y).
                """
            );
            await Write(
                Path.Combine(rules, "rules.dpli"),
                """
                :- clr_module('Rules').
                :- clr_export(double/2, det, [in(value, integer), out(result, integer)]).
                """
            );
            await Write(Path.Combine(rules, "Foreign.cs"), ForeignSource);

            var application = await Run(work, environment, "run", "--project", Path.Combine(rules, "ForeignRules.dplproj"));
            Assert.True(application.ExitCode == 0, $"Running the generated application failed:\n{application.Log}");
            Assert.Equal(["startup", "42"], OutputLines(application.Log)[^2..]);

            var consumer = Directory.CreateDirectory(Path.Combine(work, "Consumer")).FullName;
            await Write(
                Path.Combine(consumer, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net10.0</TargetFramework>
                    </PropertyGroup>
                    <ItemGroup>
                        <ProjectReference Include="../ForeignRules/ForeignRules.dplproj" />
                        <PackageReference Include="DotProlog.Compiler" Version="{version}" />
                    </ItemGroup>
                </Project>
                """
            );
            await Write(
                Path.Combine(consumer, "Program.cs"),
                """
                using DotProlog.Compiler;
                using DotProlog.Runtime;
                using ForeignRules;

                using var engine = new PrologEngine();
                using var rules = RulesModule.Create(engine);
                System.Console.WriteLine(rules.Double(21));
                engine.ConsultOrThrow("runtime_double(Y) :- foreign_double(42, Y).", "runtime.pl");
                var host = new PrologHost(engine.Machine);
                PrologValue[] result = host.CallOnce(host.Bind("runtime_double", 1), PrologInput.Output)!;
                System.Console.WriteLine(PrologMarshal.ToInteger(result[0]));
                """
            );

            var facade = await Run(work, environment, "run", "--project", Path.Combine(consumer, "Consumer.csproj"));
            Assert.True(facade.ExitCode == 0, $"Running the typed facade consumer failed:\n{facade.Log}");
            Assert.Equal(["startup", "42", "42", "84"], OutputLines(facade.Log)[^4..]);

            var native = Path.Combine(root, "native");
            var published = await Run(
                work,
                environment,
                "publish",
                Path.Combine(rules, "ForeignRules.dplproj"),
                "-c",
                "Release",
                "-r",
                RuntimeInformation.RuntimeIdentifier,
                "-p:PublishAot=true",
                "-p:RestoreAdditionalProjectSources=https://api.nuget.org/v3/index.json",
                "-o",
                native
            );
            Assert.True(published.ExitCode == 0, $"Publishing the foreign predicate application failed:\n{published.Log}");
            Assert.DoesNotContain("warning IL", published.Log, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(native, "*.dll"));
            var executable = Path.Combine(native, OperatingSystem.IsWindows() ? "ForeignRules.exe" : "ForeignRules");
            var nativeRun = await ChildProcess.RunAsync(executable, [], work, environment);
            Assert.True(nativeRun.ExitCode == 0, $"Running the native foreign predicates failed:\n{nativeRun.Log}");
            Assert.Equal(["startup", "42"], OutputLines(nativeRun.Log));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string ForeignSource = """
        using DotProlog.Runtime;

        namespace ForeignRules;

        internal static class Foreign
        {
            [PrologPredicate(Name = "foreign_startup", Arity = 0)]
            internal static bool Startup(ref PrologContext context)
            {
                context.Machine.Output.WriteLine("startup");
                return true;
            }

            [PrologPredicate(Name = "foreign_double", Arity = 2)]
            internal static bool Double(ref PrologContext context, Cell input, Cell output)
            {
                if (input.Tag != CellTag.Integer || !Cell.FitsInteger(input.Integer * 2))
                    return false;
                return context.Machine.Unify(output, Cell.Integer60(input.Integer * 2));
            }

            [PrologPredicate(Name = "foreign_write", Arity = 1)]
            internal static bool Write(ref PrologContext context, Cell value)
            {
                context.Machine.Output.WriteLine(context.Format(value));
                return true;
            }
        }
        """;

    private static string[] OutputLines(string output) =>
        output
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("X.509 certificate chain validation", StringComparison.Ordinal))
            .ToArray();

    private static string CreateTempDirectory()
    {
        var path = Directory.CreateTempSubdirectory("dotprolog foreign ").FullName;
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

    private static Task Write(string path, string text) =>
        File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

    private static Task<(int ExitCode, string Log)> Run(
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments
    ) => ChildProcess.RunAsync("dotnet", arguments, workingDirectory, environment);
}
