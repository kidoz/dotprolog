using System.Runtime.InteropServices;

namespace DotProlog.Runtime;

/// <summary>
/// Type tests, standard-order comparison, and term construction and inspection. Registered
/// explicitly by <see cref="CoreBuiltins.RegisterAll"/>, like every other native predicate.
/// </summary>
internal static class TermBuiltins
{
    internal static void Register(BuiltinRegistry registry, SymbolTable symbols)
    {
        RegisterTypeTests(registry);
        RegisterComparisons(registry, symbols);
        RegisterInspection(registry, symbols);
    }

    private static void RegisterTypeTests(BuiltinRegistry registry)
    {
        registry.Register("var", 1, static machine => machine.Argument(0).Tag == CellTag.Reference);
        registry.Register("nonvar", 1, static machine => machine.Argument(0).Tag != CellTag.Reference);
        registry.Register("atom", 1, static machine => machine.Argument(0).Tag == CellTag.Atom);
        registry.Register("integer", 1, static machine => machine.Argument(0).Tag is CellTag.Integer or CellTag.BigInteger);
        registry.Register("float", 1, static machine => machine.Argument(0).Tag == CellTag.Float);
        registry.Register(
            "number",
            1,
            static machine =>
                machine.Argument(0).Tag is CellTag.Integer or CellTag.BigInteger or CellTag.Rational or CellTag.Float
        );
        registry.Register(
            "rational",
            1,
            static machine => machine.Argument(0).Tag is CellTag.Integer or CellTag.BigInteger or CellTag.Rational
        );
        registry.Register("compound", 1, static machine => machine.Argument(0).Tag == CellTag.Structure);
        registry.Register(
            "atomic",
            1,
            static machine =>
                machine.Argument(0).Tag
                    is CellTag.Atom
                        or CellTag.Integer
                        or CellTag.BigInteger
                        or CellTag.Rational
                        or CellTag.Float
                        or CellTag.String
        );
        registry.Register("callable", 1, static machine => machine.Argument(0).Tag is CellTag.Atom or CellTag.Structure);
        registry.Register("string", 1, static machine => machine.Argument(0).Tag == CellTag.String);
        registry.Register("is_list", 1, static machine => TermList.IsProper(machine, machine.Argument(0)));
        registry.Register("$skip_list", 3, SkipList);
        registry.Register("$freeze", 2, Freeze);
        registry.Register("$frozen_goal", 2, FrozenGoal);
        registry.Register("ground", 1, static machine => IsGround(machine, machine.Argument(0)));
        registry.Register("acyclic_term", 1, static machine => IsAcyclic(machine, machine.Argument(0)));
    }

    /// <summary>
    /// <c>'$skip_list'(-Count, +List, -Tail)</c>, after SWI-Prolog's helper: the number of list cells
    /// and what they end in — <c>[]</c>, an unbound tail, or anything else. It fails for a cyclic list,
    /// which has neither, so <c>length/2</c> fails on one rather than looping.
    /// </summary>
    private static bool SkipList(Machine machine) =>
        TermList.TrySkip(machine, machine.Argument(1), out var count, out Cell tail)
        && machine.Unify(machine.Argument(0), Cell.Integer60(count))
        && machine.Unify(machine.Argument(2), tail);

    /// <summary>
    /// <c>'$freeze'(+Var, +Goal)</c>: freezes <c>Goal</c> on the unbound variable <c>Var</c>, after any
    /// goal already frozen there. <c>freeze/2</c> in the library calls a bound variable's goal at once.
    /// </summary>
    private static bool Freeze(Machine machine)
    {
        Cell variable = machine.Argument(0);
        if (variable.Tag != CellTag.Reference)
        {
            throw PrologErrors.Uninstantiation(machine, variable);
        }

        machine.Freeze(variable.Index, machine.Argument(1));
        return true;
    }

    /// <summary>
    /// <c>'$frozen_goal'(+Var, -Goal)</c>: the goal frozen on an unbound variable, several joined as
    /// <c>'$and'(Earlier, Later)</c>. Fails for a bound variable or one with nothing frozen on it.
    /// </summary>
    private static bool FrozenGoal(Machine machine)
    {
        Cell variable = machine.Argument(0);
        return variable.Tag == CellTag.Reference
            && machine.TryGetFrozen(variable.Index, out Cell goal)
            && machine.Unify(machine.Argument(1), goal);
    }

    private static void RegisterComparisons(BuiltinRegistry registry, SymbolTable symbols)
    {
        registry.Register("==", 2, static machine => Order(machine) == 0);
        registry.Register("\\==", 2, static machine => Order(machine) != 0);
        registry.Register("subsumes_term", 2, SubsumesTerm);
        registry.Register("@<", 2, static machine => Order(machine) < 0);
        registry.Register("@>", 2, static machine => Order(machine) > 0);
        registry.Register("@=<", 2, static machine => Order(machine) <= 0);
        registry.Register("@>=", 2, static machine => Order(machine) >= 0);
        registry.Register("\\=", 2, static machine => !machine.CanUnify(machine.Argument(0), machine.Argument(1)));
        registry.Register(
            "unify_with_occurs_check",
            2,
            static machine => machine.UnifyWithOccursCheck(machine.Argument(0), machine.Argument(1))
        );

        var less = symbols.InternAtom("<");
        var equal = symbols.InternAtom("=");
        var greater = symbols.InternAtom(">");
        registry.Register("compare", 3, machine => Compare(machine, less, equal, greater));
    }

    private static void RegisterInspection(BuiltinRegistry registry, SymbolTable symbols)
    {
        registry.Register("functor", 3, Functor3);
        registry.Register("arg", 3, Arg3);
        registry.Register("setarg", 3, static machine => SetArg(machine, backtrackable: true));
        registry.Register("nb_setarg", 3, static machine => SetArg(machine, backtrackable: false));
        registry.Register("copy_term", 2, CopyTerm);
        registry.Register("term_size", 2, TermSize);
        registry.Register("term_variables", 2, TermVariables);

        var emptyList = symbols.EmptyList;
        registry.Register("=..", 2, machine => Univ(machine, emptyList));
    }

    /// <summary>
    /// <c>copy_term(+Term, -Copy)</c>: the same term with every variable renamed apart.
    /// </summary>
    /// <remarks>
    /// The copy goes out through a <see cref="TermBuffer"/> and straight back onto the heap, which is
    /// the same machinery <c>findall/3</c> uses to carry a solution across backtracking. Sharing it
    /// means the two agree about what copying a term means.
    /// </remarks>
    private static bool CopyTerm(Machine machine)
    {
        Cell original = machine.Argument(0);
        Cell frozen = machine.HasFrozen ? FrozenPairs(machine, original) : Cell.Atom(machine.Symbols.EmptyList);
        if (TermList.IsEmpty(machine, frozen))
        {
            var buffer = new TermBuffer();
            var root = buffer.Copy(machine, original);
            var origin = buffer.Materialize(machine);
            return machine.Unify(machine.Argument(1), machine.HeapAt(origin + root));
        }

        // As in SWI-Prolog, the copy's variables carry copies of the goals frozen on the original's,
        // so the term and its goals are copied together and the copied goals frozen again.
        var pairFunctor = machine.Symbols.InternFunctor("-", 2);
        var together = new TermBuffer();
        var joined = together.Copy(machine, machine.CreateStructure(pairFunctor, [original, frozen]));
        var start = together.Materialize(machine);
        Cell copy = machine.HeapAt(start + joined);
        List<Cell> pairs = [];
        TermList.Read(machine, machine.HeapAt(copy.Index + 2), pairs);
        foreach (Cell pair in pairs)
        {
            Cell structure = machine.Dereference(pair);
            machine.Freeze(machine.Dereference(machine.HeapAt(structure.Index + 1)).Index, machine.HeapAt(structure.Index + 2));
        }

        return machine.Unify(machine.Argument(1), machine.HeapAt(copy.Index + 1));
    }

    /// <summary>The <c>Var-Goal</c> pairs of the frozen variables in <paramref name="term"/>, as a list.</summary>
    private static Cell FrozenPairs(Machine machine, Cell term)
    {
        var pairFunctor = machine.Symbols.InternFunctor("-", 2);
        List<Cell> pairs = [];
        foreach (var address in CollectVariables(machine, term))
        {
            if (machine.TryGetFrozen(address, out Cell goal))
            {
                pairs.Add(machine.CreateStructure(pairFunctor, [Cell.Reference(address), goal]));
            }
        }

        return TermList.Build(machine, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pairs));
    }

    /// <summary>
    /// <c>term_size(+Term, -Cells)</c>: the number of cells a detached copy of the term occupies —
    /// the same copy <c>findall/3</c> and <c>nb_setval/2</c> take, never materialized. A shared
    /// subterm therefore counts once per occurrence, and a cyclic term raises
    /// <c>representation_error(cyclic_term)</c>; both are documented in the SWI ledger.
    /// </summary>
    private static bool TermSize(Machine machine)
    {
        Cell size = machine.Argument(1);
        if (size.Tag is not (CellTag.Reference or CellTag.Integer or CellTag.BigInteger))
        {
            throw PrologErrors.Type(machine, "integer", size);
        }

        var buffer = new TermBuffer();
        buffer.Copy(machine, machine.Argument(0));
        return machine.Unify(size, Cell.Integer60(buffer.Count));
    }

    /// <summary>
    /// <c>term_variables(+Term, -Variables)</c>: the term's distinct unbound variables, in the order
    /// a left-to-right walk first reaches them.
    /// </summary>
    private static bool TermVariables(Machine machine)
    {
        Cell result = machine.Argument(1);
        List<Cell> existing = [];
        Cell resultTail = TermList.Read(machine, result, existing);
        if (!TermList.IsEmpty(machine, resultTail) && resultTail.Tag != CellTag.Reference)
        {
            throw PrologErrors.Type(machine, "list", result);
        }

        List<Cell> found = [];
        HashSet<int> seen = [];
        List<Cell> work = [machine.Argument(0)];

        while (work.Count > 0)
        {
            Cell cell = machine.Dereference(work[^1]);
            work.RemoveAt(work.Count - 1);

            if (cell.Tag == CellTag.Reference)
            {
                if (seen.Add(cell.Index))
                {
                    found.Add(cell);
                }

                continue;
            }

            if (cell.Tag != CellTag.Structure)
            {
                continue;
            }

            // Pushed in reverse so the leftmost argument is walked first.
            var arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
            for (var i = arity; i >= 1; i--)
            {
                work.Add(machine.HeapAt(cell.Index + i));
            }
        }

        return machine.Unify(result, TermList.Build(machine, CollectionsMarshal.AsSpan(found)));
    }

    private static int Order(Machine machine) => TermOrder.Compare(machine, machine.Argument(0), machine.Argument(1));

    /// <summary>
    /// Tests whether the first term can be instantiated to the second without binding either term.
    /// Variables reachable from the specific term are rigid, including variables shared by both
    /// arguments; variables found only in the general term may acquire temporary conceptual
    /// substitutions.
    /// </summary>
    private static bool SubsumesTerm(Machine machine)
    {
        Cell general = machine.Argument(0);
        Cell specific = machine.Argument(1);
        HashSet<int> rigidVariables = CollectVariables(machine, specific);
        var substitutions = new Dictionary<int, Cell>();
        var visitedStructures = new HashSet<ulong>();
        List<(Cell General, Cell Specific)> work = [(general, specific)];

        while (work.Count > 0)
        {
            (Cell candidate, Cell instance) = work[^1];
            work.RemoveAt(work.Count - 1);
            candidate = machine.Dereference(candidate);
            instance = machine.Dereference(instance);

            if (candidate == instance)
            {
                continue;
            }

            if (candidate.Tag == CellTag.Reference)
            {
                if (rigidVariables.Contains(candidate.Index))
                {
                    return false;
                }

                if (substitutions.TryGetValue(candidate.Index, out Cell substitution))
                {
                    work.Add((substitution, instance));
                }
                else
                {
                    substitutions.Add(candidate.Index, instance);
                }

                continue;
            }

            if (candidate.Tag != CellTag.Structure || instance.Tag != CellTag.Structure)
            {
                return false;
            }

            var candidateFunctor = machine.HeapAt(candidate.Index).Index;
            if (candidateFunctor != machine.HeapAt(instance.Index).Index)
            {
                return false;
            }

            var pair = ((ulong)(uint)candidate.Index << 32) | (uint)instance.Index;
            if (!visitedStructures.Add(pair))
            {
                continue;
            }

            var arity = machine.Symbols.ArityOf(candidateFunctor);
            for (var i = arity; i >= 1; i--)
            {
                work.Add((machine.HeapAt(candidate.Index + i), machine.HeapAt(instance.Index + i)));
            }
        }

        return true;
    }

    private static HashSet<int> CollectVariables(Machine machine, Cell term)
    {
        var variables = new HashSet<int>();
        var visitedStructures = new HashSet<int>();
        List<Cell> work = [term];

        while (work.Count > 0)
        {
            Cell cell = machine.Dereference(work[^1]);
            work.RemoveAt(work.Count - 1);

            if (cell.Tag == CellTag.Reference)
            {
                variables.Add(cell.Index);
                continue;
            }

            if (cell.Tag != CellTag.Structure || !visitedStructures.Add(cell.Index))
            {
                continue;
            }

            var arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
            for (var i = arity; i >= 1; i--)
            {
                work.Add(machine.HeapAt(cell.Index + i));
            }
        }

        return variables;
    }

    private static bool Compare(Machine machine, int less, int equal, int greater)
    {
        Cell requested = machine.Argument(0);
        if (requested.Tag != CellTag.Reference)
        {
            if (requested.Tag != CellTag.Atom)
            {
                throw PrologErrors.Type(machine, "atom", requested);
            }

            if (requested.Index != less && requested.Index != equal && requested.Index != greater)
            {
                throw PrologErrors.Domain(machine, "order", requested);
            }
        }

        var order = TermOrder.Compare(machine, machine.Argument(1), machine.Argument(2));
        var result =
            order < 0 ? less
            : order > 0 ? greater
            : equal;
        return machine.Unify(requested, Cell.Atom(result));
    }

    private static bool Functor3(Machine machine)
    {
        Cell term = machine.Argument(0);

        if (term.Tag != CellTag.Reference)
        {
            if (term.Tag == CellTag.Structure)
            {
                Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(term.Index).Index);
                return machine.Unify(machine.Argument(1), Cell.Atom(functor.NameAtom))
                    && machine.Unify(machine.Argument(2), Cell.Integer60(functor.Arity));
            }

            return machine.Unify(machine.Argument(1), term) && machine.Unify(machine.Argument(2), Cell.Integer60(0));
        }

        Cell name = machine.Argument(1);
        Cell arity = machine.Argument(2);

        if (name.Tag == CellTag.Reference || arity.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (arity.Tag is not (CellTag.Integer or CellTag.BigInteger))
        {
            throw PrologErrors.Type(machine, "integer", arity);
        }

        if (arity.Tag == CellTag.BigInteger)
        {
            // Outside the fixnum range the value cannot be a representable arity either way.
            throw machine.Symbols.GetBig(arity.Index).Sign < 0
                ? PrologErrors.Domain(machine, "not_less_than_zero", arity)
                : PrologErrors.Representation(machine, "max_arity");
        }

        if (arity.Integer < 0)
        {
            throw PrologErrors.Domain(machine, "not_less_than_zero", arity);
        }

        // A compound Name is not atomic at all, which the standard separates from a Name that is
        // atomic but cannot be a functor because it is a number.
        if (name.Tag == CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "atomic", name);
        }

        if (arity.Integer == 0)
        {
            return machine.Unify(term, name);
        }

        if (name.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", name);
        }

        if (arity.Integer >= Machine.ArgumentRegisterCount)
        {
            throw PrologErrors.Representation(machine, "max_arity");
        }

        var arguments = new Cell[(int)arity.Integer];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = machine.CreateVariable();
        }

        var functorId = machine.Symbols.InternFunctor(name.Index, arguments.Length);
        return machine.Unify(term, machine.CreateStructure(functorId, arguments));
    }

    private static bool Arg3(Machine machine)
    {
        Cell index = machine.Argument(0);
        Cell term = machine.Argument(1);

        if (index.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (term.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (index.Tag is not (CellTag.Integer or CellTag.BigInteger))
        {
            throw PrologErrors.Type(machine, "integer", index);
        }

        if (term.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "compound", term);
        }

        if (index.Tag == CellTag.BigInteger)
        {
            // Positive big indexes exceed every arity and fail; negative ones stay a domain error.
            return machine.Symbols.GetBig(index.Index).Sign < 0
                ? throw PrologErrors.Domain(machine, "not_less_than_zero", index)
                : false;
        }

        if (index.Integer < 0)
        {
            throw PrologErrors.Domain(machine, "not_less_than_zero", index);
        }

        var arity = machine.Symbols.ArityOf(machine.HeapAt(term.Index).Index);
        if (index.Integer == 0 || index.Integer > arity)
        {
            return false;
        }

        return machine.Unify(machine.Argument(2), machine.HeapAt(term.Index + (int)index.Integer));
    }

    /// <summary>
    /// <c>setarg(+Arg, +Term, +Value)</c> and <c>nb_setarg(+Arg, +Term, +Value)</c>: destructive
    /// assignment of a structure argument slot, undone on backtracking for the first form. The
    /// non-backtrackable form accepts atomic values only — an atom, integer, or float survives
    /// heap truncation, which is what makes the assignment able to outlive backtracking at all.
    /// </summary>
    private static bool SetArg(Machine machine, bool backtrackable)
    {
        Cell index = machine.Argument(0);
        Cell term = machine.Argument(1);
        Cell value = machine.Argument(2);

        if (index.Tag == CellTag.Reference || term.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (index.Tag is not (CellTag.Integer or CellTag.BigInteger))
        {
            throw PrologErrors.Type(machine, "integer", index);
        }

        if (term.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "compound", term);
        }

        if (index.Tag == CellTag.BigInteger)
        {
            return false;
        }

        if (
            !backtrackable
            && value.Tag is not (CellTag.Atom or CellTag.Integer or CellTag.BigInteger or CellTag.Float or CellTag.String)
        )
        {
            throw PrologErrors.Type(machine, "atomic", value);
        }

        var arity = machine.Symbols.ArityOf(machine.HeapAt(term.Index).Index);
        if (index.Integer < 1 || index.Integer > arity)
        {
            return false;
        }

        machine.SetArgument(term.Index + (int)index.Integer, value, backtrackable);
        return true;
    }

    private static bool Univ(Machine machine, int emptyList)
    {
        Cell term = machine.Argument(0);

        if (term.Tag != CellTag.Reference)
        {
            if (term.Tag == CellTag.Structure)
            {
                Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(term.Index).Index);
                var items = new Cell[functor.Arity + 1];
                items[0] = Cell.Atom(functor.NameAtom);
                for (var i = 0; i < functor.Arity; i++)
                {
                    items[i + 1] = machine.HeapAt(term.Index + 1 + i);
                }

                return machine.Unify(machine.Argument(1), machine.CreateList(items, Cell.Atom(emptyList)));
            }

            return machine.Unify(machine.Argument(1), machine.CreateList([term], Cell.Atom(emptyList)));
        }

        Cell list = machine.Argument(1);
        List<Cell> elements = [];
        Cell tail = TermList.Read(machine, list, elements);
        if (!TermList.IsEmpty(machine, tail))
        {
            throw tail.Tag == CellTag.Reference ? PrologErrors.Instantiation(machine) : PrologErrors.Type(machine, "list", list);
        }

        if (elements.Count == 0)
        {
            throw PrologErrors.Domain(machine, "non_empty_list", list);
        }

        Cell head = machine.Dereference(elements[0]);
        if (head.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (elements.Count == 1)
        {
            if (head.Tag == CellTag.Structure)
            {
                throw PrologErrors.Type(machine, "atomic", head);
            }

            return machine.Unify(term, head);
        }

        if (head.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", head);
        }

        if (elements.Count - 1 >= Machine.ArgumentRegisterCount)
        {
            throw PrologErrors.Representation(machine, "max_arity");
        }

        var functorId = machine.Symbols.InternFunctor(head.Index, elements.Count - 1);
        return machine.Unify(term, machine.CreateStructure(functorId, CollectionsMarshal.AsSpan(elements)[1..]));
    }

    private static bool IsGround(Machine machine, Cell term)
    {
        List<Cell> work = [term];

        while (work.Count > 0)
        {
            Cell cell = machine.Dereference(work[^1]);
            work.RemoveAt(work.Count - 1);

            if (cell.Tag == CellTag.Reference)
            {
                return false;
            }

            if (cell.Tag != CellTag.Structure)
            {
                continue;
            }

            var arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
            for (var i = 1; i <= arity; i++)
            {
                work.Add(machine.HeapAt(cell.Index + i));
            }
        }

        return true;
    }

    private static bool IsAcyclic(Machine machine, Cell term)
    {
        var state = new Dictionary<int, bool>();
        List<(Cell Cell, bool Exit)> work = [(term, false)];

        while (work.Count > 0)
        {
            (Cell cell, var exit) = work[^1];
            work.RemoveAt(work.Count - 1);
            cell = machine.Dereference(cell);

            if (cell.Tag != CellTag.Structure)
            {
                continue;
            }

            if (exit)
            {
                state[cell.Index] = true;
                continue;
            }

            if (state.TryGetValue(cell.Index, out var complete))
            {
                if (!complete)
                {
                    return false;
                }

                continue;
            }

            state.Add(cell.Index, false);
            work.Add((cell, true));

            var arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
            for (var i = arity; i >= 1; i--)
            {
                work.Add((machine.HeapAt(cell.Index + i), false));
            }
        }

        return true;
    }
}
