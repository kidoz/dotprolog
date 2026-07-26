namespace Prolog.CodeGen.CSharp;

/// <summary>
/// How many solutions an exported predicate has, which is what decides the shape of its C# signature.
/// </summary>
public enum Determinism
{
    /// <summary>Exactly one solution. The outputs become the return value.</summary>
    Det,

    /// <summary>No solution or one. With no outputs the method returns <see langword="bool"/>.</summary>
    Semidet,

    /// <summary>One solution or more, streamed.</summary>
    Multi,

    /// <summary>Any number of solutions, streamed.</summary>
    Nondet,
}
