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
        registry.Register("@<", 2, static machine => Order(machine) < 0);
        registry.Register("@>", 2, static machine => Order(machine) > 0);
        registry.Register("@=<", 2, static machine => Order(machine) <= 0);
        registry.Register("@>=", 2, static machine => Order(machine) >= 0);
        registry.Register("\\=", 2, static machine => !machine.CanUnify(machine.Argument(0), machine.Argument(1)));

        int less = symbols.InternAtom("<");
        int equal = symbols.InternAtom("=");
        int greater = symbols.InternAtom(">");
        registry.Register(
            "compare",
            3,
            machine =>
            {
                int order = TermOrder.Compare(machine, machine.Argument(1), machine.Argument(2));
                int atom =
                    order < 0 ? less
                    : order > 0 ? greater
                    : equal;
                return machine.Unify(machine.Argument(0), Cell.Atom(atom));
            }
        );
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
            // The nondeterministic form of arg/3 needs a builtin that can create choice points.
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

        int arity = machine.Symbols.ArityOf(machine.HeapAt(term.Index).Index);
        if (index.Integer < 1 || index.Integer > arity)
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

        List<Cell> elements = [];
        if (!TermList.IsEmpty(machine, TermList.Read(machine, machine.Argument(1), elements)))
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (elements.Count == 0)
        {
            throw PrologErrors.Domain(machine, "non_empty_list", machine.Argument(1));
        }

        Cell head = machine.Dereference(elements[0]);

        if (elements.Count == 1)
        {
            return machine.Unify(term, head);
        }

        if (head.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
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
