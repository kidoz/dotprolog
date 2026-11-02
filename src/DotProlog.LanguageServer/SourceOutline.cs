using DotProlog.Compiler;

namespace DotProlog.LanguageServer;

internal sealed record SourceOutline(
    string Text,
    IReadOnlyList<PrologSourceSymbol> Symbols,
    IReadOnlyList<PrologBuiltinHover> Hovers
);
