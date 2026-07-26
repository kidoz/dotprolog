namespace Prolog.Runtime;

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
        int root = term.Copy(machine, Normalize(machine, clause));

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

        Cell normalized = Normalize(machine, pattern);
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

        DynamicPredicate? predicate = FindPredicate(machine, head, declareIfMissing: false);
        if (predicate is null)
        {
            return false;
        }

        // Clauses are stored as ':-'(Head, Body), so match the caller's pair against the whole term.
        Cell pattern = machine.CreateStructure(RuleFunctor(machine), [head, machine.Argument(1)]);
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

    private static bool Abolish(Machine machine)
    {
        Cell indicator = machine.Argument(0);
        if (indicator.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "predicate_indicator", indicator);
        }

        Cell name = machine.Dereference(machine.HeapAt(indicator.Index + 1));
        Cell arity = machine.Dereference(machine.HeapAt(indicator.Index + 2));
        if (name.Tag != CellTag.Atom || arity.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "predicate_indicator", indicator);
        }

        int functorId = machine.Symbols.InternFunctor(name.Index, (int)arity.Integer);
        DynamicPredicate? predicate = machine.Program.FindDynamic(functorId);
        if (predicate is null)
        {
            return true;
        }

        int generation = machine.Program.NextGeneration();
        for (DynamicClause? clause = predicate.First; clause is not null; clause = clause.Next)
        {
            if (clause.Death == int.MaxValue)
            {
                clause.Death = generation;
            }
        }

        return true;
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

    /// <summary>Rewrites a bare head into the <c>Head :- true</c> form clauses are stored in.</summary>
    private static Cell Normalize(Machine machine, Cell clause)
    {
        if (clause.Tag == CellTag.Structure && machine.HeapAt(clause.Index).Index == RuleFunctor(machine))
        {
            return clause;
        }

        return machine.CreateStructure(RuleFunctor(machine), [clause, Cell.Atom(machine.Symbols.True)]);
    }

    private static int RuleFunctor(Machine machine) => machine.Symbols.InternFunctor(":-", 2);

    private static DynamicPredicate? FindPredicate(Machine machine, Cell clauseOrHead, bool declareIfMissing)
    {
        Cell head = clauseOrHead;
        if (head.Tag == CellTag.Structure && machine.HeapAt(head.Index).Index == RuleFunctor(machine))
        {
            head = machine.Dereference(machine.HeapAt(head.Index + 1));
        }

        int functorId = head.Tag switch
        {
            CellTag.Atom => machine.Symbols.InternFunctor(head.Index, 0),
            CellTag.Structure => machine.HeapAt(head.Index).Index,
            CellTag.Reference => throw PrologErrors.Instantiation(machine),
            _ => throw PrologErrors.Type(machine, "callable", head),
        };

        if (!declareIfMissing)
        {
            return machine.Program.FindDynamic(functorId);
        }

        RequireModifiable(machine, functorId);
        return machine.Program.DeclareDynamic(functorId);
    }

    /// <summary>Rejects an attempt to change a predicate that was compiled as static.</summary>
    private static void RequireModifiable(Machine machine, int functorId)
    {
        if (!machine.Program.IsDynamic(functorId) && machine.Program.IsDefined(functorId))
        {
            throw PrologErrors.Permission(machine, "modify", "static_procedure", functorId);
        }
    }
}
