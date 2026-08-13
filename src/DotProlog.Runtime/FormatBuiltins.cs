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
    /// <c>format(+Sink, +Format, +Arguments)</c>. The sink is <c>atom(A)</c>, <c>codes(C)</c>, or
    /// <c>chars(C)</c> to capture the text, or a stream or alias to write it — resolved exactly as
    /// <c>write/2</c> resolves its stream, so <c>user_error</c> reaches the error stream.
    /// </summary>
    private static bool Format3(Machine machine)
    {
        Cell sink = machine.Argument(0);
        var text = Render(machine, machine.Argument(1), machine.Argument(2));

        if (sink.Tag == CellTag.Structure && machine.Symbols.ArityOf(machine.HeapAt(sink.Index).Index) == 1)
        {
            Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(sink.Index).Index);
            Cell target = machine.HeapAt(sink.Index + 1);

            switch (machine.Symbols.AtomName(functor.NameAtom))
            {
                case "atom":
                    return machine.Unify(target, Cell.Atom(machine.Symbols.InternAtom(text)));
                case "string":
                    return machine.Unify(target, Cell.String(machine.Symbols.InternAtom(text)));
                case "codes":
                    return machine.Unify(target, TextBuiltins.BuildText(machine, text, chars: false));
                case "chars":
                    return machine.Unify(target, TextBuiltins.BuildText(machine, text, chars: true));
            }
        }

        StreamBuiltins.WriteStreamText(machine, 0, text);
        return true;
    }

    private static string Render(Machine machine, Cell format, Cell arguments)
    {
        var text = FormatText(machine, format);
        List<Cell> given = ArgumentsOf(machine, arguments);
        var output = new Layout();
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
                count = text[++i];
                i++;
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
                    TextBuiltins.TryText(machine, cell, out var value) ? value : throw PrologErrors.Type(machine, "atomic", cell)
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
                    cell.Tag == CellTag.String ? machine.Symbols.AtomName(cell.Index) : TextBuiltins.TextOfList(machine, cell)
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

                if (cell.Tag == CellTag.BigInteger)
                {
                    throw PrologErrors.Representation(machine, "character_code");
                }

                output.Append(new string((char)cell.Integer, count ?? 1));
                break;
            }

            case 'n':
                output.Append(new string('\n', count ?? 1));
                break;

            case 'i':
                Next(machine, given, ref next, arguments);
                break;

            case 't':
                output.Fill(count is null ? ' ' : (char)count.Value);
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

    /// <summary>The format string, which may be written as an atom or as a list of codes or characters.</summary>
    private static string FormatText(Machine machine, Cell format)
    {
        if (format.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return format.Tag switch
        {
            CellTag.Atom or CellTag.String => machine.Symbols.AtomName(format.Index),
            _ => TextBuiltins.TextOfList(machine, format),
        };
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
    private sealed class Layout
    {
        private readonly StringBuilder _text = new();
        private readonly List<(int Position, char Fill)> _fills = [];
        private int _lineStart;
        private int _stop;

        /// <summary>The column the next character would be written at.</summary>
        internal int CurrentColumn => _text.Length - _lineStart;

        internal void Append(char character)
        {
            _text.Append(character);

            if (character == '\n')
            {
                _lineStart = _text.Length;
                _stop = _text.Length;
                _fills.Clear();
            }
        }

        internal void Append(string text)
        {
            foreach (var character in text)
            {
                Append(character);
            }
        }

        internal void Fill(char character) => _fills.Add((_text.Length, character));

        internal void Column(int column, bool relative)
        {
            var target = relative ? _stop - _lineStart + column : column;
            var padding = target - CurrentColumn;

            if (padding > 0)
            {
                Pad(padding);
            }

            _stop = _text.Length;
            _fills.Clear();
        }

        private void Pad(int padding)
        {
            if (_fills.Count == 0)
            {
                _text.Append(' ', padding);
                return;
            }

            var share = padding / _fills.Count;
            var remainder = padding % _fills.Count;

            // Inserted back to front so that an earlier fill position is still where it was.
            for (var i = _fills.Count - 1; i >= 0; i--)
            {
                var amount = share + (i == _fills.Count - 1 ? remainder : 0);
                _text.Insert(_fills[i].Position, new string(_fills[i].Fill, amount));
            }
        }

        public override string ToString() => _text.ToString();
    }
}
