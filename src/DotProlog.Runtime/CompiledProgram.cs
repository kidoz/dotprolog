using System.ComponentModel;

namespace DotProlog.Runtime;

/// <summary>
/// Resolved symbols, constants, and targets owned by generated predicate code.
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
    private readonly int[] _staticIndexes;

    /// <summary>Creates storage for one generated compilation unit.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public CompiledProgram(int[] functors, int[] builtins, Cell[] constants, int targetCount)
        : this(functors, builtins, constants, targetCount, []) { }

    /// <summary>Creates storage including relocated static clause index identifiers.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public CompiledProgram(int[] functors, int[] builtins, Cell[] constants, int targetCount, int[] staticIndexes)
    {
        ArgumentNullException.ThrowIfNull(functors);
        ArgumentNullException.ThrowIfNull(builtins);
        ArgumentNullException.ThrowIfNull(constants);
        ArgumentOutOfRangeException.ThrowIfNegative(targetCount);
        ArgumentNullException.ThrowIfNull(staticIndexes);

        _functors = functors;
        _builtins = builtins;
        _constants = constants;
        _targets = new int[targetCount];
        _staticIndexes = staticIndexes;
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

    /// <summary>Returns a relocated static clause index identifier.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int StaticIndex(int index) => _staticIndexes[index];

    /// <summary>Records the machine target assigned to a generated block.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SetTarget(int index, int target) => _targets[index] = target;
}
