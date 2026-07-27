using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>The outcome of lowering a set of clauses into a program.</summary>
/// <param name="Diagnostics">Every diagnostic raised while lowering.</param>
/// <param name="DirectiveAddresses">Entry addresses of <c>:- Goal</c> directives, in source order.</param>
/// <param name="InitializationAddresses">Entry addresses of <c>:- initialization(Goal)</c> goals, in source order.</param>
public sealed record LoadResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<int> DirectiveAddresses,
    IReadOnlyList<int> InitializationAddresses
)
{
    /// <summary>Whether lowering completed without errors.</summary>
    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
