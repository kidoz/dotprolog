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
/// representation. Rationals join through <c>rdiv/2</c> and literals: arithmetic mixing
/// integers and rationals is exact, while a float operand widens the whole operation to double.
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

            case CellTag.Rational:
            {
                (BigInteger numerator, BigInteger denominator) = machine.Symbols.GetRational(cell.Index);
                return PrologNumber.FromRational(numerator, denominator);
            }

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

        if (number.IsRational)
        {
            return Cell.Rational(machine.Symbols.InternRational(number.Numerator, number.Denominator));
        }

        if (number.IsBig)
        {
            return Cell.Big(machine.Symbols.InternBig(number.Big));
        }

        return Cell.Integer60(number.Integer);
    }

    /// <summary>
    /// Orders two numbers, comparing integers and rationals exactly and mixed float kinds by
    /// widening to double.
    /// </summary>
    public static int Compare(PrologNumber left, PrologNumber right)
    {
        if (!left.IsFloat && !right.IsFloat)
        {
            if (!left.IsBig && !right.IsBig && !left.IsRational && !right.IsRational)
            {
                return left.Integer.CompareTo(right.Integer);
            }

            if (!left.IsRational && !right.IsRational)
            {
                return left.Big.CompareTo(right.Big);
            }

            // Cross-multiplying is exact; both denominators are positive by canonical form.
            return (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);
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
            : value.IsRational ? PrologNumber.FromRational(BigInteger.Abs(value.Numerator), value.Denominator)
            : value.IsBig ? PrologNumber.FromBig(BigInteger.Abs(value.Big))
            : PrologNumber.FromInteger(Math.Abs(value.Integer)),
            "sign" => Sign(machine, value),
            "float" => FloatResult(machine, value.AsDouble),
            "integer" => value.IsFloat
                ? FloatToInteger(machine, value, static operand => Math.Round(operand, MidpointRounding.AwayFromZero))
            : value.IsRational ? RationalAwayFromZero(value)
            : value,
            "truncate" => value.IsRational
                ? PrologNumber.FromBig(BigInteger.Divide(value.Numerator, value.Denominator))
                : FloatToInteger(machine, value, Math.Truncate),
            "floor" => value.IsRational
                ? PrologNumber.FromBig(FloorDivideBig(value.Numerator, value.Denominator))
                : FloatToInteger(machine, value, Math.Floor),
            "ceiling" => value.IsRational
                ? PrologNumber.FromBig(-FloorDivideBig(-value.Numerator, value.Denominator))
                : FloatToInteger(machine, value, Math.Ceiling),
            "round" => value.IsRational
                ? PrologNumber.FromBig(FloorDivideBig((2 * value.Numerator) + value.Denominator, 2 * value.Denominator))
                : FloatToInteger(machine, value, static operand => Math.Floor(operand + 0.5)),
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
            "numerator" => value.IsFloat
                ? throw PrologErrors.Type(machine, "rational", ToCell(machine, value))
                : PrologNumber.FromBig(value.Numerator),
            "denominator" => value.IsFloat
                ? throw PrologErrors.Type(machine, "rational", ToCell(machine, value))
                : PrologNumber.FromBig(value.Denominator),
            "rational" => value.IsFloat ? ExactRational(machine, value.Real) : value,
            "rationalize" => value.IsFloat ? Rationalize(machine, value.Real) : value,
            _ => throw Unevaluable(machine, functorId),
        };

    private static PrologNumber EvaluateBinary(string name, PrologNumber left, PrologNumber right, Machine machine, int functorId)
    {
        var real = left.IsFloat || right.IsFloat;
        var rational = !real && (left.IsRational || right.IsRational);

        switch (name)
        {
            case "+":
                if (real)
                {
                    return FloatResult(machine, left.AsDouble + right.AsDouble);
                }

                if (rational)
                {
                    return PrologNumber.FromRational(
                        (left.Numerator * right.Denominator) + (right.Numerator * left.Denominator),
                        left.Denominator * right.Denominator
                    );
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

                if (rational)
                {
                    return PrologNumber.FromRational(
                        (left.Numerator * right.Denominator) - (right.Numerator * left.Denominator),
                        left.Denominator * right.Denominator
                    );
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

                if (rational)
                {
                    return PrologNumber.FromRational(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
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

                if (rational)
                {
                    // Division with a rational operand stays exact; two integers keep the
                    // documented processor choice of float division.
                    return PrologNumber.FromRational(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
                }

                return FloatResult(machine, left.AsDouble / right.AsDouble);

            case "rdiv":
                if (left.IsFloat)
                {
                    throw PrologErrors.Type(machine, "rational", ToCell(machine, left));
                }

                if (right.IsFloat)
                {
                    throw PrologErrors.Type(machine, "rational", ToCell(machine, right));
                }

                if (IsZero(right))
                {
                    throw PrologErrors.Evaluation(machine, "zero_divisor");
                }

                return PrologNumber.FromRational(left.Numerator * right.Denominator, left.Denominator * right.Numerator);

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
                if (real || right.IsRational)
                {
                    return PowerFloat(machine, left.AsDouble, right.AsDouble);
                }

                return IntegerPower(machine, left, right);

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

        if (value.IsRational)
        {
            return PrologNumber.FromRational(-value.Numerator, value.Denominator);
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
            if (left.IsInteger)
            {
                if (!left.IsBig && left.Integer == 0)
                {
                    throw PrologErrors.Evaluation(machine, "zero_divisor");
                }

                if (left.IsBig || left.Integer is < -1 or > 1)
                {
                    // ISO's error for an unrepresentable integer power; SWI's rational answer
                    // is deliberately not adopted for integer bases.
                    throw PrologErrors.Type(machine, "float", ToCell(machine, left));
                }

                var oddExponent = right.IsBig ? !right.Big.IsEven : (right.Integer & 1) != 0;
                return PrologNumber.FromInteger(left.Integer == -1 && oddExponent ? -1 : 1);
            }

            // A rational base inverts exactly; its numerator is never zero in canonical form.
            PrologNumber inverse = PrologNumber.FromRational(left.Denominator, left.Numerator);
            return IntegerPower(machine, inverse, Negate(machine, right));
        }

        if (!right.IsBig && right.Integer == 0)
        {
            // Anything to the zeroth power is 1, including ISO's 0^0.
            return PrologNumber.FromInteger(1);
        }

        // Bases whose powers stay small are answered before the magnitude guard, so an
        // enormous exponent over 0, 1, or -1 does not trip the resource check.
        if (left.IsInteger && !left.IsBig && left.Integer is >= -1 and <= 1)
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

        var exponent = (int)right.Integer;
        if (left.IsRational)
        {
            return PrologNumber.FromRational(
                BigInteger.Pow(left.Numerator, exponent),
                BigInteger.Pow(left.Denominator, exponent)
            );
        }

        return PrologNumber.FromBig(BigInteger.Pow(left.Big, exponent));
    }

    private static PrologNumber Sign(Machine machine, PrologNumber value)
    {
        if (!value.IsFloat)
        {
            return PrologNumber.FromInteger(value.IsRational ? value.Numerator.Sign : value.Big.Sign);
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

    /// <summary>Rounds a rational to the nearest integer, ties away from zero, as SWI's <c>integer/1</c> does.</summary>
    private static PrologNumber RationalAwayFromZero(PrologNumber value)
    {
        BigInteger magnitude = BigInteger.Divide(
            (2 * BigInteger.Abs(value.Numerator)) + value.Denominator,
            2 * value.Denominator
        );
        return PrologNumber.FromBig(value.Numerator.Sign < 0 ? -magnitude : magnitude);
    }

    /// <summary>The exact rational value of a finite double: its mantissa scaled by its exponent.</summary>
    private static PrologNumber ExactRational(Machine machine, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponentBits = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFF;
        var exponent = exponentBits == 0 ? -1074 : exponentBits - 1075;
        if (exponentBits != 0)
        {
            mantissa |= 1L << 52;
        }

        BigInteger numerator = value < 0 ? -mantissa : mantissa;
        return exponent >= 0
            ? PrologNumber.FromBig(numerator << exponent)
            : PrologNumber.FromRational(numerator, BigInteger.One << -exponent);
    }

    /// <summary>
    /// The simplest rational that reads back as the same double, found by walking the float's
    /// continued-fraction convergents — SWI's <c>rationalize/1</c>.
    /// </summary>
    private static PrologNumber Rationalize(Machine machine, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        BigInteger previousNumerator = 0;
        BigInteger previousDenominator = 1;
        BigInteger numerator = 1;
        BigInteger denominator = 0;
        var rest = Math.Abs(value);

        while (true)
        {
            var whole = Math.Floor(rest);
            BigInteger term = new(whole);
            (numerator, previousNumerator) = ((term * numerator) + previousNumerator, numerator);
            (denominator, previousDenominator) = ((term * denominator) + previousDenominator, denominator);

            var fraction = rest - whole;
            if ((double)numerator / (double)denominator == Math.Abs(value) || fraction <= 0)
            {
                return PrologNumber.FromRational(value < 0 ? -numerator : numerator, denominator);
            }

            rest = 1.0 / fraction;
        }
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
        if (!left.IsInteger)
        {
            throw PrologErrors.Type(machine, "integer", ToCell(machine, left));
        }

        if (!right.IsInteger)
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
        value.IsFloat ? value.Real == 0 : value.IsInteger && !value.IsBig && value.Integer == 0;

    private static void ThrowIfZeroInteger(Machine machine, PrologNumber divisor)
    {
        if (divisor.IsInteger && !divisor.IsBig && divisor.Integer == 0)
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
