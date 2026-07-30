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
        // Nondeterministic: on redo, both resume the clause list where they left off.
        registry.RegisterNondeterministic("retract", 1, static machine => Retract(machine, 0), Retract);
        registry.RegisterNondeterministic("clause", 2, static machine => Clause(machine, 0), Clause);
        registry.RegisterNondeterministic(
            "current_predicate",
            1,
            static machine => CurrentPredicate(machine, 0),
            CurrentPredicate
        );
        registry.Register("retractall", 1, RetractAll);
        registry.Register("abolish", 1, Abolish);

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
                Consult(machine, machine.Argument(0));
                return true;
            }
        );
    }

    private static bool Assert(Machine machine, bool atEnd)
    {
        Cell clause = machine.Argument(0);
        if (clause.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        IRuntimeCompiler compiler = CompilerOf(machine);
        int address = compiler.CompileClause(machine, clause, out int functorId);
        RequireModifiable(machine, functorId);
        DynamicPredicate predicate = machine.Program.DeclareDynamic(functorId);

        var term = new TermBuffer();
        int root = term.Copy(machine, NormalizeClause(machine, clause));

        var entry = new DynamicClause
        {
            CodeAddress = address,
            Term = term,
            TermRoot = root,
            Birth = machine.Program.NextGeneration(),
        };

        if (atEnd)
        {
            predicate.Append(entry);
        }
        else
        {
            predicate.Prepend(entry);
        }

        return true;
    }

    /// <summary>
    /// Retracts a clause unifying with the argument, and on redo retracts a further one, as ISO
    /// requires. <paramref name="skip"/> is how many clauses of the predicate to pass over first.
    /// </summary>
    private static bool Retract(Machine machine, long skip)
    {
        Cell pattern = machine.Argument(0);
        if (pattern.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        Cell normalized = NormalizeClausePattern(machine, pattern);
        int functorId = FunctorOf(machine, normalized);
        RequireModifiable(machine, functorId);
        DynamicPredicate? predicate = FindPredicate(machine, normalized, declareIfMissing: false);
        if (predicate is null)
        {
            return false;
        }

        return MatchClause(machine, normalized, predicate, skip, erase: true);
    }

    /// <summary>
    /// <c>clause(Head, Body)</c>: enumerates the clauses of a dynamic predicate without removing them.
    /// </summary>
    private static bool Clause(Machine machine, long skip)
    {
        Cell head = machine.Argument(0);
        if (head.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        int functorId = FunctorOf(machine, head);
        RequireClauseAccessible(machine, functorId);

        Cell body = machine.Argument(1);
        if (body.Tag is not CellTag.Reference and not CellTag.Atom and not CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "callable", body);
        }

        DynamicPredicate? predicate = FindPredicate(machine, head, declareIfMissing: false);
        if (predicate is null)
        {
            return false;
        }

        // Clauses are stored as ':-'(Head, Body), so match the caller's pair against the whole term.
        Cell pattern = machine.CreateStructure(RuleFunctor(machine), [head, body]);
        return MatchClause(machine, pattern, predicate, skip, erase: false);
    }

    /// <summary>
    /// Finds the first clause at or after <paramref name="skip"/> that unifies with
    /// <paramref name="pattern"/>, and offers the rest on backtracking.
    /// </summary>
    /// <remarks>
    /// The choice point is pushed <em>before</em> the binding unification, not after. A choice point
    /// records the trail as it stands when it is created, so bindings made before it exists are never
    /// undone — the next solution would then be matched against an already-bound pattern and fail.
    /// The match is therefore tried first with <see cref="Machine.CanUnify"/>, which leaves nothing
    /// behind, and only repeated for real once the choice point is in place.
    /// </remarks>
    private static bool MatchClause(Machine machine, Cell pattern, DynamicPredicate predicate, long skip, bool erase)
    {
        int generation = machine.Program.Generation;
        long position = 0;

        for (DynamicClause? clause = predicate.First; clause is not null; clause = clause.Next, position++)
        {
            if (position < skip || !clause.IsVisibleAt(generation))
            {
                continue;
            }

            Cell candidate = machine.HeapAt(clause.Term.Materialize(machine) + clause.TermRoot);
            if (!machine.CanUnify(pattern, candidate))
            {
                continue;
            }

            if (clause.Next is not null)
            {
                machine.PushRetry(position + 1);
            }

            machine.Unify(pattern, candidate);

            if (erase)
            {
                clause.Death = machine.Program.NextGeneration();
            }

            return true;
        }

        return false;
    }

    private static bool RetractAll(Machine machine)
    {
        Cell head = machine.Argument(0);
        if (head.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        // retractall/1 leaves the predicate defined but empty, even if it did not exist before.
        DynamicPredicate predicate = FindPredicate(machine, head, declareIfMissing: true)!;
        int generation = machine.Program.Generation;

        for (DynamicClause? clause = predicate.First; clause is not null; clause = clause.Next)
        {
            if (!clause.IsVisibleAt(generation))
            {
                continue;
            }

            Cell candidate = machine.HeapAt(clause.Term.Materialize(machine) + clause.TermRoot);
            Cell candidateHead = machine.HeapAt(candidate.Index + 1);

            if (machine.CanUnify(head, candidateHead))
            {
                clause.Death = machine.Program.NextGeneration();
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
        Cell indicator = machine.Argument(0);
        ValidatePredicateIndicator(machine, indicator, variablesAllowed: true, out _, out _);

        int count = machine.Symbols.FunctorCount;
        int slash = machine.Symbols.InternFunctor("/", 2);

        for (int functorId = (int)state; functorId < count; functorId++)
        {
            if (!machine.Program.IsUserPredicate(functorId))
            {
                continue;
            }

            Functor functor = machine.Symbols.GetFunctor(functorId);
            string name = machine.Symbols.AtomName(functor.NameAtom);
            if (name.StartsWith('$'))
            {
                continue;
            }

            Cell candidate = machine.CreateStructure(slash, [Cell.Atom(functor.NameAtom), Cell.Integer60(functor.Arity)]);

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
        Cell indicator = machine.Argument(0);
        ValidatePredicateIndicator(machine, indicator, variablesAllowed: false, out Cell name, out Cell arity);

        int functorId = machine.Symbols.InternFunctor(name.Index, (int)arity.Integer);
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

        int slash = machine.Symbols.InternFunctor("/", 2);
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
        if (path.Tag != CellTag.Atom)
        {
            throw path.Tag == CellTag.Reference ? PrologErrors.Instantiation(machine) : PrologErrors.Type(machine, "atom", path);
        }

        CompilerOf(machine).ConsultFile(machine, machine.Symbols.AtomName(path.Index));
    }

    private static IRuntimeCompiler CompilerOf(Machine machine) =>
        machine.Program.RuntimeCompiler
        ?? throw new PrologException("permission_error(modify, database, no_runtime_compiler_installed)");

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

        Cell head = machine.HeapAt(clause.Index + 1);
        Cell body = CanonicalizeGoal(machine, machine.HeapAt(clause.Index + 2));
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
        int call = machine.Symbols.InternFunctor("call", 1);
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
            (Cell current, bool leaving) = work[^1];
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

                int functor = machine.HeapAt(current.Index).Index;
                int arity = machine.Symbols.ArityOf(functor);
                var arguments = new Cell[arity];
                for (int i = 0; i < arity; i++)
                {
                    Cell argument = machine.Dereference(machine.HeapAt(current.Index + i + 1));
                    arguments[i] =
                        argument.Tag == CellTag.Reference ? machine.CreateStructure(call, [argument])
                        : argument.Tag == CellTag.Structure && rewritten.TryGetValue(argument.Index, out Cell replacement)
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
            int childCount = machine.Symbols.ArityOf(machine.HeapAt(current.Index).Index);
            for (int i = childCount; i >= 1; i--)
            {
                Cell child = machine.Dereference(machine.HeapAt(current.Index + i));
                if (child.Tag == CellTag.Structure && IsControlConstruct(machine, child))
                {
                    work.Add((child, false));
                }
            }
        }

        return rewritten.TryGetValue(goal.Index, out Cell canonical) ? canonical : goal;
    }

    private static bool IsControlConstruct(Machine machine, Cell goal)
    {
        int functorId = machine.HeapAt(goal.Index).Index;
        Functor functor = machine.Symbols.GetFunctor(functorId);
        string name = machine.Symbols.AtomName(functor.NameAtom);
        return (functor.Arity == 2 && name is "," or ";" or "->" or "*->")
            || (functor.Arity == 1 && name == "\\+");
    }

    private static int FunctorOf(Machine machine, Cell clauseOrHead)
    {
        Cell head = clauseOrHead;
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
        int functorId = FunctorOf(machine, clauseOrHead);
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
            !machine.Program.IsDynamic(functorId)
            && (machine.Program.IsDefined(functorId) || machine.Program.Builtins.TryGetId(functorId, out _))
        )
        {
            throw PrologErrors.Permission(machine, "access", "private_procedure", functorId);
        }
    }

    /// <summary>Rejects an attempt to change a predicate that was compiled as static.</summary>
    private static void RequireModifiable(Machine machine, int functorId)
    {
        if (
            !machine.Program.IsDynamic(functorId)
            && (machine.Program.IsDefined(functorId) || machine.Program.Builtins.TryGetId(functorId, out _))
        )
        {
            throw PrologErrors.Permission(machine, "modify", "static_procedure", functorId);
        }
    }
}
