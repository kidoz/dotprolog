namespace DotProlog.Runtime;

/// <summary>
/// <c>op/3</c> and <c>current_op/3</c>, which read and change the table shared by the reader and the
/// term writer.
/// </summary>
/// <remarks>
/// An <c>op/3</c> run as a goal takes effect for text read afterwards — a later <c>consult/1</c>, or
/// a later goal compiled by the host — and immediately for everything written. A declaration meant
/// to affect the file it appears in has to be a directive, because the reader applies those as it
/// reads rather than waiting for the file to be loaded.
/// </remarks>
internal static class OperatorBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("op", 3, Define);
        registry.RegisterNondeterministic("current_op", 3, CurrentFirst, CurrentRetry);
    }

    /// <summary>
    /// Applies <c>op(Priority, Type, Name)</c>, raising the ISO error for each way the arguments can
    /// be wrong. Name may be one atom or a list of them.
    /// </summary>
    internal static bool Define(Machine machine)
    {
        Cell priority = machine.Argument(0);
        Cell type = machine.Argument(1);
        Cell names = machine.Argument(2);

        if (priority.Tag == CellTag.Reference || type.Tag == CellTag.Reference || names.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (priority.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", priority);
        }

        if (priority.Integer is < 0 or > 1200)
        {
            throw PrologErrors.Domain(machine, "operator_priority", priority);
        }

        if (type.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", type);
        }

        OperatorType specifier = machine.Symbols.AtomName(type.Index) switch
        {
            "xfx" => OperatorType.Xfx,
            "xfy" => OperatorType.Xfy,
            "yfx" => OperatorType.Yfx,
            "fy" => OperatorType.Fy,
            "fx" => OperatorType.Fx,
            "xf" => OperatorType.Xf,
            "yf" => OperatorType.Yf,
            _ => throw PrologErrors.Domain(machine, "operator_specifier", type),
        };

        // A list of names is one declaration each, which is how the ISO table itself is written.
        List<Cell> targets;
        if (names.Tag == CellTag.Atom)
        {
            targets = [names];
        }
        else if (names.Tag == CellTag.Structure && machine.HeapAt(names.Index).Index == machine.Symbols.ListFunctor)
        {
            targets = TermList.ReadProper(machine, names);
        }
        else
        {
            throw PrologErrors.Type(machine, "list", names);
        }

        var definitions = new List<(Cell Name, string Text)>(targets.Count);
        foreach (Cell target in targets)
        {
            Cell name = machine.Dereference(target);

            if (name.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (name.Tag != CellTag.Atom)
            {
                throw PrologErrors.Type(machine, "atom", name);
            }

            string text = machine.Symbols.AtomName(name.Index);
            OperatorDefinitionConflict conflict = machine.Operators.DefinitionConflict((int)priority.Integer, specifier, text);
            if (conflict != OperatorDefinitionConflict.None)
            {
                string operation = conflict == OperatorDefinitionConflict.Create ? "create" : "modify";
                throw PrologErrors.Permission(machine, operation, "operator", name);
            }

            definitions.Add((name, text));
        }

        // Validate the whole list before changing the table so a later invalid name cannot leave
        // earlier names installed.
        foreach ((_, string text) in definitions)
        {
            machine.Operators.Define((int)priority.Integer, specifier, text);
        }

        return true;
    }

    /// <summary>
    /// <c>current_op(?Priority, ?Type, ?Name)</c>: every definition in turn, in a fixed order.
    /// </summary>
    /// <param name="machine">The machine.</param>
    private static bool CurrentFirst(Machine machine)
    {
        ValidateCurrentArguments(machine);
        return Current(machine, machine.Operators.Version, 0);
    }

    private static bool CurrentRetry(Machine machine, long state) => Current(machine, (int)(state >> 32), (int)state);

    private static bool Current(Machine machine, int version, int start)
    {
        ReadOnlySpan<PrologOperator> entries = machine.Operators.Entries(version);
        for (int index = start; index < entries.Length; index++)
        {
            PrologOperator entry = entries[index];
            Cell priority = Cell.Integer60(entry.Priority);
            Cell specifier = Cell.Atom(machine.Symbols.InternAtom(NameOf(entry.Type)));
            Cell name = Cell.Atom(machine.Symbols.InternAtom(entry.Name));

            if (
                !machine.CanUnify(machine.Argument(0), priority)
                || !machine.CanUnify(machine.Argument(1), specifier)
                || !machine.CanUnify(machine.Argument(2), name)
            )
            {
                continue;
            }

            if (index + 1 < entries.Length)
            {
                machine.PushRetry(((long)version << 32) | (uint)(index + 1));
            }

            return machine.Unify(machine.Argument(0), priority)
                && machine.Unify(machine.Argument(1), specifier)
                && machine.Unify(machine.Argument(2), name);
        }

        return false;
    }

    private static string NameOf(OperatorType type) =>
        type switch
        {
            OperatorType.Xfx => "xfx",
            OperatorType.Xfy => "xfy",
            OperatorType.Yfx => "yfx",
            OperatorType.Fy => "fy",
            OperatorType.Fx => "fx",
            OperatorType.Xf => "xf",
            _ => "yf",
        };

    private static void ValidateCurrentArguments(Machine machine)
    {
        Cell priority = machine.Argument(0);
        if (priority.Tag != CellTag.Reference && (priority.Tag != CellTag.Integer || priority.Integer is < 0 or > 1200))
        {
            throw PrologErrors.Domain(machine, "operator_priority", priority);
        }

        Cell specifier = machine.Argument(1);
        if (
            specifier.Tag != CellTag.Reference
            && (specifier.Tag != CellTag.Atom || !IsSpecifier(machine.Symbols.AtomName(specifier.Index)))
        )
        {
            throw PrologErrors.Domain(machine, "operator_specifier", specifier);
        }

        Cell name = machine.Argument(2);
        if (name.Tag is not (CellTag.Reference or CellTag.Atom))
        {
            throw PrologErrors.Type(machine, "atom", name);
        }
    }

    private static bool IsSpecifier(string name) => name is "xfx" or "xfy" or "yfx" or "fy" or "fx" or "xf" or "yf";
}
