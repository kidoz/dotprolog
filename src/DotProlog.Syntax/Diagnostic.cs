namespace DotProlog.Syntax;

/// <summary>
/// A user-facing parser or compiler message. Identifiers are stable across releases so that
/// build output can be suppressed or asserted on; see <see cref="DiagnosticIds"/>.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>DPL0003</c>.</param>
/// <param name="Severity">Whether the message fails the read.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="Span">Source range the message points at.</param>
/// <param name="FileName">Source file the span belongs to, when known.</param>
public sealed record Diagnostic(string Id, DiagnosticSeverity Severity, string Message, SourceSpan Span, string? FileName = null)
{
    /// <inheritdoc />
    public override string ToString()
    {
        var location = FileName is null ? $"({Span.Line},{Span.Column})" : $"{FileName}({Span.Line},{Span.Column})";
        var severity = Severity == DiagnosticSeverity.Error ? "error" : "warning";
        return $"{location}: {severity} {Id}: {Message}";
    }
}
