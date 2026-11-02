using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>A physical source definition, before DCG lowering or code generation.</summary>
/// <param name="FileName">Physical owner of this clause.</param>
/// <param name="Module">Defining module from the source context.</param>
/// <param name="Name">Decoded predicate name.</param>
/// <param name="Arity">Arity as written, excluding DCG state arguments.</param>
/// <param name="IsNonterminal">Whether the definition is a DCG rule.</param>
/// <param name="Range">Parsed clause term range.</param>
/// <param name="SelectionRange">Original name or operator token range.</param>
public sealed record PrologSourceSymbol(
    string FileName,
    string Module,
    string Name,
    int Arity,
    bool IsNonterminal,
    SourceSpan Range,
    SourceSpan SelectionRange
);
