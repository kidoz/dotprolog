using System.ComponentModel;

namespace DotProlog.Runtime;

/// <summary>
/// Resolved symbols, constants, and targets owned by generated C# predicate code.
/// </summary>
/// <remarks>
/// This is a generated-code contract. Application code should use <see cref="PrologHost"/> rather
/// than constructing compiled programs directly.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledProgram
{
    private readonly int[] _functors;
    private readonly int[] _builtins;
    private readonly Cell[] _constants;
    private readonly int[] _targets;

    /// <summary>Creates storage for one generated compilation unit.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public CompiledProgram(int[] functors, int[] builtins, Cell[] constants, int targetCount)
    {
        ArgumentNullException.ThrowIfNull(functors);
        ArgumentNullException.ThrowIfNull(builtins);
        ArgumentNullException.ThrowIfNull(constants);
        ArgumentOutOfRangeException.ThrowIfNegative(targetCount);

        _functors = functors;
        _builtins = builtins;
        _constants = constants;
        _targets = new int[targetCount];
    }

    /// <summary>Returns a resolved functor identifier.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Functor(int index) => _functors[index];

    /// <summary>Returns a resolved builtin identifier.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Builtin(int index) => _builtins[index];

    /// <summary>Returns a resolved constant cell.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Cell Constant(int index) => _constants[index];

    /// <summary>Returns the machine target assigned to a generated block.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Target(int index) => _targets[index];

    /// <summary>Records the machine target assigned to a generated block.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SetTarget(int index, int target) => _targets[index] = target;
}
