using DotProlog.Compiler;
using DotProlog.Syntax;

namespace DotProlog.LanguageServer;

internal sealed record SourceOutline(
    string Text,
    IReadOnlyList<PrologSourceSymbol> Symbols,
    IReadOnlyList<PrologBuiltinHover> Hovers,
    PrologCompletionText CompletionText,
    IReadOnlyList<PrologCompletionIndex> Completions,
    IReadOnlyList<IReadOnlyList<PrologDefinitionSite>> Definitions,
    IReadOnlyList<PrologRenameAnalysis> Renames
);
