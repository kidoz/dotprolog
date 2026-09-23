namespace DotProlog.Runtime;

/// <summary>
/// Parsing and naming for <see cref="PrologLanguageMode"/> where it crosses a text surface: the
/// command line tool, the MSBuild property, and diagnostics. One table keeps those spellings from
/// drifting apart.
/// </summary>
public static class PrologLanguageModes
{
    /// <summary>The accepted mode names, spelled as a usage message lists them.</summary>
    public const string Names = "modern|strict-iso";

    /// <summary>Parses a mode name, accepting any casing and both spellings of the ISO mode.</summary>
    /// <param name="text">The name to parse.</param>
    /// <param name="languageMode">The parsed mode, or <see cref="PrologLanguageMode.Modern"/>.</param>
    /// <returns><c>true</c> when <paramref name="text"/> named a mode.</returns>
    public static bool TryParse(string? text, out PrologLanguageMode languageMode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "modern":
                languageMode = PrologLanguageMode.Modern;
                return true;
            case "strict-iso" or "strictiso":
                languageMode = PrologLanguageMode.StrictIso;
                return true;
            default:
                languageMode = PrologLanguageMode.Modern;
                return false;
        }
    }
}
