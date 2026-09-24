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
/// A character is a one-character atom and a code is its UTF-16 code unit. <see cref="IsCode"/>,
/// <see cref="IsCharacter"/>, and <see cref="AppendCode"/> are the only places that representation
/// is decided here.
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

    /// <summary>Whether <paramref name="code"/> is a character code.</summary>
    internal static bool IsCode(long code) => code is >= 0 and <= char.MaxValue;

    /// <summary>Whether <paramref name="name"/> is the name of a one-character atom.</summary>
    internal static bool IsCharacter(string name) => name.Length == 1;

    /// <summary>Appends the character whose code is <paramref name="code"/>.</summary>
    internal static void AppendCode(StringBuilder text, long code) => text.Append((char)code);

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
                if (element.Tag != CellTag.Integer || !IsCode(element.Integer))
                {
                    return raise ? throw PrologErrors.Type(machine, "character_code", element) : false;
                }

                AppendCode(builder, element.Integer);
            }
            else
            {
                string? name = element.Tag == CellTag.Atom ? machine.Symbols.AtomName(element.Index) : null;
                if (name is null || !IsCharacter(name))
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
