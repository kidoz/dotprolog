using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotProlog.Packages;

/// <summary>Shared commands for the standalone tool and DotProlog integration.</summary>
public static partial class PackageCommands
{
    /// <summary>Executes a package command; returns 64 for usage and 65 for package errors.</summary>
    public static int Execute(string[] arguments, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (arguments is ["--version"])
        {
            var version = typeof(PackageCommands)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion.Split('+')[0];
            output.WriteLine("propack " + version);
            return 0;
        }
        try
        {
            var args = arguments.ToList();
            var directory = TakeOption(args, "--project") ?? Directory.GetCurrentDirectory();
            var cache = TakeOption(args, "--cache");
            var engine = TakeOption(args, "--engine");
            var workspace = new PackageWorkspace(directory, cache);
            if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
            {
                output.WriteLine(
                    "propack init owner/name | add owner/name [--version ^1.2.3] [--path DIR | --git URL --rev REV | --archive ZIP --sha256 HASH]"
                );
                output.WriteLine(
                    "propack remove owner/name | restore [--locked] | update | tree | run main.pl [--engine swi|dotprolog]"
                );
                output.WriteLine("propack --version");
                output.WriteLine(
                    "All commands accept --project DIR and --cache DIR. Run requires an installed engine; restore does not."
                );
                return args.Count == 0 ? 64 : 0;
            }
            var command = args[0];
            args.RemoveAt(0);
            switch (command)
            {
                case "init":
                    RequireCount(args, 1);
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "propack.json");
                    if (File.Exists(path))
                        throw new InvalidDataException("propack.json already exists.");
                    WriteManifest(path, new PackageManifest { Name = args[0], Engine = engine ?? "dotprolog" });
                    PackageWorkspace.ReadManifest(directory);
                    output.WriteLine("Created propack.json.");
                    return 0;
                case "add":
                    var source = new PackageSource
                    {
                        Version = TakeOption(args, "--version") ?? "*",
                        Path = TakeOption(args, "--path"),
                        Git = TakeOption(args, "--git"),
                        Revision = TakeOption(args, "--rev"),
                        Archive = TakeOption(args, "--archive"),
                        Sha256 = TakeOption(args, "--sha256"),
                    };
                    RequireCount(args, 1);
                    var manifest = PackageWorkspace.ReadManifest(directory);
                    manifest.Dependencies[args[0]] = source;
                    WriteManifest(Path.Combine(directory, "propack.json"), manifest);
                    output.WriteLine("Dependency recorded; run update to resolve the graph.");
                    return 0;
                case "remove":
                    RequireCount(args, 1);
                    var current = PackageWorkspace.ReadManifest(directory);
                    if (!current.Dependencies.Remove(args[0]))
                        throw new InvalidDataException("Dependency is not declared.");
                    WriteManifest(Path.Combine(directory, "propack.json"), current);
                    output.WriteLine("Dependency removed; run update to resolve the graph.");
                    return 0;
                case "restore":
                    var locked = args.Remove("--locked");
                    RequireCount(args, 0);
                    workspace.Restore(locked);
                    output.WriteLine("Packages restored and verified.");
                    return 0;
                case "update":
                    RequireCount(args, 0);
                    workspace.Restore(update: true);
                    output.WriteLine("Dependency lock updated.");
                    return 0;
                case "tree":
                    RequireCount(args, 0);
                    output.Write(workspace.Describe());
                    return 0;
                case "run":
                    RequireCount(args, 1);
                    return Run(workspace, directory, args[0], engine ?? workspace.DefaultEngine, output, error, cache);
                default:
                    throw new ArgumentException($"Unknown package command: {command}");
            }
        }
        catch (Exception exception)
            when (exception
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or ArgumentException
                        or InvalidOperationException
                        or Win32Exception
                        or HttpRequestException
                        or OperationCanceledException
            )
        {
            error.WriteLine($"propack: {exception.Message}");
            return exception is ArgumentException ? 64 : 65;
        }
    }

    private static int Run(
        PackageWorkspace workspace,
        string directory,
        string file,
        string engine,
        TextWriter output,
        TextWriter error,
        string? cache
    )
    {
        var source = Path.GetFullPath(Path.Combine(directory, file));
        if (!File.Exists(source))
            throw new InvalidDataException($"Source file not found: {source}");
        var start = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetFullPath(directory),
        };
        if (engine == "swi")
        {
            var versionText = PackageFiles.ProcessOutput("swipl", ["--version"]);
            var version = VersionPattern().Match(versionText).Value;
            if (version.Length == 0)
                throw new InvalidDataException("Could not determine SWI-Prolog version.");
            var modules = workspace.GetLibraries(engine, version);
            start.FileName = "swipl";
            foreach (var dir in modules.Values.Select(p => Path.GetDirectoryName(p)!).Distinct(StringComparer.Ordinal))
            {
                start.ArgumentList.Add("-p");
                start.ArgumentList.Add("library=" + QuoteAtom(dir));
            }
            foreach (var argument in new[] { "-q", "-f", "none", "-s", source, "-g", "halt", "-t", "halt(1)" })
                start.ArgumentList.Add(argument);
        }
        else if (engine == "dotprolog")
        {
            workspace.GetLibraries(engine);
            start.FileName = "dotnet-prolog";
            foreach (var argument in new[] { "run", "--packages", Path.GetFullPath(directory), source })
                start.ArgumentList.Add(argument);
            if (cache is not null)
                start.Environment["PROPACK_CACHE"] = Path.GetFullPath(cache);
        }
        else
            throw new ArgumentException("Engine must be swi or dotprolog.");
        using var process = Process.Start(start) ?? throw new IOException("Could not start Prolog engine.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        output.Write(stdout.GetAwaiter().GetResult());
        error.Write(stderr.GetAwaiter().GetResult());
        return process.ExitCode;
    }

    private static string QuoteAtom(string text) =>
        "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string? TakeOption(List<string> args, string option)
    {
        var index = args.IndexOf(option);
        if (index < 0)
            return null;
        if (index + 1 == args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Missing value after {option}.");
        var value = args[index + 1];
        args.RemoveRange(index, 2);
        return value;
    }

    private static void RequireCount(List<string> args, int count)
    {
        if (args.Count != count || args.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
            throw new ArgumentException("Unexpected arguments; run propack help.");
    }

    private static void WriteManifest(string path, PackageManifest manifest)
    {
        PackageWorkspace.ValidateManifest(manifest);
        PackageFiles.WriteAtomic(path, JsonSerializer.Serialize(manifest, PackageJsonContext.Default.PackageManifest) + "\n");
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
