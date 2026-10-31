using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Integration.Tests;

/// <summary>Installs preview artifacts and consumes an unchanged external library outside the repository.</summary>
public sealed class PropackPreviewTests
{
    [Fact]
    public async Task InstalledAndNativeToolsConsumePinnedHeapsInCleanProjects()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DOTPROLOG_RUN_PACKAGE_PREVIEW_TESTS") == "1",
            "Set DOTPROLOG_RUN_PACKAGE_PREVIEW_TESTS=1; this test requires Python, SWI-Prolog and a native toolchain."
        );
        var root = Directory.CreateTempSubdirectory("propack preview ").FullName;
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal))
            root = "/private" + root;
        try
        {
            var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            var version = "0.17.0-preview." + Guid.NewGuid().ToString("N");
            var distribution = Environment.GetEnvironmentVariable("DOTPROLOG_PREVIEW_ARTIFACTS");
            if (distribution is null)
            {
                distribution = Path.Combine(root, "distribution");
                await Success(
                    "python",
                    [
                        Path.Combine(RepositoryLayout.Root, "tests", "Integration", "prepare-propack-preview.py"),
                        "--rid",
                        rid,
                        "--version",
                        version,
                        "--output",
                        distribution,
                    ],
                    root
                );
            }
            else
            {
                distribution = Path.GetFullPath(distribution, RepositoryLayout.Root);
                var packageName = Path.GetFileName(Assert.Single(Directory.GetFiles(distribution, "*.nupkg")));
                version = packageName["DotProlog.Packages.Cli.".Length..^".nupkg".Length];
            }
            foreach (
                var line in await File.ReadAllLinesAsync(
                    Path.Combine(distribution, "SHA256SUMS"),
                    TestContext.Current.CancellationToken
                )
            )
            {
                var parts = line.Split("  ", 2, StringSplitOptions.None);
                var hash = Convert.ToHexStringLower(
                    SHA256.HashData(
                        await File.ReadAllBytesAsync(Path.Combine(distribution, parts[1]), TestContext.Current.CancellationToken)
                    )
                );
                Assert.Equal(parts[0], hash);
            }
            var package = Path.Combine(distribution, "DotProlog.Packages.Cli." + version + ".nupkg");
            await using (var zip = await ZipFile.OpenReadAsync(package, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(zip.GetEntry("tools/net10.0/any/propack.dll"));
                Assert.NotNull(zip.GetEntry("tools/net10.0/any/DotProlog.Packages.dll"));
                Assert.NotNull(zip.GetEntry("tools/net10.0/any/DotnetToolSettings.xml"));
                Assert.DoesNotContain(
                    zip.Entries,
                    e =>
                        e.FullName.Contains(".agents", StringComparison.Ordinal)
                        || e.FullName.Contains(".pdb", StringComparison.Ordinal)
                );
            }
            var config = Path.Combine(root, "NuGet.Config");
            await Write(config, "<configuration><packageSources><clear /></packageSources></configuration>");
            var tools = Path.Combine(root, "installed tools");
            await Success(
                "dotnet",
                [
                    "tool",
                    "install",
                    "DotProlog.Packages.Cli",
                    "--tool-path",
                    tools,
                    "--version",
                    version,
                    "--configfile",
                    config,
                    "--add-source",
                    distribution,
                ],
                root
            );
            var installed = Path.Combine(tools, Exe("propack"));
            Assert.Equal("propack " + version, (await Success(installed, ["--version"], root)).Trim());
            var extracted = Path.Combine(root, "extracted native");
            await ZipFile.ExtractToDirectoryAsync(
                Assert.Single(Directory.GetFiles(distribution, "*.zip")),
                extracted,
                TestContext.Current.CancellationToken
            );
            var native = Path.Combine(extracted, Exe("propack"));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(native, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Equal("propack " + version, (await Success(native, ["--version"], root)).Trim());
            await Success(native, ["--help"], root);

            var source = Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Tool");
            var engineDirectory = Path.Combine(root, "engine");
            await Success(
                "dotnet",
                [
                    "publish",
                    source,
                    "-c",
                    "Release",
                    "-r",
                    rid,
                    "-o",
                    engineDirectory,
                    "--artifacts-path",
                    Path.Combine(root, "engine-build"),
                    "-nodereuse:false",
                ],
                root
            );
            var engine = Path.Combine(engineDirectory, Exe("dotnet-prolog"));
            var environment = new Dictionary<string, string?>
            {
                ["PATH"] = engineDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            };
            var fixture = Path.Combine(RepositoryLayout.Root, "tests", "fixtures", "packages", "swi-heaps");
            using var upstream = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(fixture, "UPSTREAM.json"), TestContext.Current.CancellationToken)
            );
            Assert.Equal(
                upstream.RootElement.GetProperty("sha256").GetString(),
                Convert.ToHexStringLower(
                    SHA256.HashData(
                        await File.ReadAllBytesAsync(Path.Combine(fixture, "heaps.pl"), TestContext.Current.CancellationToken)
                    )
                )
            );

            foreach (var cli in new[] { installed, native })
            {
                var consumer = Path.Combine(root, cli == installed ? "managed consumer" : "native consumer");
                var library = Path.Combine(consumer, "external package");
                var app = Path.Combine(consumer, "app");
                Directory.CreateDirectory(library);
                Directory.CreateDirectory(app);
                foreach (var file in Directory.GetFiles(fixture))
                    File.Copy(file, Path.Combine(library, Path.GetFileName(file)));
                var cache = Path.Combine(consumer, "cache");
                await Success(cli, ["init", "preview/consumer", "--project", app, "--cache", cache], consumer);
                await Success(
                    cli,
                    [
                        "add",
                        "swi/heaps",
                        "--path",
                        "../external package",
                        "--version",
                        "^1.0.0",
                        "--project",
                        app,
                        "--cache",
                        cache,
                    ],
                    consumer
                );
                await Success(cli, ["restore", "--project", app, "--cache", cache], consumer);
                await Success(cli, ["restore", "--locked", "--project", app, "--cache", cache], consumer);
                await Write(
                    Path.Combine(app, "main.pl"),
                    """
                    :- module(preview_consumer, []).
                    :- use_module(library(heaps)).
                    :- initialization(preview_consumer:main).
                    checks :-
                        empty_heap(Empty), is_heap(Empty), heap_size(Empty, 0),
                        add_to_heap(Empty, 2, two, H1), add_to_heap(H1, 1, one, H2),
                        min_of_heap(H2, 1, one), min_of_heap(H2, 1, one, 2, two),
                        heap_to_list(H2, [1-one,2-two]),
                        get_from_heap(H2, 1, one, Rest), heap_size(Rest, 1),
                        singleton_heap(Single, 0, zero), merge_heaps(Rest, Single, Merged),
                        heap_to_list(Merged, [0-zero,2-two]),
                        list_to_heap([3-three,1-one,2-two], H3),
                        delete_from_heap(H3, 2, two, H4), heap_to_list(H4, [1-one,3-three]),
                        \+ get_from_heap(Empty, _, _, _), \+ is_heap(not_a_heap).
                    main :- (checks -> writeln('heaps-preview: passed'); halt(1)).
                    """
                );
                foreach (var backend in new[] { "dotprolog", "swi" })
                {
                    var result = await ChildProcess.RunAsync(
                        cli,
                        ["run", "main.pl", "--engine", backend, "--project", app, "--cache", cache],
                        consumer,
                        environment
                    );
                    Assert.True(result.ExitCode == 0, result.Log);
                    Assert.Equal("heaps-preview: passed", result.Log.Trim());
                }
                // SWI must load the restored copy, rather than satisfying the probe from its bundled heaps library.
                using var locked = JsonDocument.Parse(
                    await File.ReadAllTextAsync(Path.Combine(app, "propack.lock.json"), TestContext.Current.CancellationToken)
                );
                var moduleFile = Path.Combine(
                        cache,
                        locked.RootElement.GetProperty("packages")[0].GetProperty("hash").GetString()!,
                        "heaps.pl"
                    )
                    .Replace('\\', '/');
                await Write(
                    Path.Combine(app, "source.pl"),
                    $":- use_module(library(heaps)).\n:- initialization(check).\ncheck :- (module_property(heaps, file(Loaded)), same_file(Loaded, '{moduleFile.Replace("'", "''", StringComparison.Ordinal)}') -> writeln('restored-source: passed'); halt(1)).\n"
                );
                var sourceCheck = await Success(
                    cli,
                    ["run", "source.pl", "--engine", "swi", "--project", app, "--cache", cache],
                    consumer
                );
                Assert.Equal("restored-source: passed", sourceCheck.Trim());
                await File.AppendAllTextAsync(
                    Path.Combine(library, "README.md"),
                    "\nLocal development edit.\n",
                    TestContext.Current.CancellationToken
                );
                var stale = await ChildProcess.RunAsync(
                    cli,
                    ["restore", "--locked", "--project", app, "--cache", cache],
                    consumer
                );
                Assert.Equal(65, stale.ExitCode);
                await Success(cli, ["update", "--project", app, "--cache", cache], consumer);
                await Success(cli, ["restore", "--locked", "--project", app, "--cache", cache], consumer);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static Task Write(string path, string text) =>
        File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

    private static async Task<string> Success(string executable, string[] args, string directory)
    {
        var result = await ChildProcess.RunAsync(executable, args, directory);
        Assert.True(result.ExitCode == 0, result.Log);
        return result.Log;
    }
}
