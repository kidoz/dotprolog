using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>Arithmetic evaluation through <c>is/2</c> and the arithmetic comparisons.</summary>
public sealed class ArithmeticTests
{
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
        (RunResult result, string output, _) = PrologTestHost.Execute(":- initialization((2 < 1, write(no))).");

        Assert.Equal(RunResult.Success, result);
        Assert.Equal("Warning: initialization goal failed.\n", output);
    }

    [Fact]
    public void DivisionByZeroIsAnEvaluationError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(X is 1 // 0).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("zero_divisor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluatingAnUnboundVariableIsAnInstantiationError()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText(":- initialization(X is Y + 1).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains("instantiation_error", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 / 0", "zero_divisor")]
    // An integer zero divisor is zero_divisor even for 0/0; only float 0.0/0.0 is undefined.
    [InlineData("0 / 0", "zero_divisor")]
    [InlineData("1.0 / 0.0", "zero_divisor")]
    [InlineData("0.0 / 0.0", "undefined")]
    [InlineData("sqrt(-1)", "undefined")]
    [InlineData("log(0)", "zero_divisor")]
    [InlineData("atan2(0, 0)", "undefined")]
    [InlineData("exp(1000)", "float_overflow")]
    [InlineData("max_tagged_integer + 1", "int_overflow")]
    [InlineData("floor(1)", "type_error(float, 1)")]
    [InlineData("\\ 1.0", "type_error(integer, 1.0)")]
    public void RaisesIsoArithmeticErrors(string expression, string expected)
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.True(engine.ConsultText($":- initialization(X is {expression}).").Success);

        PrologException exception = Assert.Throws<PrologException>(() => engine.RunPendingGoals());

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }
}
