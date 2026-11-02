using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>A statically analyzed predicate occurrence and its available source implementations.</summary>
/// <param name="FileName">The physical source path or document URI containing the occurrence.</param>
/// <param name="Range">The occurrence's name-token range.</param>
/// <param name="Module">The resolved defining module, or null when no source target is known.</param>
/// <param name="Name">The unqualified predicate name.</param>
/// <param name="Arity">The callable arity, including DCG expansion arguments.</param>
/// <param name="Definitions">All matching source clauses; empty for builtin or unresolved targets.</param>
/// <param name="Context">An analysis-local physical source expansion identifier; separate expansions must agree before navigation.</param>
public sealed record PrologDefinitionSite(
    string FileName,
    SourceSpan Range,
    string? Module,
    string Name,
    int Arity,
    IReadOnlyList<PrologSourceSymbol> Definitions,
    int Context
)
{
    /// <summary>Whether this occurrence is a parsed clause head rather than a call.</summary>
    public bool IsDefinition { get; init; }
}
