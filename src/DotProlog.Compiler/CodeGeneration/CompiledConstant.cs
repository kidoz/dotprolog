using DotProlog.Runtime;

namespace DotProlog.Compiler;

internal sealed record CompiledConstant(CellTag Tag, string? Text, long Integer, double Float);
