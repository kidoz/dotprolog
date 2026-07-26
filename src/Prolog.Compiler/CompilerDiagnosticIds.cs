namespace Prolog.Compiler;

/// <summary>Stable diagnostic identifiers produced when lowering terms to bytecode.</summary>
public static class CompilerDiagnosticIds
{
    /// <summary>A clause head is neither an atom nor a compound term.</summary>
    public const string InvalidClauseHead = "DPL1001";

    /// <summary>A goal uses a control construct or form the current release does not compile.</summary>
    public const string UnsupportedGoal = "DPL1002";

    /// <summary>An integer literal does not fit in a term cell.</summary>
    public const string IntegerOutOfRange = "DPL1003";

    /// <summary>A predicate or compound term exceeds the maximum supported arity.</summary>
    public const string ArityTooLarge = "DPL1004";
}
