using System.Globalization;
using System.Numerics;
using System.Text;
using DotProlog.Runtime;

namespace DotProlog.Syntax;

/// <summary>
/// Turns Prolog source text into <see cref="Token"/>s. The lexer never throws on malformed input:
/// it records a <see cref="Diagnostic"/> and produces the closest reasonable token so the reader can
/// keep going and report more than one problem per file.
/// </summary>
internal sealed class Lexer
{
    private const string SymbolCharacters = "+-*/\\^<>=~:.?@#&$";

    private readonly string _text;
    private readonly string? _fileName;
    private readonly List<Diagnostic> _diagnostics;
    private readonly CharacterConversionTable? _conversions;
    private readonly PrologFlags? _flags;
    private int _position;
    private int _line = 1;
    private int _lineStart;

    internal Lexer(
        string text,
        string? fileName,
        List<Diagnostic> diagnostics,
        CharacterConversionTable? conversions = null,
        PrologFlags? flags = null
    )
    {
        _text = text;
        _fileName = fileName;
        _diagnostics = diagnostics;
        _conversions = conversions;
        _flags = flags;
    }

    /// <summary>Reads the next token, or an <see cref="TokenKind.Eof"/> token at end of input.</summary>
    internal Token Next()
    {
        var layout = false;
        int start;
        char c;
        int code;
        int width;

        // Loop rather than recurse so a long run of invalid characters cannot exhaust the stack.
        while (true)
        {
            layout |= SkipLayout();
            start = _position;

            if (_position >= _text.Length)
            {
                return new Token(TokenKind.Eof, string.Empty, SpanFrom(start), layout);
            }

            c = InputAt(_position);
            (code, width) = CharacterAt(_position);
            if (
                c is '_' or '\'' or '"' or '`'
                || IsAtomStart(code)
                || char.IsAsciiDigit(c)
                || IsStructural(c)
                || SymbolCharacters.Contains(c, StringComparison.Ordinal)
                || IsSolo(code)
            )
            {
                break;
            }

            Advance(width);
            var shown = width == 2 ? _text.Substring(start, 2) : c.ToString();
            Report(DiagnosticIds.UnexpectedCharacter, $"Unexpected character '{shown}'.", SpanFrom(start));
        }

        if (c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or '|')
        {
            // '[]' and '{}' are atoms, not bracket pairs, when they are written adjacently.
            if ((c == '[' && Peek(1) == ']') || (c == '{' && Peek(1) == '}'))
            {
                Advance(2);
                return new Token(TokenKind.Atom, ConvertedText(start, 2), SpanFrom(start), layout);
            }

            Advance();
            return new Token(TokenKind.Punctuation, ConvertedText(start, 1), SpanFrom(start), layout);
        }

        if (c is '!' or ';')
        {
            Advance();
            return new Token(TokenKind.Atom, ConvertedText(start, 1), SpanFrom(start), layout);
        }

        if (char.IsAsciiDigit(c))
        {
            return ReadNumber(start, layout);
        }

        if (c == '_' || CharacterClass.IsUpper(code))
        {
            while (_position < _text.Length && IsAlphanumericAt(_position, out var step))
            {
                Advance(step);
            }

            return new Token(TokenKind.Variable, ConvertedText(start, _position - start), SpanFrom(start), layout);
        }

        if (IsAtomStart(code))
        {
            while (_position < _text.Length && IsAlphanumericAt(_position, out var step))
            {
                Advance(step);
            }

            return new Token(TokenKind.Atom, ConvertedText(start, _position - start), SpanFrom(start), layout);
        }

        if (IsSolo(code))
        {
            Advance(width);
            return new Token(TokenKind.Atom, ConvertedText(start, width), SpanFrom(start), layout);
        }

        if (c == '\'')
        {
            var name = ReadQuoted('\'', out _);
            return new Token(TokenKind.Atom, name, SpanFrom(start), layout, Quoted: true);
        }

        if (c == '"')
        {
            var value = ReadQuoted('"', out _);
            return new Token(TokenKind.String, value, SpanFrom(start), layout);
        }

        if (c == '`')
        {
            var name = ReadQuoted('`', out _);
            return new Token(TokenKind.Atom, name, SpanFrom(start), layout, Quoted: true);
        }

        if (SymbolCharacters.Contains(c, StringComparison.Ordinal))
        {
            while (_position < _text.Length && SymbolCharacters.Contains(InputAt(_position), StringComparison.Ordinal))
            {
                Advance();
            }

            var symbol = ConvertedText(start, _position - start);

            // A lone '.' followed by layout or end of input terminates a clause.
            if (symbol == "." && (_position >= _text.Length || IsLayout(InputAt(_position)) || InputAt(_position) == '%'))
            {
                return new Token(TokenKind.End, symbol, SpanFrom(start), layout);
            }

            return new Token(TokenKind.Atom, symbol, SpanFrom(start), layout);
        }

        // Unreachable: the loop above only exits on a character one of the branches handles.
        throw new InvalidOperationException($"Unhandled token start character '{c}'.");
    }

    private static bool IsStructural(char c) => c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or '|' or '!' or ';';

    /// <summary>Whether characters are Unicode code points; strict ISO mode keeps UTF-16 code units.</summary>
    private bool CodePoints => _flags?.CodePointCharacters ?? true;

    /// <summary>
    /// The character at <paramref name="position"/> and its width in code units: a surrogate pair is
    /// one character when characters are code points; otherwise it is the converted code unit.
    /// </summary>
    private (int Code, int Width) CharacterAt(int position)
    {
        if (CodePoints && position < _text.Length && CodePointText.IsPairAt(_text, position))
        {
            return (Convert(char.ConvertToUtf32(_text[position], _text[position + 1])), 2);
        }

        if (position < 0 || position >= _text.Length)
        {
            return ('\0', 1);
        }

        var input = _text[position];
        return (input is '\'' or '"' or '`' || (CodePoints && char.IsSurrogate(input)) ? input : Convert(input), 1);
    }

    private bool IsAlphanumericAt(int position, out int width)
    {
        (var code, width) = CharacterAt(position);
        return code == '_'
            || CharacterClass.IsLetterOrDigit(code)
            || (CodePoints && code >= 0x80 && CharacterClass.IsIdentifierContinue(code));
    }

    /// <summary>
    /// Whether <paramref name="code"/> starts an atom made of letters. A character outside ASCII
    /// starts one when it may start an identifier — every letter, and in <c>Modern</c> the letter
    /// numbers too — and is not an uppercase letter, which starts a variable.
    /// </summary>
    private bool IsAtomStart(int code) =>
        CharacterClass.IsLetter(code) || (CodePoints && code >= 0x80 && CharacterClass.IsIdentifierStart(code));

    /// <summary>Whether <paramref name="code"/> is a one-character atom by itself: a symbol outside ASCII in <c>Modern</c>.</summary>
    private bool IsSolo(int code) => CodePoints && CharacterClass.IsSolo(code);

    private static bool IsLayout(char c) => char.IsWhiteSpace(c);

    private char Peek(int offset) => InputAt(_position + offset);

    private char RawPeek(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    /// <summary>
    /// The code unit at <paramref name="position"/>, through character conversion. A converted
    /// character outside the Basic Multilingual Plane is no source structure the lexer compares
    /// against, so it reads as U+FFFF here; <see cref="CharacterAt"/> carries its real code.
    /// </summary>
    private char InputAt(int position)
    {
        if (position < 0 || position >= _text.Length)
        {
            return '\0';
        }

        var input = _text[position];
        if (input is '\'' or '"' or '`' || (CodePoints && char.IsSurrogate(input)))
        {
            return input;
        }

        var converted = Convert(input);
        return converted <= char.MaxValue ? (char)converted : '\uFFFF';
    }

    private int Convert(int code) =>
        _flags?.CharConversion == true && _conversions is not null ? _conversions.Convert(code) : code;

    /// <summary>
    /// The source text between two code-unit positions, each character converted. Conversion works per
    /// character — per code point in the default mode — and may change the UTF-16 length.
    /// </summary>
    private string ConvertedText(int start, int length)
    {
        if (_flags?.CharConversion != true || _conversions is null)
        {
            return _text.Substring(start, length);
        }

        var converted = new StringBuilder(length);
        for (var index = start; index < start + length; )
        {
            var (code, width) = CharacterClass.At(_text, index, CodePoints);
            var output = code is '\'' or '"' or '`' ? code : Convert(code);
            if (output <= char.MaxValue)
            {
                converted.Append((char)output);
            }
            else
            {
                converted.Append(char.ConvertFromUtf32(output));
            }

            index += width;
        }

        return converted.ToString();
    }

    private void Advance(int count = 1)
    {
        for (var index = 0; index < count && _position < _text.Length; index++)
        {
            if (_text[_position] == '\n')
            {
                _line++;
                _lineStart = _position + 1;
            }

            _position++;
        }
    }

    private bool SkipLayout()
    {
        var before = _position;
        while (_position < _text.Length)
        {
            var c = InputAt(_position);
            if (c == '\n')
            {
                Advance();
            }
            else if (IsLayout(c))
            {
                Advance();
            }
            else if (c == '%')
            {
                while (_position < _text.Length && InputAt(_position) != '\n')
                {
                    Advance();
                }
            }
            else if (c == '/' && Peek(1) == '*')
            {
                var commentStart = _position;
                Advance(2);
                while (_position < _text.Length && !(InputAt(_position) == '*' && Peek(1) == '/'))
                {
                    Advance();
                }

                if (_position >= _text.Length)
                {
                    Report(DiagnosticIds.UnterminatedQuoted, "Unterminated block comment.", SpanFrom(commentStart));
                }
                else
                {
                    Advance(2);
                }
            }
            else
            {
                break;
            }
        }

        return _position != before;
    }

    private Token ReadNumber(int start, bool layout)
    {
        if (InputAt(_position) == '0' && Peek(1) == '\'')
        {
            Advance(2);
            if (_position >= _text.Length)
            {
                Report(DiagnosticIds.InvalidNumber, "Unterminated character-code literal.", SpanFrom(start));
                return new Token(TokenKind.Integer, "0", SpanFrom(start), layout);
            }

            int code;
            if (_text[_position] == '\\')
            {
                var builder = new StringBuilder();
                var reported = _diagnostics.Count;
                SourceSpan quote = SpanFrom(start);
                ReadEscape(builder);

                // A line continuation denotes no character, so it leaves the literal without one.
                if (builder.Length == 0 && _diagnostics.Count == reported)
                {
                    Report(
                        DiagnosticIds.InvalidNumber,
                        "A character-code literal needs a character, not a line continuation.",
                        quote
                    );
                }

                code =
                    builder.Length == 0 ? 0
                    : CodePoints && builder.Length == 2 && char.IsHighSurrogate(builder[0])
                        ? char.ConvertToUtf32(builder[0], builder[1])
                    : builder[0];
            }
            else if (_text[_position] == '\'' && Peek(1) == '\'')
            {
                Advance(2);
                code = '\'';
            }
            else if (CodePoints && CodePointText.IsPairAt(_text, _position))
            {
                code = char.ConvertToUtf32(_text[_position], _text[_position + 1]);
                Advance(2);
            }
            else
            {
                code = _text[_position];

                // The character is a single quoted character, so the rule for quoted text holds:
                // a control or layout character other than the space has to be written as an escape.
                if (code != ' ' && (char.IsControl((char)code) || IsLayout((char)code)))
                {
                    Report(
                        DiagnosticIds.InvalidQuotedCharacter,
                        "Control and layout characters in a character-code literal must use an escape sequence.",
                        SpanFrom(_position)
                    );
                }

                Advance();
            }

            return new Token(TokenKind.Integer, ConvertedText(start, _position - start), SpanFrom(start), layout, Integer: code);
        }

        if (InputAt(_position) == '0' && Peek(1) is 'x' or 'o' or 'b')
        {
            var marker = Peek(1);
            var radix = marker switch
            {
                'x' => 16,
                'o' => 8,
                _ => 2,
            };
            Advance(2);
            var digitsStart = _position;
            while (_position < _text.Length && IsRadixDigit(InputAt(_position), radix))
            {
                Advance();
            }

            if (_position == digitsStart)
            {
                Report(DiagnosticIds.InvalidNumber, $"Expected digits after '0{marker}'.", SpanFrom(start));
                return new Token(TokenKind.Integer, ConvertedText(start, _position - start), SpanFrom(start), layout);
            }

            BigInteger radixValue = 0;
            foreach (var digit in ConvertedText(digitsStart, _position - digitsStart))
            {
                radixValue =
                    (radixValue * radix) + (char.IsAsciiDigit(digit) ? digit - '0' : char.ToLowerInvariant(digit) - 'a' + 10);
            }

            var overflow = radixValue > long.MaxValue;
            return new Token(
                TokenKind.Integer,
                ConvertedText(start, _position - start),
                SpanFrom(start),
                layout,
                Integer: overflow ? 0 : (long)radixValue,
                IntegerOverflow: overflow,
                Big: overflow ? radixValue : default
            );
        }

        while (_position < _text.Length && char.IsAsciiDigit(InputAt(_position)))
        {
            Advance();
        }

        var isFloat = false;
        if (_position < _text.Length && InputAt(_position) == '.' && char.IsAsciiDigit(Peek(1)))
        {
            isFloat = true;
            Advance();
            while (_position < _text.Length && char.IsAsciiDigit(InputAt(_position)))
            {
                Advance();
            }
        }

        if (isFloat && _position < _text.Length && (InputAt(_position) is 'e' or 'E'))
        {
            var exponentOffset = Peek(1) is '+' or '-' ? 2 : 1;
            if (char.IsAsciiDigit(Peek(exponentOffset)))
            {
                isFloat = true;
                Advance(exponentOffset);
                while (_position < _text.Length && char.IsAsciiDigit(InputAt(_position)))
                {
                    Advance();
                }
            }
        }

        if (
            !isFloat
            && _position < _text.Length
            && InputAt(_position) == 'r'
            && char.IsAsciiDigit(Peek(1))
            && (_flags?.RationalLiterals ?? true)
        )
        {
            var numeratorText = ConvertedText(start, _position - start);
            Advance();
            var denominatorStart = _position;
            while (_position < _text.Length && char.IsAsciiDigit(InputAt(_position)))
            {
                Advance();
            }

            BigInteger numerator = BigInteger.Parse(numeratorText, NumberStyles.None, CultureInfo.InvariantCulture);
            BigInteger denominator = BigInteger.Parse(
                ConvertedText(denominatorStart, _position - denominatorStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture
            );
            SourceSpan rationalSpan = SpanFrom(start);
            if (denominator.IsZero)
            {
                Report(DiagnosticIds.InvalidNumber, "A rational literal cannot have a zero denominator.", rationalSpan);
                return new Token(TokenKind.Integer, ConvertedText(start, _position - start), rationalSpan, layout);
            }

            return new Token(
                TokenKind.Integer,
                ConvertedText(start, _position - start),
                rationalSpan,
                layout,
                Big: numerator,
                RationalDenominator: denominator
            );
        }

        var literal = ConvertedText(start, _position - start);
        SourceSpan span = SpanFrom(start);

        if (isFloat)
        {
            var value = double.Parse(literal, CultureInfo.InvariantCulture);
            return new Token(TokenKind.Float, literal, span, layout, Float: value, FloatOverflow: double.IsInfinity(value));
        }

        if (!long.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
        {
            BigInteger big = BigInteger.Parse(literal, NumberStyles.None, CultureInfo.InvariantCulture);
            return new Token(TokenKind.Integer, literal, span, layout, IntegerOverflow: true, Big: big);
        }

        return new Token(TokenKind.Integer, literal, span, layout, Integer: integer);
    }

    private static bool IsRadixDigit(char c, int radix) =>
        radix switch
        {
            16 => char.IsAsciiHexDigit(c),
            8 => c is >= '0' and <= '7',
            _ => c is '0' or '1',
        };

    private string ReadQuoted(char quote, out bool terminated)
    {
        var start = _position;
        Advance();
        var builder = new StringBuilder();
        terminated = false;

        while (_position < _text.Length)
        {
            var c = _text[_position];
            if (c == quote)
            {
                if (RawPeek(1) == quote)
                {
                    builder.Append(quote);
                    Advance(2);
                    continue;
                }

                Advance();
                terminated = true;
                break;
            }

            if (c == '\\')
            {
                ReadEscape(builder);
                continue;
            }

            if (c != ' ' && (char.IsControl(c) || IsLayout(c)))
            {
                Report(
                    DiagnosticIds.InvalidQuotedCharacter,
                    "Control and layout characters inside quoted text must use an escape sequence.",
                    SpanFrom(_position)
                );
                Advance();
                continue;
            }

            builder.Append(c);
            Advance();
        }

        if (!terminated)
        {
            Report(
                DiagnosticIds.UnterminatedQuoted,
                $"Unterminated {(quote == '"' ? "string" : "quoted atom")}.",
                SpanFrom(start)
            );
        }

        return builder.ToString();
    }

    private void ReadEscape(StringBuilder builder)
    {
        var start = _position;
        Advance();
        if (_position >= _text.Length)
        {
            Report(DiagnosticIds.InvalidEscape, "Unterminated escape sequence.", SpanFrom(start));
            return;
        }

        var c = _text[_position];
        Advance();

        switch (c)
        {
            case 'n':
                builder.Append('\n');
                return;
            case 't':
                builder.Append('\t');
                return;
            case 'r':
                builder.Append('\r');
                return;
            case 'a':
                builder.Append('\a');
                return;
            case 'b':
                builder.Append('\b');
                return;
            case 'f':
                builder.Append('\f');
                return;
            case 'v':
                builder.Append('\v');
                return;
            case 'e':
                builder.Append('\u001b');
                return;
            case 'd':
                builder.Append('\u007f');
                return;
            case '0':
                if (_position < _text.Length && (_text[_position] == '\\' || IsEscapeDigit(_text[_position], 8)))
                {
                    ReadNumericEscape(builder, start, radix: 8, "octal", firstDigit: 0);
                }
                else
                {
                    builder.Append('\0');
                }

                return;
            case '\\' or '\'' or '"' or '`':
                builder.Append(c);
                return;
            case '\n':
                return;
            case 'x':
                ReadNumericEscape(builder, start, radix: 16, "hexadecimal");
                return;
            case 'o':
                ReadNumericEscape(builder, start, radix: 8, "octal");
                return;
            case 'u' when CodePoints:
                ReadFixedEscape(builder, start, 'u', digits: 4);
                return;
            case 'U' when CodePoints:
                ReadFixedEscape(builder, start, 'U', digits: 8);
                return;
            case >= '1' and <= '7':
                ReadNumericEscape(builder, start, radix: 8, "octal", firstDigit: c - '0');
                return;

            default:
                Report(DiagnosticIds.InvalidEscape, $"Unrecognised escape sequence '\\{c}'.", SpanFrom(start));
                return;
        }
    }

    private void ReadNumericEscape(StringBuilder builder, int start, int radix, string description, int? firstDigit = null)
    {
        var digitsStart = _position;
        var value = firstDigit ?? 0;
        var overflow = false;
        var largest = CodePoints ? 0x10FFFF : char.MaxValue;
        while (_position < _text.Length && IsEscapeDigit(_text[_position], radix))
        {
            var digit = EscapeDigitValue(_text[_position]);
            if (value > (largest - digit) / radix)
            {
                overflow = true;
            }
            else if (!overflow)
            {
                value = (value * radix) + digit;
            }

            Advance();
        }

        if (_position == digitsStart && firstDigit is null)
        {
            Report(DiagnosticIds.InvalidEscape, $"Expected {description} digits in numeric escape.", SpanFrom(start));
            return;
        }

        if (_position >= _text.Length || _text[_position] != '\\')
        {
            Report(DiagnosticIds.InvalidEscape, "Expected '\\' to terminate numeric escape.", SpanFrom(start));
            return;
        }

        Advance();
        if (overflow)
        {
            Report(DiagnosticIds.InvalidEscape, "Numeric escape exceeds the supported character range.", SpanFrom(start));
            return;
        }

        AppendEscapedCode(builder, start, value);
    }

    /// <summary>
    /// <c>\uXXXX</c> and <c>\UXXXXXXXX</c>: exactly four or eight hexadecimal digits naming a character,
    /// as SWI-Prolog reads them. ISO has no such escape, so strict ISO mode never reaches here.
    /// </summary>
    private void ReadFixedEscape(StringBuilder builder, int start, char letter, int digits)
    {
        long value = 0;
        for (var index = 0; index < digits; index++)
        {
            if (_position >= _text.Length || !char.IsAsciiHexDigit(_text[_position]))
            {
                Report(
                    DiagnosticIds.InvalidEscape,
                    $"Expected {digits} hexadecimal digits in '\\{letter}' escape.",
                    SpanFrom(start)
                );
                return;
            }

            value = (value * 16) + EscapeDigitValue(_text[_position]);
            Advance();
        }

        if (value > 0x10FFFF)
        {
            Report(DiagnosticIds.InvalidEscape, "Numeric escape exceeds the supported character range.", SpanFrom(start));
            return;
        }

        AppendEscapedCode(builder, start, (int)value);
    }

    /// <summary>
    /// Appends an escaped character. When characters are code points a surrogate names no character,
    /// so an escape can neither produce one nor be joined with another into a pair.
    /// </summary>
    private void AppendEscapedCode(StringBuilder builder, int start, int value)
    {
        if (CodePoints && value is >= 0xD800 and <= 0xDFFF)
        {
            Report(DiagnosticIds.InvalidEscape, "A surrogate code is not a character.", SpanFrom(start));
            return;
        }

        if (value <= char.MaxValue)
        {
            builder.Append((char)value);
        }
        else
        {
            builder.Append(char.ConvertFromUtf32(value));
        }
    }

    private static bool IsEscapeDigit(char c, int radix) => radix == 16 ? char.IsAsciiHexDigit(c) : c is >= '0' and <= '7';

    private static int EscapeDigitValue(char c) => char.IsAsciiDigit(c) ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10;

    private SourceSpan SpanFrom(int start) => new(start, Math.Max(1, _position - start), _line, start - _lineStart + 1);

    private void Report(string id, string message, SourceSpan span) =>
        _diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, _fileName));
}
