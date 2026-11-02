using DotProlog.Syntax;

namespace DotProlog.Compiler;

internal sealed record PrologCallSite(
    string FileName,
    SourceSpan Range,
    PredicateIndicator Predicate,
    string? DefinitionModule,
    bool Grammar,
    PrologBuiltinDocumentation? Documentation,
    int Context
);
