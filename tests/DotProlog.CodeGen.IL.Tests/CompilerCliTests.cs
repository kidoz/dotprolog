using System.Globalization;
using DotProlog.Compiler.Cli;

namespace DotProlog.CodeGen.IL.Tests;

public sealed class CompilerCliTests
{
    [Theory]
    [InlineData("--help", "Usage: plc")]
    [InlineData("--version", "plc ")]
    public async Task InformationalOptionsSucceedWithoutSources(string argument, string expectedPrefix)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var exit = await Program.ExecuteAsync([argument], output, error, TestContext.Current.CancellationToken);
        Assert.Equal(0, exit);
        Assert.StartsWith(expectedPrefix, output.ToString(), StringComparison.Ordinal);
        Assert.NotEqual(expectedPrefix, output.ToString().TrimEnd());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--output")]
    [InlineData("--packages")]
    [InlineData("--mode", "invalid")]
    [InlineData("--rid", "osx-arm64")]
    public async Task InvalidArgumentsReturnUsage(params string[] arguments)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var exit = await Program.ExecuteAsync(arguments, output, error, TestContext.Current.CancellationToken);
        Assert.Equal(64, exit);
        Assert.Contains("error:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceErrorsLeaveNoOutputAndExistingDirectoriesArePreserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotprolog-il-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "bad.pl");
            var destination = Path.Combine(root, "output");
            await File.WriteAllTextAsync(source, "p( .", TestContext.Current.CancellationToken);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            var exit = await Program.ExecuteAsync(
                [source, "--output", destination],
                TextWriter.Null,
                error,
                TestContext.Current.CancellationToken
            );
            Assert.Equal(65, exit);
            Assert.False(Directory.Exists(destination));
            Assert.Contains("DPL", error.ToString(), StringComparison.Ordinal);
            Directory.CreateDirectory(destination);
            var sentinel = Path.Combine(destination, "keep.txt");
            await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken);
            exit = await Program.ExecuteAsync(
                [source, "--output", destination],
                TextWriter.Null,
                error,
                TestContext.Current.CancellationToken
            );
            Assert.Equal(64, exit);
            Assert.Equal("keep", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PathsWithSpacesAndQuotesProduceOnlyIlAndPublishingAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotprolog-il-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "quoted ' source.pl");
            var destination = Path.Combine(root, "quoted ' output");
            await File.WriteAllTextAsync(source, ":- initialization(writeln(hello)).", TestContext.Current.CancellationToken);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            var exit = await Program.ExecuteAsync(
                [source, "--output", destination],
                TextWriter.Null,
                error,
                TestContext.Current.CancellationToken
            );
            Assert.True(exit == 0, error.ToString());
            Assert.True(File.Exists(Path.Combine(destination, "PrologProgram.dll")));
            Assert.True(File.Exists(Path.Combine(destination, "PrologProgram.pdb")));
            Assert.Empty(Directory.GetFiles(destination, "*.cs"));
            var project = await File.ReadAllTextAsync(
                Path.Combine(destination, "PrologProgram.csproj"),
                TestContext.Current.CancellationToken
            );
            Assert.Contains("Name=\"CoreCompile\"", project, StringComparison.Ordinal);
            Assert.Contains("<PublishAot>true</PublishAot>", project, StringComparison.Ordinal);
            Assert.Contains("<ImportDirectoryBuildProps>false", project, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageWorkspaceCompilesAndRunsWithoutPackageSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotprolog-il-packages-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(workspace);
        try
        {
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
            var source = Path.Combine(root, "main.pl");
            var destination = Path.Combine(root, "output");
            await File.WriteAllTextAsync(
                source,
                ":- use_module(library(words)).\n:- initialization(word(hello)).\n",
                TestContext.Current.CancellationToken
            );
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            var exit = await Program.ExecuteAsync(
                [source, "--output", destination, "--packages", workspace],
                TextWriter.Null,
                error,
                TestContext.Current.CancellationToken
            );
            Assert.True(exit == 0, error.ToString());
            Directory.Delete(workspace, recursive: true);
            Directory.Delete(cache, recursive: true);

            var context = new System.Runtime.Loader.AssemblyLoadContext(null, isCollectible: true);
            try
            {
                using var image = File.OpenRead(Path.Combine(destination, "PrologProgram.dll"));
                var run = context.LoadFromStream(image).EntryPoint!.CreateDelegate<Func<int>>();
                Assert.Equal(0, run());
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageErrorsReturnCompileErrorAndLeaveNoOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotprolog-il-package-error-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "propack.json"),
                "{\"name\":\"demo/app\"}",
                TestContext.Current.CancellationToken
            );
            var source = Path.Combine(root, "main.pl");
            var destination = Path.Combine(root, "output");
            await File.WriteAllTextAsync(source, "p(a).\n", TestContext.Current.CancellationToken);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            var exit = await Program.ExecuteAsync(
                [source, "--output", destination, "--packages", workspace],
                TextWriter.Null,
                error,
                TestContext.Current.CancellationToken
            );
            Assert.Equal(65, exit);
            Assert.Contains("Missing propack.lock.json", error.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
