namespace DotProlog.Runtime;

/// <summary>
/// The predicates that change the clause database at run time, plus runtime <c>consult/1</c>.
/// </summary>
/// <remarks>
/// Compiling a term or a file needs the compiler, which the runtime does not reference. Both reach it
/// through <see cref="BytecodeProgram.RuntimeCompiler"/>, so the dependency points the right way and
/// nothing here needs reflection or a JIT — the whole path stays valid under NativeAOT.
/// </remarks>
internal static class DatabaseBuiltins
{
    internal static void Register(BuiltinRegistry registry)
    {
        registry.Register("assertz", 1, static machine => Assert(machine, atEnd: true));
        registry.Register("assert", 1, static machine => Assert(machine, atEnd: true));
        registry.Register("asserta", 1, static machine => Assert(machine, atEnd: false));
        // Nondeterministic: on redo, both resume the clause list where they left off. The generation
        // is snapshotted on the first call and carried through every redo, so the whole enumeration
        // sees exactly the clauses that existed when it started — the logical update view.
        registry.RegisterNondeterministic(
            "retract",
            1,
            static machine => Retract(machine, machine.Program.Generation, resumed: false),
            static (machine, generation) => Retract(machine, (int)generation, resumed: true)
        );
        registry.RegisterNondeterministic(
            "clause",
            2,
            static machine => Clause(machine, machine.Program.Generation, resumed: false),
            static (machine, state) =>
                state < 0 ? StaticModuleClause(machine, checked((int)(-state - 1))) : Clause(machine, (int)state, resumed: true)
        );
        registry.RegisterNondeterministic(
            "current_predicate",
            1,
            static machine => CurrentPredicate(machine, 0),
            CurrentPredicate
        );
        registry.Register("retractall", 1, RetractAll);
        registry.Register("abolish", 1, Abolish);
        registry.Register("$assertz", 2, static machine => ContextualAssert(machine, atEnd: true));
        registry.Register("$asserta", 2, static machine => ContextualAssert(machine, atEnd: false));
        registry.RegisterNondeterministic(
            "$retract",
            2,
            static machine => ContextualRetract(machine, machine.Program.Generation),
            static (machine, generation) => Retract(machine, (int)generation, resumed: true)
        );
        registry.RegisterNondeterministic(
            "$clause",
            3,
            static machine => ContextualClause(machine, machine.Program.Generation),
            static (machine, state) =>
                state < 0 ? StaticModuleClause(machine, checked((int)(-state - 1))) : Clause(machine, (int)state, resumed: true)
        );
        registry.Register("$abolish", 2, ContextualAbolish);

        registry.Register(
            "consult",
            1,
            static machine =>
            {
                Consult(machine, machine.Argument(0));
                return true;
            }
        );

        registry.Register(
            "ensure_loaded",
            1,
            static machine =>
            {
                EnsureLoaded(machine, machine.Argument(0));
                return true;
            }
        );
    }

    private static bool Assert(Machine machine, bool atEnd)
    {
        var clause = machine.Argument(0);
        if (clause.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        clause = QualifyClause(machine, "user", clause, "modify");
        ValidateAssertionBody(machine, clause);

        // Copied before compiling, so a cyclic clause is rejected here rather than looping the
        // compiler's term traversal.
        var normalized = NormalizeClause(machine, clause);
        var term = new TermBuffer();
        var root = term.Copy(machine, normalized);

        DynamicCodeBlock storage = default;
        try
        {
            var compiler = CompilerOf(machine);
            int functorId;
            var address =
                machine.Program.ReuseDynamicCode && compiler is IReclaimableClauseCompiler ownedCompiler
                    ? ownedCompiler.CompileDynamicClause(machine, clause, out functorId, out storage)
                    : compiler.CompileClause(machine, clause, out functorId);
            RequireModifiable(machine, functorId);
            var predicate = machine.Program.DeclareDynamic(functorId);

            var entry = new DynamicClause
            {
                CodeAddress = address,
                CodeStorage = storage,
                Term = term,
                TermRoot = root,
                Birth = machine.Program.NextGeneration(),
                IndexKey = ClauseIndexing.ClauseKeyFromHeap(
                    machine,
                    normalized,
                    machine.Symbols.InternFunctor(":-", 2),
                    functorId
                ),
            };

            if (atEnd)
            {
                predicate.Append(entry);
            }
            else
            {
                predicate.Prepend(entry);
            }

            storage = default;
            RecordDynamic(machine, functorId);
            return true;
        }
        finally
        {
            machine.Program.ReleaseDynamicCode(storage);
        }
    }

    private static bool ContextualAssert(Machine machine, bool atEnd)
    {
        var module = Context(machine, 0);
        var qualified = QualifyClause(machine, module, machine.Argument(1), "modify");
        machine.SetArgument(0, qualified);
        return Assert(machine, atEnd);
    }

    private static bool ContextualRetract(Machine machine, int generation)
    {
        var module = Context(machine, 0);
        machine.SetArgument(0, QualifyClause(machine, module, machine.Argument(1), "modify"));
        return Retract(machine, generation, resumed: false);
    }

    private static bool ContextualClause(Machine machine, int generation)
    {
        var module = Context(machine, 0);
        var head = QualifyHead(machine, module, machine.Argument(1), "access");
        machine.SetArgument(0, head);
        machine.SetArgument(1, machine.Argument(2));
        if (TryStaticModulePredicate(machine, head, out var predicate))
        {
            return StaticModuleClause(machine, predicate!, 0);
        }

        return Clause(machine, generation, resumed: false);
    }

    private static bool StaticModuleClause(Machine machine, int index) =>
        TryStaticModulePredicate(machine, machine.Argument(0), out var predicate)
        && StaticModuleClause(machine, predicate!, index);

    private static bool StaticModuleClause(Machine machine, ModulePredicateDefinition predicate, int index)
    {
        var body = machine.Argument(1);
        if (body.Tag is not CellTag.Reference and not CellTag.Atom and not CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "callable", body);
        }

        var pattern = machine.CreateStructure(RuleFunctor(machine), [machine.Argument(0), body]);
        var clauses = predicate.StaticClauses;
        for (; index < clauses.Count; index++)
        {
            var clause = clauses[index];
            var candidate = machine.HeapAt(clause.Term.Materialize(machine) + clause.Root);
            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (index + 1 < clauses.Count)
            {
                machine.PushRetry(-(index + 2L));
            }

            return machine.Unify(pattern, candidate);
        }

        return false;
    }

    private static bool TryStaticModulePredicate(Machine machine, Cell head, out ModulePredicateDefinition? predicate)
    {
        predicate = null;
        var functorId = FunctorOf(machine, head);
        if (machine.Program.IsDynamic(functorId))
        {
            predicate = null;
            return false;
        }

        var functor = machine.Symbols.GetFunctor(functorId);
        var compiledName = machine.Symbols.AtomName(functor.NameAtom);
        var separator = compiledName.LastIndexOf(':');
        var module = separator > 0 ? compiledName[..separator] : "user";
        var name = separator > 0 ? compiledName[(separator + 1)..] : compiledName;
        return machine.Program.Modules.TryGet(module, out var definition)
            && definition!.InterfacePrepared
            && definition.TryPredicate(new ModulePredicateIndicator(name, functor.Arity), out predicate)
            && predicate!.Defined;
    }

    private static bool ContextualAbolish(Machine machine)
    {
        var module = Context(machine, 0);
        machine.SetArgument(0, QualifyIndicator(machine, module, machine.Argument(1), "modify"));
        return Abolish(machine);
    }

    private static string Context(Machine machine, int argument)
    {
        var module = machine.Argument(argument);
        if (module.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (module.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", module);
        }

        var name = machine.Symbols.AtomName(module.Index);
        if (!machine.Program.Modules.Contains(name))
        {
            throw PrologErrors.Existence(machine, "module", module);
        }

        return name;
    }

    private static Cell QualifyClause(Machine machine, string context, Cell clause, string operation)
    {
        clause = Unqualify(machine, clause, ref context);
        var rule = machine.Symbols.InternFunctor(":-", 2);
        if (clause.Tag == CellTag.Structure && machine.HeapAt(clause.Index).Index == rule)
        {
            var head = machine.Dereference(machine.HeapAt(clause.Index + 1));
            var body = machine.HeapAt(clause.Index + 2);
            return machine.CreateStructure(rule, [QualifyHead(machine, context, head, operation), body]);
        }

        return QualifyHead(machine, context, clause, operation);
    }

    private static Cell QualifyHead(Machine machine, string context, Cell head, string operation)
    {
        head = Unqualify(machine, head, ref context);
        if (head.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        string name;
        int arity;
        Cell[] arguments;
        if (head.Tag == CellTag.Atom)
        {
            name = machine.Symbols.AtomName(head.Index);
            arity = 0;
            arguments = [];
        }
        else if (head.Tag == CellTag.Structure)
        {
            var functor = machine.Symbols.GetFunctor(machine.HeapAt(head.Index).Index);
            name = machine.Symbols.AtomName(functor.NameAtom);
            arity = functor.Arity;
            arguments = new Cell[arity];
            for (var index = 0; index < arity; index++)
            {
                arguments[index] = machine.HeapAt(head.Index + index + 1);
            }
        }
        else
        {
            throw PrologErrors.Type(machine, "callable", head);
        }

        var indicator = new ModulePredicateIndicator(name, arity);
        var definition = machine.Program.Modules.Declare(context);
        var locallyDefined =
            definition.TryPredicate(indicator, out var local)
            && local! is { Defined: true } or { Dynamic: true } or { Multifile: true };
        if (definition.InterfacePrepared && !locallyDefined && definition.Imports.ContainsKey(indicator))
        {
            throw PrologErrors.Permission(machine, operation, "implicit", PredicateIndicator(machine, indicator));
        }

        var qualifiedName = context == "user" ? name : $"{context}:{name}";
        if (arity == 0)
        {
            return Cell.Atom(machine.Symbols.InternAtom(qualifiedName));
        }

        return machine.CreateStructure(machine.Symbols.InternFunctor(qualifiedName, arity), arguments);
    }

    private static Cell QualifyIndicator(Machine machine, string context, Cell value, string operation)
    {
        value = Unqualify(machine, value, ref context);
        ValidatePredicateIndicator(machine, value, variablesAllowed: false, out var name, out var arity);
        var indicator = new ModulePredicateIndicator(machine.Symbols.AtomName(name.Index), (int)arity.Integer);
        var definition = machine.Program.Modules.Declare(context);
        var locallyDefined =
            definition.TryPredicate(indicator, out var local)
            && local! is { Defined: true } or { Dynamic: true } or { Multifile: true };
        if (definition.InterfacePrepared && !locallyDefined && definition.Imports.ContainsKey(indicator))
        {
            throw PrologErrors.Permission(machine, operation, "implicit", value);
        }

        var qualifiedName = context == "user" ? indicator.Name : $"{context}:{indicator.Name}";
        return machine.CreateStructure(
            machine.Symbols.InternFunctor("/", 2),
            [Cell.Atom(machine.Symbols.InternAtom(qualifiedName)), arity]
        );
    }

    private static Cell Unqualify(Machine machine, Cell value, ref string context)
    {
        value = machine.Dereference(value);
        var colon = machine.Symbols.InternFunctor(":", 2);
        while (value.Tag == CellTag.Structure && machine.HeapAt(value.Index).Index == colon)
        {
            var module = machine.Dereference(machine.HeapAt(value.Index + 1));
            if (module.Tag == CellTag.Reference)
            {
                throw PrologErrors.Instantiation(machine);
            }

            if (module.Tag != CellTag.Atom)
            {
                throw PrologErrors.Type(machine, "atom", module);
            }

            context = machine.Symbols.AtomName(module.Index);
            if (!machine.Program.Modules.Contains(context))
            {
                throw PrologErrors.Existence(machine, "module", module);
            }

            value = machine.Dereference(machine.HeapAt(value.Index + 2));
        }

        return value;
    }

    private static Cell PredicateIndicator(Machine machine, ModulePredicateIndicator indicator) =>
        machine.CreateStructure(
            machine.Symbols.InternFunctor("/", 2),
            [Cell.Atom(machine.Symbols.InternAtom(indicator.Name)), Cell.Integer60(indicator.Arity)]
        );

    private static void RecordDynamic(Machine machine, int functorId)
    {
        var functor = machine.Symbols.GetFunctor(functorId);
        var compiledName = machine.Symbols.AtomName(functor.NameAtom);
        var separator = compiledName.LastIndexOf(':');
        var module = separator > 0 ? compiledName[..separator] : "user";
        var name = separator > 0 ? compiledName[(separator + 1)..] : compiledName;
        var metadata = machine.Program.Modules.Declare(module).Predicate(new ModulePredicateIndicator(name, functor.Arity));
        metadata.Defined = true;
        metadata.Dynamic = true;
    }

    /// <summary>
    /// Retracts a clause unifying with the argument, and on redo retracts a further one, as ISO
    /// requires. <paramref name="generation"/> fixes which clauses the whole call can see.
    /// </summary>
    private static bool Retract(Machine machine, int generation, bool resumed)
    {
        var pattern = machine.Argument(0);
        if (pattern.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (!resumed)
        {
            pattern = QualifyClause(machine, "user", pattern, "modify");
            machine.SetArgument(0, pattern);
        }

        var normalized = NormalizeClausePattern(machine, pattern);
        var functorId = FunctorOf(machine, normalized);
        RequireModifiable(machine, functorId);
        var predicate = FindPredicate(machine, normalized, declareIfMissing: false);
        if (predicate is null)
        {
            return false;
        }

        var from = resumed ? machine.BuiltinCursor : predicate.First;
        return MatchClause(machine, normalized, from, generation, erase: true);
    }

    /// <summary>
    /// <c>clause(Head, Body)</c>: enumerates visible clauses without removing them.
    /// </summary>
    private static bool Clause(Machine machine, int generation, bool resumed)
    {
        var head = machine.Argument(0);
        if (head.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (!resumed)
        {
            head = QualifyHead(machine, "user", head, "access");
            machine.SetArgument(0, head);
            if (TryStaticModulePredicate(machine, head, out var staticPredicate))
            {
                return StaticModuleClause(machine, staticPredicate!, 0);
            }
        }

        var functorId = FunctorOf(machine, head);
        RequireClauseAccessible(machine, functorId);

        var body = machine.Argument(1);
        if (body.Tag is not CellTag.Reference and not CellTag.Atom and not CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "callable", body);
        }

        var predicate = FindPredicate(machine, head, declareIfMissing: false);
        if (predicate is null)
        {
            return false;
        }

        // Clauses are stored as ':-'(Head, Body), so match the caller's pair against the whole term.
        var pattern = machine.CreateStructure(RuleFunctor(machine), [head, body]);
        var from = resumed ? machine.BuiltinCursor : predicate.First;
        return MatchClause(machine, pattern, from, generation, erase: false);
    }

    /// <summary>
    /// Finds the first clause at or after <paramref name="from"/> visible at
    /// <paramref name="generation"/> that unifies with <paramref name="pattern"/>, and offers the
    /// rest on backtracking.
    /// </summary>
    /// <remarks>
    /// The choice point is pushed <em>before</em> the binding unification, not after. A choice point
    /// records the trail as it stands when it is created, so bindings made before it exists are never
    /// undone — the next solution would then be matched against an already-bound pattern and fail.
    /// The match is therefore tried first with <see cref="Machine.CanUnify"/>, which leaves nothing
    /// behind, and only repeated for real once the choice point is in place.
    /// </remarks>
    private static bool MatchClause(Machine machine, Cell pattern, DynamicClause? from, int generation, bool erase)
    {
        for (var clause = from; clause is not null; clause = clause.Next)
        {
            if (!clause.IsVisibleAt(generation))
            {
                continue;
            }

            var candidate = machine.HeapAt(clause.Term.Materialize(machine) + clause.TermRoot);
            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (DynamicPredicate.FirstVisible(clause.Next, generation) is DynamicClause next)
            {
                machine.PushRetry(generation, next);
            }

            machine.Unify(pattern, candidate);

            if (erase && clause.Death == int.MaxValue)
            {
                clause.Death = machine.Program.NextGeneration();
                machine.Program.NoteRetraction();
            }

            return true;
        }

        return false;
    }

    private static bool RetractAll(Machine machine)
    {
        var head = machine.Argument(0);
        if (head.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        head = QualifyHead(machine, "user", head, "modify");
        machine.SetArgument(0, head);

        // retractall/1 leaves the predicate defined but empty, even if it did not exist before.
        var predicate = FindPredicate(machine, head, declareIfMissing: true)!;
        var generation = machine.Program.Generation;

        for (var clause = predicate.First; clause is not null; clause = clause.Next)
        {
            if (!clause.IsVisibleAt(generation))
            {
                continue;
            }

            var candidate = machine.HeapAt(clause.Term.Materialize(machine) + clause.TermRoot);
            var candidateHead = machine.HeapAt(candidate.Index + 1);

            if (machine.CanUnify(head, candidateHead))
            {
                clause.Death = machine.Program.NextGeneration();
                machine.Program.NoteRetraction();
            }
        }

        return true;
    }

    /// <summary>
    /// <c>current_predicate(?Name/Arity)</c>: enumerates currently defined user procedures in stable
    /// functor-identifier order. Native built-ins, bundled library predicates, and internal names
    /// are deliberately absent for strict ISO behavior.
    /// </summary>
    private static bool CurrentPredicate(Machine machine, long state)
    {
        var indicator = machine.Argument(0);
        ValidatePredicateIndicator(machine, indicator, variablesAllowed: true, out _, out _);

        var count = machine.Symbols.FunctorCount;
        var slash = machine.Symbols.InternFunctor("/", 2);

        for (var functorId = (int)state; functorId < count; functorId++)
        {
            if (!machine.Program.IsUserPredicate(functorId))
            {
                continue;
            }

            var functor = machine.Symbols.GetFunctor(functorId);
            var name = machine.Symbols.AtomName(functor.NameAtom);
            if (name.StartsWith('$'))
            {
                continue;
            }

            var candidate = machine.CreateStructure(slash, [Cell.Atom(functor.NameAtom), Cell.Integer60(functor.Arity)]);

            if (!machine.CanUnify(indicator, candidate))
            {
                continue;
            }

            if (functorId + 1 < count)
            {
                machine.PushRetry(functorId + 1);
            }

            return machine.Unify(indicator, candidate);
        }

        return false;
    }

    private static bool Abolish(Machine machine)
    {
        var indicator = QualifyIndicator(machine, "user", machine.Argument(0), "modify");
        machine.SetArgument(0, indicator);
        ValidatePredicateIndicator(machine, indicator, variablesAllowed: false, out var name, out var arity);

        var functorId = machine.Symbols.InternFunctor(name.Index, (int)arity.Integer);
        RequireModifiable(machine, functorId);
        machine.Program.AbolishDynamic(functorId);
        return true;
    }

    private static void ValidatePredicateIndicator(
        Machine machine,
        Cell indicator,
        bool variablesAllowed,
        out Cell name,
        out Cell arity
    )
    {
        if (indicator.Tag == CellTag.Reference)
        {
            if (!variablesAllowed)
            {
                throw PrologErrors.Instantiation(machine);
            }

            name = indicator;
            arity = indicator;
            return;
        }

        var slash = machine.Symbols.InternFunctor("/", 2);
        if (indicator.Tag != CellTag.Structure || machine.HeapAt(indicator.Index).Index != slash)
        {
            throw PrologErrors.Type(machine, "predicate_indicator", indicator);
        }

        name = machine.Dereference(machine.HeapAt(indicator.Index + 1));
        arity = machine.Dereference(machine.HeapAt(indicator.Index + 2));

        if (arity.Tag == CellTag.Reference)
        {
            if (!variablesAllowed)
            {
                throw PrologErrors.Instantiation(machine);
            }
        }
        else if (arity.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", arity);
        }
        else if (arity.Integer < 0)
        {
            throw PrologErrors.Domain(machine, "not_less_than_zero", arity);
        }
        else if (arity.Integer >= Machine.ArgumentRegisterCount)
        {
            throw PrologErrors.Representation(machine, "max_arity");
        }

        if (name.Tag == CellTag.Reference)
        {
            if (!variablesAllowed)
            {
                throw PrologErrors.Instantiation(machine);
            }
        }
        else if (name.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", name);
        }
    }

    private static void Consult(Machine machine, Cell path)
    {
        CompilerOf(machine).ConsultFile(machine, SourcePath(machine, path));
    }

    private static void EnsureLoaded(Machine machine, Cell path) =>
        CompilerOf(machine).EnsureLoadedFile(machine, SourcePath(machine, path));

    private static string SourcePath(Machine machine, Cell path)
    {
        if (path.Tag != CellTag.Atom)
        {
            throw path.Tag == CellTag.Reference ? PrologErrors.Instantiation(machine) : PrologErrors.Type(machine, "atom", path);
        }

        return machine.Symbols.AtomName(path.Index);
    }

    private static IRuntimeCompiler CompilerOf(Machine machine) =>
        machine.Program.RuntimeCompiler
        ?? throw new PrologException("permission_error(modify, database, no_runtime_compiler_installed)");

    private static void ValidateAssertionBody(Machine machine, Cell clause)
    {
        if (clause.Tag != CellTag.Structure || machine.HeapAt(clause.Index).Index != RuleFunctor(machine))
        {
            return;
        }

        var body = machine.Dereference(machine.HeapAt(clause.Index + 2));
        List<(Cell Goal, Cell Whole)> pending = [(body, body)];
        HashSet<int> visited = [];
        while (pending.Count > 0)
        {
            var index = pending.Count - 1;
            (var goal, var whole) = pending[index];
            pending.RemoveAt(index);
            goal = machine.Dereference(goal);
            if (goal.Tag is CellTag.Reference or CellTag.Atom)
            {
                continue;
            }

            if (goal.Tag != CellTag.Structure)
            {
                throw PrologErrors.Type(machine, "callable", whole);
            }

            var functor = machine.Symbols.GetFunctor(machine.HeapAt(goal.Index).Index);
            var name = machine.Symbols.AtomName(functor.NameAtom);
            var binary = functor.Arity == 2 && name is "," or ";" or "->" or "*->";
            var unary = functor.Arity == 1 && name == "\\+";
            if ((!binary && !unary) || !visited.Add(goal.Index))
            {
                continue;
            }

            if (binary)
            {
                pending.Add((machine.HeapAt(goal.Index + 2), whole));
                pending.Add((machine.HeapAt(goal.Index + 1), whole));
            }
            else
            {
                var inner = machine.Dereference(machine.HeapAt(goal.Index + 1));
                pending.Add((inner, inner));
            }
        }
    }

    /// <summary>
    /// Rewrites a clause into its ISO database form: facts acquire a <c>true</c> body and variables
    /// used directly as goals become <c>call(Variable)</c>.
    /// </summary>
    internal static Cell NormalizeClause(Machine machine, Cell clause)
    {
        if (clause.Tag != CellTag.Structure || machine.HeapAt(clause.Index).Index != RuleFunctor(machine))
        {
            return machine.CreateStructure(RuleFunctor(machine), [clause, Cell.Atom(machine.Symbols.True)]);
        }

        var head = machine.HeapAt(clause.Index + 1);
        var body = CanonicalizeGoal(machine, machine.HeapAt(clause.Index + 2));
        return machine.CreateStructure(RuleFunctor(machine), [head, body]);
    }

    /// <summary>
    /// Gives a fact pattern its implicit <c>true</c> body while preserving variables in an explicit
    /// rule body so they can match and receive the canonical stored goal.
    /// </summary>
    private static Cell NormalizeClausePattern(Machine machine, Cell clause)
    {
        if (clause.Tag == CellTag.Structure && machine.HeapAt(clause.Index).Index == RuleFunctor(machine))
        {
            return clause;
        }

        return machine.CreateStructure(RuleFunctor(machine), [clause, Cell.Atom(machine.Symbols.True)]);
    }

    private static int RuleFunctor(Machine machine) => machine.Symbols.InternFunctor(":-", 2);

    /// <summary>
    /// Canonicalizes variable goals without CLR recursion. Only arguments that are themselves goal
    /// positions in an ISO control construct are traversed; a variable passed to an ordinary
    /// predicate remains an ordinary argument.
    /// </summary>
    private static Cell CanonicalizeGoal(Machine machine, Cell goal)
    {
        goal = machine.Dereference(goal);
        var call = machine.Symbols.InternFunctor("call", 1);
        if (goal.Tag == CellTag.Reference)
        {
            return machine.CreateStructure(call, [goal]);
        }

        if (goal.Tag != CellTag.Structure || !IsControlConstruct(machine, goal))
        {
            return goal;
        }

        var rewritten = new Dictionary<int, Cell>();
        var active = new HashSet<int>();
        var complete = new HashSet<int>();
        List<(Cell Goal, bool Leaving)> work = [(goal, false)];

        while (work.Count > 0)
        {
            (var current, var leaving) = work[^1];
            work.RemoveAt(work.Count - 1);
            current = machine.Dereference(current);

            if (current.Tag != CellTag.Structure || !IsControlConstruct(machine, current))
            {
                continue;
            }

            if (leaving)
            {
                active.Remove(current.Index);
                if (!complete.Add(current.Index))
                {
                    continue;
                }

                var functor = machine.HeapAt(current.Index).Index;
                var arity = machine.Symbols.ArityOf(functor);
                var arguments = new Cell[arity];
                for (var i = 0; i < arity; i++)
                {
                    var argument = machine.Dereference(machine.HeapAt(current.Index + i + 1));
                    arguments[i] =
                        argument.Tag == CellTag.Reference ? machine.CreateStructure(call, [argument])
                        : argument.Tag == CellTag.Structure && rewritten.TryGetValue(argument.Index, out var replacement)
                            ? replacement
                        : argument;
                }

                rewritten.Add(current.Index, machine.CreateStructure(functor, arguments));
                continue;
            }

            if (complete.Contains(current.Index))
            {
                continue;
            }

            // Rational control terms are outside ISO's finite source-term model. Preserve one
            // already-seen edge rather than recursing forever or manufacturing a different cycle.
            if (!active.Add(current.Index))
            {
                continue;
            }

            work.Add((current, true));
            var childCount = machine.Symbols.ArityOf(machine.HeapAt(current.Index).Index);
            for (var i = childCount; i >= 1; i--)
            {
                var child = machine.Dereference(machine.HeapAt(current.Index + i));
                if (child.Tag == CellTag.Structure && IsControlConstruct(machine, child))
                {
                    work.Add((child, false));
                }
            }
        }

        return rewritten.TryGetValue(goal.Index, out var canonical) ? canonical : goal;
    }

    private static bool IsControlConstruct(Machine machine, Cell goal)
    {
        var functorId = machine.HeapAt(goal.Index).Index;
        var functor = machine.Symbols.GetFunctor(functorId);
        var name = machine.Symbols.AtomName(functor.NameAtom);
        return (functor.Arity == 2 && name is "," or ";" or "->" or "*->") || (functor.Arity == 1 && name == "\\+");
    }

    private static int FunctorOf(Machine machine, Cell clauseOrHead)
    {
        var head = clauseOrHead;
        if (head.Tag == CellTag.Structure && machine.HeapAt(head.Index).Index == RuleFunctor(machine))
        {
            head = machine.Dereference(machine.HeapAt(head.Index + 1));
        }

        return head.Tag switch
        {
            CellTag.Atom => machine.Symbols.InternFunctor(head.Index, 0),
            CellTag.Structure => machine.HeapAt(head.Index).Index,
            CellTag.Reference => throw PrologErrors.Instantiation(machine),
            _ => throw PrologErrors.Type(machine, "callable", head),
        };
    }

    private static DynamicPredicate? FindPredicate(Machine machine, Cell clauseOrHead, bool declareIfMissing)
    {
        var functorId = FunctorOf(machine, clauseOrHead);
        if (!declareIfMissing)
        {
            return machine.Program.FindDynamic(functorId);
        }

        RequireModifiable(machine, functorId);
        return machine.Program.DeclareDynamic(functorId);
    }

    /// <summary>Rejects clause inspection of static and built-in procedures, which are private.</summary>
    private static void RequireClauseAccessible(Machine machine, int functorId)
    {
        if (
            IsControlProcedure(machine, functorId)
            || (
                !machine.Program.IsDynamic(functorId)
                && (machine.Program.IsDefined(functorId) || machine.Program.Builtins.TryGetId(functorId, out _))
            )
        )
        {
            throw PrologErrors.Permission(machine, "access", "private_procedure", functorId);
        }
    }

    /// <summary>Rejects an attempt to change a static or control procedure.</summary>
    private static void RequireModifiable(Machine machine, int functorId)
    {
        if (
            IsControlProcedure(machine, functorId)
            || (
                !machine.Program.IsDynamic(functorId)
                && (machine.Program.IsDefined(functorId) || machine.Program.Builtins.TryGetId(functorId, out _))
            )
        )
        {
            throw PrologErrors.Permission(machine, "modify", "static_procedure", functorId);
        }
    }

    private static bool IsControlProcedure(Machine machine, int functorId)
    {
        var functor = machine.Symbols.GetFunctor(functorId);
        var name = machine.Symbols.AtomName(functor.NameAtom);
        return (functor.Arity == 0 && name == "!")
            || (functor.Arity == 1 && name is "call" or "\\+")
            || (functor.Arity == 2 && name is "," or ";" or "->" or "*->");
    }
}
