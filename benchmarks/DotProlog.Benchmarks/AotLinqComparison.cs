using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DotProlog.Benchmarks;

/// <summary>Compares NativeAOT tool size and process startup with both .NET 10 LINQ modes.</summary>
internal static class AotLinqComparison
{
    private const int DefaultIterations = 20;

    public static int Run(ReadOnlySpan<string> arguments)
    {
        if (!TryReadArguments(arguments, out int iterations, out string rid))
        {
            Console.Error.WriteLine("Usage: --aot-linq [iterations] [runtime-identifier]");
            return 64;
        }

        string repositoryRoot = FindRepositoryRoot();
        string comparisonRoot = Path.Combine(Path.GetTempPath(), $"dotprolog-aot-linq-{Guid.NewGuid():N}");
        Directory.CreateDirectory(comparisonRoot);

        try
        {
            ComparisonResult size = Measure(repositoryRoot, comparisonRoot, rid, iterations, useSizeOptimizedLinq: true);
            ComparisonResult speed = Measure(repositoryRoot, comparisonRoot, rid, iterations, useSizeOptimizedLinq: false);

            Console.WriteLine($"RID: {rid}; process launches per mode: {iterations}");
            Console.WriteLine("Mode\tPublish bytes\tMean startup (ms)");
            WriteResult(size);
            WriteResult(speed);
            return 0;
        }
        finally
        {
            Directory.Delete(comparisonRoot, recursive: true);
        }
    }

    private static ComparisonResult Measure(
        string repositoryRoot,
        string comparisonRoot,
        string rid,
        int iterations,
        bool useSizeOptimizedLinq
    )
    {
        string mode = useSizeOptimizedLinq ? "size" : "speed";
        string publishDirectory = Path.Combine(comparisonRoot, mode);
        string project = Path.Combine(repositoryRoot, "src", "DotProlog.Tool", "DotProlog.Tool.csproj");

        RunProcess(
            "dotnet",
            [
                "publish",
                project,
                "-c",
                "Release",
                "-r",
                rid,
                $"-p:UseSizeOptimizedLinq={useSizeOptimizedLinq.ToString().ToLowerInvariant()}",
                "-p:DebugType=none",
                "-p:PublishDocumentationFiles=false",
                "-o",
                publishDirectory,
            ],
            repositoryRoot,
            expectedOutput: null
        );

        string executable = Path.Combine(publishDirectory, OperatingSystem.IsWindows() ? "dotnet-prolog.exe" : "dotnet-prolog");
        string program = Path.Combine(repositoryRoot, "samples", "HelloProlog", "hello.pl");

        RunProcess(executable, ["run", program], repositoryRoot, "Hello! World!");

        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            RunProcess(executable, ["run", program], repositoryRoot, "Hello! World!");
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        long publishBytes = Directory
            .EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories)
            .Where(IsDeploymentFile)
            .Sum(path => new FileInfo(path).Length);

        return new ComparisonResult(mode, publishBytes, elapsed.TotalMilliseconds / iterations);
    }

    private static void RunProcess(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string? expectedOutput
    )
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with {process.ExitCode}:{Environment.NewLine}{output}{error}"
            );
        }

        if (expectedOutput is not null && output.Trim() != expectedOutput)
        {
            throw new InvalidOperationException($"{fileName} produced '{output.Trim()}' instead of '{expectedOutput}'.");
        }
    }

    private static bool TryReadArguments(ReadOnlySpan<string> arguments, out int iterations, out string rid)
    {
        iterations = DefaultIterations;
        rid = RuntimeInformation.RuntimeIdentifier;

        if (arguments.Length > 2)
        {
            return false;
        }

        if (
            arguments.Length >= 1
            && (!int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out iterations) || iterations <= 0)
        )
        {
            return false;
        }

        if (arguments.Length == 2)
        {
            rid = arguments[1];
        }

        return !string.IsNullOrWhiteSpace(rid);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DotProlog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the DotProlog repository root.");
    }

    private static bool IsDeploymentFile(string path)
    {
        if (Path.GetExtension(path) is ".pdb" or ".dbg")
        {
            return false;
        }

        return !path.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith(".dSYM", StringComparison.Ordinal));
    }

    private static void WriteResult(ComparisonResult result) =>
        Console.WriteLine(
            FormattableString.Invariant($"{result.Mode}\t{result.PublishBytes}\t{result.MeanStartupMilliseconds:F3}")
        );

    private readonly record struct ComparisonResult(string Mode, long PublishBytes, double MeanStartupMilliseconds);
}
