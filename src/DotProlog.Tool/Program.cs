using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Tool;

/// <summary>Command line entry point for the <c>dotnet prolog</c> tool.</summary>
internal static class Program
{
    private const string LanguageModeNames = PrologLanguageModes.Names;
    private const int ExitLintWarnings = 1;
    private const int ExitSuccess = 0;
    private const int ExitUsage = 64;
    private const int ExitCompileError = 65;
    private const int ExitGoalFailed = 66;
    private const int ExitRuntimeError = 70;

    private static int Main(string[] args) => Execute(args, Console.Out, Console.Error);

    /// <summary>Executes a tool command against explicit output streams.</summary>
    internal static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WriteUsage(output);
            return args.Length == 0 ? ExitUsage : ExitSuccess;
        }

        return args[0] switch
        {
            "lint" => Lint(args.AsSpan(1), error),
            "run" => Run(args.AsSpan(1), output, error),
            _ => UnknownCommand(args[0], error),
        };
    }

    private static int Run(ReadOnlySpan<string> args, TextWriter output, TextWriter error)
    {
        PrologLanguageMode languageMode = PrologLanguageMode.Extended;
        if (args.Length > 1 && args[0] == "--mode")
        {
            if (!PrologLanguageModes.TryParse(args[1], out languageMode))
            {
                error.WriteLine($"error: unknown language mode: {args[1]}");
                error.WriteLine($"       expected one of: {LanguageModeNames}");
                return ExitUsage;
            }

            args = args[2..];
        }

        if (args.Length != 1)
        {
            error.WriteLine($"Usage: dotnet prolog run [--mode {LanguageModeNames}] <file.pl>");
            return ExitUsage;
        }

        string path = args[0];
        if (!File.Exists(path))
        {
            error.WriteLine($"error: file not found: {path}");
            return ExitUsage;
        }

        var engine = new PrologEngine(languageMode);
        engine.Output = output;
        engine.Error = error;

        LoadResult loaded = engine.ConsultFile(path);
        foreach (Diagnostic diagnostic in loaded.Diagnostics)
        {
            error.WriteLine(diagnostic.ToString());
        }

        if (!loaded.Success)
        {
            return ExitCompileError;
        }

        try
        {
            RunResult result = engine.RunPendingGoals();
            output.Flush();

            return result switch
            {
                RunResult.Halted => engine.Machine.ExitCode,
                RunResult.Failure => ExitGoalFailed,
                _ => ExitSuccess,
            };
        }
        catch (PrologException exception)
        {
            output.Flush();
            error.WriteLine($"error: {exception.Message}");
            return ExitRuntimeError;
        }
    }

    private static int Lint(ReadOnlySpan<string> args, TextWriter error)
    {
        PrologLanguageMode languageMode = PrologLanguageMode.Extended;
        bool warningsAsErrors = false;
        List<string> paths = [];

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument == "--warnings-as-errors")
            {
                warningsAsErrors = true;
                continue;
            }

            if (argument == "--mode")
            {
                if (index + 1 >= args.Length)
                {
                    error.WriteLine("error: missing language mode after --mode");
                    error.WriteLine($"       expected one of: {LanguageModeNames}");
                    return ExitUsage;
                }

                string selected = args[++index];
                if (!PrologLanguageModes.TryParse(selected, out languageMode))
                {
                    error.WriteLine($"error: unknown language mode: {selected}");
                    error.WriteLine($"       expected one of: {LanguageModeNames}");
                    return ExitUsage;
                }

                continue;
            }

            if (argument.StartsWith('-'))
            {
                error.WriteLine($"error: unknown lint option: {argument}");
                WriteLintUsage(error);
                return ExitUsage;
            }

            paths.Add(argument);
        }

        if (paths.Count == 0)
        {
            WriteLintUsage(error);
            return ExitUsage;
        }

        bool foundErrors = false;
        bool foundWarnings = false;
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                error.WriteLine($"error: file not found: {path}");
                foundErrors = true;
                continue;
            }

            string absolute = Path.GetFullPath(path);
            string source;
            try
            {
                source = File.ReadAllText(absolute);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"error: cannot read {path}: {exception.Message}");
                foundErrors = true;
                continue;
            }

            var program = new BytecodeProgram(languageMode);
            ParseResult parsed = TermReader.ReadProgram(
                source,
                absolute,
                program.Operators,
                program.CharacterConversions,
                program.Flags
            );
            IReadOnlyList<Diagnostic> diagnostics = [.. parsed.Diagnostics, .. PrologLinter.Analyze(parsed.Clauses, absolute)];

            foreach (Diagnostic diagnostic in diagnostics)
            {
                error.WriteLine(diagnostic.ToString());
                foundErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
                foundWarnings |= diagnostic.Severity == DiagnosticSeverity.Warning;
            }
        }

        if (foundErrors)
        {
            return ExitCompileError;
        }

        return warningsAsErrors && foundWarnings ? ExitLintWarnings : ExitSuccess;
    }

    private static int UnknownCommand(string command, TextWriter error)
    {
        error.WriteLine($"error: unknown command '{command}'");
        WriteUsage(error);
        return ExitUsage;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("dotnet prolog — run and analyze Prolog on .NET");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine($"  dotnet prolog run [--mode {LanguageModeNames}] <file.pl>");
        output.WriteLine("      consult a file and run its goals");
        output.WriteLine($"  dotnet prolog lint [--mode {LanguageModeNames}] [--warnings-as-errors] <file.pl>...");
        output.WriteLine("      analyze source without consulting it or executing directives");
        output.WriteLine();
        output.WriteLine("Language modes:");
        output.WriteLine("  extended     ISO plus the DotProlog extensions (default)");
        output.WriteLine("  strict-iso   only the standardized ISO/IEC 13211 surface");
        output.WriteLine("  modern       extended, with double_quotes starting at chars");
    }

    private static void WriteLintUsage(TextWriter output) =>
        output.WriteLine($"Usage: dotnet prolog lint [--mode {LanguageModeNames}] [--warnings-as-errors] <file.pl>...");
}
