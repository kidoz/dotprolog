using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reads Prolog source and expands includes without consulting source or executing directives.</summary>
public static class PrologLintSourceLoader
{
    private const int MaxIncludeDepth = 64;

    /// <summary>Reads one physical source unit using the language state owned by <paramref name="program"/>.</summary>
    public static PrologLintSourceLoadResult Load(string fileName, string source, BytecodeProgram program) =>
        Load(fileName, source, program, File.ReadAllText);

    /// <summary>Reads source and includes using a caller-owned source provider, without executing goals.</summary>
    /// <param name="fileName">Physical path of the root source.</param>
    /// <param name="source">Root source text, including unsaved changes when applicable.</param>
    /// <param name="program">Fresh language state for this source unit.</param>
    /// <param name="readSource">Reads an absolute include path. I/O failures become source-resolution diagnostics.</param>
    public static PrologLintSourceLoadResult Load(
        string fileName,
        string source,
        BytecodeProgram program,
        Func<string, string> readSource
    ) => Load(fileName, source, program, readSource, CancellationToken.None);

    /// <summary>Reads source and includes with cooperative cancellation at clause and include boundaries.</summary>
    public static PrologLintSourceLoadResult Load(
        string fileName,
        string source,
        BytecodeProgram program,
        Func<string, string> readSource,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(readSource);
        cancellationToken.ThrowIfCancellationRequested();

        var absolute = Path.GetFullPath(fileName);
        HashSet<string> activeIncludes = new(PathComparer);
        Dictionary<SyntaxTerm, string> owners = new(ReferenceEqualityComparer.Instance);
        ParseResult parsed = ReadWithIncludes(source, absolute, program, activeIncludes, owners, readSource, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string[] clauseFiles = [.. parsed.Clauses.Select(clause => owners[clause])];
        var unit = new PrologLintSource(
            absolute,
            source,
            parsed.Clauses,
            program.InitialDoubleQuotes,
            program.Flags.CodePointCharacters,
            clauseFiles
        )
        {
            HasAnalysisGaps = parsed.Diagnostics.Count > 0,
        };
        return new PrologLintSourceLoadResult(unit, parsed.Diagnostics);
    }

    private static ParseResult ReadWithIncludes(
        string source,
        string fileName,
        BytecodeProgram program,
        HashSet<string> activeIncludes,
        Dictionary<SyntaxTerm, string> owners,
        Func<string, string> readSource,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
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
                directive => ExpandInclude(directive, fileName, program, activeIncludes, owners, readSource, cancellationToken),
                clauseBoundary: _ => cancellationToken.ThrowIfCancellationRequested()
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
        Dictionary<SyntaxTerm, string> owners,
        Func<string, string> readSource,
        CancellationToken cancellationToken
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

            cancellationToken.ThrowIfCancellationRequested();
            var source = readSource(path);
            return ReadWithIncludes(source, path, program, activeIncludes, owners, readSource, cancellationToken);
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
