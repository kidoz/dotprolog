namespace DotProlog.Runtime;

/// <summary>
/// Which predefined language surface a Prolog program accepts, and which initial flag values come
/// with it. A mode is a curated dialect rather than a flag matrix: a program that wants a
/// combination no mode names sets the flag itself with <c>set_prolog_flag/2</c>, or a host seeds
/// its initial value with <see cref="PrologFlagOverrides"/>.
/// </summary>
public enum PrologLanguageMode
{
    /// <summary>
    /// The default: ISO constructs plus the documented DotProlog extensions, with the defaults the
    /// newer Prolog systems settled on — <c>double_quotes</c> starts at <c>chars</c>, so a
    /// double-quoted token reads as a list of one-character atoms. This is also the dialect whose
    /// extension direction is SWI-Prolog; the coverage ledger lives in
    /// docs/reference/swi-compatibility.md.
    /// </summary>
    Modern,

    /// <summary>
    /// Only the standardized ISO/IEC 13211 Parts 1, 2, and 3 surface, with <c>double_quotes</c>
    /// starting at <c>codes</c>.
    /// </summary>
    StrictIso,
}
