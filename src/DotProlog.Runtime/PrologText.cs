using System.Text;

namespace DotProlog.Runtime;

/// <summary>
/// Reads a term as text for the text-accepting extension predicates, with SWI-Prolog's rules for
/// which kinds of term count as text and which error a rejected term raises. Each predicate names
/// the kinds it accepts; the rules live only here.
/// </summary>
/// <remarks>
/// <para>
/// Where lists are accepted, <c>[]</c> is empty text, and the first element decides whether the
/// list is a code list (an integer) or a character list (anything else). A variable anywhere in an
/// accepted list is an instantiation error; a bad first element is
/// <c>type_error(character_code, E)</c>, and a later one <c>type_error(character, E)</c> in a
/// character list or <c>type_error(character_code, E)</c> in a code list.
/// </para>
/// <para>
/// What a character is depends on the program's mode: a Unicode code point, or — in strict ISO
/// mode — a UTF-16 code unit. <see cref="IsCode"/>, <see cref="IsCharacter"/>,
/// and <see cref="AppendCode"/> are where that model is decided.
/// </para>
/// </remarks>
internal static class PrologText
{
    /// <summary>Reads <paramref name="cell"/> as text, raising SWI's error when it is not text of an accepted kind.</summary>
    internal static string Read(Machine machine, Cell cell, TextKinds kinds)
    {
        TryRead(machine, cell, kinds, raise: true, out var text);
        return text;
    }

    /// <summary>
    /// Reads <paramref name="cell"/> as text without raising: <see langword="false"/> when it is
    /// unbound, partial, or not text of an accepted kind, leaving the error to the predicate.
    /// </summary>
    internal static bool TryRead(Machine machine, Cell cell, TextKinds kinds, out string text) =>
        TryRead(machine, cell, kinds, raise: false, out text);

    /// <summary>
    /// Unifies <paramref name="target"/> with <paramref name="result"/>, except that a target
    /// already bound to text of <paramref name="compared"/> kinds is compared by content, as SWI
    /// does for bound outputs: <c>atom_string(abc, [a,b,c])</c> succeeds.
    /// </summary>
    internal static bool UnifyOrCompare(Machine machine, Cell target, Cell result, string resultText, TextKinds compared)
    {
        target = machine.Dereference(target);
        if (target.Tag != CellTag.Reference && TryRead(machine, target, compared, out var existing))
        {
            return string.Equals(existing, resultText, StringComparison.Ordinal);
        }

        return machine.Unify(target, result);
    }

    /// <summary>
    /// Whether <paramref name="code"/> is a character code: a Unicode scalar value, or in strict ISO
    /// mode a UTF-16 code unit.
    /// </summary>
    internal static bool IsCode(Machine machine, long code) =>
        machine.Symbols.CodePoints
            ? code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF)
            : code is >= 0 and <= char.MaxValue;

    /// <summary>Whether <paramref name="name"/> is the name of a one-character atom.</summary>
    internal static bool IsCharacter(Machine machine, string name) =>
        name.Length == 1
            ? !(machine.Symbols.CodePoints && char.IsSurrogate(name[0]))
            : name.Length == 2 && machine.Symbols.CodePoints && CodePointText.IsPairAt(name, 0);

    /// <summary>The code of a one-character atom's name.</summary>
    internal static int CodeOf(string character) =>
        character.Length == 2 ? char.ConvertToUtf32(character[0], character[1]) : character[0];

    /// <summary>The name of the one-character atom whose code is <paramref name="code"/>.</summary>
    internal static string CharacterOf(long code) =>
        code <= char.MaxValue ? ((char)code).ToString() : char.ConvertFromUtf32((int)code);

    /// <summary>Appends the character whose code is <paramref name="code"/>.</summary>
    internal static void AppendCode(StringBuilder text, long code)
    {
        if (code <= char.MaxValue)
        {
            text.Append((char)code);
        }
        else
        {
            text.Append(char.ConvertFromUtf32((int)code));
        }
    }

    /// <summary>The text indexed by character, under the program's model.</summary>
    internal static CodePointText Characters(Machine machine, string text) => CodePointText.Of(text, machine.Symbols.CodePoints);

    private static bool TryRead(Machine machine, Cell cell, TextKinds kinds, bool raise, out string text)
    {
        cell = machine.Dereference(cell);
        text = string.Empty;

        switch (cell.Tag)
        {
            case CellTag.Reference:
                return raise ? throw PrologErrors.Instantiation(machine) : false;

            case CellTag.Atom when (kinds & TextKinds.List) != 0 && cell.Index == machine.Symbols.EmptyList:
                return true;

            case CellTag.Atom when (kinds & TextKinds.Atom) != 0:
            case CellTag.String when (kinds & TextKinds.String) != 0:
                text = machine.Symbols.AtomName(cell.Index);
                return true;

            case CellTag.Integer
            or CellTag.BigInteger
            or CellTag.Float
            or CellTag.Rational when (kinds & TextKinds.Number) != 0:
                text = TermWriter.ToDisplayString(machine, cell);
                return true;

            case CellTag.Structure when (kinds & TextKinds.List) != 0 && IsListCell(machine, cell):
                return TryReadList(machine, cell, kinds, raise, out text);

            default:
                return raise ? throw Rejected(machine, cell, kinds) : false;
        }
    }

    private static bool TryReadList(Machine machine, Cell list, TextKinds kinds, bool raise, out string text)
    {
        var builder = new StringBuilder();
        text = string.Empty;
        bool? codes = null;
        Cell cell = list;

        while (IsListCell(machine, cell))
        {
            Cell element = machine.Dereference(machine.HeapAt(cell.Index + 1));
            if (element.Tag == CellTag.Reference)
            {
                return raise ? throw PrologErrors.Instantiation(machine) : false;
            }

            var first = codes is null;
            codes ??= element.Tag is CellTag.Integer or CellTag.BigInteger;

            if (codes.Value)
            {
                if (element.Tag != CellTag.Integer || !IsCode(machine, element.Integer))
                {
                    return raise ? throw PrologErrors.Type(machine, "character_code", element) : false;
                }

                AppendCode(builder, element.Integer);
            }
            else
            {
                string? name = element.Tag == CellTag.Atom ? machine.Symbols.AtomName(element.Index) : null;
                if (name is null || !IsCharacter(machine, name))
                {
                    return raise ? throw PrologErrors.Type(machine, first ? "character_code" : "character", element) : false;
                }

                builder.Append(name);
            }

            cell = machine.Dereference(machine.HeapAt(cell.Index + 2));
        }

        if (TermList.IsEmpty(machine, cell))
        {
            text = builder.ToString();
            return true;
        }

        if (!raise)
        {
            return false;
        }

        throw cell.Tag == CellTag.Reference ? PrologErrors.Instantiation(machine) : Rejected(machine, list, kinds);
    }

    private static bool IsListCell(Machine machine, Cell cell) =>
        cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == machine.Symbols.ListFunctor;

    /// <summary>
    /// SWI's error for a term that is not text: <c>list</c> when only lists and strings are text,
    /// <c>text</c> when lists and atoms are, and <c>atomic</c> when numbers are but lists are not.
    /// </summary>
    private static PrologException Rejected(Machine machine, Cell culprit, TextKinds kinds)
    {
        var expected =
            (kinds & TextKinds.List) == 0 ? ((kinds & TextKinds.Number) != 0 ? "atomic" : "text")
            : (kinds & (TextKinds.Atom | TextKinds.Number)) == 0 ? "list"
            : "text";

        return PrologErrors.Type(machine, expected, culprit);
    }
}
