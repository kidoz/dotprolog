namespace Prolog.Runtime;

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
                machine.RequestHalt(code.Tag == CellTag.Integer ? (int)code.Integer : 0);
                return false;
            }
        );

        registry.Register(
            "nl",
            0,
            static machine =>
            {
                machine.Output.Write('\n');
                return true;
            }
        );

        registry.Register(
            "write",
            1,
            static machine =>
            {
                TermWriter.Write(machine, machine.Argument(0), machine.Output);
                return true;
            }
        );

        registry.Register(
            "print",
            1,
            static machine =>
            {
                TermWriter.Write(machine, machine.Argument(0), machine.Output);
                return true;
            }
        );

        registry.Register(
            "writeq",
            1,
            static machine =>
            {
                TermWriter.Write(machine, machine.Argument(0), machine.Output, quoted: true);
                return true;
            }
        );

        registry.Register(
            "writeln",
            1,
            static machine =>
            {
                TermWriter.Write(machine, machine.Argument(0), machine.Output);
                machine.Output.Write('\n');
                return true;
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

        // between/3 is the simplest nondeterministic native predicate, and the clearest example of one.
        registry.RegisterNondeterministic("between", 3, static machine => Between(machine, long.MinValue), Between);

        TextBuiltins.Register(registry);
        SortBuiltins.Register(registry);
        FormatBuiltins.Register(registry);
        DatabaseBuiltins.Register(registry);
        ControlPredicates.Install(program);
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

        long value = next == long.MinValue ? low.Integer : next;
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
