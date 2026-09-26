using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using DotProlog.CodeGen.IL;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            return await ExecuteAsync(args, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    internal static async Task<int> ExecuteAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        if (args is ["--version"])
        {
            var version = typeof(Program)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;
            await output.WriteLineAsync("plc " + version.Split('+')[0]).ConfigureAwait(false);
            return 0;
        }
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            await output
                .WriteLineAsync(
                    "Usage: plc <source.pl> [more.pl ...] --output <new-directory> [--aot] [--rid <RID>] [--mode modern|strict-iso] [--flag name=value]"
                )
                .ConfigureAwait(false);
            await output
                .WriteLineAsync("Writes PrologProgram.dll directly as IL. --aot additionally publishes a native executable.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("Use --version to print the compiler version.").ConfigureAwait(false);
            return args.Length == 0 ? 64 : 0;
        }
        List<string> sources = [];
        List<string> flags = [];
        string? destination = null;
        string? rid = null;
        var mode = PrologLanguageMode.Modern;
        var aot = false;
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument == "--aot")
            {
                aot = true;
                continue;
            }
            if (argument is "--output" or "-o" or "--rid" or "--mode" or "--flag")
            {
                if (++i == args.Length)
                {
                    await error.WriteLineAsync($"error: missing value after {argument}").ConfigureAwait(false);
                    return 64;
                }
                var value = args[i];
                switch (argument)
                {
                    case "--output" or "-o":
                        destination = value;
                        break;
                    case "--rid":
                        rid = value;
                        break;
                    case "--flag":
                        flags.Add(value);
                        break;
                    case "--mode":
                        if (!PrologLanguageModes.TryParse(value, out mode))
                        {
                            await error.WriteLineAsync($"error: expected mode {PrologLanguageModes.Names}").ConfigureAwait(false);
                            return 64;
                        }
                        break;
                }
            }
            else if (argument.StartsWith('-'))
            {
                await error.WriteLineAsync($"error: unknown option {argument}").ConfigureAwait(false);
                return 64;
            }
            else
            {
                sources.Add(argument);
            }
        }
        if (sources.Count == 0 || string.IsNullOrWhiteSpace(destination) || (rid is not null && !aot))
        {
            await error.WriteLineAsync("error: sources and --output are required; --rid requires --aot").ConfigureAwait(false);
            return 64;
        }
        if (!PrologFlagOverrides.TryParse(string.Join(';', flags), out var overrides, out var flagError))
        {
            await error.WriteLineAsync($"error: {flagError}").ConfigureAwait(false);
            return 64;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination = Path.GetFullPath(destination);
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                await error.WriteLineAsync("error: output path already exists; choose a new directory").ConfigureAwait(false);
                return 64;
            }
            List<(string Name, string Text)> inputs = [];
            foreach (var path in sources)
            {
                inputs.Add((Path.GetFullPath(path), await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)));
            }
            using var assembly = new MemoryStream();
            var diagnostics = IlAssemblyEmitter.Emit(inputs, "PrologProgram", assembly, mode, overrides);
            foreach (var diagnostic in diagnostics)
            {
                await error.WriteLineAsync(diagnostic.ToString()).ConfigureAwait(false);
            }
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return 65;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await CompilationArtifacts.WriteAsync(destination, assembly.ToArray(), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"IL assembly: {Path.Combine(destination, "PrologProgram.dll")}").ConfigureAwait(false);
            if (!aot)
            {
                return 0;
            }
            return await PublishAsync(
                    destination,
                    rid ?? System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                    output,
                    error,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("error: compilation cancelled or native publishing timed out").ConfigureAwait(false);
            return 130;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or PrologException
                        or Win32Exception
            )
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return 70;
        }
    }

    private static async Task<int> PublishAsync(
        string directory,
        string rid,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (
            var argument in new[]
            {
                "publish",
                "PrologProgram.csproj",
                "-c",
                "Release",
                "-r",
                rid,
                "-o",
                "native",
                "--nologo",
                "-nodereuse:false",
            }
        )
        {
            start.ArgumentList.Add(argument);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using var process = Process.Start(start) ?? throw new IOException("Could not start dotnet publish.");
        process.StandardInput.Close();
        var stdout = PumpAsync(process.StandardOutput, output, timeout.Token);
        var stderr = PumpAsync(process.StandardError, error, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The child exited between the state check and Kill.
                }
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
            }
        }
    }

    private static async Task PumpAsync(StreamReader source, TextWriter destination, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
    }
}
