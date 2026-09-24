namespace DotProlog.Runtime;

/// <summary>
/// The string library over the interned string term. Which kinds of term each predicate reads as
/// text follows SWI-Prolog predicate by predicate, through <see cref="PrologText"/>: most accept
/// atoms, strings, numbers, and character or code lists, while <c>string_concat/3</c>,
/// <c>sub_string/5</c>, and the case mappings reject lists as SWI's do. Results are strings.
/// </summary>
internal static class StringBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("string_length", 2, StringLength);
        registry.Register("atom_string", 2, static machine => AtomString(machine, atom: 0, text: 1));
        registry.Register("string_to_atom", 2, static machine => AtomString(machine, atom: 1, text: 0));
        registry.Register("string_chars", 2, static machine => StringList(machine, chars: true));
        registry.Register("string_codes", 2, static machine => StringList(machine, chars: false));
        registry.Register("number_string", 2, NumberString);
        registry.Register("split_string", 4, SplitString);
        registry.Register("text_to_string", 2, TextToString);
        registry.Register("string_lower", 2, static machine => MapCase(machine, static text => text.ToLowerInvariant()));
        registry.Register("string_upper", 2, static machine => MapCase(machine, static text => text.ToUpperInvariant()));
        registry.Register("$as_string", 2, AsString);
        registry.Register("$substring_length", 2, SubstringLength);
        registry.Register("$string_code_length", 2, StringCodeLength);
        registry.Register("$string_concat", 3, StringConcat);
        registry.Register("$string_slice", 4, StringSlice);
        registry.Register("$string_code", 3, StringCode);
        registry.Register("$term_source_text", 3, TermSourceText);
        registry.Register(
            "$character_code",
            1,
            static machine => machine.Argument(0) is { Tag: CellTag.Integer } code && PrologText.IsCode(machine, code.Integer)
        );
    }

    /// <summary>
    /// <c>'$term_source_text'(+Source, +Mode, -Atom)</c>: the text a term is parsed from, as an atom
    /// for the reader. Mode <c>any</c> accepts every kind of text, as <c>term_to_atom/2</c>,
    /// <c>term_string/2</c>, and <c>atom_to_term/3</c> do; mode <c>text</c> accepts atoms, strings,
    /// and lists but not numbers, as <c>read_term_from_atom/3</c> and <c>term_string/3</c> do.
    /// </summary>
    /// <remarks>
    /// <c>[]</c> is the atom <c>'[]'</c> here, so it parses as the empty list. Elsewhere a list-accepting
    /// reader takes it for empty text, but empty text is no term, and the atom's own text is the only
    /// reading a parser can use.
    /// </remarks>
    private static bool TermSourceText(Machine machine)
    {
        Cell source = machine.Argument(0);
        Cell mode = machine.Argument(1);
        var kinds =
            mode.Tag == CellTag.Atom && machine.Symbols.AtomName(mode.Index) == "any"
                ? TextKinds.Any
                : TextKinds.Atom | TextKinds.String | TextKinds.List;
        var text =
            source.Tag == CellTag.Atom && source.Index == machine.Symbols.EmptyList
                ? machine.Symbols.AtomName(source.Index)
                : PrologText.Read(machine, source, kinds);
        return machine.Unify(machine.Argument(2), Cell.Atom(machine.Symbols.InternAtom(text)));
    }

    private static Cell StringCell(Machine machine, string text) => Cell.String(machine.Symbols.InternAtom(text));

    private static bool StringLength(Machine machine)
    {
        var text = PrologText.Read(machine, machine.Argument(0), TextKinds.Any);
        return machine.Unify(machine.Argument(1), Cell.Integer60(PrologText.Characters(machine, text).Length));
    }

    /// <summary>
    /// <c>atom_string(?Atom, ?String)</c> and, with the arguments swapped,
    /// <c>string_to_atom(?String, ?Atom)</c>. Either side may be any text; the string side is
    /// checked first, and a bound pair is compared by content.
    /// </summary>
    private static bool AtomString(Machine machine, int atom, int text)
    {
        Cell atomCell = machine.Argument(atom);
        Cell textCell = machine.Argument(text);
        string? textValue = null;
        string? atomValue = null;

        if (textCell.Tag != CellTag.Reference)
        {
            textValue = PrologText.TryRead(machine, textCell, TextKinds.Any, out var value)
                ? value
                : throw PrologErrors.Type(machine, "string", textCell);
        }

        if (atomCell.Tag != CellTag.Reference)
        {
            atomValue = PrologText.TryRead(machine, atomCell, TextKinds.Any, out var value)
                ? value
                : throw PrologErrors.Type(machine, "atom", atomCell);
        }

        if (atomValue is not null)
        {
            return textValue is null
                ? machine.Unify(textCell, StringCell(machine, atomValue))
                : string.Equals(atomValue, textValue, StringComparison.Ordinal);
        }

        return textValue is not null
            ? machine.Unify(atomCell, Cell.Atom(machine.Symbols.InternAtom(textValue)))
            : throw PrologErrors.Instantiation(machine);
    }

    private static bool StringList(Machine machine, bool chars)
    {
        Cell source = machine.Argument(0);
        if (source.Tag != CellTag.Reference)
        {
            var text = PrologText.Read(machine, source, TextKinds.Any);
            return PrologText.UnifyOrCompare(
                machine,
                machine.Argument(1),
                TextBuiltins.BuildText(machine, text, chars),
                text,
                TextKinds.Any
            );
        }

        var listText = PrologText.Read(machine, machine.Argument(1), TextKinds.String | TextKinds.List);
        return machine.Unify(source, StringCell(machine, listText));
    }

    private static bool NumberString(Machine machine)
    {
        Cell text = machine.Argument(1);
        if (text.Tag != CellTag.Reference)
        {
            var value = PrologText.Read(machine, text, TextKinds.String | TextKinds.List);

            // SWI fails, rather than trimming, when the number is surrounded by layout.
            if (value.Length == 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
            {
                return false;
            }

            return TextBuiltins.TryParseNumber(machine, value, out PrologNumber number)
                && machine.Unify(machine.Argument(0), ArithmeticEvaluator.ToCell(machine, number));
        }

        Cell number2 = machine.Argument(0);
        if (number2.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (number2.Tag is not (CellTag.Integer or CellTag.BigInteger or CellTag.Float or CellTag.Rational))
        {
            throw PrologErrors.Type(machine, "number", number2);
        }

        return machine.Unify(text, StringCell(machine, PrologText.Read(machine, number2, TextKinds.Number)));
    }

    /// <summary>
    /// <c>split_string(+String, +SepChars, +PadChars, -SubStrings)</c>, matching SWI as probed:
    /// pad characters are stripped from both ends of the whole string first, then each separator
    /// splits, with pad characters stripped around it — which is what makes runs of separators
    /// that are also pad characters act as one.
    /// </summary>
    private static bool SplitString(Machine machine)
    {
        const TextKinds kinds = TextKinds.Atom | TextKinds.String | TextKinds.List;
        CodePointText text = PrologText.Characters(machine, PrologText.Read(machine, machine.Argument(0), kinds));
        HashSet<int> separators = CodeSet(machine, PrologText.Read(machine, machine.Argument(1), kinds));
        HashSet<int> pad = CodeSet(machine, PrologText.Read(machine, machine.Argument(2), kinds));

        var start = 0;
        var end = text.Length;
        while (start < end && pad.Contains(text.CodeAt(start)))
        {
            start++;
        }

        while (end > start && pad.Contains(text.CodeAt(end - 1)))
        {
            end--;
        }

        List<Cell> fields = [];
        var fieldStart = start;
        for (var i = start; i <= end; i++)
        {
            if (i < end && !separators.Contains(text.CodeAt(i)))
            {
                continue;
            }

            var fieldEnd = i;
            while (fieldEnd > fieldStart && pad.Contains(text.CodeAt(fieldEnd - 1)))
            {
                fieldEnd--;
            }

            fields.Add(StringCell(machine, text.Slice(fieldStart, fieldEnd - fieldStart)));

            fieldStart = i + 1;
            while (fieldStart < end && pad.Contains(text.CodeAt(fieldStart)))
            {
                fieldStart++;
            }

            i = fieldStart - 1;
        }

        return machine.Unify(machine.Argument(3), machine.CreateList([.. fields], Cell.Atom(machine.Symbols.EmptyList)));
    }

    /// <summary>The codes of the characters of <paramref name="text"/>, as a set.</summary>
    private static HashSet<int> CodeSet(Machine machine, string text)
    {
        CodePointText characters = PrologText.Characters(machine, text);
        HashSet<int> codes = [];
        for (var i = 0; i < characters.Length; i++)
        {
            codes.Add(characters.CodeAt(i));
        }

        return codes;
    }

    /// <summary><c>text_to_string(+Text, ?String)</c>: there is no reverse mode, as in SWI.</summary>
    private static bool TextToString(Machine machine)
    {
        var text = PrologText.Read(machine, machine.Argument(0), TextKinds.Atom | TextKinds.String | TextKinds.List);
        return PrologText.UnifyOrCompare(machine, machine.Argument(1), StringCell(machine, text), text, TextKinds.Any);
    }

    /// <summary>
    /// <c>string_lower/2</c> and <c>string_upper/2</c>. A bound result is compared after the mapping,
    /// so <c>string_upper(abc, 'ABC')</c> succeeds, and a result that is not atomic is a type error.
    /// </summary>
    private static bool MapCase(Machine machine, Func<string, string> map)
    {
        var mapped = map(PrologText.Read(machine, machine.Argument(0), TextKinds.Atomic));
        Cell result = machine.Argument(1);
        if (result.Tag == CellTag.Reference)
        {
            return machine.Unify(result, StringCell(machine, mapped));
        }

        return PrologText.TryRead(machine, result, TextKinds.Atomic, out var given)
            ? string.Equals(given, mapped, StringComparison.Ordinal)
            : throw PrologErrors.Type(machine, "string", result);
    }

    /// <summary>
    /// The text of a bound <c>sub_string/5</c> or <c>string_concat/3</c> argument, as a string for the
    /// library's content comparison. As in SWI, only atomic text is accepted there.
    /// </summary>
    private static bool AsString(Machine machine) =>
        machine.Unify(machine.Argument(1), StringCell(machine, SubstringText(machine, machine.Argument(0))));

    /// <summary>The length of the string <c>sub_string/5</c> takes apart.</summary>
    private static bool SubstringLength(Machine machine) =>
        machine.Unify(
            machine.Argument(1),
            Cell.Integer60(PrologText.Characters(machine, SubstringText(machine, machine.Argument(0))).Length)
        );

    private static string SubstringText(Machine machine, Cell cell)
    {
        if (cell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return PrologText.TryRead(machine, cell, TextKinds.Atomic, out var text)
            ? text
            : throw PrologErrors.Type(machine, "string", cell);
    }

    /// <summary>The length of the text <c>string_code/3</c> indexes, which may be a list but not a number.</summary>
    private static bool StringCodeLength(Machine machine)
    {
        var text = PrologText.Read(machine, machine.Argument(0), TextKinds.Atom | TextKinds.String | TextKinds.List);
        return machine.Unify(machine.Argument(1), Cell.Integer60(PrologText.Characters(machine, text).Length));
    }

    private static bool StringConcat(Machine machine)
    {
        var left = PrologText.Read(machine, machine.Argument(0), TextKinds.Atomic);
        var right = PrologText.Read(machine, machine.Argument(1), TextKinds.Atomic);
        return machine.Unify(machine.Argument(2), StringCell(machine, left + right));
    }

    private static bool StringSlice(Machine machine)
    {
        CodePointText text = PrologText.Characters(machine, PrologText.Read(machine, machine.Argument(0), TextKinds.Atomic));
        Cell before = machine.Argument(1);
        Cell length = machine.Argument(2);
        if (
            before.Tag is not (CellTag.Integer or CellTag.BigInteger)
            || length.Tag is not (CellTag.Integer or CellTag.BigInteger)
        )
        {
            throw PrologErrors.Type(
                machine,
                "integer",
                before.Tag is not (CellTag.Integer or CellTag.BigInteger) ? before : length
            );
        }

        if (before.Tag == CellTag.BigInteger || length.Tag == CellTag.BigInteger)
        {
            return false;
        }

        var start = (int)before.Integer;
        var count = (int)length.Integer;
        if (start < 0 || count < 0 || start + count > text.Length)
        {
            return false;
        }

        return machine.Unify(machine.Argument(3), StringCell(machine, text.Slice(start, count)));
    }

    /// <summary>
    /// <c>string_code(+Index, +Text, -Code)</c> for a bound index: SWI's index errors, then failure
    /// for a position outside the text.
    /// </summary>
    private static bool StringCode(Machine machine)
    {
        Cell index = machine.Argument(0);
        if (index.Tag is not (CellTag.Integer or CellTag.BigInteger))
        {
            throw PrologErrors.Type(machine, "integer", index);
        }

        var negative = index.Tag == CellTag.BigInteger ? machine.Symbols.GetBig(index.Index).Sign < 0 : index.Integer < 0;
        if (negative)
        {
            throw PrologErrors.Domain(machine, "not_less_than_zero", index);
        }

        if (index.Tag == CellTag.BigInteger)
        {
            return false;
        }

        CodePointText text = PrologText.Characters(
            machine,
            PrologText.Read(machine, machine.Argument(1), TextKinds.Atom | TextKinds.String | TextKinds.List)
        );
        var position = (int)index.Integer;
        if (position < 1 || position > text.Length)
        {
            return false;
        }

        return machine.Unify(machine.Argument(2), Cell.Integer60(text.CodeAt(position - 1)));
    }
}
