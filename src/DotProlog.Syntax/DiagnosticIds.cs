namespace DotProlog.Syntax;

/// <summary>Stable diagnostic identifiers produced by the reader.</summary>
public static class DiagnosticIds
{
    /// <summary>A character that cannot begin any Prolog token.</summary>
    public const string UnexpectedCharacter = "DPL0001";

    /// <summary>A quoted atom, string, or block comment reached end of file unterminated.</summary>
    public const string UnterminatedQuoted = "DPL0002";

    /// <summary>A token appeared where the grammar does not allow it.</summary>
    public const string UnexpectedToken = "DPL0003";

    /// <summary>An operator was used at a priority its context does not permit.</summary>
    public const string OperatorPriorityClash = "DPL0004";

    /// <summary>A clause was not terminated by an end token.</summary>
    public const string MissingEndToken = "DPL0005";

    /// <summary>A numeric literal is malformed or out of range.</summary>
    public const string InvalidNumber = "DPL0006";

    /// <summary>An escape sequence inside a quoted token is not recognised.</summary>
    public const string InvalidEscape = "DPL0007";

    /// <summary>A positive integer literal exceeds the implementation's storage range.</summary>
    public const string MaxIntegerExceeded = "DPL0008";

    /// <summary>A negative integer literal exceeds the implementation's storage range.</summary>
    public const string MinIntegerExceeded = "DPL0009";

    /// <summary>A floating-point literal exceeds the finite implementation range.</summary>
    public const string FloatOverflow = "DPL0010";

    /// <summary>An unescaped control or layout character appeared inside a quoted token.</summary>
    public const string InvalidQuotedCharacter = "DPL0011";
}
