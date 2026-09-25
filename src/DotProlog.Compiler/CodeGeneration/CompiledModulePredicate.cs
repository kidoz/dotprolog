namespace DotProlog.Compiler;

internal sealed record CompiledModulePredicate(
    string Name,
    int Arity,
    bool Defined,
    bool Exported,
    bool Dynamic,
    bool Multifile,
    string? MetapredicateTemplate,
    List<CompiledModuleClause> StaticClauses
);
