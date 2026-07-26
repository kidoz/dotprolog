using System.Globalization;

namespace Prolog.Runtime;

/// <summary>
/// Renders a term to text. Traversal is iterative over an explicit work stack, so a deeply nested
/// term cannot exhaust the CLR stack.
/// </summary>
/// <remarks>
/// Output is canonical apart from list notation: operators are written in functional form
/// (<c>+(1, 2)</c>, not <c>1+2</c>) until the writer learns the operator table.
/// </remarks>
public static class TermWriter
{
    private const string SymbolCharacters = "+-*/\\^<>=~:.?@#&$";

    /// <summary>Writes <paramref name="term"/> to <paramref name="output"/>.</summary>
    /// <param name="machine">Machine owning the heap the term lives on.</param>
    /// <param name="term">The term to write.</param>
    /// <param name="output">Destination.</param>
    /// <param name="quoted">Whether atoms are quoted so the output can be read back, as <c>writeq/1</c> does.</param>
    public static void Write(Machine machine, Cell term, TextWriter output, bool quoted = false)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(output);

        List<WriteItem> work = [new WriteItem(WriteItemKind.Term, term, null)];

        while (work.Count > 0)
        {
            WriteItem item = work[^1];
            work.RemoveAt(work.Count - 1);

            switch (item.Kind)
            {
                case WriteItemKind.Text:
                    output.Write(item.Text);
                    break;

                case WriteItemKind.ListTail:
                    WriteListTail(machine, item.Cell, output, work);
                    break;

                default:
                    WriteTerm(machine, item.Cell, output, quoted, work);
                    break;
            }
        }
    }

    /// <summary>Renders <paramref name="term"/> to a string.</summary>
    /// <param name="machine">Machine owning the heap the term lives on.</param>
    /// <param name="term">The term to render.</param>
    /// <param name="quoted">Whether atoms are quoted so the output can be read back.</param>
    public static string ToDisplayString(Machine machine, Cell term, bool quoted = false)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(machine, term, writer, quoted);
        return writer.ToString();
    }

    private static void WriteTerm(Machine machine, Cell cell, TextWriter output, bool quoted, List<WriteItem> work)
    {
        cell = machine.Dereference(cell);

        switch (cell.Tag)
        {
            case CellTag.Reference:
                output.Write("_G");
                output.Write(cell.Index.ToString(CultureInfo.InvariantCulture));
                return;

            case CellTag.Atom:
                WriteAtom(machine.Symbols.AtomName(cell.Index), output, quoted);
                return;

            case CellTag.Integer:
                output.Write(cell.Integer.ToString(CultureInfo.InvariantCulture));
                return;

            case CellTag.Float:
                WriteFloat(machine.Symbols.GetFloat(cell.Index), output);
                return;

            case CellTag.Structure:
                break;

            default:
                output.Write(cell.ToString());
                return;
        }

        int functorId = machine.HeapAt(cell.Index).Index;
        Functor functor = machine.Symbols.GetFunctor(functorId);

        if (functorId == machine.Symbols.ListFunctor)
        {
            output.Write('[');
            work.Add(new WriteItem(WriteItemKind.ListTail, machine.HeapAt(cell.Index + 2), null));
            work.Add(new WriteItem(WriteItemKind.Term, machine.HeapAt(cell.Index + 1), null));
            return;
        }

        WriteAtom(machine.Symbols.AtomName(functor.NameAtom), output, quoted);
        output.Write('(');
        work.Add(new WriteItem(WriteItemKind.Text, default, ")"));
        for (int i = functor.Arity; i >= 1; i--)
        {
            work.Add(new WriteItem(WriteItemKind.Term, machine.HeapAt(cell.Index + i), null));
            if (i > 1)
            {
                work.Add(new WriteItem(WriteItemKind.Text, default, ","));
            }
        }
    }

    private static void WriteListTail(Machine machine, Cell cell, TextWriter output, List<WriteItem> work)
    {
        cell = machine.Dereference(cell);

        if (cell.Tag == CellTag.Atom && cell.Index == machine.Symbols.EmptyList)
        {
            output.Write(']');
            return;
        }

        if (cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == machine.Symbols.ListFunctor)
        {
            output.Write(',');
            work.Add(new WriteItem(WriteItemKind.ListTail, machine.HeapAt(cell.Index + 2), null));
            work.Add(new WriteItem(WriteItemKind.Term, machine.HeapAt(cell.Index + 1), null));
            return;
        }

        output.Write('|');
        work.Add(new WriteItem(WriteItemKind.Text, default, "]"));
        work.Add(new WriteItem(WriteItemKind.Term, cell, null));
    }

    private static void WriteFloat(double value, TextWriter output)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        output.Write(text);

        // A Prolog float must be readable back as a float, so it always carries a decimal point.
        if (!text.Contains('.', StringComparison.Ordinal) && !text.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            output.Write(".0");
        }
    }

    private static void WriteAtom(string name, TextWriter output, bool quoted)
    {
        if (!quoted || !NeedsQuotes(name))
        {
            output.Write(name);
            return;
        }

        output.Write('\'');
        foreach (char c in name)
        {
            switch (c)
            {
                case '\'':
                    output.Write("\\'");
                    break;
                case '\\':
                    output.Write("\\\\");
                    break;
                case '\n':
                    output.Write("\\n");
                    break;
                case '\t':
                    output.Write("\\t");
                    break;
                default:
                    output.Write(c);
                    break;
            }
        }

        output.Write('\'');
    }

    private static bool NeedsQuotes(string name)
    {
        if (name.Length == 0)
        {
            return true;
        }

        if (name is "[]" or "{}" or "!" or ";")
        {
            return false;
        }

        if (char.IsLower(name[0]))
        {
            foreach (char c in name)
            {
                if (c != '_' && !char.IsLetterOrDigit(c))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (char c in name)
        {
            if (!SymbolCharacters.Contains(c, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private enum WriteItemKind
    {
        Term,
        Text,
        ListTail,
    }

    private readonly record struct WriteItem(WriteItemKind Kind, Cell Cell, string? Text);
}
