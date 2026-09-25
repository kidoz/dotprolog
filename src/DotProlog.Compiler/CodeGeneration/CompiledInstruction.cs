using DotProlog.Runtime;

namespace DotProlog.Compiler;

internal sealed class CompiledInstruction
{
    internal required int Address { get; init; }
    internal required int NextAddress { get; init; }
    internal required OpCode OpCode { get; init; }
    internal int First { get; init; }
    internal int Second { get; init; }
    internal int FirstReference { get; set; } = -1;
}
