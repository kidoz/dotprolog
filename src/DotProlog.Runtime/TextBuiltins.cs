using System.Globalization;
using System.Text;

namespace DotProlog.Runtime;

/// <summary>
/// The text predicates: conversions between atoms, numbers, character lists, and code lists, plus
/// concatenation and sub-atom search.
/// </summary>
/// <remarks>
/// <para>
/// An atom is the only text term this engine has — there is no string type — so the SWI-Prolog
/// string predicates are absent rather than aliased to their atom counterparts. Aliasing would let
/// portable code compile here and then behave differently, which is worse than not compiling.
/// </para>
/// <para>
/// A character is a one-character atom and a code is its UTF-16 code unit, so a character outside
/// the Basic Multilingual Plane occupies two codes. That matches how .NET measures a string, and
/// <c>atom_length/2</c> reports the same count.
/// </para>
/// </remarks>
internal static class TextBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("atom_length", 2, AtomLength);
        registry.Register("atom_chars", 2, static machine => Convert(machine, chars: true, numeric: false));
        registry.Register("atom_codes", 2, static machine => Convert(machine, chars: false, numeric: false));
        registry.Register("number_chars", 2, static machine => Convert(machine, chars: true, numeric: true));
        registry.Register("number_codes", 2, static machine => Convert(machine, chars: false, numeric: true));
        registry.Register("char_code", 2, CharCode);
        registry.Register("atom_number", 2, AtomNumber);
        registry.Register("upcase_atom", 2, static machine => ChangeCase(machine, upper: true));
        registry.Register("downcase_atom", 2, static machine => ChangeCase(machine, upper: false));
        registry.Register("atomic_list_concat", 2, static machine => Join(machine, machine.Argument(0), "", 1));
        registry.Register("atomic_list_concat", 3, AtomicListConcat3);

        registry.RegisterNondeterministic("atom_concat", 3, static machine => AtomConcat(machine, 0), AtomConcat);
        registry.RegisterNondeterministic("sub_atom", 5, static machine => SubAtom(machine, 0), SubAtom);
    }

    /// <summary>The text of an atomic term: an atom's name, or a number written as the writer writes it.</summary>
    internal static bool TryText(Machine machine, Cell cell, out string text)
    {
        switch (cell.Tag)
        {
            case CellTag.Atom:
                text = machine.Symbols.AtomName(cell.Index);
                return true;

            case CellTag.Integer:
                text = cell.Integer.ToString(CultureInfo.InvariantCulture);
                return true;

            case CellTag.Float:
                text = TermWriter.ToDisplayString(machine, cell);
                return true;

            default:
                text = string.Empty;
                return false;
        }
    }

    /// <summary>The text of an argument that must be atomic.</summary>
    private static string TextArgument(Machine machine, int index)
    {
        Cell cell = machine.Argument(index);

        if (cell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return TryText(machine, cell, out string text) ? text : throw PrologErrors.Type(machine, "atomic", cell);
    }

    private static bool AtomLength(Machine machine) =>
        machine.Unify(machine.Argument(1), Cell.Integer60(TextArgument(machine, 0).Length));

    private static bool CharCode(Machine machine)
    {
        Cell character = machine.Argument(0);
        Cell code = machine.Argument(1);

        if (character.Tag == CellTag.Atom)
        {
            string name = machine.Symbols.AtomName(character.Index);
            return name.Length == 1
                ? machine.Unify(code, Cell.Integer60(name[0]))
                : throw PrologErrors.Type(machine, "character", character);
        }

        if (character.Tag != CellTag.Reference)
        {
            throw PrologErrors.Type(machine, "character", character);
        }

        if (code.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (code.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", code);
        }

        if (code.Integer is < 0 or > char.MaxValue)
        {
            throw PrologErrors.Representation(machine, "character_code");
        }

        string text = ((char)code.Integer).ToString();
        return machine.Unify(character, Cell.Atom(machine.Symbols.InternAtom(text)));
    }

    private static bool ChangeCase(Machine machine, bool upper)
    {
        string text = TextArgument(machine, 0);
        string changed = upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
        return machine.Unify(machine.Argument(1), Cell.Atom(machine.Symbols.InternAtom(changed)));
    }

    /// <summary>
    /// <c>atom_number(?Atom, ?Number)</c>: fails rather than raising when the atom is not a number,
    /// which is what makes it the predicate to test with.
    /// </summary>
    private static bool AtomNumber(Machine machine)
    {
        Cell atom = machine.Argument(0);

        if (atom.Tag == CellTag.Atom)
        {
            string text = machine.Symbols.AtomName(atom.Index);
            return TryParseNumber(text, out PrologNumber number)
                && machine.Unify(machine.Argument(1), ArithmeticEvaluator.ToCell(machine, number));
        }

        if (atom.Tag != CellTag.Reference)
        {
            throw PrologErrors.Type(machine, "atom", atom);
        }

        Cell value = machine.Argument(1);
        if (value.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return TryText(machine, value, out string written)
            ? machine.Unify(atom, Cell.Atom(machine.Symbols.InternAtom(written)))
            : throw PrologErrors.Type(machine, "number", value);
    }

    /// <summary>
    /// The four list conversions. <paramref name="chars"/> selects one-character atoms over codes,
    /// and <paramref name="numeric"/> selects the <c>number_</c> forms, which parse rather than
    /// intern and raise a syntax error on text that is not a number.
    /// </summary>
    private static bool Convert(Machine machine, bool chars, bool numeric)
    {
        Cell list = machine.Argument(1);

        // A proper list decides the direction, even when the first argument is bound: that is what
        // lets number_codes/2 check a parse rather than only produce one.
        if (TermList.IsProper(machine, list))
        {
            string text = ReadText(machine, list, chars);

            if (!numeric)
            {
                Cell bound = machine.Argument(0);

                // With the first argument already bound, the question is whether those are its
                // characters — so its text is compared. Interning an atom and unifying instead
                // would make atom_chars(1.0, ['1', '.', '0']) fail, since a float is not an atom.
                return bound.Tag == CellTag.Reference
                    ? machine.Unify(bound, Cell.Atom(machine.Symbols.InternAtom(text)))
                    : TryText(machine, bound, out string existing) && string.Equals(existing, text, StringComparison.Ordinal);
            }

            return TryParseNumber(text, out PrologNumber parsed)
                ? machine.Unify(machine.Argument(0), ArithmeticEvaluator.ToCell(machine, parsed))
                : throw machine.CreateBall(SyntaxErrorTerm(machine, "illegal_number"), "syntax_error(illegal_number)");
        }

        Cell source = machine.Argument(0);
        if (source.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (numeric && source.Tag is not (CellTag.Integer or CellTag.Float))
        {
            throw PrologErrors.Type(machine, "number", source);
        }

        string written = TryText(machine, source, out string value) ? value : throw PrologErrors.Type(machine, "atom", source);
        return machine.Unify(list, BuildText(machine, written, chars));
    }

    /// <summary>Reads a proper list of characters or codes as text.</summary>
    private static string ReadText(Machine machine, Cell list, bool chars) =>
        ReadText(machine, TermList.ReadProper(machine, list), chars);

    private static string ReadText(Machine machine, List<Cell> elements, bool chars)
    {
        var text = new StringBuilder();

        foreach (Cell element in elements)
        {
            Cell cell = machine.Dereference(element);

            if (cell.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (chars)
            {
                string name =
                    cell.Tag == CellTag.Atom
                        ? machine.Symbols.AtomName(cell.Index)
                        : throw PrologErrors.Type(machine, "character", cell);

                text.Append(name.Length == 1 ? name : throw PrologErrors.Type(machine, "character", cell));
                continue;
            }

            if (cell.Tag != CellTag.Integer)
            {
                throw PrologErrors.Type(machine, "integer", cell);
            }

            if (cell.Integer is < 0 or > char.MaxValue)
            {
                throw PrologErrors.Representation(machine, "character_code");
            }

            text.Append((char)cell.Integer);
        }

        return text.ToString();
    }

    /// <summary>Builds a list of characters or codes from text.</summary>
    internal static Cell BuildText(Machine machine, string text, bool chars)
    {
        var items = new Cell[text.Length];

        for (int i = 0; i < text.Length; i++)
        {
            items[i] = chars ? Cell.Atom(machine.Symbols.InternAtom(text[i].ToString())) : Cell.Integer60(text[i]);
        }

        return TermList.Build(machine, items);
    }

    /// <summary>
    /// <c>atom_concat(?A, ?B, ?C)</c>. With A and B both bound it concatenates; otherwise C must be
    /// bound and every split of it is offered in turn.
    /// </summary>
    /// <param name="machine">The machine.</param>
    /// <param name="state">The split position to try, counted from the left.</param>
    private static bool AtomConcat(Machine machine, long state)
    {
        Cell first = machine.Argument(0);
        Cell second = machine.Argument(1);

        if (TryText(machine, first, out string left) && TryText(machine, second, out string right))
        {
            return machine.Unify(machine.Argument(2), Cell.Atom(machine.Symbols.InternAtom(left + right)));
        }

        Cell whole = machine.Argument(2);
        if (whole.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (!TryText(machine, whole, out string text))
        {
            throw PrologErrors.Type(machine, "atomic", whole);
        }

        int split = (int)state;
        if (split > text.Length)
        {
            return false;
        }

        // Pushed before any binding: a choice point records the trail as it stands when created, so
        // a binding made first would survive the backtrack that reaches the retry.
        if (split < text.Length)
        {
            machine.PushRetry(split + 1);
        }

        return machine.Unify(first, Cell.Atom(machine.Symbols.InternAtom(text[..split])))
            && machine.Unify(second, Cell.Atom(machine.Symbols.InternAtom(text[split..])));
    }

    /// <summary>
    /// <c>sub_atom(+Atom, ?Before, ?Length, ?After, ?SubAtom)</c>: every sub-atom in turn, narrowed
    /// by whichever of the four positional arguments are already bound.
    /// </summary>
    /// <param name="machine">The machine.</param>
    /// <param name="state">
    /// Where to resume. With SubAtom bound it is the next position to search from; otherwise it
    /// encodes the (Before, Length) pair as <c>Before * (n + 1) + Length</c>.
    /// </param>
    private static bool SubAtom(Machine machine, long state)
    {
        string text = TextArgument(machine, 0);
        long? before = Constraint(machine, 1);
        long? length = Constraint(machine, 2);
        long? after = Constraint(machine, 3);

        Cell sub = machine.Argument(4);
        return sub.Tag != CellTag.Reference && TryText(machine, sub, out string wanted)
            ? Search(machine, text, wanted, before, after, state)
            : Enumerate(machine, text, before, length, after, state);
    }

    /// <summary>SubAtom is known, so the solutions are its occurrences and nothing else is scanned.</summary>
    private static bool Search(Machine machine, string text, string wanted, long? before, long? after, long state)
    {
        int start = (int)state;

        while (start + wanted.Length <= text.Length)
        {
            int found = text.IndexOf(wanted, start, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            int tail = text.Length - found - wanted.Length;
            if ((before is null || before == found) && (after is null || after == tail))
            {
                if (found + wanted.Length < text.Length)
                {
                    machine.PushRetry(found + 1);
                }

                return machine.Unify(machine.Argument(1), Cell.Integer60(found))
                    && machine.Unify(machine.Argument(2), Cell.Integer60(wanted.Length))
                    && machine.Unify(machine.Argument(3), Cell.Integer60(tail));
            }

            start = found + 1;
        }

        return false;
    }

    /// <summary>SubAtom is unbound, so every (Before, Length) pair the bound arguments allow is offered.</summary>
    private static bool Enumerate(Machine machine, string text, long? before, long? length, long? after, long state)
    {
        long span = text.Length + 1;
        long candidate = Advance(text.Length, state, before, length, after);
        if (candidate < 0)
        {
            return false;
        }

        int start = (int)(candidate / span);
        int size = (int)(candidate % span);

        if (Advance(text.Length, candidate + 1, before, length, after) >= 0)
        {
            machine.PushRetry(candidate + 1);
        }

        return machine.Unify(machine.Argument(1), Cell.Integer60(start))
            && machine.Unify(machine.Argument(2), Cell.Integer60(size))
            && machine.Unify(machine.Argument(3), Cell.Integer60(text.Length - start - size))
            && machine.Unify(machine.Argument(4), Cell.Atom(machine.Symbols.InternAtom(text.Substring(start, size))));
    }

    /// <summary>
    /// The first candidate at or after <paramref name="from"/> that satisfies the bound arguments, or
    /// -1 when there is none. Skipping here rather than by failing keeps a constrained call from
    /// walking the engine through every rejected pair.
    /// </summary>
    private static long Advance(int textLength, long from, long? before, long? length, long? after)
    {
        long span = textLength + 1;

        for (long candidate = Math.Max(from, 0); candidate < span * span; candidate++)
        {
            long start = candidate / span;
            long size = candidate % span;

            if (start + size > textLength)
            {
                continue;
            }

            if (
                (before is null || before == start)
                && (length is null || length == size)
                && (after is null || after == textLength - start - size)
            )
            {
                return candidate;
            }
        }

        return -1;
    }

    /// <summary>An argument that must be a non-negative integer if it is bound at all.</summary>
    private static long? Constraint(Machine machine, int index)
    {
        Cell cell = machine.Argument(index);

        return cell.Tag switch
        {
            CellTag.Reference => null,
            CellTag.Integer => cell.Integer,
            _ => throw PrologErrors.Type(machine, "integer", cell),
        };
    }

    /// <summary><c>atomic_list_concat/3</c>, which joins when the list is proper and splits otherwise.</summary>
    private static bool AtomicListConcat3(Machine machine)
    {
        string separator = TextArgument(machine, 1);
        Cell list = machine.Argument(0);

        if (TermList.IsProper(machine, list))
        {
            return Join(machine, list, separator, 2);
        }

        if (separator.Length == 0)
        {
            throw PrologErrors.Domain(machine, "non_empty_atom", machine.Argument(1));
        }

        string text = TextArgument(machine, 2);
        string[] parts = text.Split(separator, StringSplitOptions.None);
        var items = new Cell[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            items[i] = Cell.Atom(machine.Symbols.InternAtom(parts[i]));
        }

        return machine.Unify(list, TermList.Build(machine, items));
    }

    private static bool Join(Machine machine, Cell list, string separator, int resultIndex)
    {
        var text = new StringBuilder();
        List<Cell> elements = TermList.ReadProper(machine, list);

        for (int i = 0; i < elements.Count; i++)
        {
            Cell element = machine.Dereference(elements[i]);

            if (element.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (i > 0)
            {
                text.Append(separator);
            }

            text.Append(TryText(machine, element, out string part) ? part : throw PrologErrors.Type(machine, "atomic", element));
        }

        return machine.Unify(machine.Argument(resultIndex), Cell.Atom(machine.Symbols.InternAtom(text.ToString())));
    }

    private static Cell SyntaxErrorTerm(Machine machine, string what)
    {
        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("syntax_error", 1),
            [Cell.Atom(machine.Symbols.InternAtom(what))]
        );

        return machine.CreateStructure(machine.Symbols.InternFunctor("error", 2), [formal, machine.CreateVariable()]);
    }

    /// <summary>
    /// Parses Prolog number syntax: an optional sign, then a decimal, <c>0x</c>/<c>0o</c>/<c>0b</c>,
    /// or <c>0'c</c> integer, or a float with a fraction and an optional exponent.
    /// </summary>
    /// <remarks>
    /// This is a second, smaller number reader than the one in the syntax layer, because the runtime
    /// deliberately does not depend on it. The two must agree on what a number looks like; the tests
    /// for <c>atom_number/2</c> are what hold them together.
    /// </remarks>
    internal static bool TryParseNumber(string text, out PrologNumber number)
    {
        number = default;
        ReadOnlySpan<char> span = text.AsSpan().Trim();

        if (span.Length == 0)
        {
            return false;
        }

        bool negative = span[0] == '-';
        if (span[0] is '-' or '+')
        {
            span = span[1..];
        }

        if (span.Length == 0)
        {
            return false;
        }

        if (TryParseRadix(span, out long radixValue))
        {
            number = PrologNumber.FromInteger(negative ? -radixValue : radixValue);
            return true;
        }

        foreach (char c in span)
        {
            if (!char.IsAsciiDigit(c) && c != '.' && c != 'e' && c != 'E' && c != '+' && c != '-')
            {
                return false;
            }
        }

        bool real = span.Contains('.');

        if (!real)
        {
            if (!long.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out long integer))
            {
                return false;
            }

            number = PrologNumber.FromInteger(negative ? -integer : integer);
            return true;
        }

        // A Prolog float needs digits on both sides of the point, so ".5" and "1." are not numbers.
        int point = span.IndexOf('.');
        if (point == 0 || (point > 0 && (point == span.Length - 1 || !char.IsAsciiDigit(span[point + 1]))))
        {
            return false;
        }

        if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return false;
        }

        number = PrologNumber.FromReal(negative ? -value : value);
        return true;
    }

    private static bool TryParseRadix(ReadOnlySpan<char> span, out long value)
    {
        value = 0;

        if (span.Length < 3 || span[0] != '0')
        {
            return false;
        }

        // 0'c is the code of the character that follows, which is how Prolog spells a character literal.
        if (span[1] == '\'')
        {
            if (span.Length != 3)
            {
                return false;
            }

            value = span[2];
            return true;
        }

        int radix = char.ToLowerInvariant(span[1]) switch
        {
            'x' => 16,
            'o' => 8,
            'b' => 2,
            _ => 0,
        };

        if (radix == 0)
        {
            return false;
        }

        foreach (char c in span[2..])
        {
            int digit =
                char.IsAsciiDigit(c) ? c - '0'
                : char.IsAsciiLetter(c) ? char.ToLowerInvariant(c) - 'a' + 10
                : -1;

            if (digit < 0 || digit >= radix)
            {
                return false;
            }

            value = (value * radix) + digit;
        }

        return true;
    }

    /// <summary>
    /// Reads a list of codes or characters as text, accepting either. Which one it is is decided by
    /// the first element, so <c>format/2</c>'s <c>~s</c> takes both spellings.
    /// </summary>
    internal static string TextOfList(Machine machine, Cell list)
    {
        List<Cell> elements = TermList.ReadProper(machine, list);
        bool chars = elements.Count > 0 && machine.Dereference(elements[0]).Tag == CellTag.Atom;
        return ReadText(machine, elements, chars);
    }
}
