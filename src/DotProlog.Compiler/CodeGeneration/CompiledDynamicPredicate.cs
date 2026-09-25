namespace DotProlog.Compiler;

internal sealed record CompiledDynamicPredicate(int Functor, List<int> Aliases, List<CompiledDynamicClause> Clauses);
