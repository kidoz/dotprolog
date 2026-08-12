namespace DotProlog.Runtime;

/// <summary>
/// Initial flag values a host layers over a language mode's defaults. The mode stays the curated
/// profile; an override moves one flag's starting value without changing anything else the mode
/// chose. Only flags whose starting value is meaningful before any source is read are overridable,
/// and each one is admitted deliberately; see ADR 0048.
/// </summary>
public sealed record PrologFlagOverrides
{
    /// <summary>The accepted entries, spelled as a usage message lists them.</summary>
    public const string Entries = "double_quotes=codes|chars|atom";

    /// <summary>The empty override set: every flag starts at its mode default.</summary>
    public static PrologFlagOverrides None { get; } = new();

    /// <summary>The initial <c>double_quotes</c> value, or <c>null</c> to keep the mode's default.</summary>
    public DoubleQuotesMode? DoubleQuotes { get; init; }

    /// <summary>Whether every flag is left at its mode default.</summary>
    public bool IsEmpty => DoubleQuotes is null;

    /// <summary>
    /// Parses a semicolon-separated <c>name=value</c> override list — the one spelling shared by
    /// the <c>DotPrologFlags</c> MSBuild property and the command line tool, so the two surfaces
    /// cannot drift apart. Blank text parses to <see cref="None"/>.
    /// </summary>
    /// <param name="text">The override list to parse.</param>
    /// <param name="overrides">The parsed overrides, or <see cref="None"/>.</param>
    /// <param name="error">Why parsing failed, phrased for a diagnostic; <c>null</c> on success.</param>
    /// <returns><c>true</c> when <paramref name="text"/> was a valid override list.</returns>
    public static bool TryParse(string? text, out PrologFlagOverrides overrides, out string? error)
    {
        overrides = None;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        DoubleQuotesMode? doubleQuotes = null;
        foreach (var rawEntry in text.Split(';'))
        {
            var entry = rawEntry.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            var separator = entry.IndexOf('=');
            if (separator < 0)
            {
                error = $"'{entry}' is not a name=value pair. Expected: {Entries}.";
                return false;
            }

            var name = entry[..separator].Trim().ToLowerInvariant();
            var value = entry[(separator + 1)..].Trim().ToLowerInvariant();
            switch (name)
            {
                case "double_quotes":
                    if (doubleQuotes is not null)
                    {
                        error = "double_quotes is overridden more than once.";
                        return false;
                    }

                    doubleQuotes = value switch
                    {
                        "codes" => DoubleQuotesMode.Codes,
                        "chars" => DoubleQuotesMode.Chars,
                        "atom" => DoubleQuotesMode.Atom,
                        _ => null,
                    };
                    if (doubleQuotes is null)
                    {
                        error = $"'{value}' is not a double_quotes value. Expected: {Entries}.";
                        return false;
                    }

                    break;
                default:
                    error = $"'{name}' is not an overridable flag. Expected: {Entries}.";
                    return false;
            }
        }

        overrides = doubleQuotes is null ? None : new PrologFlagOverrides { DoubleQuotes = doubleQuotes };
        return true;
    }
}
