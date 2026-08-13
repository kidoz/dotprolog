using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>
/// Evaluates arithmetic expressions for <c>is/2</c> and the arithmetic comparisons.
/// </summary>
/// <remarks>
/// <para>
/// Evaluation recurses over the expression term. That is bounded by expression depth rather than by
/// Prolog call depth, so it does not make the CLR stack part of the Prolog control stack.
/// </para>
/// <para>
/// Integer arithmetic is unbounded: operations run on <see cref="long"/> while both
/// operands are fixnums and redo in <see cref="BigInteger"/> when the 64-bit operation overflows,
/// and every integer result is normalized so values in the fixnum range never take the big
/// representation.
/// </para>
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

            case CellTag.BigInteger:
                return PrologNumber.FromBig(machine.Symbols.GetBig(cell.Index));

            case CellTag.Float:
                return PrologNumber.FromReal(machine.Symbols.GetFloat(cell.Index));

            case CellTag.Reference:
                throw PrologErrors.Instantiation(machine);

            case CellTag.Atom:
            {
                var constantName = machine.Symbols.AtomName(cell.Index);
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

        var functorId = machine.HeapAt(cell.Index).Index;
        Functor functor = machine.Symbols.GetFunctor(functorId);
        var name = machine.Symbols.AtomName(functor.NameAtom);
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
    public static Cell ToCell(Machine machine, PrologNumber number)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (number.IsFloat)
        {
            return Cell.Float(machine.Symbols.InternFloat(number.Real));
        }

        if (number.IsBig)
        {
            return Cell.Big(machine.Symbols.InternBig(number.Big));
        }

        return Cell.Integer60(number.Integer);
    }

    /// <summary>Orders two numbers, comparing integers exactly and mixed kinds by widening to double.</summary>
    public static int Compare(PrologNumber left, PrologNumber right)
    {
        if (!left.IsFloat && !right.IsFloat)
        {
            if (!left.IsBig && !right.IsBig)
            {
                return left.Integer.CompareTo(right.Integer);
            }

            return left.Big.CompareTo(right.Big);
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

    private static PrologNumber EvaluateUnary(string name, PrologNumber value, Machine machine, int functorId) =>
        name switch
        {
            "+" => value,
            "-" => Negate(machine, value),
            "abs" => value.IsFloat ? FloatResult(machine, Math.Abs(value.Real))
            : value.IsBig ? PrologNumber.FromBig(BigInteger.Abs(value.Big))
            : PrologNumber.FromInteger(Math.Abs(value.Integer)),
            "sign" => Sign(machine, value),
            "float" => FloatResult(machine, value.AsDouble),
            "integer" => value.IsFloat
                ? FloatToInteger(machine, value, static operand => Math.Round(operand, MidpointRounding.AwayFromZero))
                : value,
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
            "\\" => Complement(machine, value),
            _ => throw Unevaluable(machine, functorId),
        };

    private static PrologNumber EvaluateBinary(string name, PrologNumber left, PrologNumber right, Machine machine, int functorId)
    {
        var real = left.IsFloat || right.IsFloat;

        switch (name)
        {
            case "+":
                if (real)
                {
                    return FloatResult(machine, left.AsDouble + right.AsDouble);
                }

                if (!left.IsBig && !right.IsBig)
                {
                    // Two fixnums cannot overflow 64 bits; FromInteger widens past 60.
                    return PrologNumber.FromInteger(left.Integer + right.Integer);
                }

                return PrologNumber.FromBig(left.Big + right.Big);

            case "-":
                if (real)
                {
                    return FloatResult(machine, left.AsDouble - right.AsDouble);
                }

                if (!left.IsBig && !right.IsBig)
                {
                    return PrologNumber.FromInteger(left.Integer - right.Integer);
                }

                return PrologNumber.FromBig(left.Big - right.Big);

            case "*":
                if (real)
                {
                    return FloatResult(machine, left.AsDouble * right.AsDouble);
                }

                if (!left.IsBig && !right.IsBig)
                {
                    try
                    {
                        return PrologNumber.FromInteger(checked(left.Integer * right.Integer));
                    }
                    catch (OverflowException)
                    {
                        // Falls through to the wide multiply below.
                    }
                }

                return PrologNumber.FromBig(left.Big * right.Big);

            case "/":
                if (IsZero(right))
                {
                    // An integer zero divisor is zero_divisor even for 0/0; only the float
                    // 0.0/0.0, whose IEEE result is NaN, is undefined.
                    throw PrologErrors.Evaluation(machine, real && left.AsDouble == 0 ? "undefined" : "zero_divisor");
                }

                return FloatResult(machine, left.AsDouble / right.AsDouble);

            case "//":
            {
                RequireIntegers(machine, left, right);
                ThrowIfZeroInteger(machine, right);
                if (!left.IsBig && !right.IsBig)
                {
                    // long.MinValue cannot occur: fixnums are 60-bit, so the quotient fits.
                    return PrologNumber.FromInteger(left.Integer / right.Integer);
                }

                return PrologNumber.FromBig(BigInteger.Divide(left.Big, right.Big));
            }

            case "div":
            {
                RequireIntegers(machine, left, right);
                ThrowIfZeroInteger(machine, right);
                if (!left.IsBig && !right.IsBig)
                {
                    return PrologNumber.FromInteger(FloorDivide(left.Integer, right.Integer));
                }

                return PrologNumber.FromBig(FloorDivideBig(left.Big, right.Big));
            }

            case "mod":
            {
                RequireIntegers(machine, left, right);
                ThrowIfZeroInteger(machine, right);
                if (!left.IsBig && !right.IsBig)
                {
                    var remainder = left.Integer % right.Integer;
                    return PrologNumber.FromInteger(
                        remainder != 0 && (remainder < 0) != (right.Integer < 0) ? remainder + right.Integer : remainder
                    );
                }

                BigInteger bigRemainder = left.Big % right.Big;
                return PrologNumber.FromBig(
                    !bigRemainder.IsZero && bigRemainder.Sign != right.Big.Sign ? bigRemainder + right.Big : bigRemainder
                );
            }

            case "rem":
            {
                RequireIntegers(machine, left, right);
                ThrowIfZeroInteger(machine, right);
                if (!left.IsBig && !right.IsBig)
                {
                    return PrologNumber.FromInteger(left.Integer % right.Integer);
                }

                return PrologNumber.FromBig(left.Big % right.Big);
            }

            case "min":
                return Minimum(left, right);

            case "max":
                return Maximum(left, right);

            case "**":
                return PowerFloat(machine, left.AsDouble, right.AsDouble);

            case "^":
                return real ? PowerFloat(machine, left.AsDouble, right.AsDouble) : IntegerPower(machine, left, right);

            case "atan2":
                if (left.AsDouble == 0 && right.AsDouble == 0)
                {
                    throw PrologErrors.Evaluation(machine, "undefined");
                }

                return FloatResult(machine, Math.Atan2(left.AsDouble, right.AsDouble));

            case ">>":
                RequireIntegers(machine, left, right);
                return ShiftRight(machine, left, right);

            case "<<":
                RequireIntegers(machine, left, right);
                return ShiftLeft(machine, left, right);

            case "/\\":
                RequireIntegers(machine, left, right);
                if (!left.IsBig && !right.IsBig)
                {
                    return PrologNumber.FromInteger(left.Integer & right.Integer);
                }

                return PrologNumber.FromBig(left.Big & right.Big);

            case "\\/":
                RequireIntegers(machine, left, right);
                if (!left.IsBig && !right.IsBig)
                {
                    return PrologNumber.FromInteger(left.Integer | right.Integer);
                }

                return PrologNumber.FromBig(left.Big | right.Big);

            case "xor":
                RequireIntegers(machine, left, right);
                if (!left.IsBig && !right.IsBig)
                {
                    return PrologNumber.FromInteger(left.Integer ^ right.Integer);
                }

                return PrologNumber.FromBig(left.Big ^ right.Big);

            default:
                throw Unevaluable(machine, functorId);
        }
    }

    private static PrologNumber Negate(Machine machine, PrologNumber value)
    {
        if (value.IsFloat)
        {
            return FloatResult(machine, -value.Real);
        }

        return value.IsBig ? PrologNumber.FromBig(-value.Big) : PrologNumber.FromInteger(-value.Integer);
    }

    private static PrologNumber Complement(Machine machine, PrologNumber value)
    {
        RequireIntegers(machine, value, value);
        return value.IsBig ? PrologNumber.FromBig(~value.Big) : PrologNumber.FromInteger(~value.Integer);
    }

    private static PrologNumber IntegerPower(Machine machine, PrologNumber left, PrologNumber right)
    {
        if (right.IsBig ? right.Big.Sign < 0 : right.Integer < 0)
        {
            if (!left.IsBig && left.Integer == 0)
            {
                throw PrologErrors.Evaluation(machine, "zero_divisor");
            }

            if (left.IsBig || left.Integer is < -1 or > 1)
            {
                throw PrologErrors.Type(machine, "float", ToCell(machine, left));
            }

            var oddExponent = right.IsBig ? !right.Big.IsEven : (right.Integer & 1) != 0;
            return PrologNumber.FromInteger(left.Integer == -1 && oddExponent ? -1 : 1);
        }

        if (!right.IsBig && right.Integer == 0)
        {
            // Anything to the zeroth power is 1, including ISO's 0^0.
            return PrologNumber.FromInteger(1);
        }

        // Bases whose powers stay small are answered before the magnitude guard, so an
        // enormous exponent over 0, 1, or -1 does not trip the resource check.
        if (!left.IsBig && left.Integer is >= -1 and <= 1)
        {
            var oddPower = right.IsBig ? !right.Big.IsEven : (right.Integer & 1) != 0;
            return PrologNumber.FromInteger(left.Integer == -1 && !oddPower ? 1 : left.Integer);
        }

        if (right.IsBig || right.Integer > int.MaxValue)
        {
            // The result would need more bits than a BigInteger can hold; SWI reports the
            // equivalent GMP allocation failure as a resource error.
            throw PrologErrors.Resource(machine, "memory");
        }

        return PrologNumber.FromBig(BigInteger.Pow(left.Big, (int)right.Integer));
    }

    private static PrologNumber Sign(Machine machine, PrologNumber value)
    {
        if (!value.IsFloat)
        {
            return PrologNumber.FromInteger(value.IsBig ? value.Big.Sign : Math.Sign(value.Integer));
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
        var operand = RequireFloat(machine, value);
        var result = operation(operand);

        if (double.IsNaN(result) || double.IsInfinity(result))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        // Exact: an integral double converts to BigInteger without rounding, and FromBig
        // normalizes back to a fixnum when it fits.
        return PrologNumber.FromBig(new BigInteger(result));
    }

    private static PrologNumber FloatPart(Machine machine, PrologNumber value, bool fractional)
    {
        var operand = RequireFloat(machine, value);
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

    private static void RequireIntegers(Machine machine, PrologNumber left, PrologNumber right)
    {
        if (left.IsFloat)
        {
            throw PrologErrors.Type(machine, "integer", ToCell(machine, left));
        }

        if (right.IsFloat)
        {
            throw PrologErrors.Type(machine, "integer", ToCell(machine, right));
        }
    }

    private static double RequireFloat(Machine machine, PrologNumber value)
    {
        if (value.IsFloat)
        {
            return value.Real;
        }

        throw PrologErrors.Type(machine, "float", ToCell(machine, value));
    }

    private static PrologNumber Minimum(PrologNumber left, PrologNumber right)
    {
        var order = Compare(left, right);
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
        var order = Compare(left, right);
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
        var quotient = left / right;
        var remainder = left % right;
        return remainder != 0 && (remainder < 0) != (right < 0) ? quotient - 1 : quotient;
    }

    private static BigInteger FloorDivideBig(BigInteger left, BigInteger right)
    {
        BigInteger quotient = BigInteger.DivRem(left, right, out BigInteger remainder);
        return !remainder.IsZero && remainder.Sign != right.Sign ? quotient - 1 : quotient;
    }

    private static PrologNumber ShiftLeft(Machine machine, PrologNumber value, PrologNumber count)
    {
        if (count.IsBig ? count.Big.Sign < 0 : count.Integer < 0)
        {
            return ShiftRight(machine, value, Negate(machine, count));
        }

        if (!value.IsBig && value.Integer == 0)
        {
            return PrologNumber.FromInteger(0);
        }

        if (count.IsBig || count.Integer > int.MaxValue)
        {
            throw PrologErrors.Resource(machine, "memory");
        }

        return PrologNumber.FromBig(value.Big << (int)count.Integer);
    }

    private static PrologNumber ShiftRight(Machine machine, PrologNumber value, PrologNumber count)
    {
        if (count.IsBig ? count.Big.Sign < 0 : count.Integer < 0)
        {
            return ShiftLeft(machine, value, Negate(machine, count));
        }

        if (count.IsBig || count.Integer > int.MaxValue)
        {
            // Every bit is shifted out; what remains is the sign, as an arithmetic shift.
            var negative = value.IsBig ? value.Big.Sign < 0 : value.Integer < 0;
            return PrologNumber.FromInteger(negative ? -1 : 0);
        }

        if (!value.IsBig)
        {
            var shift = count.Integer;
            return PrologNumber.FromInteger(shift >= 60 ? (value.Integer < 0 ? -1 : 0) : value.Integer >> (int)shift);
        }

        return PrologNumber.FromBig(value.Big >> (int)count.Integer);
    }

    private static bool IsZero(PrologNumber value) =>
        value.IsFloat ? value.Real == 0
        : value.IsBig ? false
        : value.Integer == 0;

    private static void ThrowIfZeroInteger(Machine machine, PrologNumber divisor)
    {
        if (!divisor.IsFloat && !divisor.IsBig && divisor.Integer == 0)
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
