namespace DotProlog.Runtime;

/// <summary>ISO/IEC 13211-2 module enumeration and procedure reflection.</summary>
internal static class ModuleBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.RegisterNondeterministic("current_module", 1, static machine => CurrentModule(machine, 0), CurrentModule);
        registry.RegisterNondeterministic(
            "predicate_property",
            2,
            static machine => PredicateProperty(machine, "user", prototypeArgument: 0, propertyArgument: 1, state: 0),
            static (machine, state) => PredicateProperty(machine, "user", 0, 1, state)
        );
        registry.RegisterNondeterministic(
            "$predicate_property",
            3,
            static machine => PredicateProperty(machine, Context(machine, 0), 1, 2, 0),
            static (machine, state) => PredicateProperty(machine, Context(machine, 0), 1, 2, state)
        );
        registry.RegisterNondeterministic(
            "$current_predicate",
            2,
            static machine => CurrentPredicate(machine, Context(machine, 0), 1, 0),
            static (machine, state) => CurrentPredicate(machine, Context(machine, 0), 1, state)
        );
    }

    private static string Context(Machine machine, int argument)
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

        return machine.Symbols.AtomName(module.Index);
    }

    private static bool CurrentModule(Machine machine, long state)
    {
        Cell requested = machine.Argument(0);
        if (requested.Tag is not (CellTag.Reference or CellTag.Atom))
        {
            throw PrologErrors.Type(machine, "atom", requested);
        }

        ModuleDefinition[] modules = [.. machine.Program.Modules.Definitions];
        for (var index = (int)state; index < modules.Length; index++)
        {
            if (modules[index].Name != "user" && !modules[index].InterfacePrepared)
            {
                continue;
            }

            Cell candidate = Cell.Atom(machine.Symbols.InternAtom(modules[index].Name));
            if (!machine.CanUnify(requested, candidate))
            {
                continue;
            }

            if (index + 1 < modules.Length)
            {
                machine.PushRetry(index + 1);
            }

            return machine.Unify(requested, candidate);
        }

        return false;
    }

    private static bool CurrentPredicate(Machine machine, string context, int argument, long state)
    {
        Cell requested = machine.Argument(argument);
        Cell pattern = requested;
        var colon = machine.Symbols.InternFunctor(":", 2);
        if (requested.Tag == CellTag.Structure && machine.HeapAt(requested.Index).Index == colon)
        {
            Cell moduleTerm = machine.Dereference(machine.HeapAt(requested.Index + 1));
            if (moduleTerm.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (moduleTerm.Tag != CellTag.Atom)
            {
                throw PrologErrors.Type(machine, "atom", moduleTerm);
            }

            context = machine.Symbols.AtomName(moduleTerm.Index);
            pattern = machine.Dereference(machine.HeapAt(requested.Index + 2));
        }

        ValidatePredicateIndicator(machine, pattern);

        if (!machine.Program.Modules.TryGet(context, out ModuleDefinition? module))
        {
            throw PrologErrors.Existence(machine, "module", Atom(machine, context));
        }

        List<ModulePredicateIndicator> visible = [];
        foreach (ModulePredicateDefinition predicate in module!.Predicates)
        {
            if (!predicate.Defined || !IsUserProcedure(machine, predicate.DefiningModule, predicate.Indicator))
            {
                continue;
            }

            visible.Add(predicate.Indicator);
        }

        foreach ((ModulePredicateIndicator indicator, string source) in module.Imports)
        {
            string defining = source;
            FollowDefinition(machine, indicator, ref defining, out ModulePredicateDefinition? metadata);
            if (metadata is { Defined: true } && IsUserProcedure(machine, defining, indicator) && !visible.Contains(indicator))
            {
                visible.Add(indicator);
            }
        }

        var slash = machine.Symbols.InternFunctor("/", 2);
        for (var index = (int)state; index < visible.Count; index++)
        {
            ModulePredicateIndicator indicator = visible[index];
            Cell candidate = machine.CreateStructure(slash, [Atom(machine, indicator.Name), Cell.Integer60(indicator.Arity)]);
            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (index + 1 < visible.Count)
            {
                machine.PushRetry(index + 1);
            }

            return machine.Unify(pattern, candidate);
        }

        return false;
    }

    private static bool IsUserProcedure(Machine machine, string module, ModulePredicateIndicator indicator)
    {
        var compiledName = module == "user" ? indicator.Name : $"{module}:{indicator.Name}";
        var functor = machine.Symbols.InternFunctor(compiledName, indicator.Arity);
        return machine.Program.IsUserPredicate(functor);
    }

    private static void ValidatePredicateIndicator(Machine machine, Cell indicator)
    {
        indicator = machine.Dereference(indicator);
        if (indicator.Tag == CellTag.Reference)
        {
            return;
        }

        var slash = machine.Symbols.InternFunctor("/", 2);
        if (indicator.Tag != CellTag.Structure || machine.HeapAt(indicator.Index).Index != slash)
        {
            throw PrologErrors.Type(machine, "predicate_indicator", indicator);
        }

        Cell name = machine.Dereference(machine.HeapAt(indicator.Index + 1));
        Cell arity = machine.Dereference(machine.HeapAt(indicator.Index + 2));
        if (name.Tag is not (CellTag.Reference or CellTag.Atom))
        {
            throw PrologErrors.Type(machine, "atom", name);
        }

        if (arity.Tag == CellTag.Reference)
        {
            return;
        }

        if (arity.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", arity);
        }

        if (arity.Integer < 0)
        {
            throw PrologErrors.Domain(machine, "not_less_than_zero", arity);
        }

        if (arity.Integer >= Machine.ArgumentRegisterCount)
        {
            throw PrologErrors.Representation(machine, "max_arity");
        }
    }

    private static bool PredicateProperty(
        Machine machine,
        string context,
        int prototypeArgument,
        int propertyArgument,
        long state
    )
    {
        Cell prototype = machine.Argument(prototypeArgument);
        Cell requestedProperty = machine.Argument(propertyArgument);
        ValidateProperty(machine, requestedProperty);

        ResolvedProcedure procedure = ResolveProcedure(machine, context, prototype);
        List<Cell> properties = Properties(machine, procedure);

        for (var index = (int)state; index < properties.Count; index++)
        {
            if (!machine.CanUnify(requestedProperty, properties[index]))
            {
                continue;
            }

            if (index + 1 < properties.Count)
            {
                machine.PushRetry(index + 1);
            }

            return machine.Unify(requestedProperty, properties[index]);
        }

        return false;
    }

    private static ResolvedProcedure ResolveProcedure(Machine machine, string context, Cell prototype)
    {
        prototype = machine.Dereference(prototype);
        if (prototype.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        string module = context;
        var colon = machine.Symbols.InternFunctor(":", 2);
        while (prototype.Tag == CellTag.Structure && machine.HeapAt(prototype.Index).Index == colon)
        {
            Cell qualifier = machine.Dereference(machine.HeapAt(prototype.Index + 1));
            if (qualifier.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (qualifier.Tag != CellTag.Atom)
            {
                throw PrologErrors.Type(machine, "atom", qualifier);
            }

            module = machine.Symbols.AtomName(qualifier.Index);
            if (!machine.Program.Modules.Contains(module))
            {
                throw PrologErrors.Existence(machine, "module", qualifier);
            }

            prototype = machine.Dereference(machine.HeapAt(prototype.Index + 2));
        }

        string name;
        int arity;
        switch (prototype.Tag)
        {
            case CellTag.Atom:
                name = machine.Symbols.AtomName(prototype.Index);
                arity = 0;
                break;
            case CellTag.Structure:
                Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(prototype.Index).Index);
                name = machine.Symbols.AtomName(functor.NameAtom);
                arity = functor.Arity;
                break;
            default:
                throw PrologErrors.Type(machine, "callable", prototype);
        }

        var separator = name.LastIndexOf(':');
        if (separator > 0)
        {
            module = name[..separator];
            name = name[(separator + 1)..];
        }

        if (!machine.Program.Modules.TryGet(module, out ModuleDefinition? visibleModule))
        {
            Cell culprit = Cell.Atom(machine.Symbols.InternAtom(module));
            throw PrologErrors.Existence(machine, "module", culprit);
        }

        var indicator = new ModulePredicateIndicator(name, arity);
        var defining = module;
        var imported = false;
        string? importedFrom = null;
        if (!visibleModule!.TryPredicate(indicator, out ModulePredicateDefinition? metadata) || !metadata!.Defined)
        {
            if (visibleModule.Imports.TryGetValue(indicator, out string? source))
            {
                defining = source;
                imported = true;
                importedFrom = source;
                FollowDefinition(machine, indicator, ref defining, out metadata);
            }
            else
            {
                metadata = null;
            }
        }

        var compiledName = defining == "user" ? name : $"{defining}:{name}";
        var functorId = machine.Symbols.InternFunctor(compiledName, arity);
        var builtinFunctor = machine.Symbols.InternFunctor(name, arity);
        var builtin = machine.Program.Builtins.TryGetId(builtinFunctor, out _);
        var defined = machine.Program.IsDefined(functorId) || machine.Program.IsDynamic(functorId) || builtin;

        return new ResolvedProcedure(indicator, defining, imported, importedFrom, metadata, functorId, builtin, defined);
    }

    private static void FollowDefinition(
        Machine machine,
        ModulePredicateIndicator indicator,
        ref string module,
        out ModulePredicateDefinition? metadata
    )
    {
        HashSet<string> visited = [];
        metadata = null;
        while (visited.Add(module) && machine.Program.Modules.TryGet(module, out ModuleDefinition? definition))
        {
            if (definition!.TryPredicate(indicator, out metadata) && metadata!.Defined)
            {
                return;
            }

            if (!definition.Imports.TryGetValue(indicator, out string? source))
            {
                return;
            }

            module = source;
        }
    }

    private static List<Cell> Properties(Machine machine, ResolvedProcedure procedure)
    {
        if (!procedure.Defined)
        {
            return [];
        }

        List<Cell> properties = [];
        properties.Add(Atom(machine, machine.Program.IsDynamic(procedure.FunctorId) ? "dynamic" : "static"));
        bool isoModule =
            machine.Program.Modules.TryGet(procedure.DefiningModule, out ModuleDefinition? definition)
            && definition!.InterfacePrepared;
        properties.Add(
            Atom(machine, procedure.Metadata is { Exported: false } && !procedure.Builtin && !isoModule ? "private" : "public")
        );

        if (procedure.Builtin)
        {
            properties.Add(Atom(machine, "built_in"));
        }

        if (procedure.Metadata is { Multifile: true })
        {
            properties.Add(Atom(machine, "multifile"));
        }

        if (procedure.Metadata is { Exported: true })
        {
            properties.Add(Atom(machine, "exported"));
        }

        if (procedure.Metadata?.MetapredicateTemplate is string template)
        {
            Cell[] modes = [.. template.Select(mode => Atom(machine, mode.ToString()))];
            Cell modeIndicator = machine.CreateStructure(
                machine.Symbols.InternFunctor(procedure.Indicator.Name, modes.Length),
                modes
            );
            properties.Add(machine.CreateStructure(machine.Symbols.InternFunctor("metapredicate", 1), [modeIndicator]));
        }

        if (procedure.Imported)
        {
            properties.Add(
                machine.CreateStructure(
                    machine.Symbols.InternFunctor("imported_from", 1),
                    [Atom(machine, procedure.ImportedFrom!)]
                )
            );
        }

        properties.Add(
            machine.CreateStructure(machine.Symbols.InternFunctor("defined_in", 1), [Atom(machine, procedure.DefiningModule)])
        );
        return properties;
    }

    private static void ValidateProperty(Machine machine, Cell property)
    {
        property = machine.Dereference(property);
        if (property.Tag == CellTag.Reference)
        {
            return;
        }

        if (property.Tag == CellTag.Atom)
        {
            string name = machine.Symbols.AtomName(property.Index);
            if (name is "static" or "dynamic" or "public" or "private" or "built_in" or "multifile" or "exported")
            {
                return;
            }

            throw PrologErrors.Domain(machine, "predicate_property", property);
        }

        if (property.Tag == CellTag.Structure)
        {
            Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(property.Index).Index);
            string name = machine.Symbols.AtomName(functor.NameAtom);
            if (functor.Arity == 1 && name is "metapredicate" or "imported_from" or "defined_in")
            {
                return;
            }
        }

        throw PrologErrors.Domain(machine, "predicate_property", property);
    }

    private static Cell Atom(Machine machine, string name) => Cell.Atom(machine.Symbols.InternAtom(name));

    private sealed record ResolvedProcedure(
        ModulePredicateIndicator Indicator,
        string DefiningModule,
        bool Imported,
        string? ImportedFrom,
        ModulePredicateDefinition? Metadata,
        int FunctorId,
        bool Builtin,
        bool Defined
    );
}
