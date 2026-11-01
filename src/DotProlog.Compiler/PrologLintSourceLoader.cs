using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reads Prolog source and expands includes without consulting source or executing directives.</summary>
public static class PrologLintSourceLoader
{
    private const int MaxIncludeDepth = 64;

    /// <summary>Reads one physical source unit using the language state owned by <paramref name="program"/>.</summary>
    public static PrologLintSourceLoadResult Load(string fileName, string source, BytecodeProgram program)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(program);

        var absolute = Path.GetFullPath(fileName);
        HashSet<string> activeIncludes = new(PathComparer);
        Dictionary<SyntaxTerm, string> owners = new(ReferenceEqualityComparer.Instance);
        ParseResult parsed = ReadWithIncludes(source, absolute, program, activeIncludes, owners);
        string[] clauseFiles = [.. parsed.Clauses.Select(clause => owners[clause])];
        var unit = new PrologLintSource(
            absolute,
            source,
            parsed.Clauses,
            program.InitialDoubleQuotes,
            program.Flags.CodePointCharacters,
            clauseFiles
        );
        return new PrologLintSourceLoadResult(unit, parsed.Diagnostics);
    }

    private static ParseResult ReadWithIncludes(
        string source,
        string fileName,
        BytecodeProgram program,
        HashSet<string> activeIncludes,
        Dictionary<SyntaxTerm, string> owners
    )
    {
        if (!activeIncludes.Add(fileName))
        {
            return new ParseResult([], [ResolutionWarning($"Include cycle detected at {fileName}.", fileName, default)]);
        }

        try
        {
            if (activeIncludes.Count > MaxIncludeDepth)
            {
                return new ParseResult(
                    [],
                    [ResolutionWarning($"Include expansion exceeds the maximum depth of {MaxIncludeDepth}.", fileName, default)]
                );
            }

            ParseResult parsed = TermReader.ReadProgram(
                source,
                fileName,
                program.Operators,
                program.CharacterConversions,
                program.Flags,
                directive => ExpandInclude(directive, fileName, program, activeIncludes, owners)
            );
            foreach (SyntaxTerm clause in parsed.Clauses)
            {
                owners.TryAdd(clause, fileName);
            }

            return parsed;
        }
        finally
        {
            activeIncludes.Remove(fileName);
        }
    }

    private static ParseResult? ExpandInclude(
        SyntaxTerm directive,
        string includingFile,
        BytecodeProgram program,
        HashSet<string> activeIncludes,
        Dictionary<SyntaxTerm, string> owners
    )
    {
        if (
            directive
            is not CompoundTerm
            {
                Name: ":-",
                Arity: 1,
                Arguments: [CompoundTerm { Name: "include", Arity: 1, Arguments: [AtomTerm include] }],
            }
        )
        {
            return null;
        }

        try
        {
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(includingFile) ?? string.Empty, include.Name));
            if (activeIncludes.Contains(path))
            {
                return new ParseResult(
                    [],
                    [ResolutionWarning($"Include cycle detected at {include.Name}.", includingFile, include.Span)]
                );
            }

            if (activeIncludes.Count >= MaxIncludeDepth)
            {
                return new ParseResult(
                    [],
                    [
                        ResolutionWarning(
                            $"Include expansion exceeds the maximum depth of {MaxIncludeDepth}.",
                            includingFile,
                            include.Span
                        ),
                    ]
                );
            }

            var source = File.ReadAllText(path);
            return ReadWithIncludes(source, path, program, activeIncludes, owners);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new ParseResult(
                [],
                [ResolutionWarning($"Cannot include {include.Name}: {exception.Message}", includingFile, include.Span)]
            );
        }
    }

    private static Diagnostic ResolutionWarning(string message, string fileName, SourceSpan span) =>
        new(LintDiagnosticIds.UnresolvedLintSource, DiagnosticSeverity.Warning, message, span, fileName);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
