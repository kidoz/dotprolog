using System.Runtime.InteropServices;

namespace DotProlog.Runtime;

/// <summary>
/// The native predicates every program gets. Registration is an explicit list rather than a scan of
/// attributes or assemblies, which is what keeps the set intact under trimming and NativeAOT.
/// </summary>
public static class CoreBuiltins
{
    /// <summary>Registers the core builtins into <paramref name="program"/>'s registry.</summary>
    public static void RegisterAll(BytecodeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        BuiltinRegistry registry = program.Builtins;

        registry.Register("true", 0, static _ => true);
        registry.Register("fail", 0, static _ => false);
        registry.Register("false", 0, static _ => false);

        registry.Register(
            "halt",
            0,
            static machine =>
            {
                machine.RequestHalt(0);
                return false;
            }
        );

        registry.Register(
            "halt",
            1,
            static machine =>
            {
                Cell code = machine.Argument(0);
                if (code.Tag == CellTag.Reference)
                {
                    throw PrologErrors.Instantiation(machine);
                }

                if (code.Tag != CellTag.Integer)
                {
                    throw PrologErrors.Type(machine, "integer", code);
                }

                machine.RequestHalt((int)code.Integer);
                return false;
            }
        );

        registry.Register("=", 2, static machine => machine.Unify(machine.Argument(0), machine.Argument(1)));

        registry.Register(
            "is",
            2,
            static machine =>
            {
                PrologNumber value = ArithmeticEvaluator.Evaluate(machine, machine.Argument(1));
                return machine.Unify(machine.Argument(0), ArithmeticEvaluator.ToCell(machine, value));
            }
        );

        registry.Register("=:=", 2, static machine => CompareArguments(machine) == 0);
        registry.Register("=\\=", 2, static machine => CompareArguments(machine) != 0);
        registry.Register("<", 2, static machine => CompareArguments(machine) < 0);
        registry.Register(">", 2, static machine => CompareArguments(machine) > 0);
        registry.Register("=<", 2, static machine => CompareArguments(machine) <= 0);
        registry.Register(">=", 2, static machine => CompareArguments(machine) >= 0);

        TermBuiltins.Register(registry, program.Symbols);

        registry.Register(
            "throw",
            1,
            static machine =>
            {
                Cell ball = machine.Argument(0);
                throw ball.Tag == CellTag.Reference
                    ? PrologErrors.Instantiation(machine)
                    : machine.CreateBall(ball, TermWriter.ToDisplayString(machine, ball, quoted: true));
            }
        );

        // The three halves of findall/3's failure-driven loop; see the bootstrap library.
        registry.Register(
            "$collect_begin",
            0,
            static machine =>
            {
                machine.BeginCollect();
                return true;
            }
        );

        registry.Register(
            "$collect_add",
            1,
            static machine =>
            {
                machine.AddCollected(machine.Argument(0));
                return true;
            }
        );

        registry.Register("$collect_end", 1, static machine => machine.Unify(machine.Argument(0), machine.EndCollect()));
        registry.Register(
            "$validate_callable",
            1,
            static machine =>
            {
                machine.ValidateCallable(machine.Argument(0));
                return true;
            }
        );
        registry.Register("$validate_partial_list", 1, ValidatePartialList);
        registry.Register("$validate_proper_list", 1, ValidateProperList);
        registry.Register("$validate_terminal_sequence", 1, ValidateTerminalSequence);
        registry.Register(
            "$grammar_soft_cut",
            0,
            static machine => machine.Program.LanguageMode != PrologLanguageMode.StrictIso
        );

        // Records where a host query's variables live, so each solution can be read back. The engine
        // compiles '$bindings'(v(V1, ..., Vn)) as the first goal of a query it was handed.
        registry.Register(
            "$bindings",
            1,
            static machine =>
            {
                machine.QueryBindings = machine.Argument(0);
                return true;
            }
        );

        registry.RegisterNondeterministic("repeat", 0, static machine => Repeat(machine, 0), Repeat);

        // between/3 is the simplest nondeterministic native predicate, and the clearest example of one.
        registry.RegisterNondeterministic("between", 3, static machine => Between(machine, long.MinValue), Between);

        registry.Register("succ", 2, Succ);
        registry.Register("plus", 3, Plus);

        // The one piece call/2..8 needs from the runtime: everything else about them is Prolog.
        registry.Register("$add_args", 3, AddArguments);

        // Resolves Module:Goal to the predicate a module system compiled it to.
        registry.Register("$qualify", 3, Qualify);

        TextBuiltins.Register(registry);
        OperatorBuiltins.Register(registry);
        StreamBuiltins.Register(registry);
        SortBuiltins.Register(registry);
        FormatBuiltins.Register(registry);
        DatabaseBuiltins.Register(registry);
        PrologFlagBuiltins.Register(registry);
        CharacterConversionBuiltins.Register(registry);
        ControlPredicates.Install(program);
    }

    /// <summary><c>succ(?Int, ?Successor)</c> over the natural numbers, in either direction.</summary>
    private static bool Succ(Machine machine)
    {
        Cell value = machine.Argument(0);
        Cell successor = machine.Argument(1);

        if (value.Tag == CellTag.Integer)
        {
            return value.Integer >= 0
                ? machine.Unify(successor, Cell.Integer60(value.Integer + 1))
                : throw PrologErrors.Type(machine, "not_less_than_zero", value);
        }

        if (value.Tag != CellTag.Reference)
        {
            throw PrologErrors.Type(machine, "integer", value);
        }

        if (successor.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (successor.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", successor);
        }

        // succ(X, 0) has no solution, because 0 is not the successor of a natural number.
        return successor.Integer > 0 && machine.Unify(value, Cell.Integer60(successor.Integer - 1));
    }

    /// <summary><c>plus(?A, ?B, ?Sum)</c>: any one of the three may be the unbound one.</summary>
    private static bool Plus(Machine machine)
    {
        Cell first = machine.Argument(0);
        Cell second = machine.Argument(1);
        Cell sum = machine.Argument(2);

        if (first.Tag == CellTag.Integer && second.Tag == CellTag.Integer)
        {
            return machine.Unify(sum, Cell.Integer60(first.Integer + second.Integer));
        }

        if (sum.Tag != CellTag.Integer)
        {
            throw sum.Tag == CellTag.Reference ? PrologErrors.Instantiation(machine) : PrologErrors.Type(machine, "integer", sum);
        }

        if (first.Tag == CellTag.Integer)
        {
            return machine.Unify(second, Cell.Integer60(sum.Integer - first.Integer));
        }

        return second.Tag == CellTag.Integer
            ? machine.Unify(first, Cell.Integer60(sum.Integer - second.Integer))
            : throw PrologErrors.Instantiation(machine);
    }

    /// <summary>Accepts a proper or partial list and reports every other tail as an ISO list type error.</summary>
    private static bool ValidatePartialList(Machine machine)
    {
        Cell list = machine.Argument(0);
        List<Cell> elements = [];
        Cell tail = TermList.Read(machine, list, elements);

        return TermList.IsEmpty(machine, tail) || tail.Tag == CellTag.Reference
            ? true
            : throw PrologErrors.Type(machine, "list", list);
    }

    /// <summary>Accepts a proper list and preserves the required partial-list error distinction.</summary>
    private static bool ValidateProperList(Machine machine)
    {
        _ = TermList.ReadProper(machine, machine.Argument(0));
        return true;
    }

    /// <summary>
    /// Accepts a proper or partial terminal sequence and reports an ISO list type error for any other tail.
    /// </summary>
    private static bool ValidateTerminalSequence(Machine machine)
    {
        Cell sequence = machine.Argument(0);
        List<Cell> elements = [];
        Cell tail = TermList.Read(machine, sequence, elements);

        return TermList.IsEmpty(machine, tail) || tail.Tag == CellTag.Reference
            ? true
            : throw PrologErrors.Type(machine, "list", sequence);
    }

    /// <summary>
    /// <c>'$qualify'(+Module, +Goal, -Resolved)</c>: the predicate a goal names inside a module.
    /// </summary>
    /// <remarks>
    /// A module's predicates are compiled under the name <c>module:predicate</c>, so resolving is
    /// interning that name and checking it exists. When it does not, the plain name is used, which is
    /// what makes a library predicate and anything in <c>user</c> reachable from inside a module.
    /// </remarks>
    private static bool Qualify(Machine machine)
    {
        Cell module = machine.Argument(0);
        Cell goal = machine.Argument(1);

        if (module.Tag == CellTag.Reference || goal.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (module.Tag != CellTag.Atom)
        {
            throw PrologErrors.Type(machine, "atom", module);
        }

        // A nested qualification names the innermost module, as M1:M2:G means M2:G.
        var colon = machine.Symbols.InternFunctor(":", 2);
        while (goal.Tag == CellTag.Structure && machine.HeapAt(goal.Index).Index == colon)
        {
            module = machine.Dereference(machine.HeapAt(goal.Index + 1));
            goal = machine.Dereference(machine.HeapAt(goal.Index + 2));

            if (module.Tag != CellTag.Atom)
            {
                throw module.Tag == CellTag.Reference
                    ? PrologErrors.Instantiation(machine)
                    : PrologErrors.Type(machine, "atom", module);
            }
        }

        var prefix = machine.Symbols.AtomName(module.Index);

        if (goal.Tag == CellTag.Atom)
        {
            var qualified = machine.Symbols.InternFunctor($"{prefix}:{machine.Symbols.AtomName(goal.Index)}", 0);
            return machine.Unify(
                machine.Argument(2),
                machine.Program.IsDefined(qualified) ? Cell.Atom(machine.Symbols.GetFunctor(qualified).NameAtom) : goal
            );
        }

        if (goal.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "callable", goal);
        }

        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(goal.Index).Index);
        var target = machine.Symbols.InternFunctor($"{prefix}:{machine.Symbols.AtomName(functor.NameAtom)}", functor.Arity);

        if (!machine.Program.IsDefined(target) && !machine.Program.IsDynamic(target))
        {
            return machine.Unify(machine.Argument(2), goal);
        }

        var arguments = new Cell[functor.Arity];
        for (var i = 0; i < functor.Arity; i++)
        {
            arguments[i] = machine.HeapAt(goal.Index + 1 + i);
        }

        return machine.Unify(machine.Argument(2), machine.CreateStructure(target, arguments));
    }

    /// <summary>
    /// <c>'$add_args'(+Goal, +Extra, -Expanded)</c>: appends arguments to a goal, which is the whole
    /// of what <c>call/2</c> and its higher arities do before meta-calling the result.
    /// </summary>
    private static bool AddArguments(Machine machine)
    {
        Cell goal = machine.Argument(0);

        if (goal.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        List<Cell> extra = TermList.ReadProper(machine, machine.Argument(1));

        // A qualified closure keeps its qualification: M:foo with an extra argument is M:foo(X), so
        // that the resolution happens once, when the goal is finally called.
        var colon = machine.Symbols.InternFunctor(":", 2);
        if (goal.Tag == CellTag.Structure && machine.HeapAt(goal.Index).Index == colon && extra.Count > 0)
        {
            Cell module = machine.HeapAt(goal.Index + 1);
            Cell inner = machine.Dereference(machine.HeapAt(goal.Index + 2));
            Cell expanded = machine.CreateVariable();

            return Extend(machine, inner, extra, expanded)
                && machine.Unify(machine.Argument(2), machine.CreateStructure(colon, [module, expanded]));
        }

        return Extend(machine, goal, extra, machine.Argument(2));
    }

    /// <summary>Appends <paramref name="extra"/> to <paramref name="goal"/> and unifies with <paramref name="target"/>.</summary>
    private static bool Extend(Machine machine, Cell goal, List<Cell> extra, Cell target)
    {
        if (extra.Count == 0)
        {
            return machine.Unify(target, goal);
        }

        if (goal.Tag == CellTag.Atom)
        {
            var atomFunctor = machine.Symbols.InternFunctor(goal.Index, extra.Count);
            return machine.Unify(target, machine.CreateStructure(atomFunctor, CollectionsMarshal.AsSpan(extra)));
        }

        if (goal.Tag != CellTag.Structure)
        {
            throw PrologErrors.Type(machine, "callable", goal);
        }

        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(goal.Index).Index);
        var arity = functor.Arity + extra.Count;

        if (arity >= Machine.ArgumentRegisterCount)
        {
            throw PrologErrors.Representation(machine, "max_arity");
        }

        var arguments = new Cell[arity];
        for (var i = 0; i < functor.Arity; i++)
        {
            arguments[i] = machine.HeapAt(goal.Index + 1 + i);
        }

        extra.CopyTo(arguments, functor.Arity);

        return machine.Unify(target, machine.CreateStructure(machine.Symbols.InternFunctor(functor.NameAtom, arity), arguments));
    }

    /// <summary><c>repeat/0</c>: succeeds now and on every subsequent retry.</summary>
    private static bool Repeat(Machine machine, long state)
    {
        machine.PushRetry(state);
        return true;
    }

    /// <summary>
    /// <c>between(Low, High, X)</c>: unifies X with each integer from Low to High in turn.
    /// </summary>
    /// <param name="machine">The machine.</param>
    /// <param name="next">
    /// The value to try, or <see cref="long.MinValue"/> on the first call, which means "start at Low".
    /// </param>
    private static bool Between(Machine machine, long next)
    {
        Cell low = machine.Argument(0);
        Cell high = machine.Argument(1);

        if (low.Tag == CellTag.Reference || high.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (low.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", low);
        }

        if (high.Tag != CellTag.Integer)
        {
            throw PrologErrors.Type(machine, "integer", high);
        }

        var value = next == long.MinValue ? low.Integer : next;
        Cell target = machine.Argument(2);

        // With X already bound, between/3 is a range check with no solutions to enumerate.
        if (target.Tag == CellTag.Integer)
        {
            return target.Integer >= low.Integer && target.Integer <= high.Integer;
        }

        if (value > high.Integer)
        {
            return false;
        }

        if (value < high.Integer)
        {
            machine.PushRetry(value + 1);
        }

        return machine.Unify(target, Cell.Integer60(value));
    }

    private static int CompareArguments(Machine machine) =>
        ArithmeticEvaluator.Compare(
            ArithmeticEvaluator.Evaluate(machine, machine.Argument(0)),
            ArithmeticEvaluator.Evaluate(machine, machine.Argument(1))
        );
}
