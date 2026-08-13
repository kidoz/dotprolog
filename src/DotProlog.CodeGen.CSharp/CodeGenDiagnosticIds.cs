namespace DotProlog.CodeGen.CSharp;

/// <summary>Stable diagnostic identifiers produced when reading a <c>.dpli</c> contract.</summary>
public static class CodeGenDiagnosticIds
{
    /// <summary>The contract does not declare a CLR type name with <c>clr_module/1</c>.</summary>
    public const string MissingModuleDeclaration = "DPL2001";

    /// <summary>A directive in the contract is not one the reader understands.</summary>
    public const string UnknownDirective = "DPL2002";

    /// <summary>An export does not name a predicate as <c>Name/Arity</c>.</summary>
    public const string InvalidPredicateIndicator = "DPL2003";

    /// <summary>An export names a determinism that is not det, semidet, multi, or nondet.</summary>
    public const string UnknownDeterminism = "DPL2004";

    /// <summary>An argument specification is not <c>in(Name, Type)</c> or <c>out(Name, Type)</c>.</summary>
    public const string InvalidArgument = "DPL2005";

    /// <summary>An argument names a type the contract mapper does not support.</summary>
    public const string UnknownType = "DPL2006";

    /// <summary>The number of argument specifications does not match the predicate's arity.</summary>
    public const string ArityMismatch = "DPL2007";

    /// <summary>A <c>det</c> export declares no outputs, so its C# signature would have no result.</summary>
    public const string DeterministicExportNeedsOutput = "DPL2008";

    /// <summary>The generated method name given to <c>clr_export/4</c> is not an atom.</summary>
    public const string InvalidClrName = "DPL2009";

    /// <summary>Two exports would generate the same method name and parameter list.</summary>
    public const string DuplicateMethodSignature = "DPL2010";

    /// <summary>The <c>clr_module/1</c> atom is not a valid C# identifier.</summary>
    public const string InvalidModuleTypeName = "DPL2011";

    /// <summary>The <c>clr_namespace/1</c> atom is not a dot-separated sequence of C# identifiers.</summary>
    public const string InvalidNamespace = "DPL2012";

    /// <summary>An exported predicate or argument name does not map to a C# identifier.</summary>
    public const string NameDoesNotMapToIdentifier = "DPL2013";

    /// <summary>A <c>multi</c> export declares no outputs, a shape the facade contract leaves undefined.</summary>
    public const string MultiExportNeedsOutput = "DPL2014";
}
