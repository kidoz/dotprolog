namespace DotProlog.Runtime;

/// <summary>ISO predicates that mutate and enumerate the program's input-character mappings.</summary>
internal static class CharacterConversionBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("char_conversion", 2, CharConversion);
        registry.RegisterNondeterministic("current_char_conversion", 2, CurrentCharConversionFirst, CurrentCharConversionRetry);
    }

    private static bool CharConversion(Machine machine)
    {
        char input = RequireConversionCharacter(machine, machine.Argument(0));
        char output = RequireConversionCharacter(machine, machine.Argument(1));
        machine.Program.CharacterConversions.Set(input, output);
        return true;
    }

    private static bool CurrentCharConversionFirst(Machine machine)
    {
        ValidatePattern(machine, machine.Argument(0));
        ValidatePattern(machine, machine.Argument(1));
        return CurrentCharConversion(machine, machine.Program.CharacterConversions.Version, 0);
    }

    private static bool CurrentCharConversionRetry(Machine machine, long state) =>
        CurrentCharConversion(machine, (int)(state >> 32), (int)state);

    private static bool CurrentCharConversion(Machine machine, int version, int start)
    {
        ReadOnlySpan<CharacterConversionTable.Entry> entries = machine.Program.CharacterConversions.Entries(version);
        for (int index = start; index < entries.Length; index++)
        {
            CharacterConversionTable.Entry entry = entries[index];
            Cell input = Character(machine, entry.Input);
            Cell output = Character(machine, entry.Output);
            if (!machine.CanUnify(machine.Argument(0), input) || !machine.CanUnify(machine.Argument(1), output))
            {
                continue;
            }

            if (index + 1 < entries.Length)
            {
                machine.PushRetry(((long)version << 32) | (uint)(index + 1));
            }

            return machine.Unify(machine.Argument(0), input) && machine.Unify(machine.Argument(1), output);
        }

        return false;
    }

    private static void ValidatePattern(Machine machine, Cell cell)
    {
        if (cell.Tag != CellTag.Reference)
        {
            _ = RequireCharacter(machine, cell);
        }
    }

    private static char RequireConversionCharacter(Machine machine, Cell cell)
    {
        if (cell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        return TryCharacter(machine, cell, out char character)
            ? character
            : throw PrologErrors.Representation(machine, "character");
    }

    private static char RequireCharacter(Machine machine, Cell cell)
    {
        if (!TryCharacter(machine, cell, out char character))
        {
            throw PrologErrors.Type(machine, "character", cell);
        }

        return character;
    }

    private static bool TryCharacter(Machine machine, Cell cell, out char character)
    {
        character = default;
        if (cell.Tag != CellTag.Atom)
        {
            return false;
        }

        string text = machine.Symbols.AtomName(cell.Index);
        if (text.Length != 1)
        {
            return false;
        }

        character = text[0];
        return true;
    }

    private static Cell Character(Machine machine, char value) => Cell.Atom(machine.Symbols.InternAtom(value.ToString()));
}
