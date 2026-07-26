using Prolog.Runtime;

namespace Prolog.Compiler.Tests;

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
    [InlineData("6 / 3", "2")]
    [InlineData("7 / 2", "3.5")]
    [InlineData("abs(-4)", "4")]
    [InlineData("min(3, 5)", "3")]
    [InlineData("max(3, 5)", "5")]
    [InlineData("2 ^ 10", "1024")]
    [InlineData("- (3)", "-3")]
    public void EvaluatesExpressions(string expression, string expected)
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
}
