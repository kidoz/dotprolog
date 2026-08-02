namespace DotProlog.Compiler;

/// <summary>Stable diagnostic identifiers produced by source linting.</summary>
public static class LintDiagnosticIds
{
    /// <summary>An ordinary named variable occurs only once in its clause.</summary>
    public const string SingletonVariable = "DPL3001";

    /// <summary>An underscore-prefixed singleton marker occurs more than once in its clause.</summary>
    public const string RepeatedSingletonMarker = "DPL3002";

    /// <summary>A tab character appears in source governed by a spaces-only profile.</summary>
    public const string TabCharacter = "DPL3003";

    /// <summary>A clause continuation uses an inconsistent indentation width.</summary>
    public const string InconsistentIndentation = "DPL3004";

    /// <summary>A source line exceeds the configured length.</summary>
    public const string LineTooLong = "DPL3005";

    /// <summary>A clause spans more than the configured number of lines.</summary>
    public const string ClauseTooLong = "DPL3006";

    /// <summary>A comma outside quoted text or a comment is not followed by layout.</summary>
    public const string MissingSpaceAfterComma = "DPL3007";

    /// <summary>A clause or rule body does not begin at the configured line boundary.</summary>
    public const string ClauseLayout = "DPL3008";

    /// <summary>Two conjunction subgoals begin on the same source line.</summary>
    public const string SubgoalLayout = "DPL3009";

    /// <summary>A source line ends in spaces or tabs.</summary>
    public const string TrailingWhitespace = "DPL3010";
}
