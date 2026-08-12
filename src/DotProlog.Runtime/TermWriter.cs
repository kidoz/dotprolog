using System.Globalization;

namespace DotProlog.Runtime;

/// <summary>
/// Renders a term to text, honouring the operator table so that <c>+(1, 2)</c> is written
/// <c>1+2</c>. Traversal is iterative over an explicit work stack, so a deeply nested term cannot
/// exhaust the CLR stack.
/// </summary>
/// <remarks>
/// <para>
/// Two things decide the output. **Priority** decides brackets: a term written where an argument of
/// priority at most <c>N</c> is expected gets brackets when its own priority exceeds <c>N</c>, which
/// is what keeps <c>(1+2)*3</c> from being written as <c>1+2*3</c>.
/// </para>
/// <para>
/// **Token separation** decides spaces. Rather than a table of which operators need spaces around
/// them, the writer remembers the last character it wrote and inserts a space when that character
/// and the next would otherwise lex as one token — two symbol characters, two alphanumerics, or a
/// sign directly before a digit. That is what writes <c>1+2</c> tightly but <c>1 - -2</c> and
/// <c>a mod b</c> apart, without listing a single operator by name.
/// </para>
/// </remarks>
public static class TermWriter
{
    internal const string SymbolCharacters = "+-*/\\^<>=~:.?@#&$";

    /// <summary>The priority an argument of a compound term or a list element may have.</summary>
    private const int ArgumentPriority = 999;

    /// <summary>The priority of a whole term.</summary>
    private const int TopPriority = 1200;

    /// <summary>Writes <paramref name="term"/> to <paramref name="output"/>.</summary>
    /// <param name="machine">Machine owning the heap the term lives on.</param>
    /// <param name="term">The term to write.</param>
    /// <param name="output">Destination.</param>
    /// <param name="quoted">Whether atoms are quoted so the output can be read back, as <c>writeq/1</c> does.</param>
    /// <param name="ignoreOperators">
    /// Whether to write every compound term in functional notation, as <c>write_canonical/1</c> does.
    /// </param>
    /// <param name="numberVariables">Whether <c>'$VAR'(N)</c> terms use ISO variable names.</param>
    /// <param name="operators">Operator table to use, or the machine's table when omitted.</param>
    public static void Write(
        Machine machine,
        Cell term,
        TextWriter output,
        bool quoted = false,
        bool ignoreOperators = false,
        bool numberVariables = false,
        OperatorTable? operators = null
    )
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(output);

        WriteCore(
            machine,
            term,
            output,
            quoted,
            ignoreOperators,
            numberVariables,
            variableNames: null,
            operators ?? machine.Operators
        );
    }

    /// <summary>Writes a term using the ISO <c>variable_names/1</c> write option.</summary>
    internal static void WriteWithVariableNames(
        Machine machine,
        Cell term,
        TextWriter output,
        bool quoted,
        bool ignoreOperators,
        bool numberVariables,
        IReadOnlyList<NamedVariable> variableNames,
        OperatorTable? operators = null,
        bool spacingNextArgument = false
    )
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(variableNames);

        WriteCore(
            machine,
            term,
            output,
            quoted,
            ignoreOperators,
            numberVariables,
            variableNames,
            operators ?? machine.Operators,
            spacingNextArgument
        );
    }

    private static void WriteCore(
        Machine machine,
        Cell term,
        TextWriter output,
        bool quoted,
        bool ignoreOperators,
        bool numberVariables,
        IReadOnlyList<NamedVariable>? variableNames,
        OperatorTable operators,
        bool spacingNextArgument = false
    )
    {
        var writer = new Emitter(output);
        List<Item> work = [Item.OfTerm(term, TopPriority)];
        HashSet<int> active = [];

        // SWI's spacing(next_argument) write option: a space after the commas separating
        // compound arguments and list elements, which is the listing layout portray_clause emits.
        var argumentComma = spacingNextArgument ? ", " : ",";

        while (work.Count > 0)
        {
            Item item = work[^1];
            work.RemoveAt(work.Count - 1);

            switch (item.Kind)
            {
                case ItemKind.Text:
                    writer.Write(item.Literal!);
                    break;

                case ItemKind.ListTail:
                    WriteListTail(machine, item.Cell, work, active, argumentComma);
                    break;

                case ItemKind.PrefixGuard:
                    writer.GuardAfterPrefixOperator(sign: item.MaxPriority != 0);
                    break;

                case ItemKind.Leave:
                    active.Remove(item.MaxPriority);
                    break;

                default:
                    WriteTerm(
                        machine,
                        item,
                        output: writer,
                        quoted,
                        ignoreOperators,
                        numberVariables,
                        variableNames,
                        operators,
                        work,
                        active,
                        argumentComma
                    );
                    break;
            }
        }
    }

    /// <summary>Renders <paramref name="term"/> to a string.</summary>
    /// <param name="machine">Machine owning the heap the term lives on.</param>
    /// <param name="term">The term to render.</param>
    /// <param name="quoted">Whether atoms are quoted so the output can be read back.</param>
    /// <param name="ignoreOperators">Whether to write every compound term in functional notation.</param>
    /// <param name="numberVariables">Whether <c>'$VAR'(N)</c> terms use ISO variable names.</param>
    public static string ToDisplayString(
        Machine machine,
        Cell term,
        bool quoted = false,
        bool ignoreOperators = false,
        bool numberVariables = false
    )
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(machine, term, writer, quoted, ignoreOperators, numberVariables);
        return writer.ToString();
    }

    private static void WriteTerm(
        Machine machine,
        Item item,
        Emitter output,
        bool quoted,
        bool ignoreOperators,
        bool numberVariables,
        IReadOnlyList<NamedVariable>? variableNames,
        OperatorTable operators,
        List<Item> work,
        HashSet<int> active,
        string argumentComma = ","
    )
    {
        Cell cell = machine.Dereference(item.Cell);

        switch (cell.Tag)
        {
            case CellTag.Reference:
                if (TryWriteNamedVariable(machine, cell, variableNames, output))
                {
                    return;
                }

                output.Write($"_G{cell.Index.ToString(CultureInfo.InvariantCulture)}");
                return;

            case CellTag.Atom:
                WriteAtom(machine, operators, cell.Index, item.MaxPriority, output, quoted, ignoreOperators);
                return;

            case CellTag.Integer:
                output.Write(cell.Integer.ToString(CultureInfo.InvariantCulture));
                return;

            case CellTag.Float:
                output.Write(FloatText(machine.Symbols.GetFloat(cell.Index)));
                return;

            case CellTag.Structure:
                break;

            default:
                output.Write(cell.ToString());
                return;
        }

        var functorId = machine.HeapAt(cell.Index).Index;
        Functor functor = machine.Symbols.GetFunctor(functorId);

        if (numberVariables && TryWriteNumberVariable(machine, cell, functor, output))
        {
            return;
        }

        // A rational term would unfold forever; re-entering a structure that is still being
        // written is cut off with an ellipsis so the writer always terminates.
        if (!active.Add(cell.Index))
        {
            output.Write("...");
            return;
        }

        work.Add(Item.OfLeave(cell.Index));

        if (!ignoreOperators && functorId == machine.Symbols.ListFunctor)
        {
            output.Write("[");
            work.Add(Item.OfListTail(machine.HeapAt(cell.Index + 2)));
            work.Add(Item.OfTerm(machine.HeapAt(cell.Index + 1), ArgumentPriority));
            return;
        }

        var name = machine.Symbols.AtomName(functor.NameAtom);

        if (!ignoreOperators)
        {
            // {}/1 is written in its own notation, which is not an operator but is read as one shape.
            if (functor.Arity == 1 && functor.NameAtom == machine.Symbols.Curly)
            {
                output.Write("{");
                work.Add(Item.OfText("}"));
                work.Add(Item.OfTerm(machine.HeapAt(cell.Index + 1), TopPriority));
                return;
            }

            if (TryWriteOperator(machine, operators, cell, functor, name, item.MaxPriority, output, quoted, work))
            {
                return;
            }
        }

        WriteAtomText(name, output, quoted);
        output.Write("(");
        work.Add(Item.OfText(")"));

        for (var i = functor.Arity; i >= 1; i--)
        {
            work.Add(Item.OfTerm(machine.HeapAt(cell.Index + i), ArgumentPriority));
            if (i > 1)
            {
                work.Add(Item.OfText(argumentComma));
            }
        }
    }

    private static bool TryWriteNamedVariable(
        Machine machine,
        Cell variable,
        IReadOnlyList<NamedVariable>? variableNames,
        Emitter output
    )
    {
        if (variableNames is null)
        {
            return false;
        }

        foreach (NamedVariable named in variableNames)
        {
            if (TermOrder.AreIdentical(machine, variable, named.Term))
            {
                output.Write(named.Name);
                return true;
            }
        }

        return false;
    }

    private static bool TryWriteNumberVariable(Machine machine, Cell cell, Functor functor, Emitter output)
    {
        if (functor.Arity != 1 || machine.Symbols.AtomName(functor.NameAtom) != "$VAR")
        {
            return false;
        }

        Cell number = machine.Dereference(machine.HeapAt(cell.Index + 1));
        if (number.Tag != CellTag.Integer || number.Integer < 0)
        {
            return false;
        }

        var value = number.Integer;
        var letter = (char)('A' + (value % 26));
        var suffix = value / 26;
        output.Write(suffix == 0 ? letter.ToString() : $"{letter}{suffix.ToString(CultureInfo.InvariantCulture)}");

        return true;
    }

    /// <summary>
    /// Writes a compound term in operator notation when its functor has a matching definition, and
    /// reports whether it did.
    /// </summary>
    private static bool TryWriteOperator(
        Machine machine,
        OperatorTable operators,
        Cell cell,
        Functor functor,
        string name,
        int maxPriority,
        Emitter output,
        bool quoted,
        List<Item> work
    )
    {
        if (functor.Arity == 2 && operators.TryGetInfixOrPostfix(name, out PrologOperator infix) && infix.IsInfix)
        {
            var bracket = infix.Priority > maxPriority;
            if (bracket)
            {
                output.Write("(");
                work.Add(Item.OfText(")"));
            }

            // Pushed in reverse: right argument, then the operator, then the left.
            work.Add(Item.OfTerm(machine.HeapAt(cell.Index + 2), infix.RightPriority));
            work.Add(Item.OfText(OperatorText(name, quoted)));
            work.Add(Item.OfTerm(machine.HeapAt(cell.Index + 1), infix.LeftPriority));
            return true;
        }

        if (functor.Arity == 1 && operators.TryGetPrefix(name, out PrologOperator prefix))
        {
            var bracket = prefix.Priority > maxPriority;
            if (bracket)
            {
                output.Write("(");
                work.Add(Item.OfText(")"));
            }

            work.Add(Item.OfTerm(machine.HeapAt(cell.Index + 1), prefix.RightPriority));
            work.Add(Item.OfPrefixGuard(sign: name is "-" or "+"));
            work.Add(Item.OfText(OperatorText(name, quoted)));
            return true;
        }

        if (functor.Arity == 1 && operators.TryGetInfixOrPostfix(name, out PrologOperator postfix) && postfix.IsPostfix)
        {
            var bracket = postfix.Priority > maxPriority;
            if (bracket)
            {
                output.Write("(");
                work.Add(Item.OfText(")"));
            }

            work.Add(Item.OfText(OperatorText(name, quoted)));
            work.Add(Item.OfTerm(machine.HeapAt(cell.Index + 1), postfix.LeftPriority));
            return true;
        }

        return false;
    }

    private static void WriteListTail(
        Machine machine,
        Cell cell,
        List<Item> work,
        HashSet<int> active,
        string argumentComma = ","
    )
    {
        cell = machine.Dereference(cell);

        if (cell.Tag == CellTag.Atom && cell.Index == machine.Symbols.EmptyList)
        {
            work.Add(Item.OfText("]"));
            return;
        }

        if (cell.Tag == CellTag.Structure && machine.HeapAt(cell.Index).Index == machine.Symbols.ListFunctor)
        {
            // A circular tail re-enters a cell already being written; cut it off like any cycle.
            if (!active.Add(cell.Index))
            {
                work.Add(Item.OfText("|...]"));
                return;
            }

            work.Add(Item.OfLeave(cell.Index));
            work.Add(Item.OfListTail(machine.HeapAt(cell.Index + 2)));
            work.Add(Item.OfTerm(machine.HeapAt(cell.Index + 1), ArgumentPriority));
            work.Add(Item.OfText(argumentComma));
            return;
        }

        work.Add(Item.OfText("]"));
        work.Add(Item.OfTerm(cell, ArgumentPriority));
        work.Add(Item.OfText("|"));
    }

    /// <summary>
    /// Writes an atom, bracketing it when it is an operator whose priority exceeds what the position
    /// allows — which is what makes <c>f((:-))</c> read back as the atom rather than as a syntax error.
    /// </summary>
    private static void WriteAtom(
        Machine machine,
        OperatorTable operators,
        int atomId,
        int maxPriority,
        Emitter output,
        bool quoted,
        bool ignoreOperators
    )
    {
        var name = machine.Symbols.AtomName(atomId);

        if (!ignoreOperators && operators.MaxPriority(name) > maxPriority)
        {
            output.Write("(");
            WriteAtomText(name, output, quoted);
            output.Write(")");
            return;
        }

        WriteAtomText(name, output, quoted);
    }

    private static string FloatText(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);

        // A Prolog float must be readable back as a float, so it always carries a decimal point.
        if (text.Contains('.', StringComparison.Ordinal))
        {
            return text;
        }

        var exponent = text.IndexOf('E', StringComparison.Ordinal);
        if (exponent < 0)
        {
            exponent = text.IndexOf('e', StringComparison.Ordinal);
        }

        return exponent >= 0 ? text.Insert(exponent, ".0") : text + ".0";
    }

    /// <summary>
    /// An operator's name as it appears between or before its arguments.
    /// </summary>
    /// <remarks>
    /// The comma is the exception to quoting. As an atom it needs quotes — <c>f(',')</c> is the only
    /// way to pass it as an argument — but as an operator it must be bare, because <c>a','b</c> does
    /// not read back as a conjunction while <c>a,b</c> does.
    /// </remarks>
    private static string OperatorText(string name, bool quoted) => name == "," ? "," : QuotedAtomText(name, quoted);

    private static void WriteAtomText(string name, Emitter output, bool quoted) => output.Write(QuotedAtomText(name, quoted));

    private static string QuotedAtomText(string name, bool quoted)
    {
        if (!quoted || !NeedsQuotes(name))
        {
            return name;
        }

        var quotedText = new System.Text.StringBuilder(name.Length + 2);
        quotedText.Append('\'');

        foreach (var c in name)
        {
            _ = c switch
            {
                '\'' => quotedText.Append("\\'"),
                '\\' => quotedText.Append("\\\\"),
                '\a' => quotedText.Append("\\a"),
                '\b' => quotedText.Append("\\b"),
                '\f' => quotedText.Append("\\f"),
                '\n' => quotedText.Append("\\n"),
                '\r' => quotedText.Append("\\r"),
                '\t' => quotedText.Append("\\t"),
                '\v' => quotedText.Append("\\v"),
                // The reader rejects a raw control character between quotes, so the rest of them
                // leave as the delimited hexadecimal escape it accepts back.
                _ when char.IsControl(c) => quotedText
                    .Append("\\x")
                    .Append(((int)c).ToString("x", CultureInfo.InvariantCulture))
                    .Append('\\'),
                _ => quotedText.Append(c),
            };
        }

        return quotedText.Append('\'').ToString();
    }

    private static bool NeedsQuotes(string name)
    {
        if (name.Length == 0)
        {
            return true;
        }

        if (name == ".")
        {
            return true;
        }

        if (name is "[]" or "{}" or "!" or ";")
        {
            return false;
        }

        if (char.IsLower(name[0]))
        {
            foreach (var c in name)
            {
                if (c != '_' && !char.IsLetterOrDigit(c))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var c in name)
        {
            if (!SymbolCharacters.Contains(c, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes text while keeping adjacent tokens apart.
    /// </summary>
    /// <remarks>
    /// The whole rule is here: a space goes in whenever the character just written and the one about
    /// to be written would lex as a single token. Without it <c>1 - -2</c> would be written
    /// <c>1--2</c>, which reads back as <c>1</c> and the operator <c>--</c>, and <c>a mod b</c> would
    /// become <c>amodb</c>.
    /// </remarks>
    private sealed class Emitter(TextWriter output)
    {
        private char _last;
        private bool _afterPrefix;
        private bool _afterSign;

        internal void Write(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (NeedsSeparator(_last, text[0]) || NeedsPrefixSeparator(text[0]))
            {
                output.Write(' ');
            }

            _afterPrefix = false;
            _afterSign = false;
            output.Write(text);
            _last = text[^1];
        }

        /// <summary>
        /// Says that what comes next is the argument of a prefix operator, which the characters
        /// alone cannot tell from an infix one.
        /// </summary>
        /// <param name="sign">Whether the operator was <c>-</c> or <c>+</c>.</param>
        /// <remarks>
        /// Two things go wrong without this. An operator directly followed by <c>(</c> reads as
        /// functor notation, so <c>\+(a,b)</c> is the binary term rather than negation applied to a
        /// conjunction. And a sign directly followed by a digit reads as a negative number, so
        /// <c>-(1)</c> written as <c>-1</c> comes back as the integer.
        /// </remarks>
        internal void GuardAfterPrefixOperator(bool sign)
        {
            _afterPrefix = true;
            _afterSign = sign;
        }

        private bool NeedsPrefixSeparator(char next) => (_afterPrefix && next == '(') || (_afterSign && char.IsAsciiDigit(next));

        private static bool NeedsSeparator(char last, char next)
        {
            if (last == '\0')
            {
                return false;
            }

            if (IsSymbol(last) && IsSymbol(next))
            {
                return true;
            }

            if (IsAlphanumeric(last) && IsAlphanumeric(next))
            {
                return true;
            }

            return false;
        }

        private static bool IsSymbol(char c) => SymbolCharacters.Contains(c, StringComparison.Ordinal);

        private static bool IsAlphanumeric(char c) => c == '_' || char.IsLetterOrDigit(c);
    }

    private enum ItemKind
    {
        Term,
        Text,
        ListTail,
        PrefixGuard,
        Leave,
    }

    internal readonly record struct NamedVariable(string Name, Cell Term);

    private readonly record struct Item(ItemKind Kind, Cell Cell, string? Literal, int MaxPriority)
    {
        internal static Item OfTerm(Cell cell, int maxPriority) => new(ItemKind.Term, cell, null, maxPriority);

        internal static Item OfText(string text) => new(ItemKind.Text, default, text, 0);

        internal static Item OfListTail(Cell cell) => new(ItemKind.ListTail, cell, null, 0);

        /// <summary>A marker between a prefix operator and its argument. <c>MaxPriority</c> carries
        /// whether the operator was a sign, which is the only extra bit the marker needs.</summary>
        internal static Item OfPrefixGuard(bool sign) => new(ItemKind.PrefixGuard, default, null, sign ? 1 : 0);

        /// <summary>Marks leaving the structure at heap index <paramref name="index"/>, carried in
        /// <c>MaxPriority</c>.</summary>
        internal static Item OfLeave(int index) => new(ItemKind.Leave, default, null, index);
    }
}
