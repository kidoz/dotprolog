namespace DotProlog.Runtime;

/// <summary>ISO predicates that inspect and change Prolog execution flags.</summary>
internal static class PrologFlagBuiltins
{
    private const int FlagCount = 10;

    internal static void Register(BuiltinRegistry registry)
    {
        registry.RegisterNondeterministic(
            "current_prolog_flag",
            2,
            static machine => CurrentPrologFlag(machine, 0),
            CurrentPrologFlag
        );
        registry.Register("set_prolog_flag", 2, SetPrologFlag);
        registry.RegisterNondeterministic(
            "$current_prolog_flag",
            3,
            static machine => CurrentPrologFlag(machine, Context(machine, 0).Flags, 1, 0),
            static (machine, state) => CurrentPrologFlag(machine, Context(machine, 0).Flags, 1, state)
        );
        registry.Register("$set_prolog_flag", 3, static machine => SetPrologFlag(machine, Context(machine, 0).Flags, 1));
    }

    private static bool CurrentPrologFlag(Machine machine, long state) =>
        CurrentPrologFlag(machine, machine.Program.Flags, 0, state);

    private static bool CurrentPrologFlag(Machine machine, PrologFlags flags, int offset, long state)
    {
        Cell flag = machine.Argument(offset);
        if (flag.Tag is not (CellTag.Reference or CellTag.Atom))
        {
            throw PrologErrors.Type(machine, "atom", flag);
        }

        Cell pattern = machine.CreateStructure(machine.Symbols.InternFunctor("-", 2), [flag, machine.Argument(offset + 1)]);

        for (var index = (int)state; index < FlagCount; index++)
        {
            (var name, Cell value) = ValueAt(machine, flags, index);
            Cell candidate = machine.CreateStructure(
                machine.Symbols.InternFunctor("-", 2),
                [Cell.Atom(machine.Symbols.InternAtom(name)), value]
            );

            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (index + 1 < FlagCount)
            {
                machine.PushRetry(index + 1);
            }

            return machine.Unify(pattern, candidate);
        }

        return false;
    }

    private static bool SetPrologFlag(Machine machine) => SetPrologFlag(machine, machine.Program.Flags, 0);

    private static bool SetPrologFlag(Machine machine, PrologFlags flags, int offset)
    {
        Cell flag = machine.Argument(offset);
        Cell value = machine.Argument(offset + 1);

        if (flag.Tag == CellTag.Reference || value.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (flag.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", flag);
        }

        var name = machine.Symbols.AtomName(flag.Index);
        return name switch
        {
            "char_conversion" => SetOnOff(
                machine,
                flags,
                name,
                value,
                static (target, enabled) => target.SetCharConversion(enabled)
            ),
            "debug" => SetOnOff(machine, flags, name, value, static (target, enabled) => target.Debug = enabled),
            "double_quotes" => SetDoubleQuotes(machine, flags, name, value),
            "unknown" => SetUnknown(machine, flags, name, value),
            "bounded" => RejectReadOnly(machine, name, value, Cell.Atom(machine.Symbols.InternAtom("true"))),
            "max_integer" => RejectReadOnly(machine, name, value, Cell.Integer60(Cell.MaxInteger)),
            "min_integer" => RejectReadOnly(machine, name, value, Cell.Integer60(Cell.MinInteger)),
            "integer_rounding_function" => RejectReadOnly(
                machine,
                name,
                value,
                Cell.Atom(machine.Symbols.InternAtom("toward_zero"))
            ),
            "max_arity" => RejectReadOnly(machine, name, value, Cell.Integer60(Machine.ArgumentRegisterCount - 1)),
            "colon_sets_calling_context" => RejectReadOnly(machine, name, value, Cell.Atom(machine.Symbols.InternAtom("true"))),
            _ => throw PrologErrors.Domain(machine, "prolog_flag", flag),
        };
    }

    private static bool SetOnOff(Machine machine, PrologFlags flags, string flag, Cell value, Action<PrologFlags, bool> update)
    {
        var atom = RequireValue(machine, flag, value, "on", "off");
        update(flags, atom == "on");
        return true;
    }

    private static bool SetDoubleQuotes(Machine machine, PrologFlags flags, string flag, Cell value)
    {
        var atom = RequireValue(machine, flag, value, "codes", "chars", "atom");
        flags.DoubleQuotes = atom switch
        {
            "codes" => DoubleQuotesMode.Codes,
            "chars" => DoubleQuotesMode.Chars,
            _ => DoubleQuotesMode.Atom,
        };
        return true;
    }

    private static bool SetUnknown(Machine machine, PrologFlags flags, string flag, Cell value)
    {
        var atom = RequireValue(machine, flag, value, "error", "warning", "fail");
        flags.Unknown = atom switch
        {
            "error" => UnknownProcedureAction.Error,
            "warning" => UnknownProcedureAction.Warning,
            _ => UnknownProcedureAction.Fail,
        };
        return true;
    }

    private static string RequireValue(Machine machine, string flag, Cell value, params string[] allowed)
    {
        if (value.Tag == CellTag.Atom)
        {
            var atom = machine.Symbols.AtomName(value.Index);
            if (allowed.Contains(atom, StringComparer.Ordinal))
            {
                return atom;
            }
        }

        throw InvalidValue(machine, flag, value);
    }

    private static bool RejectReadOnly(Machine machine, string flag, Cell value, Cell permitted)
    {
        if (!machine.CanUnify(value, permitted))
        {
            throw InvalidValue(machine, flag, value);
        }

        throw PrologErrors.Permission(machine, "modify", "flag", Cell.Atom(machine.Symbols.InternAtom(flag)));
    }

    private static PrologException InvalidValue(Machine machine, string flag, Cell value)
    {
        Cell culprit = machine.CreateStructure(
            machine.Symbols.InternFunctor("+", 2),
            [Cell.Atom(machine.Symbols.InternAtom(flag)), value]
        );
        return PrologErrors.Domain(machine, "flag_value", culprit);
    }

    private static (string Name, Cell Value) ValueAt(Machine machine, PrologFlags flags, int index) =>
        index switch
        {
            0 => ("bounded", Atom(machine, "true")),
            1 => ("max_integer", Cell.Integer60(Cell.MaxInteger)),
            2 => ("min_integer", Cell.Integer60(Cell.MinInteger)),
            3 => ("integer_rounding_function", Atom(machine, "toward_zero")),
            4 => ("max_arity", Cell.Integer60(Machine.ArgumentRegisterCount - 1)),
            5 => ("char_conversion", Atom(machine, flags.CharConversion ? "on" : "off")),
            6 => ("debug", Atom(machine, flags.Debug ? "on" : "off")),
            7 => ("double_quotes", Atom(machine, DoubleQuotesName(flags.DoubleQuotes))),
            8 => ("unknown", Atom(machine, UnknownName(flags.Unknown))),
            _ => ("colon_sets_calling_context", Atom(machine, "true")),
        };

    private static Cell Atom(Machine machine, string name) => Cell.Atom(machine.Symbols.InternAtom(name));

    private static string DoubleQuotesName(DoubleQuotesMode mode) =>
        mode switch
        {
            DoubleQuotesMode.Codes => "codes",
            DoubleQuotesMode.Chars => "chars",
            _ => "atom",
        };

    private static string UnknownName(UnknownProcedureAction action) =>
        action switch
        {
            UnknownProcedureAction.Error => "error",
            UnknownProcedureAction.Warning => "warning",
            _ => "fail",
        };

    private static ModuleDefinition Context(Machine machine, int argument)
    {
        Cell module = machine.Argument(argument);
        if (module.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (module.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", module);
        }

        string name = machine.Symbols.AtomName(module.Index);
        return machine.Program.Modules.TryGet(name, out ModuleDefinition? definition)
            ? definition!
            : throw PrologErrors.Existence(machine, "module", module);
    }
}
