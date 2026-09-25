using DotProlog.Runtime;

namespace DotProlog.Compiler;

internal sealed record CompiledModule(
    string Name,
    bool InterfacePrepared,
    List<PrologOperator> Operators,
    List<(int Input, int Output)> CharacterConversions,
    bool CharConversion,
    bool Debug,
    DoubleQuotesMode DoubleQuotes,
    UnknownProcedureAction Unknown,
    List<CompiledModulePredicate> Predicates,
    List<CompiledModuleImport> Imports
);
