using System.Globalization;
using System.Text;

namespace DotProlog.Runtime;

/// <summary>
/// <c>format/1</c>, <c>format/2</c>, and <c>format/3</c>: the directive-driven writer.
/// </summary>
/// <remarks>
/// <para>
/// Supported directives are <c>~w ~p ~q ~a ~d ~D ~f ~e ~g ~s ~c ~n ~i ~t ~| ~+ ~~</c>. A directive
/// may carry a numeric argument (<c>~2f</c>), take that argument from the argument list
/// (<c>~*c</c>), or carry a character (<c>~`-t</c>). Anything else raises
/// <c>domain_error(format_directive, Char)</c> rather than being written through, so a typo in a
/// format string is reported where it is made.
/// </para>
/// <para>
/// Column stops are honoured: <c>~t</c> marks where padding may be inserted, <c>~N|</c> pads to
/// column N, and <c>~N+</c> pads to N columns past the previous stop. That is what makes a table
/// line up, and it is the reason to reach for <c>format/2</c> over <c>write/1</c> at all.
/// </para>
/// </remarks>
internal static class FormatBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        // The public format/1,2,3 are library wrappers that expand ~@ — a goal a native builtin
        // cannot run — before handing the text to this native engine.
        registry.Register(
            "$format",
            2,
            static machine =>
            {
                StreamBuiltins.WriteCurrentText(machine, Render(machine, machine.Argument(0), machine.Argument(1)));
                return true;
            }
        );

        registry.Register("$format", 3, Format3);

        registry.Register(
            "tab",
            1,
            static machine =>
            {
                PrologNumber count = ArithmeticEvaluator.Evaluate(machine, machine.Argument(0));
                if (count.IsFloat)
                {
                    throw PrologErrors.Type(machine, "integer", ArithmeticEvaluator.ToCell(machine, count));
                }

                StreamBuiltins.WriteCurrentText(machine, new string(' ', (int)Math.Max(count.Integer, 0)));
                return true;
            }
        );
    }

    /// <summary>
    /// <c>format(+Sink, +Format, +Arguments)</c>. The sink is <c>atom(A)</c>, <c>string(S)</c>,
    /// <c>codes(C)</c>, <c>codes(C, Tail)</c>, <c>chars(C)</c>, or <c>chars(C, Tail)</c> to capture the
    /// text, or a stream or alias to write it — resolved exactly as <c>write/2</c> resolves its
    /// stream, so <c>user_error</c> reaches the error stream. The sink is checked before the format.
    /// </summary>
    private static bool Format3(Machine machine)
    {
        var capture = StreamBuiltins.IsCaptureSink(machine, 0);
        var text = Render(machine, machine.Argument(1), machine.Argument(2));

        if (capture)
        {
            return StreamBuiltins.DeliverCapture(machine, machine.Argument(0), text);
        }

        StreamBuiltins.WriteStreamText(machine, 0, text);
        return true;
    }

    private static string Render(Machine machine, Cell format, Cell arguments)
    {
        var text = FormatText(machine, format);
        List<Cell> given = ArgumentsOf(machine, arguments);
        var output = new Layout(machine.Symbols.CodePoints);
        var next = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '~')
            {
                output.Append(text[i]);
                continue;
            }

            i++;
            if (i >= text.Length)
            {
                throw PrologErrors.Domain(machine, "format_string", format);
            }

            // ~`Ct carries a fill character, ~*c takes its count from the arguments, and ~2f
            // carries digits. All three arrive at the directive as the same optional number.
            int? count = null;

            if (text[i] == '`' && i + 1 < text.Length)
            {
                (count, var width) = CharacterClass.At(text, ++i, machine.Symbols.CodePoints);
                i += width;
            }
            else if (text[i] == '*')
            {
                count = (int)IntegerArgument(machine, given, ref next, arguments);
                i++;
            }
            else
            {
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i > start)
                {
                    count = int.Parse(text[start..i], CultureInfo.InvariantCulture);
                }
            }

            if (i >= text.Length)
            {
                throw PrologErrors.Domain(machine, "format_string", format);
            }

            Directive(machine, output, text[i], count, given, ref next, arguments);
        }

        if (next < given.Count)
        {
            throw PrologErrors.Domain(machine, "format_arguments", machine.Dereference(arguments));
        }

        return output.ToString();
    }

    private static void Directive(
        Machine machine,
        Layout output,
        char directive,
        int? count,
        List<Cell> given,
        ref int next,
        Cell arguments
    )
    {
        switch (directive)
        {
            case 'w':
            case 'p':
                output.Append(TermWriter.ToDisplayString(machine, Next(machine, given, ref next, arguments)));
                break;

            case 'q':
                output.Append(TermWriter.ToDisplayString(machine, Next(machine, given, ref next, arguments), quoted: true));
                break;

            case 'a':
            {
                Cell cell = Next(machine, given, ref next, arguments);
                output.Append(
                    PrologText.TryRead(machine, cell, TextKinds.Atom | TextKinds.String, out var value)
                        ? value
                        : throw PrologErrors.FormatArgumentType(machine, 'a', cell)
                );
                break;
            }

            case 'd':
            case 'D':
                output.Append(Decimal(machine, Next(machine, given, ref next, arguments), count, directive == 'D'));
                break;

            case 'e':
            case 'f':
            case 'g':
                output.Append(Real(machine, Next(machine, given, ref next, arguments), directive, count ?? 6));
                break;

            case 'r':
            case 'R':
                output.Append(Radix(machine, Next(machine, given, ref next, arguments), count, directive == 'R'));
                break;

            case 's':
            {
                Cell cell = Next(machine, given, ref next, arguments);
                output.Append(
                    PrologText.TryRead(machine, cell, TextKinds.Atom | TextKinds.String | TextKinds.List, out var value)
                        ? value
                        : throw PrologErrors.FormatArgumentType(machine, 's', cell)
                );
                break;
            }

            case 'W':
            {
                Cell term = Next(machine, given, ref next, arguments);
                Cell options = Next(machine, given, ref next, arguments);
                output.Append(StreamBuiltins.RenderTermWithOptions(machine, term, options));
                break;
            }

            case 'c':
            {
                Cell cell = Next(machine, given, ref next, arguments);
                if (cell.Tag is not (CellTag.Integer or CellTag.BigInteger))
                {
                    throw PrologErrors.Type(machine, "integer", cell);
                }

                // SWI's errors: a negative code is not an argument ~c prints, and a code past Unicode
                // or in the surrogate range names no code point.
                if (cell.Tag == CellTag.Integer && cell.Integer < 0)
                {
                    throw PrologErrors.FormatArgumentType(machine, 'c', cell);
                }

                if (cell.Tag == CellTag.BigInteger || !PrologText.IsCode(machine, cell.Integer))
                {
                    throw PrologErrors.Representation(machine, "code_point");
                }

                var character = PrologText.CharacterOf(cell.Integer);
                for (var copies = count ?? 1; copies > 0; copies--)
                {
                    output.Append(character);
                }

                break;
            }

            case 'n':
                output.Append(new string('\n', count ?? 1));
                break;

            case 'i':
                Next(machine, given, ref next, arguments);
                break;

            case 't':
                output.Fill(count ?? ' ');
                break;

            case '|':
                output.Column(count ?? output.CurrentColumn, relative: false);
                break;

            case '+':
                output.Column(count ?? 8, relative: true);
                break;

            case '~':
                output.Append('~');
                break;

            default:
                throw PrologErrors.Domain(
                    machine,
                    "format_directive",
                    Cell.Atom(machine.Symbols.InternAtom(directive.ToString()))
                );
        }
    }

    /// <summary>
    /// <c>~Nr</c> and <c>~NR</c>: the integer in radix N between 2 and 36, lowercase or uppercase.
    /// The radix is the directive's count and has no default.
    /// </summary>
    private static string Radix(Machine machine, Cell cell, int? radix, bool uppercase)
    {
        if (cell.Tag is not (CellTag.Integer or CellTag.BigInteger))
        {
            throw PrologErrors.Type(machine, "integer", cell);
        }

        if (radix is null or < 2 or > 36)
        {
            throw PrologErrors.Domain(machine, "radix", Cell.Integer60(radix ?? 0));
        }

        if (cell.Tag == CellTag.BigInteger)
        {
            return BigRadix(machine.Symbols.GetBig(cell.Index), radix.Value, uppercase);
        }

        var value = cell.Integer;
        var negative = value < 0;
        var magnitude = (ulong)Math.Abs(value);
        var alphabet = uppercase ? "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" : "0123456789abcdefghijklmnopqrstuvwxyz";
        var digits = new StringBuilder();

        do
        {
            digits.Insert(0, alphabet[(int)(magnitude % (ulong)radix.Value)]);
            magnitude /= (ulong)radix.Value;
        } while (magnitude > 0);

        return negative ? $"-{digits}" : digits.ToString();
    }

    private static string BigRadix(System.Numerics.BigInteger value, int radix, bool uppercase)
    {
        var alphabet = uppercase ? "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" : "0123456789abcdefghijklmnopqrstuvwxyz";
        var negative = value.Sign < 0;
        System.Numerics.BigInteger magnitude = System.Numerics.BigInteger.Abs(value);
        var digits = new StringBuilder();

        do
        {
            magnitude = System.Numerics.BigInteger.DivRem(magnitude, radix, out System.Numerics.BigInteger digit);
            digits.Insert(0, alphabet[(int)digit]);
        } while (!magnitude.IsZero);

        return negative ? $"-{digits}" : digits.ToString();
    }

    private static string Decimal(Machine machine, Cell cell, int? shift, bool grouped)
    {
        if (cell.Tag is not (CellTag.Integer or CellTag.BigInteger))
        {
            throw PrologErrors.Type(machine, "integer", cell);
        }

        if (cell.Tag == CellTag.BigInteger)
        {
            return BigDecimal(machine.Symbols.GetBig(cell.Index), shift, grouped);
        }

        var value = cell.Integer;

        if (grouped)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        if (shift is null or 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // ~Nd writes the integer with a decimal point N digits from the right, which is how money
        // held as minor units is printed without ever becoming a float.
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture).PadLeft(shift.Value + 1, '0');
        var sign = value < 0 ? "-" : "";
        return $"{sign}{digits[..^shift.Value]}.{digits[^shift.Value..]}";
    }

    private static string BigDecimal(System.Numerics.BigInteger value, int? shift, bool grouped)
    {
        if (grouped)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        if (shift is null or 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var digits = System.Numerics.BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).PadLeft(shift.Value + 1, '0');
        var sign = value.Sign < 0 ? "-" : "";
        return $"{sign}{digits[..^shift.Value]}.{digits[^shift.Value..]}";
    }

    private static string Real(Machine machine, Cell cell, char directive, int digits)
    {
        var value = cell.Tag switch
        {
            CellTag.Integer => cell.Integer,
            CellTag.BigInteger => (double)machine.Symbols.GetBig(cell.Index),
            CellTag.Float => machine.Symbols.GetFloat(cell.Index),
            _ => throw PrologErrors.Type(machine, "number", cell),
        };

        // ~e is C's %e, whose exponent is two digits. The .NET "e" specifier writes three, so the
        // shape is spelled out as a custom format instead.
        if (directive == 'e')
        {
            var mantissa = digits > 0 ? $"0.{new string('0', digits)}" : "0";
            return value.ToString($"{mantissa}e+00", CultureInfo.InvariantCulture);
        }

        return value.ToString($"{directive}{digits.ToString(CultureInfo.InvariantCulture)}", CultureInfo.InvariantCulture);
    }

    private static Cell Next(Machine machine, List<Cell> given, ref int next, Cell arguments) =>
        next < given.Count
            ? machine.Dereference(given[next++])
            : throw PrologErrors.Domain(machine, "format_arguments", machine.Dereference(arguments));

    private static long IntegerArgument(Machine machine, List<Cell> given, ref int next, Cell arguments)
    {
        Cell cell = Next(machine, given, ref next, arguments);
        return cell.Tag == CellTag.Integer ? cell.Integer : throw PrologErrors.Type(machine, "integer", cell);
    }

    /// <summary>
    /// The format string: an atom, a string, or a list of codes or characters, where <c>[]</c> is the
    /// empty format. Anything else is SWI's <c>type_error(text, Format)</c>.
    /// </summary>
    private static string FormatText(Machine machine, Cell format)
    {
        if (format.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return PrologText.TryRead(machine, format, TextKinds.Atom | TextKinds.String | TextKinds.List, out var text)
            ? text
            : throw PrologErrors.Type(machine, "text", format);
    }

    /// <summary>
    /// The arguments, which are a list when there is more than one and may be the bare term when
    /// there is exactly one — <c>format("~w", hello)</c> is as common as the list form.
    /// </summary>
    private static List<Cell> ArgumentsOf(Machine machine, Cell arguments)
    {
        if (TermList.IsProper(machine, arguments))
        {
            List<Cell> elements = [];
            TermList.Read(machine, arguments, elements);
            return elements;
        }

        return [arguments];
    }

    /// <summary>
    /// The text built so far, with the column stops <c>~t</c> and <c>~|</c> need.
    /// </summary>
    /// <remarks>
    /// Padding is decided when a column stop is reached, not when <c>~t</c> is seen, because how much
    /// to insert is only known once the segment's text is complete. Each <c>~t</c> records a position;
    /// the padding is then shared out among those positions, with any remainder going to the last, so
    /// that a single <c>~t</c> right-aligns and two of them centre.
    /// </remarks>
    /// <summary>
    /// The text being formatted, with its column stops. Columns count characters as the program's mode
    /// counts them — code points in <c>Modern</c>, code units in strict ISO mode — while fill positions
    /// stay indexes into the text.
    /// </summary>
    private sealed class Layout(bool codePoints)
    {
        private readonly StringBuilder _text = new();
        private readonly List<(int Position, int Fill)> _fills = [];
        private int _column;
        private int _stop;

        /// <summary>The column the next character would be written at.</summary>
        internal int CurrentColumn => _column;

        internal void Append(char character)
        {
            _text.Append(character);

            if (character == '\n')
            {
                _column = 0;
                _stop = 0;
                _fills.Clear();
            }
            else if (!(codePoints && char.IsLowSurrogate(character) && _text.Length >= 2 && char.IsHighSurrogate(_text[^2])))
            {
                _column++;
            }
        }

        internal void Append(string text)
        {
            foreach (var character in text)
            {
                Append(character);
            }
        }

        internal void Fill(int character) => _fills.Add((_text.Length, character));

        internal void Column(int column, bool relative)
        {
            var target = relative ? _stop + column : column;
            var padding = target - CurrentColumn;

            if (padding > 0)
            {
                Pad(padding);
            }

            _stop = _column;
            _fills.Clear();
        }

        private void Pad(int padding)
        {
            if (_fills.Count == 0)
            {
                _text.Append(' ', padding);
                _column += padding;
                return;
            }

            var share = padding / _fills.Count;
            var remainder = padding % _fills.Count;

            // Inserted back to front so that an earlier fill position is still where it was.
            for (var i = _fills.Count - 1; i >= 0; i--)
            {
                var amount = share + (i == _fills.Count - 1 ? remainder : 0);
                var fill = PrologText.CharacterOf(_fills[i].Fill);
                _text.Insert(_fills[i].Position, string.Concat(Enumerable.Repeat(fill, amount)));
            }

            _column += padding;
        }

        public override string ToString() => _text.ToString();
    }
}
