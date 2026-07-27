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
        registry.RegisterNondeterministic("current_op", 3, static machine => Current(machine, 0), Current);
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
        List<Cell> targets =
            names.Tag == CellTag.Structure && machine.HeapAt(names.Index).Index == machine.Symbols.ListFunctor
                ? TermList.ReadProper(machine, names)
                : [names];

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

            // ',' is not redefinable: argument lists and conjunction both depend on what it means.
            if (text == ",")
            {
                throw PrologErrors.Permission(machine, "modify", "operator", machine.Symbols.InternFunctor(",", 2));
            }

            machine.Operators.Define((int)priority.Integer, specifier, text);
        }

        return true;
    }

    /// <summary>
    /// <c>current_op(?Priority, ?Type, ?Name)</c>: every definition in turn, in a fixed order.
    /// </summary>
    /// <param name="machine">The machine.</param>
    /// <param name="state">Index into the ordered snapshot of the table.</param>
    /// <remarks>
    /// The snapshot is rebuilt on each solution rather than held across them. It costs a sort of
    /// about sixty entries and means an <c>op/3</c> executed while enumerating cannot leave the retry
    /// pointing at a definition that no longer exists.
    /// </remarks>
    private static bool Current(Machine machine, long state)
    {
        PrologOperator[] all = machine.Operators.All();
        int index = (int)state;

        if (index >= all.Length)
        {
            return false;
        }

        if (index + 1 < all.Length)
        {
            machine.PushRetry(index + 1);
        }

        PrologOperator entry = all[index];

        return machine.Unify(machine.Argument(0), Cell.Integer60(entry.Priority))
            && machine.Unify(machine.Argument(1), Cell.Atom(machine.Symbols.InternAtom(NameOf(entry.Type))))
            && machine.Unify(machine.Argument(2), Cell.Atom(machine.Symbols.InternAtom(entry.Name)));
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
}
