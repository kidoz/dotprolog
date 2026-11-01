namespace DotProlog.Compiler;

internal readonly record struct CompiledSourceLocation(
    string Document,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn
);
