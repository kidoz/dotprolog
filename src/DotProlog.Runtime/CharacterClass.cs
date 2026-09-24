using System.Globalization;
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

    /// <summary>The Unicode general category of a code.</summary>
    internal static UnicodeCategory CategoryOf(int code) => CharUnicodeInfo.GetUnicodeCategory(code);

    /// <summary>The Unicode <c>Uppercase</c> property: category Lu, and such as Ⅰ and Ⓐ besides.</summary>
    internal static bool IsUppercase(int code) =>
        CategoryOf(code) == UnicodeCategory.UppercaseLetter || UnicodeProperties.Contains(UnicodeProperties.OtherUppercase, code);

    /// <summary>The Unicode <c>Lowercase</c> property: category Ll, and such as ª and ʰ besides.</summary>
    internal static bool IsLowercase(int code) =>
        CategoryOf(code) == UnicodeCategory.LowercaseLetter || UnicodeProperties.Contains(UnicodeProperties.OtherLowercase, code);

    /// <summary>
    /// The Unicode <c>Alphabetic</c> property: the letters, the letter numbers, and the marks and
    /// symbols that write letters, such as the Devanagari vowel signs.
    /// </summary>
    internal static bool IsAlphabetic(int code) =>
        CategoryOf(code)
            is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber
        || UnicodeProperties.Contains(UnicodeProperties.OtherAlphabetic, code)
        || UnicodeProperties.Contains(UnicodeProperties.OtherUppercase, code)
        || UnicodeProperties.Contains(UnicodeProperties.OtherLowercase, code);

    /// <summary>
    /// Whether a code outside ASCII may start an identifier: the Unicode <c>ID_Start</c> property of
    /// UAX #31, which is every letter and letter number. An uppercase letter starts a variable.
    /// </summary>
    internal static bool IsIdentifierStart(int code) =>
        CategoryOf(code)
            is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber
        || UnicodeProperties.Contains(UnicodeProperties.OtherIdentifierStart, code);

    /// <summary>
    /// Whether a code outside ASCII may continue an identifier: the Unicode <c>ID_Continue</c>
    /// property, which adds combining marks, decimal digits, and connector punctuation to the
    /// starts, and the superscript and subscript digits besides, as SWI-Prolog adds them.
    /// </summary>
    internal static bool IsIdentifierContinue(int code) =>
        IsIdentifierStart(code)
        || CategoryOf(code)
            is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.ConnectorPunctuation
        || UnicodeProperties.Contains(UnicodeProperties.OtherIdentifierContinue, code)
        || code is 0xB2 or 0xB3 or 0xB9 or 0x2070 or (>= 0x2074 and <= 0x2079) or (>= 0x2080 and <= 0x2089);

    /// <summary>
    /// Whether a code outside ASCII is a solo character, an atom by itself that never joins its
    /// neighbours: a symbol, or punctuation other than a bracket or quotation mark, as SWI-Prolog
    /// reads them. 😀, €, and ∀ are each a one-character atom.
    /// </summary>
    internal static bool IsSolo(int code) =>
        code >= 0x80
        && CategoryOf(code)
            is UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OtherPunctuation
        && !IsIdentifierStart(code);

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
