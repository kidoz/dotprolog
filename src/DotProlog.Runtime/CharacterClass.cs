using System.Text;

namespace DotProlog.Runtime;

/// <summary>
/// Classifies a character by its code, the one place the text predicates and the writer decide what
/// a character is. A code below 0x10000 is classified as a UTF-16 code unit, which also covers the
/// surrogates strict ISO mode keeps as characters; a supplementary code is classified as the Unicode
/// scalar value it is.
/// </summary>
internal static class CharacterClass
{
    internal static bool IsLetter(int code) => code <= char.MaxValue ? char.IsLetter((char)code) : Rune.IsLetter(new Rune(code));

    internal static bool IsLetterOrDigit(int code) =>
        code <= char.MaxValue ? char.IsLetterOrDigit((char)code) : Rune.IsLetterOrDigit(new Rune(code));

    internal static bool IsWhiteSpace(int code) =>
        code <= char.MaxValue ? char.IsWhiteSpace((char)code) : Rune.IsWhiteSpace(new Rune(code));

    internal static bool IsControl(int code) =>
        code <= char.MaxValue ? char.IsControl((char)code) : Rune.IsControl(new Rune(code));

    internal static bool IsUpper(int code) => code <= char.MaxValue ? char.IsUpper((char)code) : Rune.IsUpper(new Rune(code));

    internal static bool IsLower(int code) => code <= char.MaxValue ? char.IsLower((char)code) : Rune.IsLower(new Rune(code));

    internal static int ToUpper(int code) =>
        code <= char.MaxValue ? char.ToUpperInvariant((char)code) : Rune.ToUpperInvariant(new Rune(code)).Value;

    internal static int ToLower(int code) =>
        code <= char.MaxValue ? char.ToLowerInvariant((char)code) : Rune.ToLowerInvariant(new Rune(code)).Value;

    /// <summary>
    /// The character starting at code unit <paramref name="index"/> and how many code units it takes:
    /// a surrogate pair is one character when characters are code points, and two otherwise.
    /// </summary>
    internal static (int Code, int Width) At(string text, int index, bool codePoints) =>
        codePoints && CodePointText.IsPairAt(text, index)
            ? (char.ConvertToUtf32(text[index], text[index + 1]), 2)
            : (text[index], 1);

    /// <summary>The last character of a non-empty <paramref name="text"/>.</summary>
    internal static int Last(string text, bool codePoints) =>
        codePoints && text.Length >= 2 && CodePointText.IsPairAt(text, text.Length - 2)
            ? char.ConvertToUtf32(text[^2], text[^1])
            : text[^1];
}
