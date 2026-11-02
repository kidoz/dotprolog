using DotProlog.Compiler;
using DotProlog.Packages;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Tool;

/// <summary>Command line entry point for the <c>dotnet prolog</c> tool.</summary>
internal static class Program
{
    private const string LanguageModeNames = PrologLanguageModes.Names;
    private const string LintProfileNames = "semantic|covington";
    private const string LintOptInRuleNames =
        LintDiagnosticIds.PossibleNonSteadfastClause
        + "|"
        + LintDiagnosticIds.PossibleNonTailRecursion
        + "|"
        + LintDiagnosticIds.PossibleMultifileCut
        + "|"
        + LintDiagnosticIds.PossibleRepeatWithoutCut
        + "|"
        + LintDiagnosticIds.PossibleFinalCut
        + "|"
        + LintDiagnosticIds.PossibleCatchAll
        + "|"
        + LintDiagnosticIds.PossibleMissingElse
        + "|"
        + LintDiagnosticIds.NonSnakeCasePredicate
        + "|"
        + LintDiagnosticIds.ExcessivePredicateArity
        + "|"
        + LintDiagnosticIds.ExcessiveSubgoalCount;
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
            "package" => PackageCommands.Execute(args[1..], output, error),
            "restore" => PackageCommands.Execute(args, output, error),
            "lint" => Lint(args.AsSpan(1), error),
            "run" => Run(args.AsSpan(1), output, error),
            _ => UnknownCommand(args[0], error),
        };
    }

    private static int Run(ReadOnlySpan<string> args, TextWriter output, TextWriter error)
    {
        PrologLanguageMode languageMode = PrologLanguageMode.Modern;
        List<string> flagEntries = [];
        List<string> paths = [];
        string? packageDirectory = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--packages")
            {
                if (++index >= args.Length)
                {
                    error.WriteLine("error: missing directory after --packages");
                    return ExitUsage;
                }
                packageDirectory = args[index];
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

                var selected = args[++index];
                if (!PrologLanguageModes.TryParse(selected, out languageMode))
                {
                    error.WriteLine($"error: unknown language mode: {selected}");
                    error.WriteLine($"       expected one of: {LanguageModeNames}");
                    return ExitUsage;
                }

                continue;
            }

            if (argument == "--flag")
            {
                if (!TryReadFlagEntry(args, ref index, flagEntries, error))
                {
                    return ExitUsage;
                }

                continue;
            }

            if (argument.StartsWith('-'))
            {
                error.WriteLine($"error: unknown run option: {argument}");
                WriteRunUsage(error);
                return ExitUsage;
            }

            paths.Add(argument);
        }

        if (paths.Count != 1)
        {
            WriteRunUsage(error);
            return ExitUsage;
        }

        if (!TryParseFlagEntries(flagEntries, error, out PrologFlagOverrides flagOverrides))
        {
            return ExitUsage;
        }

        var path = paths[0];
        if (!File.Exists(path))
        {
            error.WriteLine($"error: file not found: {path}");
            return ExitUsage;
        }

        using var engine = new PrologEngine(languageMode, flagOverrides) { Output = output, Error = error };

        // Directives run while the file loads, so an error one raises surfaces here as it would from
        // an initialization goal.
        try
        {
            if (packageDirectory is not null)
            {
                var version = typeof(Program).Assembly.GetName().Version!;
                engine.LibraryFiles = new PackageWorkspace(packageDirectory).GetLibraries(
                    "dotprolog",
                    $"{version.Major}.{version.Minor}.{version.Build}"
                );
            }
            LoadResult loaded = engine.ConsultFile(path);
            foreach (Diagnostic diagnostic in loaded.Diagnostics)
            {
                error.WriteLine(diagnostic.ToString());
            }

            if (!loaded.Success)
            {
                return ExitCompileError;
            }

            RunResult result = engine.RunPendingGoals();
            output.Flush();

            return result switch
            {
                RunResult.Halted => engine.Machine.ExitCode,
                RunResult.Failure => ExitGoalFailed,
                _ => ExitSuccess,
            };
        }
        catch (Exception exception)
            when (exception
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or System.Text.Json.JsonException
            )
        {
            error.WriteLine($"error: {exception.Message}");
            return ExitCompileError;
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
        PrologLanguageMode languageMode = PrologLanguageMode.Modern;
        PrologLintOptions lintOptions = PrologLintOptions.SemanticOnly;
        var warningsAsErrors = false;
        int? indentSize = null;
        int? maxLineLength = null;
        int? maxClauseLines = null;
        int? maxPredicateArity = null;
        int? maxClauseSubgoals = null;
        List<string> flagEntries = [];
        List<string> enabledRules = [];
        List<string> paths = [];

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
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

                var selected = args[++index];
                if (!PrologLanguageModes.TryParse(selected, out languageMode))
                {
                    error.WriteLine($"error: unknown language mode: {selected}");
                    error.WriteLine($"       expected one of: {LanguageModeNames}");
                    return ExitUsage;
                }

                continue;
            }

            if (argument == "--flag")
            {
                if (!TryReadFlagEntry(args, ref index, flagEntries, error))
                {
                    return ExitUsage;
                }

                continue;
            }

            if (argument == "--enable-rule")
            {
                if (index + 1 >= args.Length)
                {
                    error.WriteLine("error: missing diagnostic identifier after --enable-rule");
                    error.WriteLine($"       expected one of: {LintOptInRuleNames}");
                    return ExitUsage;
                }

                var selected = args[++index];
                string? rule = selected.ToUpperInvariant() switch
                {
                    LintDiagnosticIds.PossibleNonSteadfastClause => LintDiagnosticIds.PossibleNonSteadfastClause,
                    LintDiagnosticIds.PossibleNonTailRecursion => LintDiagnosticIds.PossibleNonTailRecursion,
                    LintDiagnosticIds.PossibleMultifileCut => LintDiagnosticIds.PossibleMultifileCut,
                    LintDiagnosticIds.PossibleRepeatWithoutCut => LintDiagnosticIds.PossibleRepeatWithoutCut,
                    LintDiagnosticIds.PossibleFinalCut => LintDiagnosticIds.PossibleFinalCut,
                    LintDiagnosticIds.PossibleCatchAll => LintDiagnosticIds.PossibleCatchAll,
                    LintDiagnosticIds.PossibleMissingElse => LintDiagnosticIds.PossibleMissingElse,
                    LintDiagnosticIds.NonSnakeCasePredicate => LintDiagnosticIds.NonSnakeCasePredicate,
                    LintDiagnosticIds.ExcessivePredicateArity => LintDiagnosticIds.ExcessivePredicateArity,
                    LintDiagnosticIds.ExcessiveSubgoalCount => LintDiagnosticIds.ExcessiveSubgoalCount,
                    _ => null,
                };
                if (rule is null)
                {
                    error.WriteLine($"error: unsupported lint rule: {selected}");
                    error.WriteLine($"       expected one of: {LintOptInRuleNames}");
                    return ExitUsage;
                }

                enabledRules.Add(rule);
                continue;
            }

            if (argument == "--profile")
            {
                if (index + 1 >= args.Length)
                {
                    error.WriteLine("error: missing lint profile after --profile");
                    error.WriteLine($"       expected one of: {LintProfileNames}");
                    return ExitUsage;
                }

                var selected = args[++index];
                if (selected.Equals("semantic", StringComparison.OrdinalIgnoreCase))
                {
                    lintOptions = PrologLintOptions.SemanticOnly;
                }
                else if (selected.Equals("covington", StringComparison.OrdinalIgnoreCase))
                {
                    lintOptions = PrologLintOptions.Covington;
                }
                else
                {
                    error.WriteLine($"error: unknown lint profile: {selected}");
                    error.WriteLine($"       expected one of: {LintProfileNames}");
                    return ExitUsage;
                }

                continue;
            }

            if (argument == "--indent-size")
            {
                if (!TryReadPositiveLintInteger(args, ref index, argument, error, out var value))
                {
                    return ExitUsage;
                }

                indentSize = value;
                continue;
            }

            if (argument == "--max-line-length")
            {
                if (!TryReadPositiveLintInteger(args, ref index, argument, error, out var value))
                {
                    return ExitUsage;
                }

                maxLineLength = value;
                continue;
            }

            if (argument == "--max-clause-lines")
            {
                if (!TryReadPositiveLintInteger(args, ref index, argument, error, out var value))
                {
                    return ExitUsage;
                }

                maxClauseLines = value;
                continue;
            }

            if (argument == "--max-predicate-arity")
            {
                if (!TryReadPositiveLintInteger(args, ref index, argument, error, out var value))
                {
                    return ExitUsage;
                }

                maxPredicateArity = value;
                continue;
            }

            if (argument == "--max-clause-subgoals")
            {
                if (!TryReadPositiveLintInteger(args, ref index, argument, error, out var value))
                {
                    return ExitUsage;
                }

                maxClauseSubgoals = value;
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

        if (!TryParseFlagEntries(flagEntries, error, out PrologFlagOverrides flagOverrides))
        {
            return ExitUsage;
        }

        lintOptions = lintOptions with
        {
            IndentSize = indentSize ?? lintOptions.IndentSize,
            MaxLineLength = maxLineLength ?? lintOptions.MaxLineLength,
            MaxClauseLines = maxClauseLines ?? lintOptions.MaxClauseLines,
            MaxPredicateArity = maxPredicateArity ?? lintOptions.MaxPredicateArity,
            MaxClauseSubgoals = maxClauseSubgoals ?? lintOptions.MaxClauseSubgoals,
            EnabledRules = enabledRules,
        };

        var foundErrors = false;
        var foundWarnings = false;
        List<PrologLintSource> sources = [];
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                error.WriteLine($"error: file not found: {path}");
                foundErrors = true;
                continue;
            }

            var absolute = Path.GetFullPath(path);
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

            var program = new BytecodeProgram(languageMode, flagOverrides);
            PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(absolute, source, program);
            sources.Add(loaded.Source);
            foreach (Diagnostic diagnostic in loaded.Diagnostics)
            {
                error.WriteLine(diagnostic.ToString());
                foundErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
                foundWarnings |= diagnostic.Severity == DiagnosticSeverity.Warning;
            }
        }

        foreach (Diagnostic diagnostic in PrologLinter.AnalyzeProgram(sources, lintOptions))
        {
            error.WriteLine(diagnostic.ToString());
            foundErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
            foundWarnings |= diagnostic.Severity == DiagnosticSeverity.Warning;
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

    private static bool TryReadFlagEntry(ReadOnlySpan<string> args, ref int index, List<string> flagEntries, TextWriter error)
    {
        if (index + 1 >= args.Length)
        {
            error.WriteLine("error: missing flag override after --flag");
            error.WriteLine($"       expected: {PrologFlagOverrides.Entries}");
            return false;
        }

        flagEntries.Add(args[++index]);
        return true;
    }

    private static bool TryParseFlagEntries(List<string> flagEntries, TextWriter error, out PrologFlagOverrides flagOverrides)
    {
        if (PrologFlagOverrides.TryParse(string.Join(';', flagEntries), out flagOverrides, out var flagError))
        {
            return true;
        }

        error.WriteLine($"error: invalid flag override: {flagError}");
        return false;
    }

    private static bool TryReadPositiveLintInteger(
        ReadOnlySpan<string> args,
        ref int index,
        string option,
        TextWriter error,
        out int value
    )
    {
        if (index + 1 >= args.Length)
        {
            error.WriteLine($"error: missing positive integer after {option}");
            value = 0;
            return false;
        }

        var selected = args[++index];
        if (!int.TryParse(selected, out value) || value <= 0)
        {
            error.WriteLine($"error: invalid value for {option}: {selected}");
            error.WriteLine("       expected a positive integer");
            return false;
        }

        return true;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("dotnet prolog — run and analyze Prolog on .NET");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine($"  dotnet prolog run [--mode {LanguageModeNames}] [--flag name=value] <file.pl>");
        output.WriteLine("      consult a file and run its goals; --packages DIR uses a verified package lock");
        output.WriteLine("  dotnet prolog restore [--locked] [--project DIR]");
        output.WriteLine("  dotnet prolog package <command> (init, add, remove, restore, update, check, tree, run)");
        output.WriteLine($"  dotnet prolog lint [--mode {LanguageModeNames}] [--flag name=value] [--profile {LintProfileNames}]");
        output.WriteLine($"      [--enable-rule {LintOptInRuleNames}] [--warnings-as-errors] <file.pl>...");
        output.WriteLine("      analyze source without consulting it or executing directives");
        output.WriteLine();
        output.WriteLine("Language modes:");
        output.WriteLine(
            "  modern       ISO with DotProlog extensions and modifications, double_quotes starting at chars (default)"
        );
        output.WriteLine("  strict-iso   only the standardized ISO/IEC 13211 surface, double_quotes starting at codes");
        output.WriteLine();
        output.WriteLine("Flag overrides (--flag, repeatable) layer an initial flag value over the mode:");
        output.WriteLine($"  {PrologFlagOverrides.Entries}");
    }

    private static void WriteRunUsage(TextWriter output)
    {
        output.WriteLine($"Usage: dotnet prolog run [--mode {LanguageModeNames}] [--flag name=value] <file.pl>");
    }

    private static void WriteLintUsage(TextWriter output)
    {
        output.WriteLine(
            $"Usage: dotnet prolog lint [--mode {LanguageModeNames}] [--flag name=value] [--profile {LintProfileNames}]"
        );
        output.WriteLine("       [--indent-size N] [--max-line-length N] [--max-clause-lines N]");
        output.WriteLine("       [--max-predicate-arity N] [--max-clause-subgoals N]");
        output.WriteLine($"       [--enable-rule {LintOptInRuleNames}] [--warnings-as-errors] <file.pl>...");
    }
}
