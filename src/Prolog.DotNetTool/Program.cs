using Prolog.Compiler;
using Prolog.Runtime;
using Prolog.Syntax;

namespace Prolog.DotNetTool;

/// <summary>Command line entry point for the <c>dotnet prolog</c> tool.</summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 64;
    private const int ExitCompileError = 65;
    private const int ExitGoalFailed = 66;
    private const int ExitRuntimeError = 70;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WriteUsage(Console.Out);
            return args.Length == 0 ? ExitUsage : ExitSuccess;
        }

        return args[0] switch
        {
            "run" => Run(args.AsSpan(1)),
            _ => UnknownCommand(args[0]),
        };
    }

    private static int Run(ReadOnlySpan<string> args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: dotnet prolog run <file.pl>");
            return ExitUsage;
        }

        string path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: file not found: {path}");
            return ExitUsage;
        }

        var engine = new PrologEngine();

        LoadResult loaded = engine.ConsultFile(path);
        foreach (Diagnostic diagnostic in loaded.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic.ToString());
        }

        if (!loaded.Success)
        {
            return ExitCompileError;
        }

        try
        {
            RunResult result = engine.RunPendingGoals();
            Console.Out.Flush();

            return result switch
            {
                RunResult.Halted => engine.Machine.ExitCode,
                RunResult.Failure => ExitGoalFailed,
                _ => ExitSuccess,
            };
        }
        catch (PrologException exception)
        {
            Console.Out.Flush();
            Console.Error.WriteLine($"error: {exception.Message}");
            return ExitRuntimeError;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        WriteUsage(Console.Error);
        return ExitUsage;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("dotnet prolog — run Prolog on .NET");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  dotnet prolog run <file.pl>   consult a file and run its goals");
    }
}
