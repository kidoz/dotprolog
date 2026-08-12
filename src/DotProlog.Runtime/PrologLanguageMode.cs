namespace DotProlog.Runtime;

/// <summary>
/// Which predefined language surface a Prolog program accepts, and which initial flag values come
/// with it. A mode is a curated dialect rather than a flag matrix: a program that wants a
/// combination no mode names sets the flag itself with <c>set_prolog_flag/2</c>.
/// </summary>
public enum PrologLanguageMode
{
    /// <summary>ISO constructs plus the documented DotProlog extensions.</summary>
    Extended,

    /// <summary>Only the standardized ISO/IEC 13211 Parts 1, 2, and 3 surface.</summary>
    StrictIso,

    /// <summary>
    /// The <see cref="Extended"/> surface with the defaults the newer Prolog systems settled on:
    /// <c>double_quotes</c> starts at <c>chars</c>, so a double-quoted token reads as a list of
    /// one-character atoms rather than character codes. This is also the dialect whose extension
    /// direction is SWI-Prolog; the coverage ledger lives in docs/reference/swi-compatibility.md.
    /// </summary>
    Modern,
}
