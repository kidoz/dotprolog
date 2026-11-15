using System.Numerics;

namespace DotProlog.Runtime;

/// <summary>
/// Evaluates arithmetic expressions for <c>is/2</c> and the arithmetic comparisons.
/// </summary>
/// <remarks>
/// <para>
/// Evaluation uses an explicit post-order work stack, so runtime-built expression depth does not
/// consume the CLR stack and remains independent of Prolog call depth.
/// </para>
/// <para>
/// Integer arithmetic is unbounded: operations run on <see cref="long"/> while both
/// operands are fixnums and redo in <see cref="BigInteger"/> when the 64-bit operation overflows,
/// and every integer result is normalized so values in the fixnum range never take the big
/// representation. Rationals join through <c>rdiv/2</c> and literals: arithmetic mixing
/// integers and rationals is exact, while a float operand widens the whole operation to double.
/// </para>
/// <para>
/// Stored floats are finite binary64 values. The Modern <c>inf</c>, <c>infinite</c>, and <c>nan</c>
/// names remain recognized extensions, but evaluating them raises <c>float_overflow</c> or
/// <c>undefined</c> rather than creating a non-finite float; signed zero canonicalizes to positive zero.
/// </para>
/// </remarks>
public static class ArithmeticEvaluator
{
    private const long DefaultMaxResultBytes = 64 * 1024 * 1024;

    /// <summary>Evaluates <paramref name="expression"/> to a number.</summary>
    /// <param name="machine">Machine owning the heap the expression lives on.</param>
    /// <param name="expression">The expression term.</param>
    /// <exception cref="PrologException">
    /// The expression is unbound or not evaluable, or an integer power or left shift exceeds the
    /// 64 MiB estimated result-payload budget with <c>resource_error(memory)</c>.
    /// </exception>
    public static PrologNumber Evaluate(Machine machine, Cell expression) => Evaluate(machine, expression, DefaultMaxResultBytes);

    internal static PrologNumber Evaluate(Machine machine, Cell expression, long maxResultBytes)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResultBytes);

        List<EvaluationItem> work = [EvaluationItem.Term(expression)];
        List<PrologNumber> values = [];
        while (work.Count > 0)
        {
            machine.CancellationToken.ThrowIfCancellationRequested();
            var item = work[^1];
            work.RemoveAt(work.Count - 1);
            if (item.FunctorId >= 0)
            {
                var applied = machine.Symbols.GetFunctor(item.FunctorId);
                var appliedName = machine.Symbols.AtomName(applied.NameAtom);
                if (applied.Arity == 1)
                {
                    var operand = values[^1];
                    values.RemoveAt(values.Count - 1);
                    values.Add(EvaluateUnary(appliedName, operand, machine, item.FunctorId));
                    continue;
                }

                var right = values[^1];
                var left = values[^2];
                values.RemoveRange(values.Count - 2, 2);
                values.Add(EvaluateBinary(appliedName, left, right, machine, item.FunctorId, maxResultBytes));
                continue;
            }

            var cell = machine.Dereference(item.Cell);
            switch (cell.Tag)
            {
                case CellTag.Integer:
                    values.Add(PrologNumber.FromInteger(cell.Integer));
                    continue;

                case CellTag.BigInteger:
                    values.Add(PrologNumber.FromBig(machine.Symbols.GetBig(cell.Index)));
                    continue;

                case CellTag.Rational:
                {
                    (var numerator, var denominator) = machine.Symbols.GetRational(cell.Index);
                    values.Add(PrologNumber.FromRational(numerator, denominator));
                    continue;
                }

                case CellTag.Float:
                    values.Add(PrologNumber.FromReal(machine.Symbols.GetFloat(cell.Index)));
                    continue;

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

                    values.Add(EvaluateConstant(machine, constantName));
                    continue;
                }

                case CellTag.Structure:
                    break;

                default:
                    throw PrologErrors.Type(machine, "evaluable", cell);
            }

            var functorId = machine.HeapAt(cell.Index).Index;
            var functor = machine.Symbols.GetFunctor(functorId);
            var name = machine.Symbols.AtomName(functor.NameAtom);
            if (
                machine.Program.LanguageMode == PrologLanguageMode.StrictIso
                && !IsoLanguageProfile.IsStandardEvaluable(name, functor.Arity)
            )
            {
                throw Unevaluable(machine, functorId);
            }

            if (functor.Arity is not 1 and not 2)
            {
                throw Unevaluable(machine, functorId);
            }

            work.Add(EvaluationItem.Apply(functorId));
            for (var argument = functor.Arity; argument >= 1; argument--)
            {
                work.Add(EvaluationItem.Term(machine.HeapAt(cell.Index + argument)));
            }
        }

        return values[^1].WithOwner(machine);
    }

    /// <summary>Whether <c>name/arity</c> is an evaluable function or constant in the full language surface.</summary>
    internal static bool IsKnownEvaluable(string name, int arity) =>
        (name, arity) switch
        {
            ("pi" or "e" or "inf" or "infinite" or "nan" or "max_tagged_integer" or "min_tagged_integer", 0) => true,
            (
                "+"
                    or "-"
                    or "abs"
                    or "sign"
                    or "float"
                    or "integer"
                    or "truncate"
                    or "floor"
                    or "ceiling"
                    or "round"
                    or "float_integer_part"
                    or "float_fractional_part"
                    or "sqrt"
                    or "sin"
                    or "cos"
                    or "tan"
                    or "asin"
                    or "acos"
                    or "atan"
                    or "exp"
                    or "log"
                    or "\\"
                    or "numerator"
                    or "denominator"
                    or "rational"
                    or "rationalize",
                1
            ) => true,
            (
                "+"
                    or "-"
                    or "*"
                    or "/"
                    or "rdiv"
                    or "//"
                    or "div"
                    or "mod"
                    or "rem"
                    or "min"
                    or "max"
                    or "**"
                    or "^"
                    or "atan2"
                    or ">>"
                    or "<<"
                    or "/\\"
                    or "\\/"
                    or "xor",
                2
            ) => true,
            _ => false,
        };

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
    public static int Compare(PrologNumber left, PrologNumber right) => Compare(left.Owner ?? right.Owner, left, right);

    private static int Compare(Machine? machine, PrologNumber left, PrologNumber right)
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

        var leftFloat = ConvertedDouble(machine, left);
        var rightFloat = ConvertedDouble(machine, right);
        return leftFloat.CompareTo(rightFloat);
    }

    private static PrologNumber EvaluateConstant(Machine machine, string name) =>
        name switch
        {
            "pi" => PrologNumber.FromReal(Math.PI),
            "e" => PrologNumber.FromReal(Math.E),
            "inf" or "infinite" => throw PrologErrors.Evaluation(machine, "float_overflow"),
            "nan" => throw PrologErrors.Evaluation(machine, "undefined"),
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
            "float" => FloatResult(machine, ConvertedDouble(machine, value)),
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
                : FloatToInteger(machine, value, RoundFloat),
            "float_integer_part" => FloatPart(machine, value, fractional: false),
            "float_fractional_part" => FloatPart(machine, value, fractional: true),
            "sqrt" => SquareRoot(machine, value),
            "sin" => Trigonometric(machine, value, Math.Sin),
            "cos" => Trigonometric(machine, value, Math.Cos),
            "tan" => Trigonometric(machine, value, Math.Tan),
            "asin" => FloatResult(machine, Math.Asin(value.AsDouble)),
            "acos" => FloatResult(machine, Math.Acos(value.AsDouble)),
            "atan" => FloatResult(machine, Math.Atan(value.AsDouble)),
            "exp" => FloatResult(machine, Math.Exp(value.AsDouble), underflow: true),
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

    private static PrologNumber EvaluateBinary(
        string name,
        PrologNumber left,
        PrologNumber right,
        Machine machine,
        int functorId,
        long maxResultBytes
    )
    {
        var real = left.IsFloat || right.IsFloat;
        var rational = !real && (left.IsRational || right.IsRational);

        switch (name)
        {
            case "+":
                if (real)
                {
                    return FloatResult(machine, ConvertedDouble(machine, left) + ConvertedDouble(machine, right));
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
                    return FloatResult(machine, ConvertedDouble(machine, left) - ConvertedDouble(machine, right));
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
                    return FloatResult(machine, ConvertedDouble(machine, left) * ConvertedDouble(machine, right));
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
                if (rational)
                {
                    if (IsZero(right))
                    {
                        throw PrologErrors.Evaluation(machine, "zero_divisor");
                    }

                    // Division with a rational operand stays exact as an extension.
                    // Two integer operands take the ISO-required float path below.
                    return PrologNumber.FromRational(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
                }

                var dividend = ConvertedDouble(machine, left);
                var divisor = ConvertedDouble(machine, right);
                if (divisor == 0)
                {
                    throw PrologErrors.Evaluation(machine, "zero_divisor");
                }

                return FloatResult(machine, dividend / divisor);

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

                var bigRemainder = left.Big % right.Big;
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
                return Minimum(machine, left, right);

            case "max":
                return Maximum(machine, left, right);

            case "**":
                return PowerFloat(machine, left, right);

            case "^":
                if (real || right.IsRational)
                {
                    return PowerFloat(machine, left, right);
                }

                return IntegerPower(machine, left, right, maxResultBytes);

            case "atan2":
                if (left.AsDouble == 0 && right.AsDouble == 0)
                {
                    throw PrologErrors.Evaluation(machine, "undefined");
                }

                return FloatResult(machine, Math.Atan2(left.AsDouble, right.AsDouble));

            case ">>":
                RequireIntegers(machine, left, right);
                return ShiftRight(machine, left, right, maxResultBytes);

            case "<<":
                RequireIntegers(machine, left, right);
                return ShiftLeft(machine, left, right, maxResultBytes);

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

    private static PrologNumber IntegerPower(Machine machine, PrologNumber left, PrologNumber right, long maxResultBytes)
    {
        if (right.IsBig ? right.Big.Sign < 0 : right.Integer < 0)
        {
            if (left.IsInteger)
            {
                if (!left.IsBig && left.Integer == 0)
                {
                    throw PrologErrors.Evaluation(machine, "undefined");
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
            var inverse = PrologNumber.FromRational(left.Denominator, left.Numerator);
            return IntegerPower(machine, inverse, Negate(machine, right), maxResultBytes);
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
            throw PrologErrors.Resource(machine, "memory");
        }

        var exponent = (int)right.Integer;
        EnsurePowerResultWithinBudget(machine, left.Numerator, exponent, maxResultBytes);
        if (left.IsRational)
        {
            EnsurePowerResultWithinBudget(machine, left.Denominator, exponent, maxResultBytes);
            return PrologNumber.FromRational(
                BigInteger.Pow(left.Numerator, exponent),
                BigInteger.Pow(left.Denominator, exponent)
            );
        }

        return PrologNumber.FromBig(BigInteger.Pow(left.Big, exponent));
    }

    private static void EnsurePowerResultWithinBudget(Machine machine, BigInteger value, int exponent, long maxResultBytes)
    {
        var magnitude = BigInteger.Abs(value);
        var estimatedBits = magnitude.IsPowerOfTwo
            ? ((magnitude.GetBitLength() - 1) * (double)exponent) + 1
            : Math.Floor(BigInteger.Log(magnitude, 2) * exponent) + 2;
        if (!double.IsFinite(estimatedBits) || estimatedBits > maxResultBytes * 8.0)
        {
            throw PrologErrors.Resource(machine, "memory");
        }
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
        var magnitude = BigInteger.Divide((2 * BigInteger.Abs(value.Numerator)) + value.Denominator, 2 * value.Denominator);
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

    private static double RoundFloat(double value)
    {
        var floor = Math.Floor(value);
        return value - floor < 0.5 ? floor : floor + 1;
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
        if (IsZero(value))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        if (value.Numerator.Sign < 0)
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        var operand = value.AsDouble;
        return FloatResult(machine, double.IsFinite(operand) ? Math.Log(operand) : NaturalLogMagnitude(value));
    }

    private static PrologNumber PowerFloat(Machine machine, PrologNumber left, PrologNumber right)
    {
        if (IsZero(right))
        {
            return PrologNumber.FromReal(1.0);
        }

        if (IsZero(left))
        {
            if (right.IsFloat ? right.Real < 0 : right.Numerator.Sign < 0)
            {
                throw PrologErrors.Evaluation(machine, "undefined");
            }

            return PrologNumber.FromReal(0.0);
        }

        var leftOperand = left.AsDouble;
        var rightOperand = right.AsDouble;
        if (right.IsInteger && leftOperand is 1.0 or -1.0)
        {
            var negative = leftOperand < 0 && (right.IsBig ? !right.Big.IsEven : (right.Integer & 1) != 0);
            return PrologNumber.FromReal(negative ? -1.0 : 1.0);
        }

        if (double.IsFinite(leftOperand))
        {
            return FloatResult(machine, Math.Pow(leftOperand, rightOperand), underflow: true);
        }

        if (left.Numerator.Sign < 0 && !right.IsInteger)
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        if (rightOperand == 0.5)
        {
            return SquareRoot(machine, left);
        }

        var result = Math.Exp(rightOperand * NaturalLogMagnitude(left));
        if (left.Numerator.Sign < 0 && (right.IsBig ? !right.Big.IsEven : (right.Integer & 1) != 0))
        {
            result = -result;
        }

        return FloatResult(machine, result, underflow: true);
    }

    private static PrologNumber SquareRoot(Machine machine, PrologNumber value)
    {
        var operand = value.AsDouble;
        if (double.IsFinite(operand))
        {
            return FloatResult(machine, Math.Sqrt(operand));
        }

        if (value.Numerator.Sign < 0)
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        if (value.IsBig)
        {
            var shift = (int)(value.Big.GetBitLength() - 53);
            var significand = (double)(value.Big >> shift);
            var root = Math.Sqrt((shift & 1) == 0 ? significand : 2 * significand);
            return FloatResult(machine, Math.ScaleB(root, shift / 2));
        }

        return FloatResult(machine, Math.Exp(NaturalLogMagnitude(value) / 2));
    }

    private static double NaturalLogMagnitude(PrologNumber value)
    {
        var numerator = BigInteger.Abs(value.Numerator);
        var result = NaturalLog(numerator);
        return value.IsRational ? result - NaturalLog(value.Denominator) : result;
    }

    private static double NaturalLog(BigInteger value)
    {
        var bitLength = value.GetBitLength();
        var shift = (int)Math.Max(0, bitLength - 53);
        var significand = (double)(value >> shift);
        return Math.Log(significand) + (shift * Math.Log(2.0));
    }

    private static PrologNumber Trigonometric(Machine machine, PrologNumber value, Func<double, double> operation)
    {
        var operand = value.AsDouble;
        if (double.IsFinite(operand))
        {
            return FloatResult(machine, operation(operand));
        }

        var inputBits = Math.Max(1, value.Numerator.GetBitLength() - value.Denominator.GetBitLength());
        var precision = checked((int)(inputBits + 80));
        var scale = BigInteger.One << precision;
        var pi = FixedPi(precision);
        var denominator = value.Denominator;
        var divisor = 2 * pi * denominator;
        var remainder = BigInteger.Remainder(BigInteger.Abs(value.Numerator) << precision, divisor);
        if (value.Numerator.Sign < 0 && !remainder.IsZero)
        {
            remainder = divisor - remainder;
        }

        var reduced = PrologNumber.FromRational(remainder, denominator << precision).AsDouble;
        return FloatResult(machine, operation(reduced));
    }

    private static BigInteger FixedPi(int precision)
    {
        var scale = BigInteger.One << precision;
        return (16 * FixedArcTangent(5, scale)) - (4 * FixedArcTangent(239, scale));
    }

    private static BigInteger FixedArcTangent(int inverse, BigInteger scale)
    {
        BigInteger squared = inverse * inverse;
        var power = scale / inverse;
        var sum = power;
        var term = 1;
        while (!power.IsZero)
        {
            power /= squared;
            var addend = power / ((2 * term) + 1);
            sum = term % 2 == 0 ? sum + addend : sum - addend;
            term++;
        }

        return sum;
    }

    private static double ConvertedDouble(Machine? machine, PrologNumber value)
    {
        var result = value.AsDouble;
        if (double.IsFinite(result))
        {
            return result;
        }

        if (machine is not null)
        {
            throw PrologErrors.Evaluation(machine, "float_overflow");
        }

        throw new PrologException("evaluation_error(float_overflow)");
    }

    private static PrologNumber FloatResult(Machine machine, double value, bool underflow = false)
    {
        if (double.IsNaN(value))
        {
            throw PrologErrors.Evaluation(machine, "undefined");
        }

        if (double.IsInfinity(value))
        {
            throw PrologErrors.Evaluation(machine, "float_overflow");
        }

        if (underflow && value == 0)
        {
            throw PrologErrors.Evaluation(machine, "underflow");
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

    private static PrologNumber Minimum(Machine machine, PrologNumber left, PrologNumber right)
    {
        var order = Compare(machine, left, right);
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

    private static PrologNumber Maximum(Machine machine, PrologNumber left, PrologNumber right)
    {
        var order = Compare(machine, left, right);
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
        var quotient = BigInteger.DivRem(left, right, out var remainder);
        return !remainder.IsZero && remainder.Sign != right.Sign ? quotient - 1 : quotient;
    }

    private static PrologNumber ShiftLeft(Machine machine, PrologNumber value, PrologNumber count, long maxResultBytes)
    {
        if (count.IsBig ? count.Big.Sign < 0 : count.Integer < 0)
        {
            return ShiftRight(machine, value, Negate(machine, count), maxResultBytes);
        }

        if (!value.IsBig && value.Integer == 0)
        {
            return PrologNumber.FromInteger(0);
        }

        var estimatedBits = BigInteger.Abs(value.Big).GetBitLength() + count.Big;
        if (estimatedBits > (BigInteger)maxResultBytes * 8)
        {
            throw PrologErrors.Resource(machine, "memory");
        }

        if (count.IsBig || count.Integer > int.MaxValue)
        {
            throw PrologErrors.Resource(machine, "memory");
        }

        return PrologNumber.FromBig(value.Big << (int)count.Integer);
    }

    private static PrologNumber ShiftRight(Machine machine, PrologNumber value, PrologNumber count, long maxResultBytes)
    {
        if (count.IsBig ? count.Big.Sign < 0 : count.Integer < 0)
        {
            return ShiftLeft(machine, value, Negate(machine, count), maxResultBytes);
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
        var functor = machine.Symbols.GetFunctor(functorId);
        return PrologErrors.NotEvaluable(machine, machine.Symbols.AtomName(functor.NameAtom), functor.Arity);
    }

    private readonly record struct EvaluationItem(Cell Cell, int FunctorId)
    {
        internal static EvaluationItem Term(Cell cell) => new(cell, -1);

        internal static EvaluationItem Apply(int functorId) => new(default, functorId);
    }
}
