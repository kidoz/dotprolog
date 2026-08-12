namespace DotProlog.Runtime;

/// <summary>
/// SWI-style global variables: named values scoped to the machine, the way SWI scopes them to a
/// thread. <c>nb_setval/2</c> stores a detached copy that survives backtracking; <c>b_setval/2</c>
/// stores the live term and the assignment is undone when execution backtracks past it. Both
/// getters read the same store and raise <c>existence_error(variable, Key)</c> for an unset name.
/// </summary>
internal static class GlobalVariableBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("nb_setval", 2, static machine => SetValue(machine, backtrackable: false));
        registry.Register("b_setval", 2, static machine => SetValue(machine, backtrackable: true));
        registry.Register("nb_getval", 2, GetValue);
        registry.Register("b_getval", 2, GetValue);
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
