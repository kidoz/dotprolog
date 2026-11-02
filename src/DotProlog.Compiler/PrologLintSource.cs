using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>One physical source unit supplied to whole-program lint analysis.</summary>
/// <param name="FileName">Physical source path used for diagnostics and relative resolution.</param>
/// <param name="Source">Original source text, used by layout rules.</param>
/// <param name="Clauses">Parsed clauses, including any non-executing include expansion.</param>
/// <param name="InitialDoubleQuotes">The initial double-quoted text reading for this unit.</param>
/// <param name="CodePoints">Whether source layout counts Unicode code points.</param>
/// <param name="ClauseFileNames">Physical owner of each expanded clause, when includes are present.</param>
public sealed record PrologLintSource(
    string FileName,
    string Source,
    IReadOnlyList<SyntaxTerm> Clauses,
    DoubleQuotesMode InitialDoubleQuotes,
    bool CodePoints,
    IReadOnlyList<string>? ClauseFileNames = null
)
{
    internal bool HasAnalysisGaps { get; init; }

    internal IReadOnlyList<(string FileName, SyntaxTerm? Clause)>? SourceOrder { get; init; }

    /// <summary>Returns the physical source file that owns the clause at <paramref name="index"/>.</summary>
    public string FileForClause(int index) =>
        ClauseFileNames is { } files && index >= 0 && index < files.Count ? files[index] : FileName;
}
