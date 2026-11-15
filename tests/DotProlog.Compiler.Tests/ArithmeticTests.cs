using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>Arithmetic evaluation through <c>is/2</c> and the arithmetic comparisons.</summary>
public sealed class ArithmeticTests
{
    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void MinAndMaxCompareExactOperandsWithoutFloatConversion(PrologLanguageMode mode)
    {
        using var engine = new PrologEngine(mode);

        Assert.True(engine.Query("min(9007199254740993,9007199254740992) =:= 9007199254740992").Prove());
        Assert.True(engine.Query("max(9007199254740992,9007199254740993) =:= 9007199254740993").Prove());
        Assert.True(engine.Query("min(1<<2000,1<<2001) =:= 1<<2000").Prove());
        Assert.True(engine.Query("max(1<<2000,1<<2001) =:= 1<<2001").Prove());
        if (mode == PrologLanguageMode.Modern)
        {
            Assert.True(engine.Query("min(9007199254740993 rdiv 2,4503599627370496) =:= 4503599627370496").Prove());
            Assert.True(engine.Query("max(4503599627370496,9007199254740993 rdiv 2) =:= 9007199254740993 rdiv 2").Prove());
        }
    }

    [Theory]
    [InlineData("1 + 2", "3")]
    [InlineData("3 + 4 * 2", "11")]
    [InlineData("(3 + 4) * 2", "14")]
    [InlineData("7 - 2 - 1", "4")]
    [InlineData("7 // 2", "3")]
    [InlineData("-7 // 2", "-3")]
    [InlineData("7 mod 3", "1")]
    [InlineData("-7 mod 3", "2")]
    [InlineData("-7 rem 3", "-1")]
    [InlineData("6 / 3", "2.0")]
    [InlineData("7 / 2", "3.5")]
    [InlineData("abs(-4)", "4")]
    [InlineData("min(3, 5)", "3")]
    [InlineData("max(3, 5)", "5")]
    [InlineData("2 ^ 10", "1024")]
    [InlineData("sqrt(9)", "3.0")]
    [InlineData("round(-1.5)", "-1")]
    [InlineData("truncate(-1.9)", "-1")]
    [InlineData("floor(-1.1)", "-2")]
    [InlineData("ceiling(-1.1)", "-1")]
    [InlineData("float_integer_part(-1.25)", "-1.0")]
    [InlineData("float_fractional_part(-1.25)", "-0.25")]
    [InlineData("\\ 1", "-2")]
    [InlineData("- (3)", "-3")]
    public void EvaluatesExpressions(string expression, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"X is {expression}, write(X)"));
    }

    [Theory]
    [InlineData("\\ 10", "-11")]
    [InlineData("-10 \\/ 12", "-2")]
    [InlineData("-10 /\\ 12", "4")]
    [InlineData("xor(-10, 12)", "-6")]
    [InlineData("-16 << 2", "-64")]
    [InlineData("-16 >> 2", "-4")]
    public void PinsImplementationDefinedTwoComplementBitwiseResults(string expression, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"X is {expression}, write(X)"));
    }

    [Theory]
    [InlineData("1 < 2")]
    [InlineData("2 >= 2")]
    [InlineData("2 =:= 2")]
    [InlineData("2 =\\= 3")]
    [InlineData("1.5 < 2")]
    public void ComparisonsSucceedWhenTheyHold(string comparison)
    {
        Assert.Equal("yes", PrologTestHost.RunGoal($"{comparison}, write(yes)"));
    }

    [Fact]
    public void ComparisonFailureBacktracksRatherThanThrowing()
    {
        (var result, var output, _) = PrologTestHost.Execute(":- initialization((2 < 1, write(no))).");

        Assert.Equal(RunResult.Success, result);
        Assert.Equal("Warning: initialization goal failed.\n", output);
    }

    [Fact]
    public void DivisionByZeroIsAnEvaluationError()
    {
        using var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(X is 1 // 0).").Success);

        var exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("zero_divisor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluatingAnUnboundVariableIsAnInstantiationError()
    {
        using var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(X is Y + 1).").Success);

        var exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("instantiation_error", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 / 0", "zero_divisor")]
    [InlineData("0 / 0", "zero_divisor")]
    [InlineData("1.0 / 0.0", "zero_divisor")]
    [InlineData("0.0 / 0.0", "zero_divisor")]
    [InlineData("sqrt(-1)", "undefined")]
    [InlineData("log(0)", "undefined")]
    [InlineData("atan2(0, 0)", "undefined")]
    [InlineData("exp(1000)", "float_overflow")]
    [InlineData("floor(1)", "type_error(float, 1)")]
    [InlineData("\\ 1.0", "type_error(integer, 1.0)")]
    public void RaisesIsoArithmeticErrors(string expression, string expected)
    {
        using var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText($":- initialization(X is {expression}).").Success);

        var exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern, "X is 0.0 / 0", "evaluation_error(zero_divisor)")]
    [InlineData(PrologLanguageMode.StrictIso, "X is 0.0 / 0", "evaluation_error(zero_divisor)")]
    [InlineData(PrologLanguageMode.Modern, "X is 0 / 0.0", "evaluation_error(zero_divisor)")]
    [InlineData(PrologLanguageMode.StrictIso, "X is 0 / 0.0", "evaluation_error(zero_divisor)")]
    [InlineData(PrologLanguageMode.Modern, "X is 0.0 / 0.0", "evaluation_error(zero_divisor)")]
    [InlineData(PrologLanguageMode.StrictIso, "X is 0.0 / 0.0", "evaluation_error(zero_divisor)")]
    [InlineData(PrologLanguageMode.Modern, "X is 0 ^ (-1)", "evaluation_error(undefined)")]
    [InlineData(PrologLanguageMode.StrictIso, "X is 0 ^ (-1)", "evaluation_error(undefined)")]
    [InlineData(PrologLanguageMode.Modern, "X is 0.0 ^ (-1)", "evaluation_error(undefined)")]
    [InlineData(PrologLanguageMode.StrictIso, "X is 0.0 ^ (-1)", "evaluation_error(undefined)")]
    [InlineData(PrologLanguageMode.Modern, "X is 0 ** -1", "evaluation_error(undefined)")]
    [InlineData(PrologLanguageMode.StrictIso, "X is 0 ** -1", "evaluation_error(undefined)")]
    [InlineData(PrologLanguageMode.Modern, "X is log(0.0)", "evaluation_error(undefined)")]
    [InlineData(PrologLanguageMode.StrictIso, "X is log(0.0)", "evaluation_error(undefined)")]
    public void CorrectsDivisionPowerAndLogarithmErrors(PrologLanguageMode mode, string goal, string expected) =>
        Assert.Equal(expected, ErrorInMode(mode, goal));

    [Theory]
    [InlineData("exp(-1000)")]
    [InlineData("exp(-(10 ^ 400))")]
    [InlineData("10.0 ** -400")]
    [InlineData("10 ** -400")]
    [InlineData("0.5 ** (10 ^ 400)")]
    [InlineData("(10 ^ 400) ** -1")]
    [InlineData("2.0 ^ (-10000)")]
    [InlineData("0.5 ^ (10 ^ 400)")]
    public void MathematicalFunctionsReportUnderflowInBothModes(string expression)
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Assert.Equal("evaluation_error(underflow)", ErrorInMode(mode, $"X is {expression}"));
        }
    }

    [Theory]
    [InlineData("X is 1 / (10 ^ 400)")]
    [InlineData("X is 2 / (10 ^ 400)")]
    [InlineData("X is 1.0 / (10 ^ 400)")]
    [InlineData("X is 0.0 * (10 ^ 400)")]
    [InlineData("X is (10 ^ 400) / (10 ^ 400)")]
    [InlineData("X is (10 ^ 400) / 0.0")]
    [InlineData("X is 1.0 + (10 ^ 400)")]
    [InlineData("10 ^ 400 =:= 1.0")]
    [InlineData("1.0 < 10 ^ 400")]
    public void MixedFloatOperationsRejectConversionOverflowInBothModes(string goal)
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Assert.Equal("evaluation_error(float_overflow)", ErrorInMode(mode, goal));
        }
    }

    [Theory]
    [InlineData("X is round(0.49999999999999994)", "0")]
    [InlineData("X is round(4503599627370497.0)", "4503599627370497")]
    public void RoundsWithoutPreRoundingTheOffsetInBothModes(string goal, string expected)
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Assert.Equal(expected, RunInMode(mode, $"{goal}, write(X)"));
        }
    }

    [Theory]
    [InlineData("float(2 * (2 ^ 58 + 33)) =:= 2.0 * float(2 ^ 58 + 33)")]
    [InlineData("2 ^ 59 + 96 =\\= 2.0 ^ 59")]
    [InlineData("-(2 ^ 59 + 96) =\\= -(2.0 ^ 59)")]
    [InlineData("float(2 ^ 59 + 64) =:= 2.0 ^ 59")]
    [InlineData("float(2 ^ 59 + 192) =:= 2.0 ^ 59 + 256.0")]
    public void IntegerConversionRoundsConsistentlyInBothModes(string goal)
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Assert.Equal("yes", RunInMode(mode, $"{goal}, write(yes)"));
        }
    }

    [Fact]
    public void WritesCanonicalPositiveZeroRegardlessOfCreationOrder()
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Assert.Equal("0.0-0.0,0.0", RunInMode(mode, "X is -1.0 * 0, Y is 1.0 * 0, writeq(X-Y), write(','), writeq(-0.0)"));
        }
    }

    [Theory]
    [InlineData("inf", "evaluation_error(float_overflow)")]
    [InlineData("infinite", "evaluation_error(float_overflow)")]
    [InlineData("nan", "evaluation_error(undefined)")]
    public void ModernNonFiniteConstantNamesRejectNonFiniteResults(string name, string expected) =>
        Assert.Equal(expected, ErrorInMode(PrologLanguageMode.Modern, $"X is {name}"));

    [Theory]
    [InlineData("X is sin(10 ^ 400), abs(X) =< 1.0")]
    [InlineData("X is cos(10 ^ 400), abs(X) =< 1.0")]
    [InlineData("X is tan(10 ^ 400), float(X)")]
    [InlineData("X is sqrt(10 ^ 400), X > 9.9e199, X < 1.01e200")]
    [InlineData("X is log(10 ^ 400), X > 921.0, X < 921.1")]
    [InlineData("X is (10 ^ 400) ** 0.5, X > 9.9e199, X < 1.01e200")]
    [InlineData("X is (-1) ** (10 ^ 400 + 1), X =:= -1.0")]
    [InlineData("X is (-1.0) ^ (10 ^ 400 + 1), X =:= -1.0")]
    public void MathematicalFunctionsAvoidSpuriousConversionOverflowInBothModes(string goal)
    {
        foreach (var mode in new[] { PrologLanguageMode.Modern, PrologLanguageMode.StrictIso })
        {
            Assert.Equal("yes", RunInMode(mode, $"{goal}, write(yes)"));
        }
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void EvaluatesATwentyThousandNodeRuntimeBuiltExpressionIteratively(PrologLanguageMode mode)
    {
        var program = new BytecodeProgram(mode);
        using var machine = new Machine(program);
        var unaryPlus = program.Symbols.InternFunctor("+", 1);
        var expression = Cell.Integer60(1);
        for (var i = 0; i < 20_000; i++)
        {
            expression = machine.CreateStructure(unaryPlus, [expression]);
        }

        Assert.Equal(PrologNumber.FromInteger(1), ArithmeticEvaluator.Evaluate(machine, expression));
    }

    private static string ErrorInMode(PrologLanguageMode mode, string goal) =>
        RunInMode(mode, $"catch(({goal}), error(E, _), true), write(E)");

    private static string RunInMode(PrologLanguageMode mode, string goal)
    {
        var output = new StringWriter();
        using var engine = new PrologEngine(mode) { Output = output };
        Assert.True(engine.Query(goal).Prove(), goal);
        return output.ToString();
    }
}
