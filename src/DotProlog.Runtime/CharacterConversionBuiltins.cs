namespace DotProlog.Runtime;

/// <summary>ISO predicates that mutate and enumerate the program's input-character mappings.</summary>
internal static class CharacterConversionBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("char_conversion", 2, CharConversion);
        registry.RegisterNondeterministic("current_char_conversion", 2, CurrentCharConversionFirst, CurrentCharConversionRetry);
        registry.Register(
            "$char_conversion",
            3,
            static machine => CharConversion(machine, Context(machine, 0).CharacterConversions, 1)
        );
        registry.RegisterNondeterministic(
            "$current_char_conversion",
            3,
            static machine => CurrentCharConversionFirst(machine, Context(machine, 0).CharacterConversions, 1),
            static (machine, state) =>
                CurrentCharConversion(machine, Context(machine, 0).CharacterConversions, 1, (int)(state >> 32), (int)state)
        );
    }

    private static bool CharConversion(Machine machine) => CharConversion(machine, machine.Program.CharacterConversions, 0);

    private static bool CharConversion(Machine machine, CharacterConversionTable conversions, int offset)
    {
        var input = RequireConversionCharacter(machine, machine.Argument(offset));
        var output = RequireConversionCharacter(machine, machine.Argument(offset + 1));
        conversions.Set(input, output);
        return true;
    }

    private static bool CurrentCharConversionFirst(Machine machine)
    {
        return CurrentCharConversionFirst(machine, machine.Program.CharacterConversions, 0);
    }

    private static bool CurrentCharConversionFirst(Machine machine, CharacterConversionTable conversions, int offset)
    {
        ValidatePattern(machine, machine.Argument(offset));
        ValidatePattern(machine, machine.Argument(offset + 1));
        return CurrentCharConversion(machine, conversions, offset, conversions.Version, 0);
    }

    private static bool CurrentCharConversionRetry(Machine machine, long state) =>
        CurrentCharConversion(machine, machine.Program.CharacterConversions, 0, (int)(state >> 32), (int)state);

    private static bool CurrentCharConversion(
        Machine machine,
        CharacterConversionTable conversions,
        int offset,
        int version,
        int start
    )
    {
        ReadOnlySpan<CharacterConversionTable.Entry> entries = conversions.Entries(version);
        for (var index = start; index < entries.Length; index++)
        {
            CharacterConversionTable.Entry entry = entries[index];
            Cell input = Character(machine, entry.Input);
            Cell output = Character(machine, entry.Output);
            if (!machine.CanUnify(machine.Argument(offset), input) || !machine.CanUnify(machine.Argument(offset + 1), output))
            {
                continue;
            }

            if (index + 1 < entries.Length)
            {
                machine.PushRetry(((long)version << 32) | (uint)(index + 1));
            }

            return machine.Unify(machine.Argument(offset), input) && machine.Unify(machine.Argument(offset + 1), output);
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

        return TryCharacter(machine, cell, out var character)
            ? character
            : throw PrologErrors.Representation(machine, "character");
    }

    private static char RequireCharacter(Machine machine, Cell cell)
    {
        if (!TryCharacter(machine, cell, out var character))
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

        var text = machine.Symbols.AtomName(cell.Index);
        if (text.Length != 1)
        {
            return false;
        }

        character = text[0];
        return true;
    }

    private static Cell Character(Machine machine, char value) => Cell.Atom(machine.Symbols.InternAtom(value.ToString()));

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
