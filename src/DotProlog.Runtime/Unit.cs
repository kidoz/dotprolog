namespace DotProlog.Runtime;

/// <summary>
/// One solution that binds nothing. A <c>nondet</c> export with no outputs streams a
/// <see cref="Unit"/> per success, per ADR 0006's determinism table, so a caller can count
/// solutions or stop enumerating without the facade inventing a value to return.
/// </summary>
public readonly record struct Unit
{
    /// <summary>The only value.</summary>
    public static Unit Value => default;
}
