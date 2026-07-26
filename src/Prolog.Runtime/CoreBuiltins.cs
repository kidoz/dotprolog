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
    }

    private static int CompareArguments(Machine machine) =>
        ArithmeticEvaluator.Compare(
            ArithmeticEvaluator.Evaluate(machine, machine.Argument(0)),
            ArithmeticEvaluator.Evaluate(machine, machine.Argument(1))
        );
}
