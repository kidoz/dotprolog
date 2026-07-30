namespace DotProlog.Runtime;

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
                throw PrologErrors.Instantiation(machine);

            case CellTag.Atom:
            {
                string constantName = machine.Symbols.AtomName(cell.Index);
                if (
                    machine.Program.LanguageMode == PrologLanguageMode.StrictIso
                    && !IsoLanguageProfile.IsStandardEvaluable(constantName, 0)
                )
                {
                    throw PrologErrors.NotEvaluable(machine, constantName, 0);
                }

                return EvaluateConstant(machine, constantName);
            }

            case CellTag.Structure:
                break;

            default:
                throw PrologErrors.Type(machine, "evaluable", cell);
        }

        int functorId = machine.HeapAt(cell.Index).Index;
        Functor functor = machine.Symbols.GetFunctor(functorId);
        string name = machine.Symbols.AtomName(functor.NameAtom);
        if (
            machine.Program.LanguageMode == PrologLanguageMode.StrictIso
            && !IsoLanguageProfile.IsStandardEvaluable(name, functor.Arity)
        )
        {
            throw Unevaluable(machine, functorId);
        }

        return functor.Arity switch
        {
            1 => EvaluateUnary(name, Evaluate(machine, machine.HeapAt(cell.Index + 1)), machine, functorId),
            2 => EvaluateBinary(
                name,
                Evaluate(machine, machine.HeapAt(cell.Index + 1)),
                Evaluate(machine, machine.HeapAt(cell.Index + 2)),
                machine,
                functorId
            ),
            _ => throw Unevaluable(machine, functorId),
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
            throw PrologErrors.Evaluation(machine, "int_overflow");
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

    private static PrologNumber EvaluateConstant(Machine machine, string name) =>
        name switch
        {
            "pi" => PrologNumber.FromReal(Math.PI),
            "e" => PrologNumber.FromReal(Math.E),
            "inf" or "infinite" => PrologNumber.FromReal(double.PositiveInfinity),
            "nan" => PrologNumber.FromReal(double.NaN),
            "max_tagged_integer" => PrologNumber.FromInteger(Cell.MaxInteger),
            "min_tagged_integer" => PrologNumber.FromInteger(Cell.MinInteger),
            _ => throw PrologErrors.NotEvaluable(machine, name, 0),
        };

    private static PrologNumber EvaluateUnary(string name, PrologNumber value, Machine machine, int functorId)
    {
        try
        {
            return name switch
            {
                "+" => value,
                "-" => value.IsFloat ? FloatResult(machine, -value.Real) : IntegerResult(machine, checked(-value.Integer)),
                "abs" => value.IsFloat
                    ? FloatResult(machine, Math.Abs(value.Real))
                    : IntegerResult(machine, checked(Math.Abs(value.Integer))),
                "sign" => Sign(machine, value),
                "float" => FloatResult(machine, value.AsDouble),
                "integer" => IntegerResult(machine, checked((long)Math.Round(value.AsDouble, MidpointRounding.AwayFromZero))),
                "truncate" => FloatToInteger(machine, value, Math.Truncate),
                "floor" => FloatToInteger(machine, value, Math.Floor),
                "ceiling" => FloatToInteger(machine, value, Math.Ceiling),
                "round" => FloatToInteger(machine, value, static operand => Math.Floor(operand + 0.5)),
                "float_integer_part" => FloatPart(machine, value, fractional: false),
                "float_fractional_part" => FloatPart(machine, value, fractional: true),
                "sqrt" => FloatResult(machine, Math.Sqrt(value.AsDouble)),
                "sin" => FloatResult(machine, Math.Sin(value.AsDouble)),
                "cos" => FloatResult(machine, Math.Cos(value.AsDouble)),
                "tan" => FloatResult(machine, Math.Tan(value.AsDouble)),
                "asin" => FloatResult(machine, Math.Asin(value.AsDouble)),
                "acos" => FloatResult(machine, Math.Acos(value.AsDouble)),
                "atan" => FloatResult(machine, Math.Atan(value.AsDouble)),
                "exp" => FloatResult(machine, Math.Exp(value.AsDouble)),
                "log" => Log(machine, value),
                "\\" => IntegerResult(machine, ~RequireInteger(machine, value)),
                _ => throw Unevaluable(machine, functorId),
            };
        }
        catch (OverflowException)
        {
            throw PrologErrors.Evaluation(machine, "int_overflow");
        }
    }

    private static PrologNumber EvaluateBinary(string name, PrologNumber left, PrologNumber right, Machine machine, int functorId)
    {
        bool real = left.IsFloat || right.IsFloat;

        try
        {
            switch (name)
            {
                case "+":
                    return real
                        ? FloatResult(machine, left.AsDouble + right.AsDouble)
                        : IntegerResult(machine, checked(left.Integer + right.Integer));

                case "-":
                    return real
                        ? FloatResult(machine, left.AsDouble - right.AsDouble)
                        : IntegerResult(machine, checked(left.Integer - right.Integer));

                case "*":
                    return real
                        ? FloatResult(machine, left.AsDouble * right.AsDouble)
                        : IntegerResult(machine, checked(left.Integer * right.Integer));

                case "/":
                    if (right.AsDouble == 0)
                    {
                        // An integer zero divisor is zero_divisor even for 0/0; only the float
                        // 0.0/0.0, whose IEEE result is NaN, is undefined.
                        throw PrologErrors.Evaluation(machine, real && left.AsDouble == 0 ? "undefined" : "zero_divisor");
                    }

                    return FloatResult(machine, left.AsDouble / right.AsDouble);

                case "//":
                {
                    long leftInteger = RequireInteger(machine, left);
                    long rightInteger = RequireInteger(machine, right);
                    ThrowIfZero(machine, rightInteger);
                    return IntegerResult(machine, leftInteger / rightInteger);
                }

                case "div":
                {
                    long leftInteger = RequireInteger(machine, left);
                    long rightInteger = RequireInteger(machine, right);
                    ThrowIfZero(machine, rightInteger);
                    return IntegerResult(machine, FloorDivide(leftInteger, rightInteger));
                }

                case "mod":
                {
                    long leftInteger = RequireInteger(machine, left);
                    long rightInteger = RequireInteger(machine, right);
                    ThrowIfZero(machine, rightInteger);
                    long remainder = leftInteger % rightInteger;
                    return IntegerResult(
                        machine,
                        remainder != 0 && (remainder < 0) != (rightInteger < 0) ? checked(remainder + rightInteger) : remainder
                    );
                }

                case "rem":
                {
                    long leftInteger = RequireInteger(machine, left);
                    long rightInteger = RequireInteger(machine, right);
                    ThrowIfZero(machine, rightInteger);
                    return IntegerResult(machine, leftInteger % rightInteger);
                }

                case "min":
                    return Minimum(left, right);

                case "max":
                    return Maximum(left, right);

                case "**":
                    return PowerFloat(machine, left.AsDouble, right.AsDouble);

                case "^":
                    if (real)
                    {
                        return PowerFloat(machine, left.AsDouble, right.AsDouble);
                    }

                    if (right.Integer < 0)
                    {
                        if (left.Integer == 0)
                        {
                            throw PrologErrors.Evaluation(machine, "zero_divisor");
                        }

                        if (left.Integer is < -1 or > 1)
                        {
                            throw PrologErrors.Type(machine, "float", Cell.Integer60(left.Integer));
                        }

                        return PrologNumber.FromInteger(left.Integer == -1 && (right.Integer & 1) != 0 ? -1 : 1);
                    }

                    return IntegerResult(machine, IntegerPower(machine, left.Integer, right.Integer));

                case "atan2":
                    if (left.AsDouble == 0 && right.AsDouble == 0)
                    {
                        throw PrologErrors.Evaluation(machine, "undefined");
                    }

                    return FloatResult(machine, Math.Atan2(left.AsDouble, right.AsDouble));

                case ">>":
                    return IntegerResult(
                        machine,
                        ShiftRight(machine, RequireInteger(machine, left), RequireInteger(machine, right))
                    );

                case "<<":
                    return IntegerResult(
                        machine,
                        ShiftLeft(machine, RequireInteger(machine, left), RequireInteger(machine, right))
                    );

                case "/\\":
                    return IntegerResult(machine, RequireInteger(machine, left) & RequireInteger(machine, right));

                case "\\/":
                    return IntegerResult(machine, RequireInteger(machine, left) | RequireInteger(machine, right));

                case "xor":
                    return IntegerResult(machine, RequireInteger(machine, left) ^ RequireInteger(machine, right));

                default:
                    throw Unevaluable(machine, functorId);
            }
        }
        catch (OverflowException)
        {
            throw PrologErrors.Evaluation(machine, "int_overflow");
        }
    }

    private static long IntegerPower(Machine machine, long value, long exponent)
    {
        long result = 1;
        long factor = value;
        long remaining = exponent;

        while (remaining > 0)
        {
            if ((remaining & 1) != 0)
            {
                result = checked(result * factor);
                EnsureIntegerFits(machine, result);
            }

            remaining >>= 1;
            if (remaining > 0)
            {
                factor = checked(factor * factor);
                EnsureIntegerFits(machine, factor);
            }
        }

        return result;
    }

    private static PrologNumber Sign(Machine machine, PrologNumber value)
    {
        if (!value.IsFloat)
        {
            return PrologNumber.FromInteger(Math.Sign(value.Integer));
        }

        if (double.IsNaN(value.Real))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        return FloatResult(
            machine,
            value.Real > 0 ? 1.0
                : value.Real < 0 ? -1.0
                : 0.0
        );
    }

    private static PrologNumber FloatToInteger(Machine machine, PrologNumber value, Func<double, double> operation)
    {
        double operand = RequireFloat(machine, value);
        double result = operation(operand);

        if (double.IsNaN(result))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        if (double.IsInfinity(result) || result < Cell.MinInteger || result > Cell.MaxInteger)
        {
            throw PrologErrors.Evaluation(machine, "int_overflow");
        }

        return PrologNumber.FromInteger(checked((long)result));
    }

    private static PrologNumber FloatPart(Machine machine, PrologNumber value, bool fractional)
    {
        double operand = RequireFloat(machine, value);
        return FloatResult(machine, fractional ? operand % 1.0 : Math.Truncate(operand));
    }

    private static PrologNumber Log(Machine machine, PrologNumber value)
    {
        if (value.AsDouble == 0)
        {
            throw PrologErrors.Evaluation(machine, "zero_divisor");
        }

        return FloatResult(machine, Math.Log(value.AsDouble));
    }

    private static PrologNumber PowerFloat(Machine machine, double left, double right)
    {
        if (left == 0 && right < 0)
        {
            throw PrologErrors.Evaluation(machine, "zero_divisor");
        }

        return FloatResult(machine, Math.Pow(left, right));
    }

    private static PrologNumber IntegerResult(Machine machine, long value)
    {
        EnsureIntegerFits(machine, value);
        return PrologNumber.FromInteger(value);
    }

    private static PrologNumber FloatResult(Machine machine, double value)
    {
        if (double.IsNaN(value))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        if (double.IsInfinity(value))
        {
            throw PrologErrors.Evaluation(machine, "float_overflow");
        }

        return PrologNumber.FromReal(value);
    }

    private static long RequireInteger(Machine machine, PrologNumber value)
    {
        if (value.IsFloat)
        {
            throw PrologErrors.Type(machine, "integer", ToCell(machine, value));
        }

        return value.Integer;
    }

    private static double RequireFloat(Machine machine, PrologNumber value)
    {
        if (!value.IsFloat)
        {
            throw PrologErrors.Type(machine, "float", ToCell(machine, value));
        }

        return value.Real;
    }

    private static PrologNumber Minimum(PrologNumber left, PrologNumber right)
    {
        int order = Compare(left, right);
        if (order < 0)
        {
            return left;
        }

        if (order > 0)
        {
            return right;
        }

        return left.IsFloat && !right.IsFloat ? right : left;
    }

    private static PrologNumber Maximum(PrologNumber left, PrologNumber right)
    {
        int order = Compare(left, right);
        if (order > 0)
        {
            return left;
        }

        if (order < 0)
        {
            return right;
        }

        return left.IsFloat && !right.IsFloat ? right : left;
    }

    private static long FloorDivide(long left, long right)
    {
        long quotient = left / right;
        long remainder = left % right;
        return remainder != 0 && (remainder < 0) != (right < 0) ? checked(quotient - 1) : quotient;
    }

    private static long ShiftLeft(Machine machine, long value, long count)
    {
        if (count < 0)
        {
            return count == long.MinValue ? (value < 0 ? -1 : 0) : ShiftRight(machine, value, -count);
        }

        if (value == 0)
        {
            return 0;
        }

        if (count >= 60 || value > (Cell.MaxInteger >> (int)count) || value < (Cell.MinInteger >> (int)count))
        {
            throw PrologErrors.Evaluation(machine, "int_overflow");
        }

        return value << (int)count;
    }

    private static long ShiftRight(Machine machine, long value, long count)
    {
        if (count < 0)
        {
            if (count == long.MinValue)
            {
                throw PrologErrors.Evaluation(machine, "int_overflow");
            }

            return ShiftLeft(machine, value, -count);
        }

        return count >= 60 ? (value < 0 ? -1 : 0) : value >> (int)count;
    }

    private static void EnsureIntegerFits(Machine machine, long value)
    {
        if (!Cell.FitsInteger(value))
        {
            throw PrologErrors.Evaluation(machine, "int_overflow");
        }
    }

    private static void ThrowIfZero(Machine machine, double divisor)
    {
        if (divisor == 0)
        {
            throw PrologErrors.Evaluation(machine, "zero_divisor");
        }
    }

    private static PrologException Unevaluable(Machine machine, int functorId)
    {
        Functor functor = machine.Symbols.GetFunctor(functorId);
        return PrologErrors.NotEvaluable(machine, machine.Symbols.AtomName(functor.NameAtom), functor.Arity);
    }
}
