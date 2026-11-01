using DotProlog.Runtime;

namespace DotProlog.Compiler;

/// <summary>Deduplicates constants as they are added to a program's constant pool.</summary>
internal sealed class ConstantPool
{
    private readonly BytecodeProgram _program;
    private readonly Dictionary<Cell, int> _indices = [];

    internal ConstantPool(BytecodeProgram program) => _program = program;

    // Reclaimable clauses never share their index cache with another clause or pinned code.
    internal void Reset() => _indices.Clear();

    /// <summary>Returns the pool index of <paramref name="constant"/>, adding it if it is new.</summary>
    internal int IndexOf(Cell constant)
    {
        if (_indices.TryGetValue(constant, out var index))
        {
            return index;
        }

        index = _program.AddConstant(constant);
        _indices[constant] = index;
        return index;
    }
}
