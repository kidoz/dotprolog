namespace DotProlog.Compiler;

/// <summary>Stable diagnostic identifiers produced by source linting.</summary>
public static class LintDiagnosticIds
{
    /// <summary>An ordinary named variable occurs only once in its clause.</summary>
    public const string SingletonVariable = "DPL3001";

    /// <summary>An underscore-prefixed singleton marker occurs more than once in its clause.</summary>
    public const string RepeatedSingletonMarker = "DPL3002";
}
