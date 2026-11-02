using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>A callable source occurrence and its builtin help, if statically resolved.</summary>
/// <param name="FileName">Physical source owner.</param>
/// <param name="Range">Original name/operator token span.</param>
/// <param name="Documentation">Null for a user-defined or uncertain call; retained to detect conflicting include contexts.</param>
public sealed record PrologBuiltinHover(string FileName, SourceSpan Range, PrologBuiltinDocumentation? Documentation);
