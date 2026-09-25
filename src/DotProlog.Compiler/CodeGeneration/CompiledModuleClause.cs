namespace DotProlog.Compiler;

internal sealed record CompiledModuleClause(int Root, List<CompiledTermCell> Term);
