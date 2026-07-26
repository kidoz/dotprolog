namespace Prolog.Syntax;

/// <summary>How a <see cref="Diagnostic"/> affects the outcome of a read or compile.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Advisory only; the read succeeded.</summary>
    Warning,

    /// <summary>The read or compile failed.</summary>
    Error,
}
