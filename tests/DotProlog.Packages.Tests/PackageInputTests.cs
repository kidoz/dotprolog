using System.Diagnostics;

namespace DotProlog.Packages.Tests;

public sealed class PackageInputTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "propack inputs " + Guid.NewGuid().ToString("N"));

    public PackageInputTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => PackageFiles.DeleteDirectory(_directory);

    [Theory]
    [InlineData("source-file")]
    [InlineData("source-manifest")]
    [InlineData("archive")]
    [InlineData("root-manifest")]
    [InlineData("lock")]
    [InlineData("catalog")]
    [InlineData("root-module")]
    public async Task FIFOs_fail_restore_without_waiting_for_a_writer(string location)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "FIFOs are Unix inputs.");
        var app = Path.Combine(_directory, "app");
        var source = Path.Combine(_directory, "source");
        var cache = Path.Combine(_directory, "cache");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(source);
        await Write(
            Path.Combine(app, "propack.json"),
            "{\"name\":\"demo/app\",\"dependencies\":{\"demo/lib\":{\"path\":\"../source\"}}}"
        );
        await Write(Path.Combine(source, "propack.json"), "{\"name\":\"demo/lib\"}");
        var fifo = location switch
        {
            "source-manifest" => Path.Combine(source, "propack.json"),
            "root-manifest" => Path.Combine(app, "propack.json"),
            "lock" => Path.Combine(app, "propack.lock.json"),
            "catalog" => Path.Combine(app, "catalog.json"),
            "root-module" => Path.Combine(app, "lib.pl"),
            _ => Path.Combine(source, "input.inc"),
        };
        if (location == "archive")
            await Write(
                Path.Combine(app, "propack.json"),
                "{\"name\":\"demo/app\",\"dependencies\":{\"demo/lib\":{\"archive\":\"../source/input.inc\"}}}"
            );
        if (location == "catalog")
            await Write(Path.Combine(app, "propack.json"), "{\"name\":\"demo/app\",\"catalog\":\"catalog.json\"}");
        if (location == "root-module")
            await Write(Path.Combine(app, "propack.json"), "{\"name\":\"demo/app\",\"modules\":{\"lib\":\"lib.pl\"}}");
        File.Delete(fifo);
        PackageFiles.ProcessOutput("mkfifo", [fifo]);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { CliPath(), "restore", "--project", app, "--cache", cache })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Fail("Restore blocked on a FIFO for more than 10 seconds.");
        }
        var diagnostics = await output + await error;
        Assert.True(process.ExitCode == 65, diagnostics);
        Assert.Contains("not a regular file", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_inputs_are_rejected_before_reading()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This check uses a Unix device.");
        Assert.Throws<InvalidDataException>(() => PackageFiles.HashFile("/dev/null"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_rejects_file_and_parent_symlinks_after_the_walk(bool parent)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links can require additional Windows privileges.");
        var data = Path.Combine(_directory, "data");
        Directory.CreateDirectory(data);
        var file = Path.Combine(data, "input.inc");
        File.WriteAllText(file, "original");
        Assert.Single(PackageFiles.Files(_directory));
        string relative;
        if (parent)
        {
            Directory.Move(data, Path.Combine(_directory, "moved"));
            Directory.CreateSymbolicLink(data, Path.Combine(_directory, "moved"));
            relative = "data/input.inc";
        }
        else
        {
            File.Move(file, Path.Combine(data, "moved.inc"));
            File.CreateSymbolicLink(file, Path.Combine(data, "moved.inc"));
            relative = "data/input.inc";
        }
        Assert.Throws<InvalidDataException>(() =>
        {
            using var input = PackageInput.OpenRead(_directory, relative);
        });
    }

    [Fact]
    public void Copy_limit_counts_bytes_actually_read_across_inputs()
    {
        using var input = new MemoryStream(new byte[8192]);
        long total = 128 * 1024 * 1024 - 1;
        Assert.Throws<InvalidDataException>(() => PackageFiles.CopyBounded(input, Stream.Null, ref total));
    }

    private static Task Write(string path, string text) =>
        File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

    private static string CliPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotProlog.slnx")))
            directory = directory.Parent;
        if (directory is null)
            throw new InvalidOperationException("Could not locate the package CLI in the repository.");
        return Path.Combine(
            directory.FullName,
            "src",
            "DotProlog.Packages.Cli",
            "bin",
            new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name,
            "net10.0",
            "propack.dll"
        );
    }
}
