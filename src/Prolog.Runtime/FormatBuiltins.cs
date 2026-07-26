using System.Globalization;
using System.Text;

namespace Prolog.Runtime;

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
        registry.Register(
            "format",
            1,
            static machine =>
            {
                machine.Output.Write(Render(machine, machine.Argument(0), Cell.Atom(machine.Symbols.EmptyList)));
                return true;
            }
        );

        registry.Register(
            "format",
            2,
            static machine =>
            {
                machine.Output.Write(Render(machine, machine.Argument(0), machine.Argument(1)));
                return true;
            }
        );

        registry.Register("format", 3, Format3);

        registry.Register(
            "tab",
            1,
            static machine =>
            {
                PrologNumber count = ArithmeticEvaluator.Evaluate(machine, machine.Argument(0));
                machine.Output.Write(new string(' ', (int)Math.Max(count.Integer, 0)));
                return true;
            }
        );
    }

    /// <summary>
    /// <c>format(+Sink, +Format, +Arguments)</c>. The sink is <c>atom(A)</c>, <c>codes(C)</c>, or
    /// <c>chars(C)</c> to capture the text, or a stream alias to write it.
    /// </summary>
    /// <remarks>
    /// <c>user_error</c> writes to the machine's output like <c>user_output</c> does. There is no
    /// stream system yet, and an embedding host that captures output would not expect a builtin to
    /// reach past it to the process's stderr.
    /// </remarks>
    private static bool Format3(Machine machine)
    {
        Cell sink = machine.Argument(0);
        string text = Render(machine, machine.Argument(1), machine.Argument(2));

        if (sink.Tag == CellTag.Atom)
        {
            string alias = machine.Symbols.AtomName(sink.Index);
            if (alias is "user_output" or "user_error")
            {
                machine.Output.Write(text);
                return true;
            }

            throw PrologErrors.Domain(machine, "stream_or_alias", sink);
        }

        if (sink.Tag != CellTag.Structure || machine.Symbols.ArityOf(machine.HeapAt(sink.Index).Index) != 1)
        {
            throw sink.Tag == CellTag.Reference
                ? PrologErrors.Instantiation(machine)
                : PrologErrors.Domain(machine, "stream_or_alias", sink);
        }

        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(sink.Index).Index);
        Cell target = machine.HeapAt(sink.Index + 1);

        return machine.Symbols.AtomName(functor.NameAtom) switch
        {
            "atom" => machine.Unify(target, Cell.Atom(machine.Symbols.InternAtom(text))),
            "codes" => machine.Unify(target, TextBuiltins.BuildText(machine, text, chars: false)),
            "chars" => machine.Unify(target, TextBuiltins.BuildText(machine, text, chars: true)),
            _ => throw PrologErrors.Domain(machine, "stream_or_alias", sink),
        };
    }

    private static string Render(Machine machine, Cell format, Cell arguments)
    {
        string text = FormatText(machine, format);
        List<Cell> given = ArgumentsOf(machine, arguments);
        var output = new Layout();
        int next = 0;

        for (int i = 0; i < text.Length; i++)
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
                int start = i;
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
                    TextBuiltins.TryText(machine, cell, out string value)
                        ? value
                        : throw PrologErrors.Type(machine, "atomic", cell)
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

            case 's':
                output.Append(TextBuiltins.TextOfList(machine, Next(machine, given, ref next, arguments)));
                break;

            case 'c':
            {
                Cell cell = Next(machine, given, ref next, arguments);
                if (cell.Tag != CellTag.Integer)
                {
                    throw PrologErrors.Type(machine, "integer", cell);
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

    private static string Decimal(Machine machine, Cell cell, int? shift, bool grouped)
    {
        if (cell.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", cell);
        }

        long value = cell.Integer;

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
        string digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture).PadLeft(shift.Value + 1, '0');
        string sign = value < 0 ? "-" : "";
        return $"{sign}{digits[..^shift.Value]}.{digits[^shift.Value..]}";
    }

    private static string Real(Machine machine, Cell cell, char directive, int digits)
    {
        double value = cell.Tag switch
        {
            CellTag.Integer => cell.Integer,
            CellTag.Float => machine.Symbols.GetFloat(cell.Index),
            _ => throw PrologErrors.Type(machine, "number", cell),
        };

        // ~e is C's %e, whose exponent is two digits. The .NET "e" specifier writes three, so the
        // shape is spelled out as a custom format instead.
        if (directive == 'e')
        {
            string mantissa = digits > 0 ? $"0.{new string('0', digits)}" : "0";
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

        return format.Tag == CellTag.Atom ? machine.Symbols.AtomName(format.Index) : TextBuiltins.TextOfList(machine, format);
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
            foreach (char character in text)
            {
                Append(character);
            }
        }

        internal void Fill(char character) => _fills.Add((_text.Length, character));

        internal void Column(int column, bool relative)
        {
            int target = relative ? _stop - _lineStart + column : column;
            int padding = target - CurrentColumn;

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

            int share = padding / _fills.Count;
            int remainder = padding % _fills.Count;

            // Inserted back to front so that an earlier fill position is still where it was.
            for (int i = _fills.Count - 1; i >= 0; i--)
            {
                int amount = share + (i == _fills.Count - 1 ? remainder : 0);
                _text.Insert(_fills[i].Position, new string(_fills[i].Fill, amount));
            }
        }

        public override string ToString() => _text.ToString();
    }
}
