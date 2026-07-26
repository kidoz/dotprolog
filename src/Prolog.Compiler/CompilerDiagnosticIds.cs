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

    /// <summary>A <c>:- dynamic</c> declaration names something other than a predicate indicator.</summary>
    public const string InvalidDynamicDeclaration = "DPL1005";

    /// <summary>A <c>:- dynamic</c> declaration was read by a loader with no machine to load into.</summary>
    public const string DynamicNotAvailable = "DPL1006";

    /// <summary>A grammar rule could not be translated into an ordinary clause.</summary>
    public const string InvalidGrammarRule = "DPL1007";
}
