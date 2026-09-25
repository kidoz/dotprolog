namespace DotProlog.Runtime;

/// <summary>ISO predicates that inspect and change Prolog execution flags.</summary>
internal static class PrologFlagBuiltins
{
    private const int IsoFlagCount = 8;

    // occurs_check sits past the ISO flags, so the strict mode's count simply excludes it.
    private static int FlagCount(Machine machine) =>
        machine.Program.LanguageMode == PrologLanguageMode.StrictIso ? IsoFlagCount : IsoFlagCount + 1;

    // Integers are unbounded, so the ISO flags max_integer and min_integer name no value:
    // reading one fails, as in SWI-Prolog, and no value is appropriate to set.
    private static bool IsIntegerLimit(string name) => name is "max_integer" or "min_integer";

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

        var count = FlagCount(machine);
        uint snapshot = state == 0 ? CaptureFlags(flags) : (uint)(state >> 32);
        if (state == 0 && flag.Tag == CellTag.Atom && machine.Program.LanguageMode == PrologLanguageMode.StrictIso)
        {
            var flagName = machine.Symbols.AtomName(flag.Index);
            var known = IsIntegerLimit(flagName);
            for (var index = 0; index < count; index++)
            {
                if (ValueAt(machine, snapshot, index).Name == flagName)
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                throw PrologErrors.Domain(machine, "prolog_flag", flag);
            }
        }

        for (var index = (int)state; index < count; index++)
        {
            (var name, Cell value) = ValueAt(machine, snapshot, index);
            Cell candidate = machine.CreateStructure(
                machine.Symbols.InternFunctor("-", 2),
                [Cell.Atom(machine.Symbols.InternAtom(name)), value]
            );

            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (index + 1 < count)
            {
                machine.PushRetry(((long)snapshot << 32) | (uint)(index + 1));
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
            "occurs_check" when machine.Program.LanguageMode != PrologLanguageMode.StrictIso => SetOccursCheck(
                machine,
                flags,
                name,
                value
            ),
            "bounded" => RejectReadOnly(machine, name, value, Cell.Atom(machine.Symbols.InternAtom("false"))),
            "max_integer" or "min_integer" => throw InvalidValue(machine, name, value),
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
        // The string value is an extension gated by mode, the occurs_check pattern:
        // StrictIso keeps the ISO domain of three values.
        var atom =
            machine.Program.LanguageMode == PrologLanguageMode.StrictIso
                ? RequireValue(machine, flag, value, "codes", "chars", "atom")
                : RequireValue(machine, flag, value, "codes", "chars", "atom", "string");
        flags.DoubleQuotes = atom switch
        {
            "codes" => DoubleQuotesMode.Codes,
            "chars" => DoubleQuotesMode.Chars,
            "string" => DoubleQuotesMode.String,
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

    private static bool SetOccursCheck(Machine machine, PrologFlags flags, string flag, Cell value)
    {
        var atom = RequireValue(machine, flag, value, "false", "true", "error");
        flags.OccursCheck = atom switch
        {
            "false" => OccursCheckMode.False,
            "true" => OccursCheckMode.True,
            _ => OccursCheckMode.Error,
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

    // The retry state keeps the initial mutable values in its upper word and the next index
    // in its lower word. Each enumeration owns its snapshot, including nested module calls.
    private static uint CaptureFlags(PrologFlags flags) =>
        (flags.CharConversion ? 1u : 0u)
        | (flags.Debug ? 2u : 0u)
        | ((uint)flags.DoubleQuotes << 8)
        | ((uint)flags.Unknown << 16)
        | ((uint)flags.OccursCheck << 24);

    private static (string Name, Cell Value) ValueAt(Machine machine, uint snapshot, int index) =>
        index switch
        {
            0 => ("bounded", Atom(machine, "false")),
            1 => ("integer_rounding_function", Atom(machine, "toward_zero")),
            2 => ("max_arity", Cell.Integer60(Machine.ArgumentRegisterCount - 1)),
            3 => ("char_conversion", Atom(machine, (snapshot & 1) != 0 ? "on" : "off")),
            4 => ("debug", Atom(machine, (snapshot & 2) != 0 ? "on" : "off")),
            5 => ("double_quotes", Atom(machine, DoubleQuotesName((DoubleQuotesMode)((snapshot >> 8) & 255)))),
            6 => ("unknown", Atom(machine, UnknownName((UnknownProcedureAction)((snapshot >> 16) & 255)))),
            7 => ("colon_sets_calling_context", Atom(machine, "true")),
            _ => ("occurs_check", Atom(machine, OccursCheckName((OccursCheckMode)(snapshot >> 24)))),
        };

    private static Cell Atom(Machine machine, string name) => Cell.Atom(machine.Symbols.InternAtom(name));

    private static string DoubleQuotesName(DoubleQuotesMode mode) =>
        mode switch
        {
            DoubleQuotesMode.Codes => "codes",
            DoubleQuotesMode.Chars => "chars",
            DoubleQuotesMode.String => "string",
            _ => "atom",
        };

    private static string OccursCheckName(OccursCheckMode mode) =>
        mode switch
        {
            OccursCheckMode.False => "false",
            OccursCheckMode.True => "true",
            _ => "error",
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
