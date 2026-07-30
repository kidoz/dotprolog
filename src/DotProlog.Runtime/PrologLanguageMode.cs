namespace DotProlog.Runtime;

/// <summary>Which predefined language surface a Prolog program accepts.</summary>
public enum PrologLanguageMode
{
    /// <summary>ISO constructs plus the documented DotProlog extensions.</summary>
    Extended,

    /// <summary>Only the standardized ISO/IEC 13211 Parts 1, 2, and 3 surface.</summary>
    StrictIso,
}
