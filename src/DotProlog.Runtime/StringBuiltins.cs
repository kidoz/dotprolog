namespace DotProlog.Runtime;

/// <summary>
/// The string library over the interned string term (ADR 0047). Inputs are text-lenient the way
/// SWI's are — an atom or a number is accepted wherever a string is — while results are strings.
/// The atom predicates stay strict about atoms; that phase-3 loosening was deliberately deferred.
/// </summary>
internal static class StringBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("string_length", 2, StringLength);
        registry.Register("atom_string", 2, AtomString);
        registry.Register("string_to_atom", 2, StringToAtom);
        registry.Register("string_chars", 2, static machine => StringList(machine, chars: true));
        registry.Register("string_codes", 2, static machine => StringList(machine, chars: false));
        registry.Register("number_string", 2, NumberString);
        registry.Register("split_string", 4, SplitString);
        registry.Register("string_lower", 2, static machine => MapCase(machine, static text => text.ToLowerInvariant()));
        registry.Register("string_upper", 2, static machine => MapCase(machine, static text => text.ToUpperInvariant()));
        registry.Register("$as_string", 2, AsString);
        registry.Register("$string_concat", 3, StringConcat);
        registry.Register("$string_slice", 4, StringSlice);
        registry.Register("$string_code", 3, StringCode);
    }

    /// <summary>Any text an SWI string predicate accepts: a string, an atom, or a number.</summary>
    private static bool TryAnyText(Machine machine, Cell cell, out string text)
    {
        if (cell.Tag == CellTag.String)
        {
            text = machine.Symbols.AtomName(cell.Index);
            return true;
        }

        return TextBuiltins.TryText(machine, cell, out text);
    }

    private static string RequireText(Machine machine, int argument)
    {
        Cell cell = machine.Argument(argument);
        if (cell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return TryAnyText(machine, cell, out var text) ? text : throw PrologErrors.Type(machine, "string", cell);
    }

    private static Cell StringCell(Machine machine, string text) => Cell.String(machine.Symbols.InternAtom(text));

    private static bool StringLength(Machine machine)
    {
        var text = RequireText(machine, 0);
        return machine.Unify(machine.Argument(1), Cell.Integer60(text.Length));
    }

    private static bool AtomString(Machine machine)
    {
        Cell atom = machine.Argument(0);
        if (atom.Tag != CellTag.Reference)
        {
            return TryAnyText(machine, atom, out var text)
                ? machine.Unify(machine.Argument(1), StringCell(machine, text))
                : throw PrologErrors.Type(machine, "atom", atom);
        }

        var target = RequireText(machine, 1);
        return machine.Unify(atom, Cell.Atom(machine.Symbols.InternAtom(target)));
    }

    private static bool StringToAtom(Machine machine)
    {
        Cell text = machine.Argument(0);
        if (text.Tag != CellTag.Reference)
        {
            return TryAnyText(machine, text, out var value)
                ? machine.Unify(machine.Argument(1), Cell.Atom(machine.Symbols.InternAtom(value)))
                : throw PrologErrors.Type(machine, "string", text);
        }

        var atom = RequireText(machine, 1);
        return machine.Unify(text, StringCell(machine, atom));
    }

    private static bool StringList(Machine machine, bool chars)
    {
        Cell text = machine.Argument(0);
        if (text.Tag != CellTag.Reference)
        {
            if (!TryAnyText(machine, text, out var value))
            {
                throw PrologErrors.Type(machine, "string", text);
            }

            return machine.Unify(machine.Argument(1), TextBuiltins.BuildText(machine, value, chars));
        }

        var listText = TextBuiltins.TextOfList(machine, machine.Argument(1));
        return machine.Unify(text, StringCell(machine, listText));
    }

    private static bool NumberString(Machine machine)
    {
        Cell text = machine.Argument(1);
        if (text.Tag != CellTag.Reference)
        {
            if (!TryAnyText(machine, text, out var value))
            {
                throw PrologErrors.Type(machine, "string", text);
            }

            // SWI tolerates surrounding whitespace and fails quietly on text that is not a number.
            return TextBuiltins.TryParseNumber(machine, value.Trim(), out PrologNumber number)
                && machine.Unify(machine.Argument(0), ArithmeticEvaluator.ToCell(machine, number));
        }

        Cell value2 = machine.Argument(0);
        if (value2.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (value2.Tag is not (CellTag.Integer or CellTag.Float))
        {
            throw PrologErrors.Type(machine, "number", value2);
        }

        TextBuiltins.TryText(machine, value2, out var written);
        return machine.Unify(text, StringCell(machine, written));
    }

    /// <summary>
    /// <c>split_string(+String, +SepChars, +PadChars, -SubStrings)</c>, matching SWI as probed:
    /// pad characters are stripped from both ends of the whole string first, then each separator
    /// splits, with pad characters stripped around it — which is what makes runs of separators
    /// that are also pad characters act as one.
    /// </summary>
    private static bool SplitString(Machine machine)
    {
        var text = RequireText(machine, 0);
        var separators = RequireText(machine, 1);
        var pad = RequireText(machine, 2);

        var start = 0;
        var end = text.Length;
        while (start < end && pad.Contains(text[start]))
        {
            start++;
        }

        while (end > start && pad.Contains(text[end - 1]))
        {
            end--;
        }

        List<Cell> fields = [];
        var fieldStart = start;
        for (var i = start; i <= end; i++)
        {
            if (i < end && !separators.Contains(text[i]))
            {
                continue;
            }

            var fieldEnd = i;
            while (fieldEnd > fieldStart && pad.Contains(text[fieldEnd - 1]))
            {
                fieldEnd--;
            }

            fields.Add(StringCell(machine, text[fieldStart..fieldEnd]));

            fieldStart = i + 1;
            while (fieldStart < end && pad.Contains(text[fieldStart]))
            {
                fieldStart++;
            }

            i = fieldStart - 1;
        }

        return machine.Unify(machine.Argument(3), machine.CreateList([.. fields], Cell.Atom(machine.Symbols.EmptyList)));
    }

    private static bool MapCase(Machine machine, Func<string, string> map)
    {
        var text = RequireText(machine, 0);
        return machine.Unify(machine.Argument(1), StringCell(machine, map(text)));
    }

    /// <summary>Converts any text to its string, for content comparison in the library's Prolog half.</summary>
    private static bool AsString(Machine machine)
    {
        var text = RequireText(machine, 0);
        return machine.Unify(machine.Argument(1), StringCell(machine, text));
    }

    private static bool StringConcat(Machine machine)
    {
        var left = RequireText(machine, 0);
        var right = RequireText(machine, 1);
        return machine.Unify(machine.Argument(2), StringCell(machine, left + right));
    }

    private static bool StringSlice(Machine machine)
    {
        var text = RequireText(machine, 0);
        Cell before = machine.Argument(1);
        Cell length = machine.Argument(2);
        if (before.Tag != CellTag.Integer || length.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", before.Tag != CellTag.Integer ? before : length);
        }

        var start = (int)before.Integer;
        var count = (int)length.Integer;
        if (start < 0 || count < 0 || start + count > text.Length)
        {
            return false;
        }

        return machine.Unify(machine.Argument(3), StringCell(machine, text.Substring(start, count)));
    }

    private static bool StringCode(Machine machine)
    {
        Cell index = machine.Argument(0);
        if (index.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", index);
        }

        var text = RequireText(machine, 1);
        var position = (int)index.Integer;
        if (position < 1 || position > text.Length)
        {
            return false;
        }

        return machine.Unify(machine.Argument(2), Cell.Integer60(text[position - 1]));
    }
}
