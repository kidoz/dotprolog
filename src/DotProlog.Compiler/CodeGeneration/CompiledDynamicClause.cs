namespace DotProlog.Compiler;

internal sealed record CompiledDynamicClause(int Entry, int Root, List<CompiledTermCell> Term);
