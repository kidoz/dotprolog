using System.Globalization;
using System.Numerics;
using System.Text;

namespace DotProlog.Runtime;

/// <summary>
/// Reads the text of a <c>number_chars/2</c> or <c>number_codes/2</c> list (ISO 8.16.7 and 8.16.8):
/// layout text, which may hold comments, then an optional <c>-</c> name token, quoted or not, then one
/// number token, and nothing after it, not even layout. So <c>" 1"</c>, <c>"- 1"</c>, <c>"'-'1"</c>, and
/// <c>"/**/1"</c> are numbers, and <c>"1 "</c>, <c>"+1"</c>, and <c>"0X1"</c> are not.
/// </summary>
/// <remarks>
/// The syntax is the term reader's, character conversion aside, which does not apply here. The runtime
/// cannot call the reader, which lives in an assembly that depends on it, so this reads the same
/// layout, comments, quoted names, escapes, and number tokens itself; a test runs a corpus of texts
/// through both and requires the same answer from each. A program without a compiler attached reads
/// these lists exactly as one with a compiler does.
/// </remarks>
internal static class NumberReader
{
    private const string SymbolCharacters = "+-*/\\^<>=~:.?@#&$";

    /// <summary>How reading the text turned out.</summary>
    internal enum Outcome
    {
        /// <summary>The text is a number.</summary>
        Number,

        /// <summary>The text is not a number: <c>syntax_error</c>.</summary>
        NotANumber,

        /// <summary>The text is a float beyond the finite range: <c>representation_error(max_float)</c>.</summary>
        FloatOverflow,
    }

    /// <summary>Reads <paramref name="text"/> as a number.</summary>
    /// <param name="text">The characters of the list.</param>
    /// <param name="codePoints">Whether characters are code points, as in the default mode.</param>
    /// <param name="rationals">Whether <c>NrM</c> rational literals are read, as in the default mode.</param>
    /// <param name="number">The number read, when the outcome is <see cref="Outcome.Number"/>.</param>
    internal static Outcome Read(string text, bool codePoints, bool rationals, out PrologNumber number)
    {
        number = default;
        var position = 0;

        if (!SkipLayout(text, ref position))
        {
            return Outcome.NotANumber;
        }

        var negate = false;
        if (position < text.Length && text[position] == '-')
        {
            // A lone '-': any symbol character after it makes a longer name, such as '-/' in "-/**/1".
            if (position + 1 < text.Length && SymbolCharacters.Contains(text[position + 1], StringComparison.Ordinal))
            {
                return Outcome.NotANumber;
            }

            negate = true;
            position++;
        }
        else if (position < text.Length && text[position] == '\'')
        {
            if (!TryReadQuoted(text, ref position, codePoints, out var name) || name != "-")
            {
                return Outcome.NotANumber;
            }

            negate = true;
        }

        if (negate && !SkipLayout(text, ref position))
        {
            return Outcome.NotANumber;
        }

        Outcome outcome = ReadNumberToken(text, ref position, codePoints, rationals, out PrologNumber value);
        if (outcome != Outcome.Number)
        {
            return outcome;
        }

        if (position != text.Length)
        {
            return Outcome.NotANumber;
        }

        number = negate ? Negate(value) : value;
        return Outcome.Number;
    }

    /// <summary>Skips layout characters and comments; false for a block comment left open.</summary>
    private static bool SkipLayout(string text, ref int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
            }
            else if (c == '%')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '*')
            {
                var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return false;
                }

                position = end + 2;
            }
            else
            {
                break;
            }
        }

        return true;
    }

    /// <summary>Reads a quoted name starting at the opening quote; false when it is malformed or unterminated.</summary>
    private static bool TryReadQuoted(string text, ref int position, bool codePoints, out string name)
    {
        var builder = new StringBuilder();
        name = string.Empty;
        position++;

        while (position < text.Length)
        {
            var c = text[position];
            if (c == '\'')
            {
                if (position + 1 < text.Length && text[position + 1] == '\'')
                {
                    builder.Append('\'');
                    position += 2;
                    continue;
                }

                position++;
                name = builder.ToString();
                return true;
            }

            if (c == '\\')
            {
                if (!TryReadEscape(text, ref position, codePoints, builder))
                {
                    return false;
                }

                continue;
            }

            if (c != ' ' && (char.IsControl(c) || char.IsWhiteSpace(c)))
            {
                return false;
            }

            builder.Append(c);
            position++;
        }

        return false;
    }

    /// <summary>
    /// Reads the escape starting at the backslash, appending what it denotes: nothing for a line
    /// continuation. False for an escape the reader rejects.
    /// </summary>
    private static bool TryReadEscape(string text, ref int position, bool codePoints, StringBuilder builder)
    {
        position++;
        if (position >= text.Length)
        {
            return false;
        }

        var c = text[position++];
        switch (c)
        {
            case 'n':
                builder.Append('\n');
                return true;
            case 't':
                builder.Append('\t');
                return true;
            case 'r':
                builder.Append('\r');
                return true;
            case 'a':
                builder.Append('\a');
                return true;
            case 'b':
                builder.Append('\b');
                return true;
            case 'f':
                builder.Append('\f');
                return true;
            case 'v':
                builder.Append('\v');
                return true;
            case 'e':
                builder.Append('\u001b');
                return true;
            case 'd':
                builder.Append('\u007f');
                return true;
            case '0':
                if (position < text.Length && (text[position] == '\\' || IsEscapeDigit(text[position], 8)))
                {
                    return TryReadNumericEscape(text, ref position, codePoints, 8, firstDigit: 0, builder);
                }

                builder.Append('\0');
                return true;
            case '\\' or '\'' or '"' or '`':
                builder.Append(c);
                return true;
            case '\n':
                return true;
            case 'x':
                return TryReadNumericEscape(text, ref position, codePoints, 16, firstDigit: null, builder);
            case 'o':
                return TryReadNumericEscape(text, ref position, codePoints, 8, firstDigit: null, builder);
            case 'u' when codePoints:
                return TryReadFixedEscape(text, ref position, digits: 4, builder);
            case 'U' when codePoints:
                return TryReadFixedEscape(text, ref position, digits: 8, builder);
            case >= '1' and <= '7':
                return TryReadNumericEscape(text, ref position, codePoints, 8, firstDigit: c - '0', builder);
            default:
                return false;
        }
    }

    private static bool TryReadNumericEscape(
        string text,
        ref int position,
        bool codePoints,
        int radix,
        int? firstDigit,
        StringBuilder builder
    )
    {
        var digitsStart = position;
        var value = firstDigit ?? 0;
        var overflow = false;
        var largest = codePoints ? 0x10FFFF : char.MaxValue;
        while (position < text.Length && IsEscapeDigit(text[position], radix))
        {
            var digit = EscapeDigitValue(text[position]);
            if (value > (largest - digit) / radix)
            {
                overflow = true;
            }
            else if (!overflow)
            {
                value = (value * radix) + digit;
            }

            position++;
        }

        if ((position == digitsStart && firstDigit is null) || position >= text.Length || text[position] != '\\')
        {
            return false;
        }

        position++;
        return !overflow && TryAppendCode(builder, value, codePoints);
    }

    private static bool TryReadFixedEscape(string text, ref int position, int digits, StringBuilder builder)
    {
        long value = 0;
        for (var index = 0; index < digits; index++)
        {
            if (position >= text.Length || !char.IsAsciiHexDigit(text[position]))
            {
                return false;
            }

            value = (value * 16) + EscapeDigitValue(text[position]);
            position++;
        }

        return value <= 0x10FFFF && TryAppendCode(builder, (int)value, codePoints: true);
    }

    /// <summary>Appends an escaped character; a surrogate names no character when characters are code points.</summary>
    private static bool TryAppendCode(StringBuilder builder, int value, bool codePoints)
    {
        if (codePoints && value is >= 0xD800 and <= 0xDFFF)
        {
            return false;
        }

        builder.Append(value <= char.MaxValue ? ((char)value).ToString() : char.ConvertFromUtf32(value));
        return true;
    }

    /// <summary>Reads one number token: a character code, a radix integer, a decimal integer, a rational, or a float.</summary>
    private static Outcome ReadNumberToken(
        string text,
        ref int position,
        bool codePoints,
        bool rationals,
        out PrologNumber number
    )
    {
        number = default;
        if (position >= text.Length || !char.IsAsciiDigit(text[position]))
        {
            return Outcome.NotANumber;
        }

        if (text[position] == '0' && position + 1 < text.Length && text[position + 1] == '\'')
        {
            position += 2;
            if (!TryReadCharacterCode(text, ref position, codePoints, out var code))
            {
                return Outcome.NotANumber;
            }

            number = PrologNumber.FromInteger(code);
            return Outcome.Number;
        }

        if (text[position] == '0' && position + 1 < text.Length && text[position + 1] is 'x' or 'o' or 'b')
        {
            var radix = text[position + 1] switch
            {
                'x' => 16,
                'o' => 8,
                _ => 2,
            };
            position += 2;
            var digitsStart = position;
            BigInteger value = 0;
            while (position < text.Length && IsRadixDigit(text[position], radix))
            {
                value = (value * radix) + EscapeDigitValue(text[position]);
                position++;
            }

            if (position == digitsStart)
            {
                return Outcome.NotANumber;
            }

            number = PrologNumber.FromBig(value);
            return Outcome.Number;
        }

        var start = position;
        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            position++;
        }

        var isFloat = false;
        if (position + 1 < text.Length && text[position] == '.' && char.IsAsciiDigit(text[position + 1]))
        {
            isFloat = true;
            position++;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            if (position < text.Length && text[position] is 'e' or 'E')
            {
                var exponentOffset = position + 1 < text.Length && text[position + 1] is '+' or '-' ? 2 : 1;
                if (position + exponentOffset < text.Length && char.IsAsciiDigit(text[position + exponentOffset]))
                {
                    position += exponentOffset;
                    while (position < text.Length && char.IsAsciiDigit(text[position]))
                    {
                        position++;
                    }
                }
            }
        }

        if (!isFloat && rationals && position + 1 < text.Length && text[position] == 'r' && char.IsAsciiDigit(text[position + 1]))
        {
            BigInteger numerator = BigInteger.Parse(
                text.AsSpan(start, position - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture
            );
            position++;
            var denominatorStart = position;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            BigInteger denominator = BigInteger.Parse(
                text.AsSpan(denominatorStart, position - denominatorStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture
            );
            if (denominator.IsZero)
            {
                return Outcome.NotANumber;
            }

            number = PrologNumber.FromRational(numerator, denominator);
            return Outcome.Number;
        }

        ReadOnlySpan<char> literal = text.AsSpan(start, position - start);
        if (isFloat)
        {
            var value = double.Parse(literal, CultureInfo.InvariantCulture);
            if (double.IsInfinity(value))
            {
                return Outcome.FloatOverflow;
            }

            number = PrologNumber.FromReal(value);
            return Outcome.Number;
        }

        number = long.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out var integer)
            ? PrologNumber.FromInteger(integer)
            : PrologNumber.FromBig(BigInteger.Parse(literal, NumberStyles.None, CultureInfo.InvariantCulture));
        return Outcome.Number;
    }

    /// <summary>
    /// Reads the character after <c>0'</c>: an escape that denotes exactly one character, a doubled
    /// quote, or one character that is not a control or layout character other than the space.
    /// </summary>
    private static bool TryReadCharacterCode(string text, ref int position, bool codePoints, out int code)
    {
        code = 0;
        if (position >= text.Length)
        {
            return false;
        }

        if (text[position] == '\\')
        {
            var builder = new StringBuilder();
            if (!TryReadEscape(text, ref position, codePoints, builder) || builder.Length == 0)
            {
                return false;
            }

            code =
                codePoints && builder.Length == 2 && char.IsHighSurrogate(builder[0])
                    ? char.ConvertToUtf32(builder[0], builder[1])
                    : builder[0];
            return true;
        }

        if (text[position] == '\'' && position + 1 < text.Length && text[position + 1] == '\'')
        {
            position += 2;
            code = '\'';
            return true;
        }

        if (codePoints && CodePointText.IsPairAt(text, position))
        {
            code = char.ConvertToUtf32(text[position], text[position + 1]);
            position += 2;
            return true;
        }

        var c = text[position];
        if (c != ' ' && (char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            return false;
        }

        code = c;
        position++;
        return true;
    }

    private static PrologNumber Negate(PrologNumber number) =>
        number.IsFloat ? PrologNumber.FromReal(-number.Real)
        : number.IsRational ? PrologNumber.FromRational(-number.Numerator, number.Denominator)
        : PrologNumber.FromBig(-number.Big);

    private static bool IsRadixDigit(char c, int radix) =>
        radix switch
        {
            16 => char.IsAsciiHexDigit(c),
            8 => c is >= '0' and <= '7',
            _ => c is '0' or '1',
        };

    private static bool IsEscapeDigit(char c, int radix) => radix == 16 ? char.IsAsciiHexDigit(c) : c is >= '0' and <= '7';

    private static int EscapeDigitValue(char c) => char.IsAsciiDigit(c) ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10;
}
