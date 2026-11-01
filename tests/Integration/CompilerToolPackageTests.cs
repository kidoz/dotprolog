using System.IO.Compression;

namespace Integration.Tests;

/// <summary>Proves that the packed plc tool carries everything needed by an external consumer.</summary>
public sealed class CompilerToolPackageTests
{
    [Fact]
    public async Task InstalledCompilerEmitsAndPublishesOutsideTheRepository()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DOTPROLOG_RUN_AOT_TESTS") == "1",
            "Set DOTPROLOG_RUN_AOT_TESTS=1 to pack, install and exercise the standalone compiler."
        );
        var root = Directory.CreateTempSubdirectory("dotprolog-compiler-package-").FullName;
        try
        {
            // A unique version prevents a cached or published package from satisfying this install.
            var version = "0.0.0-test." + Guid.NewGuid().ToString("N");
            var feed = Path.Combine(root, "feed");
            var project = Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Compiler.Cli");
            var (packExit, packLog) = await ChildProcess.RunAsync(
                "dotnet",
                [
                    "pack",
                    project,
                    "-c",
                    "Release",
                    "-o",
                    feed,
                    "--artifacts-path",
                    Path.Combine(root, "build"),
                    "-p:Version=" + version,
                    "-nodereuse:false",
                ],
                root
            );
            Assert.True(packExit == 0, packLog);
            await using (
                var package = await ZipFile.OpenReadAsync(
                    Path.Combine(feed, "DotProlog.Compiler.Cli." + version + ".nupkg"),
                    TestContext.Current.CancellationToken
                )
            )
            {
                foreach (
                    var assembly in new[]
                    {
                        "plc",
                        "DotProlog.CodeGen.IL",
                        "DotProlog.Compiler",
                        "DotProlog.Syntax",
                        "DotProlog.Runtime",
                        "DotProlog.Packages",
                    }
                )
                {
                    Assert.NotNull(package.GetEntry("tools/net10.0/any/" + assembly + ".dll"));
                }
                Assert.NotNull(package.GetEntry("tools/net10.0/any/DotnetToolSettings.xml"));
            }
            var consumer = Directory.CreateDirectory(Path.Combine(root, "consumer with spaces")).FullName;
            var config = Path.Combine(root, "tool-install.config");
            await File.WriteAllTextAsync(
                config,
                "<configuration><packageSources><clear /></packageSources></configuration>",
                TestContext.Current.CancellationToken
            );
            var toolPath = Path.Combine(root, "tools");
            var (installExit, installLog) = await ChildProcess.RunAsync(
                "dotnet",
                [
                    "tool",
                    "install",
                    "DotProlog.Compiler.Cli",
                    "--version",
                    version,
                    "--tool-path",
                    toolPath,
                    "--configfile",
                    config,
                    "--add-source",
                    feed,
                ],
                consumer
            );
            Assert.True(installExit == 0, installLog);
            var compiler = Path.Combine(toolPath, OperatingSystem.IsWindows() ? "plc.exe" : "plc");
            var (versionExit, versionLog) = await ChildProcess.RunAsync(compiler, ["--version"], consumer);
            Assert.True(versionExit == 0, versionLog);
            Assert.Equal("plc " + version, versionLog.Trim());
            var (helpExit, helpLog) = await ChildProcess.RunAsync(compiler, ["--help"], consumer);
            Assert.True(helpExit == 0, helpLog);
            Assert.Contains("Usage: plc", helpLog, StringComparison.Ordinal);

            var workspace = Path.Combine(consumer, "package workspace");
            var cache = Path.Combine(consumer, "package cache");
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "propack.json"),
                "{\"name\":\"demo/app\",\"version\":\"1.0.0\",\"modules\":{\"words\":\"words.pl\"}}",
                TestContext.Current.CancellationToken
            );
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "words.pl"),
                ":- module(words, [word/1]).\nword(hello).\n",
                TestContext.Current.CancellationToken
            );
            new DotProlog.Packages.PackageWorkspace(workspace, cache).Restore();
            var source = Path.Combine(consumer, "program with spaces.pl");
            await File.WriteAllTextAsync(
                source,
                """
                :- use_module(library(words)).
                parent(a,b).
                parent(b,c).
                ancestor(X,Y) :- parent(X,Y).
                ancestor(X,Y) :- parent(X,Z), ancestor(Z,Y).
                main :- findall(X,ancestor(a,X),[b,c]), word(hello), writeln('installed-plc: passed').
                :- initialization(main).
                """,
                TestContext.Current.CancellationToken
            );
            var (compileExit, compileLog) = await ChildProcess.RunAsync(
                compiler,
                ["program with spaces.pl", "--output", "output with spaces", "--packages", workspace, "--aot"],
                consumer
            );
            Assert.True(compileExit == 0, compileLog);
            Assert.DoesNotContain("warning IL", compileLog, StringComparison.Ordinal);
            var output = Path.Combine(consumer, "output with spaces");
            Assert.True(File.Exists(Path.Combine(output, "PrologProgram.pdb")));
            var intermediateSymbols = Assert.Single(
                Directory.GetFiles(Path.Combine(output, "obj"), "PrologProgram.pdb", SearchOption.AllDirectories)
            );
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(output, "PrologProgram.pdb"), TestContext.Current.CancellationToken),
                await File.ReadAllBytesAsync(intermediateSymbols, TestContext.Current.CancellationToken)
            );
            var compilerArguments = Assert.Single(
                Directory.GetFiles(Path.Combine(output, "obj"), "PrologProgram.ilc.rsp", SearchOption.AllDirectories)
            );
            Assert.Contains("-g", await File.ReadAllLinesAsync(compilerArguments, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories));
            File.Delete(source);
            Directory.Delete(workspace, recursive: true);
            Directory.Delete(cache, recursive: true);
            var (managedExit, managedLog) = await ChildProcess.RunAsync(
                "dotnet",
                [Path.Combine(output, "PrologProgram.dll")],
                consumer
            );
            Assert.True(managedExit == 0, managedLog);
            Assert.Equal("installed-plc: passed", managedLog.Trim());
            var executable = OperatingSystem.IsWindows() ? "PrologProgram.exe" : "PrologProgram";
            var (nativeExit, nativeLog) = await ChildProcess.RunAsync(Path.Combine(output, "native", executable), [], consumer);
            Assert.True(nativeExit == 0, nativeLog);
            Assert.Equal("installed-plc: passed", nativeLog.Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
