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
        registry.Register("integer", 1, static machine => machine.Argument(0).Tag == CellTag.Integer);
        registry.Register("float", 1, static machine => machine.Argument(0).Tag == CellTag.Float);
        registry.Register("number", 1, static machine => machine.Argument(0).Tag is CellTag.Integer or CellTag.Float);
        registry.Register("compound", 1, static machine => machine.Argument(0).Tag == CellTag.Structure);
        registry.Register(
            "atomic",
            1,
            static machine => machine.Argument(0).Tag is CellTag.Atom or CellTag.Integer or CellTag.Float
        );
        registry.Register("callable", 1, static machine => machine.Argument(0).Tag is CellTag.Atom or CellTag.Structure);
        registry.Register("is_list", 1, static machine => TermList.IsProper(machine, machine.Argument(0)));
        registry.Register("ground", 1, static machine => IsGround(machine, machine.Argument(0)));
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

        int less = symbols.InternAtom("<");
        int equal = symbols.InternAtom("=");
        int greater = symbols.InternAtom(">");
        registry.Register("compare", 3, machine => Compare(machine, less, equal, greater));
    }

    private static void RegisterInspection(BuiltinRegistry registry, SymbolTable symbols)
    {
        registry.Register("functor", 3, Functor3);
        registry.Register("arg", 3, Arg3);
        registry.Register("copy_term", 2, CopyTerm);
        registry.Register("term_variables", 2, TermVariables);

        int emptyList = symbols.EmptyList;
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
        var buffer = new TermBuffer();
        int root = buffer.Copy(machine, machine.Argument(0));
        int origin = buffer.Materialize(machine);
        return machine.Unify(machine.Argument(1), machine.HeapAt(origin + root));
    }

    /// <summary>
    /// <c>term_variables(+Term, -Variables)</c>: the term's distinct unbound variables, in the order
    /// a left-to-right walk first reaches them.
    /// </summary>
    private static bool TermVariables(Machine machine)
    {
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
            int arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
            for (int i = arity; i >= 1; i--)
            {
                work.Add(machine.HeapAt(cell.Index + i));
            }
        }

        return machine.Unify(machine.Argument(1), TermList.Build(machine, CollectionsMarshal.AsSpan(found)));
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

            int candidateFunctor = machine.HeapAt(candidate.Index).Index;
            if (candidateFunctor != machine.HeapAt(instance.Index).Index)
            {
                return false;
            }

            ulong pair = ((ulong)(uint)candidate.Index << 32) | (uint)instance.Index;
            if (!visitedStructures.Add(pair))
            {
                continue;
            }

            int arity = machine.Symbols.ArityOf(candidateFunctor);
            for (int i = arity; i >= 1; i--)
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

            int arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
            for (int i = arity; i >= 1; i--)
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

        int order = TermOrder.Compare(machine, machine.Argument(1), machine.Argument(2));
        int result =
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

        if (arity.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", arity);
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
        for (int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = machine.CreateVariable();
        }

        int functorId = machine.Symbols.InternFunctor(name.Index, arguments.Length);
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

        if (index.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", index);
        }

        if (term.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "compound", term);
        }

        if (index.Integer < 0)
        {
            throw PrologErrors.Domain(machine, "not_less_than_zero", index);
        }

        int arity = machine.Symbols.ArityOf(machine.HeapAt(term.Index).Index);
        if (index.Integer == 0 || index.Integer > arity)
        {
            return false;
        }

        return machine.Unify(machine.Argument(2), machine.HeapAt(term.Index + (int)index.Integer));
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
                for (int i = 0; i < functor.Arity; i++)
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

        int functorId = machine.Symbols.InternFunctor(head.Index, elements.Count - 1);
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

            int arity = machine.Symbols.ArityOf(machine.HeapAt(cell.Index).Index);
            for (int i = 1; i <= arity; i++)
            {
                work.Add(machine.HeapAt(cell.Index + i));
            }
        }

        return true;
    }
}
