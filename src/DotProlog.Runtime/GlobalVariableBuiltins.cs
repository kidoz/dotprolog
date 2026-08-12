namespace DotProlog.Runtime;

/// <summary>
/// SWI-style global variables: named values scoped to the machine, the way SWI scopes them to a
/// thread. <c>nb_setval/2</c> stores a detached copy that survives backtracking; <c>b_setval/2</c>
/// stores the live term and the assignment is undone when execution backtracks past it. Both
/// getters read the same store and raise <c>existence_error(variable, Key)</c> for an unset name;
/// <c>nb_current/2</c> reads the same store too but fails for an unset name and enumerates every
/// set variable when the name is unbound, the way SWI's does.
/// </summary>
internal static class GlobalVariableBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("nb_setval", 2, static machine => SetValue(machine, backtrackable: false));
        registry.Register("b_setval", 2, static machine => SetValue(machine, backtrackable: true));
        registry.Register("nb_getval", 2, GetValue);
        registry.Register("b_getval", 2, GetValue);
        registry.RegisterNondeterministic(
            "nb_current",
            2,
            static machine => Current(machine, 0),
            static (machine, state) => Current(machine, (int)state)
        );
    }

    private static bool Current(Machine machine, int fromKeyAtom)
    {
        Cell key = machine.Argument(0);
        if (key.Tag == CellTag.Atom)
        {
            return machine.TryGetGlobal(key.Index, out Cell value) && machine.Unify(machine.Argument(1), value);
        }

        if (key.Tag != CellTag.Reference)
        {
            throw PrologErrors.Type(machine, "atom", key);
        }

        for (var from = fromKeyAtom; machine.TryPeekNextGlobalKey(from, out var keyAtom); from = keyAtom + 1)
        {
            machine.TryGetGlobal(keyAtom, out Cell value);
            Cell pattern = machine.CreateStructure(machine.Symbols.InternFunctor("-", 2), [key, machine.Argument(1)]);
            Cell candidate = machine.CreateStructure(machine.Symbols.InternFunctor("-", 2), [Cell.Atom(keyAtom), value]);

            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (machine.TryPeekNextGlobalKey(keyAtom + 1, out _))
            {
                machine.PushRetry(keyAtom + 1);
            }

            return machine.Unify(pattern, candidate);
        }

        return false;
    }

    private static bool SetValue(Machine machine, bool backtrackable)
    {
        machine.SetGlobal(RequireKey(machine), machine.Argument(1), backtrackable);
        return true;
    }

    private static bool GetValue(Machine machine)
    {
        var key = RequireKey(machine);
        if (!machine.TryGetGlobal(key, out Cell value))
        {
            throw PrologErrors.Existence(machine, "variable", machine.Argument(0));
        }

        return machine.Unify(machine.Argument(1), value);
    }

    private static int RequireKey(Machine machine)
    {
        Cell key = machine.Argument(0);
        if (key.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (key.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", key);
        }

        return key.Index;
    }
}
