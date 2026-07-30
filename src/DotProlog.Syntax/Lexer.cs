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
        bool layout = false;
        int start;
        char c;

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
            if (
                c is '_' or '\'' or '"' or '`'
                || char.IsLetter(c)
                || char.IsAsciiDigit(c)
                || IsStructural(c)
                || SymbolCharacters.Contains(c, StringComparison.Ordinal)
            )
            {
                break;
            }

            Advance();
            Report(DiagnosticIds.UnexpectedCharacter, $"Unexpected character '{c}'.", SpanFrom(start));
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

        if (c == '_' || char.IsUpper(c))
        {
            while (_position < _text.Length && IsAlphanumeric(InputAt(_position)))
            {
                Advance();
            }

            return new Token(TokenKind.Variable, ConvertedText(start, _position - start), SpanFrom(start), layout);
        }

        if (char.IsLetter(c))
        {
            while (_position < _text.Length && IsAlphanumeric(InputAt(_position)))
            {
                Advance();
            }

            return new Token(TokenKind.Atom, ConvertedText(start, _position - start), SpanFrom(start), layout);
        }

        if (c == '\'')
        {
            string name = ReadQuoted('\'', out _);
            return new Token(TokenKind.Atom, name, SpanFrom(start), layout, Quoted: true);
        }

        if (c == '"')
        {
            string value = ReadQuoted('"', out _);
            return new Token(TokenKind.String, value, SpanFrom(start), layout);
        }

        if (c == '`')
        {
            string name = ReadQuoted('`', out _);
            return new Token(TokenKind.Atom, name, SpanFrom(start), layout, Quoted: true);
        }

        if (SymbolCharacters.Contains(c, StringComparison.Ordinal))
        {
            while (_position < _text.Length && SymbolCharacters.Contains(InputAt(_position), StringComparison.Ordinal))
            {
                Advance();
            }

            string symbol = ConvertedText(start, _position - start);

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

    private static bool IsAlphanumeric(char c) => c == '_' || char.IsLetterOrDigit(c);

    private static bool IsLayout(char c) => char.IsWhiteSpace(c);

    private char Peek(int offset) => InputAt(_position + offset);

    private char RawPeek(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private char InputAt(int position)
    {
        if (position < 0 || position >= _text.Length)
        {
            return '\0';
        }

        char input = _text[position];
        if (input is '\'' or '"' or '`')
        {
            return input;
        }

        return _flags?.CharConversion == true && _conversions is not null ? _conversions.Convert(input) : input;
    }

    private string ConvertedText(int start, int length)
    {
        if (_flags?.CharConversion != true || _conversions is null)
        {
            return _text.Substring(start, length);
        }

        return string.Create(
            length,
            (Text: _text, Start: start, Conversions: _conversions),
            static (output, state) =>
            {
                for (int index = 0; index < output.Length; index++)
                {
                    output[index] = state.Conversions.Convert(state.Text[state.Start + index]);
                }
            }
        );
    }

    private void Advance(int count = 1)
    {
        for (int index = 0; index < count && _position < _text.Length; index++)
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
        int before = _position;
        while (_position < _text.Length)
        {
            char c = InputAt(_position);
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
                int commentStart = _position;
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
                ReadEscape(builder);
                code = builder.Length > 0 ? builder[0] : 0;
            }
            else if (_text[_position] == '\'' && Peek(1) == '\'')
            {
                Advance(2);
                code = '\'';
            }
            else
            {
                code = _text[_position];
                Advance();
            }

            return new Token(TokenKind.Integer, ConvertedText(start, _position - start), SpanFrom(start), layout, Integer: code);
        }

        if (InputAt(_position) == '0' && Peek(1) is 'x' or 'o' or 'b')
        {
            char marker = Peek(1);
            int radix = marker switch
            {
                'x' => 16,
                'o' => 8,
                _ => 2,
            };
            Advance(2);
            int digitsStart = _position;
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
            foreach (char digit in ConvertedText(digitsStart, _position - digitsStart))
            {
                radixValue =
                    (radixValue * radix) + (char.IsAsciiDigit(digit) ? digit - '0' : char.ToLowerInvariant(digit) - 'a' + 10);
            }

            bool overflow = radixValue > long.MaxValue;
            return new Token(
                TokenKind.Integer,
                ConvertedText(start, _position - start),
                SpanFrom(start),
                layout,
                Integer: overflow ? 0 : (long)radixValue,
                IntegerOverflow: overflow
            );
        }

        while (_position < _text.Length && char.IsAsciiDigit(InputAt(_position)))
        {
            Advance();
        }

        bool isFloat = false;
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
            int exponentOffset = Peek(1) is '+' or '-' ? 2 : 1;
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

        string literal = ConvertedText(start, _position - start);
        SourceSpan span = SpanFrom(start);

        if (isFloat)
        {
            double value = double.Parse(literal, CultureInfo.InvariantCulture);
            return new Token(TokenKind.Float, literal, span, layout, Float: value, FloatOverflow: double.IsInfinity(value));
        }

        if (!long.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out long integer))
        {
            return new Token(TokenKind.Integer, literal, span, layout, IntegerOverflow: true);
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
        int start = _position;
        Advance();
        var builder = new StringBuilder();
        terminated = false;

        while (_position < _text.Length)
        {
            char c = _text[_position];
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
        int start = _position;
        Advance();
        if (_position >= _text.Length)
        {
            Report(DiagnosticIds.InvalidEscape, "Unterminated escape sequence.", SpanFrom(start));
            return;
        }

        char c = _text[_position];
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
        int digitsStart = _position;
        int value = firstDigit ?? 0;
        bool overflow = false;
        while (_position < _text.Length && IsEscapeDigit(_text[_position], radix))
        {
            int digit = EscapeDigitValue(_text[_position]);
            if (value > (char.MaxValue - digit) / radix)
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

        builder.Append((char)value);
    }

    private static bool IsEscapeDigit(char c, int radix) => radix == 16 ? char.IsAsciiHexDigit(c) : c is >= '0' and <= '7';

    private static int EscapeDigitValue(char c) => char.IsAsciiDigit(c) ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10;

    private SourceSpan SpanFrom(int start) => new(start, Math.Max(1, _position - start), _line, start - _lineStart + 1);

    private void Report(string id, string message, SourceSpan span) =>
        _diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, _fileName));
}
