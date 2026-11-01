namespace DotProlog.Runtime;

// Internal ownership seam: the public IRuntimeCompiler.CompileClause contract stays pinned.
// A compiler implementing this explicitly transfers private storage to the assertion caller.
internal interface IReclaimableClauseCompiler
{
    int CompileDynamicClause(Machine machine, Cell clause, out int functorId, out DynamicCodeBlock storage);
}
