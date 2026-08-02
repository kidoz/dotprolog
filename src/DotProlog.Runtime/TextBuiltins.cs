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

        return TryText(machine, cell, out var text) ? text : throw PrologErrors.Type(machine, "atomic", cell);
    }

    private static string AtomArgument(Machine machine, int index)
    {
        Cell cell = machine.Argument(index);

        if (cell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return cell.Tag == CellTag.Atom ? machine.Symbols.AtomName(cell.Index) : throw PrologErrors.Type(machine, "atom", cell);
    }

    private static bool AtomLength(Machine machine)
    {
        var actual = AtomArgument(machine, 0).Length;
        Cell length = machine.Argument(1);

        if (length.Tag == CellTag.Reference)
        {
            return machine.Unify(length, Cell.Integer60(actual));
        }

        if (length.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", length);
        }

        if (length.Integer < 0)
        {
            throw PrologErrors.Domain(machine, "not_less_than_zero", length);
        }

        return length.Integer == actual;
    }

    private static bool CharCode(Machine machine)
    {
        Cell character = machine.Argument(0);
        Cell code = machine.Argument(1);

        if (character.Tag == CellTag.Atom)
        {
            var name = machine.Symbols.AtomName(character.Index);
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

        var text = ((char)code.Integer).ToString();
        return machine.Unify(character, Cell.Atom(machine.Symbols.InternAtom(text)));
    }

    private static bool ChangeCase(Machine machine, bool upper)
    {
        var text = TextArgument(machine, 0);
        var changed = upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
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
            var text = machine.Symbols.AtomName(atom.Index);
            return TryParseNumber(machine, text, out PrologNumber number)
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

        return TryText(machine, value, out var written)
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
        Cell source = machine.Argument(0);

        if (source.Tag != CellTag.Reference)
        {
            if (numeric && source.Tag is not (CellTag.Integer or CellTag.Float))
            {
                throw PrologErrors.Type(machine, "number", source);
            }

            if (!numeric && source.Tag != CellTag.Atom)
            {
                throw PrologErrors.Type(machine, "atom", source);
            }

            // ISO 8.16.4-8.16.8: a bound first argument decides the direction. It is converted and
            // the result unified with the list, whatever the list holds — a list of unbound
            // elements is filled in, and a list of the wrong length fails.
            var written =
                source.Tag == CellTag.Atom ? machine.Symbols.AtomName(source.Index)
                : TryText(machine, source, out var value) ? value
                : throw new InvalidOperationException("Validated text source has no textual representation.");
            return machine.Unify(list, BuildText(machine, written, chars));
        }

        if (!TermList.IsProper(machine, list))
        {
            List<Cell> elements = [];
            Cell tail = TermList.Read(machine, list, elements);
            throw tail.Tag == CellTag.Reference ? PrologErrors.Instantiation(machine) : PrologErrors.Type(machine, "list", list);
        }

        var text = ReadText(machine, list, chars);

        if (!numeric)
        {
            return machine.Unify(source, Cell.Atom(machine.Symbols.InternAtom(text)));
        }

        return TryParseNumber(machine, text, out PrologNumber parsed)
            ? machine.Unify(source, ArithmeticEvaluator.ToCell(machine, parsed))
            : throw machine.CreateBall(SyntaxErrorTerm(machine, "illegal_number"), "syntax_error(illegal_number)");
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
                var name =
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

        for (var i = 0; i < text.Length; i++)
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
        Cell whole = machine.Argument(2);

        ValidateAtomOrVariable(machine, first);
        ValidateAtomOrVariable(machine, second);
        ValidateAtomOrVariable(machine, whole);

        if (first.Tag == CellTag.Reference && whole.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (second.Tag == CellTag.Reference && whole.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (first.Tag == CellTag.Atom && second.Tag == CellTag.Atom)
        {
            var left = machine.Symbols.AtomName(first.Index);
            var right = machine.Symbols.AtomName(second.Index);
            return machine.Unify(whole, Cell.Atom(machine.Symbols.InternAtom(left + right)));
        }

        var text = machine.Symbols.AtomName(whole.Index);

        var split = (int)state;
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

    private static void ValidateAtomOrVariable(Machine machine, Cell cell)
    {
        if (cell.Tag is not (CellTag.Reference or CellTag.Atom))
        {
            throw PrologErrors.Type(machine, "atom", cell);
        }
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
        var text = AtomArgument(machine, 0);
        var before = Constraint(machine, 1);
        var length = Constraint(machine, 2);
        var after = Constraint(machine, 3);

        Cell sub = machine.Argument(4);
        if (sub.Tag == CellTag.Reference)
        {
            return Enumerate(machine, text, before, length, after, state);
        }

        if (sub.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", sub);
        }

        return Search(machine, text, machine.Symbols.AtomName(sub.Index), before, after, state);
    }

    /// <summary>SubAtom is known, so the solutions are its occurrences and nothing else is scanned.</summary>
    private static bool Search(Machine machine, string text, string wanted, long? before, long? after, long state)
    {
        var start = (int)state;

        while (start + wanted.Length <= text.Length)
        {
            var found = text.IndexOf(wanted, start, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            var tail = text.Length - found - wanted.Length;
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
        var candidate = Advance(text.Length, state, before, length, after);
        if (candidate < 0)
        {
            return false;
        }

        var start = (int)(candidate / span);
        var size = (int)(candidate % span);

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

        for (var candidate = Math.Max(from, 0); candidate < span * span; candidate++)
        {
            var start = candidate / span;
            var size = candidate % span;

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
            CellTag.Integer when cell.Integer >= 0 => cell.Integer,
            CellTag.Integer => throw PrologErrors.Domain(machine, "not_less_than_zero", cell),
            _ => throw PrologErrors.Type(machine, "integer", cell),
        };
    }

    /// <summary><c>atomic_list_concat/3</c>, which joins when the list is proper and splits otherwise.</summary>
    private static bool AtomicListConcat3(Machine machine)
    {
        var separator = TextArgument(machine, 1);
        Cell list = machine.Argument(0);

        if (TermList.IsProper(machine, list))
        {
            return Join(machine, list, separator, 2);
        }

        if (separator.Length == 0)
        {
            throw PrologErrors.Domain(machine, "non_empty_atom", machine.Argument(1));
        }

        var text = TextArgument(machine, 2);
        var parts = text.Split(separator, StringSplitOptions.None);
        var items = new Cell[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            items[i] = Cell.Atom(machine.Symbols.InternAtom(parts[i]));
        }

        return machine.Unify(list, TermList.Build(machine, items));
    }

    private static bool Join(Machine machine, Cell list, string separator, int resultIndex)
    {
        var text = new StringBuilder();
        List<Cell> elements = TermList.ReadProper(machine, list);

        for (var i = 0; i < elements.Count; i++)
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

            text.Append(TryText(machine, element, out var part) ? part : throw PrologErrors.Type(machine, "atomic", element));
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
    /// for <c>atom_number/2</c> are what hold them together. Text that spells a number the term
    /// representation cannot hold raises the reader's error rather than returning false —
    /// <c>representation_error(max_integer|min_integer)</c> for an oversized integer literal and
    /// <c>syntax_error(float_overflow)</c> for a float outside binary64.
    /// </remarks>
    internal static bool TryParseNumber(Machine machine, string text, out PrologNumber number)
    {
        number = default;
        ReadOnlySpan<char> span = text.AsSpan().TrimStart();

        if (span.Length == 0)
        {
            return false;
        }

        var negative = span[0] == '-';
        if (span[0] is '-' or '+')
        {
            span = span[1..];
        }

        if (span.Length == 0)
        {
            return false;
        }

        if (TryParseRadix(span, out var radixValue, out var radixOverflow))
        {
            if (radixOverflow || !Cell.FitsInteger(negative ? -radixValue : radixValue))
            {
                throw PrologErrors.Representation(machine, negative ? "min_integer" : "max_integer");
            }

            number = PrologNumber.FromInteger(negative ? -radixValue : radixValue);
            return true;
        }

        foreach (var c in span)
        {
            if (!char.IsAsciiDigit(c) && c != '.' && c != 'e' && c != 'E' && c != '+' && c != '-')
            {
                return false;
            }
        }

        var real = span.Contains('.');

        if (!real)
        {
            foreach (var c in span)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }

            if (
                !long.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out var integer)
                || !Cell.FitsInteger(negative ? -integer : integer)
            )
            {
                throw PrologErrors.Representation(machine, negative ? "min_integer" : "max_integer");
            }

            number = PrologNumber.FromInteger(negative ? -integer : integer);
            return true;
        }

        // A Prolog float needs digits on both sides of the point, so ".5" and "1." are not numbers.
        var point = span.IndexOf('.');
        if (point == 0 || (point > 0 && (point == span.Length - 1 || !char.IsAsciiDigit(span[point + 1]))))
        {
            return false;
        }

        if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (double.IsInfinity(value))
        {
            // double.TryParse rounds an oversized literal to infinity; runtime-read floats stay
            // finite, so this raises the same error as the reader path.
            throw machine.CreateBall(SyntaxErrorTerm(machine, "float_overflow"), "syntax_error(float_overflow)");
        }

        number = PrologNumber.FromReal(negative ? -value : value);
        return true;
    }

    private static bool TryParseRadix(ReadOnlySpan<char> span, out long value, out bool overflow)
    {
        value = 0;
        overflow = false;

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

        var radix = char.ToLowerInvariant(span[1]) switch
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

        foreach (var c in span[2..])
        {
            var digit =
                char.IsAsciiDigit(c) ? c - '0'
                : char.IsAsciiLetter(c) ? char.ToLowerInvariant(c) - 'a' + 10
                : -1;

            if (digit < 0 || digit >= radix)
            {
                return false;
            }

            if (value > (long.MaxValue - digit) / radix)
            {
                overflow = true;
                continue;
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
        var chars = elements.Count > 0 && machine.Dereference(elements[0]).Tag == CellTag.Atom;
        return ReadText(machine, elements, chars);
    }
}
