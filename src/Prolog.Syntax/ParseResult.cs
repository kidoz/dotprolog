namespace Prolog.Syntax;

/// <summary>The outcome of reading Prolog source: the clauses that were read, and every diagnostic raised.</summary>
/// <param name="Clauses">Clauses and directives, in source order. Clauses that failed to read are omitted.</param>
/// <param name="Diagnostics">Every diagnostic raised while reading.</param>
public sealed record ParseResult(IReadOnlyList<SyntaxTerm> Clauses, IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Whether the read completed without errors.</summary>
    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
