namespace DotProlog.Runtime;

/// <summary>How a double-quoted token is represented as a Prolog term.</summary>
public enum DoubleQuotesMode
{
    /// <summary>A list of character codes.</summary>
    Codes,

    /// <summary>A list of one-character atoms.</summary>
    Chars,

    /// <summary>One atom containing the complete text.</summary>
    Atom,
}

/// <summary>What an undefined procedure call does.</summary>
public enum UnknownProcedureAction
{
    /// <summary>Raise <c>existence_error(procedure, Name/Arity)</c>.</summary>
    Error,

    /// <summary>Print a warning and fail.</summary>
    Warning,

    /// <summary>Fail silently.</summary>
    Fail,
}

/// <summary>The mutable ISO execution state owned by a loaded program.</summary>
public sealed class PrologFlags
{
    /// <summary>Whether input character conversion is enabled.</summary>
    public bool CharConversion { get; internal set; }

    /// <summary>Enables or disables input character conversion.</summary>
    public void SetCharConversion(bool enabled) => CharConversion = enabled;

    /// <summary>Enables or disables debugging.</summary>
    public void SetDebug(bool enabled) => Debug = enabled;

    /// <summary>Changes the representation of double-quoted tokens.</summary>
    public void SetDoubleQuotes(DoubleQuotesMode mode) => DoubleQuotes = mode;

    /// <summary>Changes the action taken by an undefined procedure.</summary>
    public void SetUnknown(UnknownProcedureAction action) => Unknown = action;

    /// <summary>Whether debugging is enabled.</summary>
    public bool Debug { get; internal set; }

    /// <summary>How double-quoted tokens are represented.</summary>
    public DoubleQuotesMode DoubleQuotes { get; internal set; } = DoubleQuotesMode.Codes;

    /// <summary>What an undefined procedure call does.</summary>
    public UnknownProcedureAction Unknown { get; internal set; } = UnknownProcedureAction.Error;

    /// <summary>Creates an independent copy of the current flag values.</summary>
    public PrologFlags Copy() =>
        new()
        {
            CharConversion = CharConversion,
            Debug = Debug,
            DoubleQuotes = DoubleQuotes,
            Unknown = Unknown,
        };

    /// <summary>Replaces every mutable flag value with those from another set.</summary>
    public void ReplaceWith(PrologFlags source)
    {
        ArgumentNullException.ThrowIfNull(source);
        CharConversion = source.CharConversion;
        Debug = source.Debug;
        DoubleQuotes = source.DoubleQuotes;
        Unknown = source.Unknown;
    }
}
