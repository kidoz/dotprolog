namespace Prolog.Runtime;

/// <summary>
/// Evaluates arithmetic expressions for <c>is/2</c> and the arithmetic comparisons.
/// </summary>
/// <remarks>
/// Evaluation recurses over the expression term. That is bounded by expression depth rather than by
/// Prolog call depth, so it does not make the CLR stack part of the Prolog control stack.
/// </remarks>
public static class ArithmeticEvaluator
{
    /// <summary>Evaluates <paramref name="expression"/> to a number.</summary>
    /// <param name="machine">Machine owning the heap the expression lives on.</param>
    /// <param name="expression">The expression term.</param>
    /// <exception cref="PrologException">The expression is unbound, or is not an evaluable term.</exception>
    public static PrologNumber Evaluate(Machine machine, Cell expression)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Cell cell = machine.Dereference(expression);

        switch (cell.Tag)
        {
            case CellTag.Integer:
                return PrologNumber.FromInteger(cell.Integer);

            case CellTag.Float:
                return PrologNumber.FromReal(machine.Symbols.GetFloat(cell.Index));

            case CellTag.Reference:
                throw new PrologException("instantiation_error");

            case CellTag.Atom:
                return EvaluateConstant(machine.Symbols.AtomName(cell.Index));

            case CellTag.Structure:
                break;

            default:
                throw new PrologException("type_error(evaluable, _)");
        }

        int functorId = machine.HeapAt(cell.Index).Index;
        Functor functor = machine.Symbols.GetFunctor(functorId);
        string name = machine.Symbols.AtomName(functor.NameAtom);

        return functor.Arity switch
        {
            1 => EvaluateUnary(name, Evaluate(machine, machine.HeapAt(cell.Index + 1)), machine.Symbols, functorId),
            2 => EvaluateBinary(
                name,
                Evaluate(machine, machine.HeapAt(cell.Index + 1)),
                Evaluate(machine, machine.HeapAt(cell.Index + 2)),
                machine.Symbols,
                functorId
            ),
            _ => throw Unevaluable(machine.Symbols, functorId),
        };
    }

    /// <summary>Converts an evaluated number back to a term cell.</summary>
    /// <exception cref="PrologException">An integer result does not fit in a term cell.</exception>
    public static Cell ToCell(Machine machine, PrologNumber number)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (number.IsFloat)
        {
            return Cell.Float(machine.Symbols.InternFloat(number.Real));
        }

        if (!Cell.FitsInteger(number.Integer))
        {
            throw new PrologException($"evaluation_error(int_overflow) for {number.Integer}");
        }

        return Cell.Integer60(number.Integer);
    }

    /// <summary>Orders two numbers, comparing across kinds by widening to double.</summary>
    public static int Compare(PrologNumber left, PrologNumber right)
    {
        if (!left.IsFloat && !right.IsFloat)
        {
            return left.Integer.CompareTo(right.Integer);
        }

        return left.AsDouble.CompareTo(right.AsDouble);
    }

    private static PrologNumber EvaluateConstant(string name) =>
        name switch
        {
            "pi" => PrologNumber.FromReal(Math.PI),
            "e" => PrologNumber.FromReal(Math.E),
            "inf" or "infinite" => PrologNumber.FromReal(double.PositiveInfinity),
            "nan" => PrologNumber.FromReal(double.NaN),
            "max_tagged_integer" => PrologNumber.FromInteger(Cell.MaxInteger),
            "min_tagged_integer" => PrologNumber.FromInteger(Cell.MinInteger),
            _ => throw new PrologException($"type_error(evaluable, {name}/0)"),
        };

    private static PrologNumber EvaluateUnary(string name, PrologNumber value, SymbolTable symbols, int functorId) =>
        name switch
        {
            "+" => value,
            "-" => value.IsFloat ? PrologNumber.FromReal(-value.Real) : PrologNumber.FromInteger(-value.Integer),
            "abs" => value.IsFloat
                ? PrologNumber.FromReal(Math.Abs(value.Real))
                : PrologNumber.FromInteger(Math.Abs(value.Integer)),
            "sign" => value.IsFloat
                ? PrologNumber.FromReal(Math.Sign(value.Real))
                : PrologNumber.FromInteger(Math.Sign(value.Integer)),
            "float" => PrologNumber.FromReal(value.AsDouble),
            "integer" => PrologNumber.FromInteger((long)Math.Round(value.AsDouble, MidpointRounding.AwayFromZero)),
            "truncate" => PrologNumber.FromInteger((long)Math.Truncate(value.AsDouble)),
            "floor" => PrologNumber.FromInteger((long)Math.Floor(value.AsDouble)),
            "ceiling" => PrologNumber.FromInteger((long)Math.Ceiling(value.AsDouble)),
            "sqrt" => PrologNumber.FromReal(Math.Sqrt(value.AsDouble)),
            _ => throw Unevaluable(symbols, functorId),
        };

    private static PrologNumber EvaluateBinary(
        string name,
        PrologNumber left,
        PrologNumber right,
        SymbolTable symbols,
        int functorId
    )
    {
        bool real = left.IsFloat || right.IsFloat;

        switch (name)
        {
            case "+":
                return real
                    ? PrologNumber.FromReal(left.AsDouble + right.AsDouble)
                    : PrologNumber.FromInteger(left.Integer + right.Integer);

            case "-":
                return real
                    ? PrologNumber.FromReal(left.AsDouble - right.AsDouble)
                    : PrologNumber.FromInteger(left.Integer - right.Integer);

            case "*":
                return real
                    ? PrologNumber.FromReal(left.AsDouble * right.AsDouble)
                    : PrologNumber.FromInteger(left.Integer * right.Integer);

            case "/":
                if (real)
                {
                    return PrologNumber.FromReal(left.AsDouble / right.AsDouble);
                }

                ThrowIfZero(right.Integer);

                // Integer division yields an integer only when it is exact; otherwise it yields a float.
                return left.Integer % right.Integer == 0
                    ? PrologNumber.FromInteger(left.Integer / right.Integer)
                    : PrologNumber.FromReal((double)left.Integer / right.Integer);

            case "//":
                RequireIntegers(real);
                ThrowIfZero(right.Integer);
                return PrologNumber.FromInteger(left.Integer / right.Integer);

            case "div":
                RequireIntegers(real);
                ThrowIfZero(right.Integer);
                return PrologNumber.FromInteger((long)Math.Floor((double)left.Integer / right.Integer));

            case "mod":
            {
                RequireIntegers(real);
                ThrowIfZero(right.Integer);
                long remainder = left.Integer % right.Integer;
                return PrologNumber.FromInteger(
                    remainder != 0 && (remainder < 0) != (right.Integer < 0) ? remainder + right.Integer : remainder
                );
            }

            case "rem":
                RequireIntegers(real);
                ThrowIfZero(right.Integer);
                return PrologNumber.FromInteger(left.Integer % right.Integer);

            case "min":
                return Compare(left, right) <= 0 ? left : right;

            case "max":
                return Compare(left, right) >= 0 ? left : right;

            case "**":
                return PrologNumber.FromReal(Math.Pow(left.AsDouble, right.AsDouble));

            case "^":
                if (real)
                {
                    return PrologNumber.FromReal(Math.Pow(left.AsDouble, right.AsDouble));
                }

                return PrologNumber.FromInteger(IntegerPower(left.Integer, right.Integer));

            case ">>":
                RequireIntegers(real);
                return PrologNumber.FromInteger(left.Integer >> (int)right.Integer);

            case "<<":
                RequireIntegers(real);
                return PrologNumber.FromInteger(left.Integer << (int)right.Integer);

            case "/\\":
                RequireIntegers(real);
                return PrologNumber.FromInteger(left.Integer & right.Integer);

            case "\\/":
                RequireIntegers(real);
                return PrologNumber.FromInteger(left.Integer | right.Integer);

            case "xor":
                RequireIntegers(real);
                return PrologNumber.FromInteger(left.Integer ^ right.Integer);

            default:
                throw Unevaluable(symbols, functorId);
        }
    }

    private static long IntegerPower(long value, long exponent)
    {
        if (exponent < 0)
        {
            throw new PrologException("type_error(float, integer_power_with_negative_exponent)");
        }

        long result = 1;
        for (long i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }

    private static void RequireIntegers(bool isReal)
    {
        if (isReal)
        {
            throw new PrologException("type_error(integer, float)");
        }
    }

    private static void ThrowIfZero(long divisor)
    {
        if (divisor == 0)
        {
            throw new PrologException("evaluation_error(zero_divisor)");
        }
    }

    private static PrologException Unevaluable(SymbolTable symbols, int functorId) =>
        new($"type_error(evaluable, {symbols.DescribeFunctor(functorId)})");
}
