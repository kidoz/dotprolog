namespace DotProlog.Compiler;

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

    /// <summary>A <c>module/2</c>, <c>use_module</c>, or <c>meta_predicate</c> declaration is malformed.</summary>
    public const string InvalidModuleDeclaration = "DPL1008";

    /// <summary>A <c>use_module</c> names a file that cannot be found or loaded.</summary>
    public const string ModuleNotFound = "DPL1009";

    /// <summary>An <c>include/1</c> declaration does not name a source file with an atom.</summary>
    public const string InvalidIncludeDeclaration = "DPL1010";

    /// <summary>An <c>include/1</c> declaration names a source file that cannot be found or read.</summary>
    public const string IncludeNotFound = "DPL1011";

    /// <summary>An <c>include/1</c> declaration recursively includes a source already being read.</summary>
    public const string IncludeCycle = "DPL1012";

    /// <summary>An <c>ensure_loaded/1</c> declaration does not name a source file with an atom.</summary>
    public const string InvalidEnsureLoadedDeclaration = "DPL1013";

    /// <summary>An <c>ensure_loaded/1</c> declaration names a source file that cannot be found or loaded.</summary>
    public const string EnsureLoadedNotFound = "DPL1014";

    /// <summary>A <c>discontiguous/1</c> declaration contains an invalid predicate indicator.</summary>
    public const string InvalidDiscontiguousDeclaration = "DPL1015";

    /// <summary>A <c>multifile/1</c> declaration contains an invalid predicate indicator.</summary>
    public const string InvalidMultifileDeclaration = "DPL1016";
}
