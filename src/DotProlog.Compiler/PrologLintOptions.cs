namespace DotProlog.Compiler;

/// <summary>Configures optional source-text rules for <see cref="PrologLinter"/>.</summary>
public sealed record PrologLintOptions
{
    /// <summary>The original source-local semantic rules, with no layout policy.</summary>
    public static PrologLintOptions SemanticOnly { get; } = new();

    /// <summary>
    /// Covington layout guidelines 2.1 through 2.7, using their recommended four-space indent,
    /// 80-column line limit, and 24-line clause limit.
    /// </summary>
    public static PrologLintOptions Covington { get; } =
        new()
        {
            DisallowTabs = true,
            IndentSize = 4,
            MaxLineLength = 80,
            MaxClauseLines = 24,
            RequireSpaceAfterComma = true,
            RequireClauseLayout = true,
            RequireOneSubgoalPerLine = true,
            CheckTrailingWhitespace = true,
        };

    /// <summary>Whether any tab character is a layout violation.</summary>
    public bool DisallowTabs { get; init; }

    /// <summary>Required indentation unit, or <see langword="null"/> to leave indentation unchecked.</summary>
    public int? IndentSize { get; init; }

    /// <summary>Maximum source-line length, or <see langword="null"/> for no limit.</summary>
    public int? MaxLineLength { get; init; }

    /// <summary>Maximum number of source lines in one clause, or <see langword="null"/> for no limit.</summary>
    public int? MaxClauseLines { get; init; }

    /// <summary>Whether a comma must be followed by layout.</summary>
    public bool RequireSpaceAfterComma { get; init; }

    /// <summary>Whether clauses start at column one and rule bodies start on a later line.</summary>
    public bool RequireClauseLayout { get; init; }

    /// <summary>Whether each conjunction subgoal starts on a later line than the preceding subgoal.</summary>
    public bool RequireOneSubgoalPerLine { get; init; }

    /// <summary>Whether spaces or tabs immediately before a line ending are rejected.</summary>
    public bool CheckTrailingWhitespace { get; init; }
}
