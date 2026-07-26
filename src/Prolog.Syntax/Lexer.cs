using System.Globalization;
using System.Text;

namespace Prolog.Syntax;

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
    private int _position;
    private int _line = 1;
    private int _lineStart;

    internal Lexer(string text, string? fileName, List<Diagnostic> diagnostics)
    {
        _text = text;
        _fileName = fileName;
        _diagnostics = diagnostics;
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

            c = _text[_position];
            if (
                c is '_' or '\'' or '"'
                || char.IsLetterOrDigit(c)
                || IsStructural(c)
                || SymbolCharacters.Contains(c, StringComparison.Ordinal)
            )
            {
                break;
            }

            _position++;
            Report(DiagnosticIds.UnexpectedCharacter, $"Unexpected character '{c}'.", SpanFrom(start));
        }

        if (c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or '|')
        {
            // '[]' and '{}' are atoms, not bracket pairs, when they are written adjacently.
            if ((c == '[' && Peek(1) == ']') || (c == '{' && Peek(1) == '}'))
            {
                _position += 2;
                return new Token(TokenKind.Atom, _text.Substring(start, 2), SpanFrom(start), layout);
            }

            _position++;
            return new Token(TokenKind.Punctuation, _text.Substring(start, 1), SpanFrom(start), layout);
        }

        if (c is '!' or ';')
        {
            _position++;
            return new Token(TokenKind.Atom, _text.Substring(start, 1), SpanFrom(start), layout);
        }

        if (char.IsAsciiDigit(c))
        {
            return ReadNumber(start, layout);
        }

        if (c == '_' || char.IsUpper(c))
        {
            while (_position < _text.Length && IsAlphanumeric(_text[_position]))
            {
                _position++;
            }

            return new Token(TokenKind.Variable, _text[start.._position], SpanFrom(start), layout);
        }

        if (char.IsLower(c))
        {
            while (_position < _text.Length && IsAlphanumeric(_text[_position]))
            {
                _position++;
            }

            return new Token(TokenKind.Atom, _text[start.._position], SpanFrom(start), layout);
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

        if (SymbolCharacters.Contains(c, StringComparison.Ordinal))
        {
            while (_position < _text.Length && SymbolCharacters.Contains(_text[_position], StringComparison.Ordinal))
            {
                _position++;
            }

            string symbol = _text[start.._position];

            // A lone '.' followed by layout or end of input terminates a clause.
            if (symbol == "." && (_position >= _text.Length || IsLayout(_text[_position]) || _text[_position] == '%'))
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

    private char Peek(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private bool SkipLayout()
    {
        int before = _position;
        while (_position < _text.Length)
        {
            char c = _text[_position];
            if (c == '\n')
            {
                _position++;
                _line++;
                _lineStart = _position;
            }
            else if (IsLayout(c))
            {
                _position++;
            }
            else if (c == '%')
            {
                while (_position < _text.Length && _text[_position] != '\n')
                {
                    _position++;
                }
            }
            else if (c == '/' && Peek(1) == '*')
            {
                int commentStart = _position;
                _position += 2;
                while (_position < _text.Length && !(_text[_position] == '*' && Peek(1) == '/'))
                {
                    if (_text[_position] == '\n')
                    {
                        _line++;
                        _lineStart = _position + 1;
                    }

                    _position++;
                }

                if (_position >= _text.Length)
                {
                    Report(DiagnosticIds.UnterminatedQuoted, "Unterminated block comment.", SpanFrom(commentStart));
                }
                else
                {
                    _position += 2;
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
        if (_text[_position] == '0' && Peek(1) == '\'')
        {
            _position += 2;
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
                _position += 2;
                code = '\'';
            }
            else
            {
                code = _text[_position];
                _position++;
            }

            return new Token(TokenKind.Integer, _text[start.._position], SpanFrom(start), layout, Integer: code);
        }

        if (_text[_position] == '0' && Peek(1) is 'x' or 'o' or 'b')
        {
            char marker = Peek(1);
            int radix = marker switch
            {
                'x' => 16,
                'o' => 8,
                _ => 2,
            };
            _position += 2;
            int digitsStart = _position;
            while (_position < _text.Length && IsRadixDigit(_text[_position], radix))
            {
                _position++;
            }

            if (_position == digitsStart)
            {
                Report(DiagnosticIds.InvalidNumber, $"Expected digits after '0{marker}'.", SpanFrom(start));
                return new Token(TokenKind.Integer, _text[start.._position], SpanFrom(start), layout);
            }

            long radixValue = 0;
            foreach (char digit in _text.AsSpan(digitsStart, _position - digitsStart))
            {
                radixValue =
                    (radixValue * radix) + (char.IsAsciiDigit(digit) ? digit - '0' : char.ToLowerInvariant(digit) - 'a' + 10);
            }

            return new Token(TokenKind.Integer, _text[start.._position], SpanFrom(start), layout, Integer: radixValue);
        }

        while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
        {
            _position++;
        }

        bool isFloat = false;
        if (_position < _text.Length && _text[_position] == '.' && char.IsAsciiDigit(Peek(1)))
        {
            isFloat = true;
            _position++;
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }
        }

        if (_position < _text.Length && (_text[_position] is 'e' or 'E'))
        {
            int exponentOffset = Peek(1) is '+' or '-' ? 2 : 1;
            if (char.IsAsciiDigit(Peek(exponentOffset)))
            {
                isFloat = true;
                _position += exponentOffset;
                while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
                {
                    _position++;
                }
            }
        }

        string literal = _text[start.._position];
        SourceSpan span = SpanFrom(start);

        if (isFloat)
        {
            double value = double.Parse(literal, CultureInfo.InvariantCulture);
            return new Token(TokenKind.Float, literal, span, layout, Float: value);
        }

        if (!long.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out long integer))
        {
            Report(DiagnosticIds.InvalidNumber, $"Integer literal '{literal}' does not fit in 64 bits.", span);
            return new Token(TokenKind.Integer, literal, span, layout);
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
        _position++;
        var builder = new StringBuilder();
        terminated = false;

        while (_position < _text.Length)
        {
            char c = _text[_position];
            if (c == quote)
            {
                if (Peek(1) == quote)
                {
                    builder.Append(quote);
                    _position += 2;
                    continue;
                }

                _position++;
                terminated = true;
                break;
            }

            if (c == '\\')
            {
                ReadEscape(builder);
                continue;
            }

            if (c == '\n')
            {
                _line++;
                _lineStart = _position + 1;
            }

            builder.Append(c);
            _position++;
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
        _position++;
        if (_position >= _text.Length)
        {
            Report(DiagnosticIds.InvalidEscape, "Unterminated escape sequence.", SpanFrom(start));
            return;
        }

        char c = _text[_position];
        _position++;

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
            case '0':
                builder.Append('\0');
                return;
            case '\\' or '\'' or '"' or '`':
                builder.Append(c);
                return;
            case '\n':
                _line++;
                _lineStart = _position;
                return;
            case 'x':
            {
                int digitsStart = _position;
                while (_position < _text.Length && char.IsAsciiHexDigit(_text[_position]))
                {
                    _position++;
                }

                if (_position == digitsStart)
                {
                    Report(DiagnosticIds.InvalidEscape, "Expected hexadecimal digits after '\\x'.", SpanFrom(start));
                    return;
                }

                builder.Append((char)Convert.ToInt32(_text[digitsStart.._position], 16));
                if (_position < _text.Length && _text[_position] == '\\')
                {
                    _position++;
                }

                return;
            }

            default:
                Report(DiagnosticIds.InvalidEscape, $"Unrecognised escape sequence '\\{c}'.", SpanFrom(start));
                return;
        }
    }

    private SourceSpan SpanFrom(int start) => new(start, Math.Max(1, _position - start), _line, start - _lineStart + 1);

    private void Report(string id, string message, SourceSpan span) =>
        _diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, _fileName));
}
