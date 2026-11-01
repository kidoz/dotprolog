using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>A parsed lint source and the reader or source-resolution diagnostics produced while loading it.</summary>
public sealed record PrologLintSourceLoadResult(PrologLintSource Source, IReadOnlyList<Diagnostic> Diagnostics);
