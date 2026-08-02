using System.Diagnostics;

namespace Integration.Tests;

/// <summary>
/// Runs a build tool as a child process and collects what it wrote.
/// </summary>
/// <remarks>
/// <para>
/// Two things here are not incidental. Standard input is redirected and closed immediately, because
/// a child that inherits the runner's standard input can block on it forever; closed, any read
/// returns end of input instead. And a child that does not finish is killed rather than waited on,
/// because a hung build in a test suite is indistinguishable from a slow one until the job's own
/// limit is reached — which cost this repository fifteen hours of runner time before it was caught.
/// </para>
/// <para>
/// The timeout is generous. These tests publish NativeAOT binaries, which legitimately takes
/// minutes on a loaded machine; the number only has to be smaller than a CI job's patience.
/// </para>
/// </remarks>
internal static class ChildProcess
{
    /// <summary>How long a single child may run before it is treated as hung.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromMinutes(15);

    /// <summary>Runs <paramref name="fileName"/> and returns its exit code and combined output.</summary>
    /// <param name="fileName">Executable to run.</param>
    /// <param name="arguments">Arguments, passed without shell interpretation.</param>
    /// <param name="workingDirectory">Directory to run in.</param>
    /// <exception cref="TimeoutException">The child did not finish within <see cref="Limit"/>.</exception>
    internal static async Task<(int ExitCode, string Log)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory
    )
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");

        process.StandardInput.Close();

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        using var expiry = new CancellationTokenSource(Limit);

        try
        {
            await process.WaitForExitAsync(expiry.Token);
        }
        catch (OperationCanceledException)
        {
            // The whole tree, because dotnet leaves MSBuild worker processes behind it.
            process.Kill(entireProcessTree: true);

            var partial = await DrainAsync(standardOutput, standardError);
            throw new TimeoutException(
                $"{fileName} {string.Join(' ', arguments)} did not finish within "
                    + $"{Limit.TotalMinutes.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} minutes. "
                    + $"Output so far:\n{partial}"
            );
        }

        // The same deadline bounds the output reads: a child that exited can still have handed its
        // output pipe to a detached grandchild, and an unbounded read would then hang until the CI
        // job's own limit.
        try
        {
            return (process.ExitCode, await standardOutput.WaitAsync(expiry.Token) + await standardError.WaitAsync(expiry.Token));
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"{fileName} {string.Join(' ', arguments)} exited, but its standard output or error "
                    + "pipe stayed open past the limit — a process it started still holds it."
            );
        }
    }

    /// <summary>Collects whatever output is ready, without waiting on a pipe a survivor may hold.</summary>
    private static async Task<string> DrainAsync(Task<string> standardOutput, Task<string> standardError)
    {
        try
        {
            return await standardOutput.WaitAsync(TimeSpan.FromSeconds(5))
                + await standardError.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            return "(unavailable: a surviving process still holds the output pipe)";
        }
    }
}
