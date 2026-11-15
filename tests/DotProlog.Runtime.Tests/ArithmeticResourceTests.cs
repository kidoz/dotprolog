using System.Numerics;
using DotProlog.Runtime;

namespace DotProlog.Runtime.Tests;

/// <summary>Arithmetic result-size limits and cooperative cancellation.</summary>
public sealed class ArithmeticResourceTests
{
    private const long BudgetBytes = 32;

    [Theory]
    [InlineData("^")]
    [InlineData("<<")]
    public void IntegerPowerAndShiftAcceptAResultAtTheBudget(string operation)
    {
        using var machine = new Machine(new BytecodeProgram());
        var count = (BudgetBytes * 8) - 1;
        var expression = BinaryExpression(machine, operation, operation == "^" ? 2 : 1, count);

        var result = ArithmeticEvaluator.Evaluate(machine, expression, BudgetBytes);

        var expected = operation == "^" ? BigInteger.Pow(2, (int)count) : BigInteger.One << (int)count;
        Assert.Equal(expected, result.Big);
    }

    [Theory]
    [InlineData("^")]
    [InlineData("<<")]
    public void IntegerPowerAndShiftRejectAResultOneBitOverTheBudget(string operation)
    {
        using var machine = new Machine(new BytecodeProgram());
        var expression = BinaryExpression(machine, operation, operation == "^" ? 2 : 1, BudgetBytes * 8);

        var error = Assert.Throws<PrologException>(() =>
            ArithmeticEvaluator.Evaluate(machine, expression, BudgetBytes)
        );

        Assert.Contains("resource_error(memory)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPowerOfTwoPowerUsesAnEstimatedResultSize()
    {
        using var machine = new Machine(new BytecodeProgram());
        var withinBudget = BinaryExpression(machine, "^", 3, 159);
        var overBudget = BinaryExpression(machine, "^", 3, 162);

        Assert.Equal(BigInteger.Pow(3, 159), ArithmeticEvaluator.Evaluate(machine, withinBudget, BudgetBytes).Big);
        Assert.Throws<PrologException>(() => ArithmeticEvaluator.Evaluate(machine, overBudget, BudgetBytes));
    }

    [Fact]
    public void RationalPowerChecksBothComponentsBeforeEitherAllocation()
    {
        using var machine = new Machine(new BytecodeProgram());
        var half = Cell.Rational(machine.Symbols.InternRational(1, 2));
        var withinBudget = machine.CreateStructure(
            machine.Symbols.InternFunctor("^", 2),
            [half, Cell.Integer60((BudgetBytes * 8) - 1)]
        );
        var overBudget = machine.CreateStructure(machine.Symbols.InternFunctor("^", 2), [half, Cell.Integer60(BudgetBytes * 8)]);

        var result = ArithmeticEvaluator.Evaluate(machine, withinBudget, BudgetBytes);

        Assert.Equal(BigInteger.One << (int)((BudgetBytes * 8) - 1), result.Denominator);
        Assert.Throws<PrologException>(() => ArithmeticEvaluator.Evaluate(machine, overBudget, BudgetBytes));
    }

    [Theory]
    [InlineData("^")]
    [InlineData("<<")]
    public void IntegerPowerAndShiftObserveCancellation(string operation)
    {
        using var machine = new Machine(new BytecodeProgram());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        machine.CancellationToken = cancellation.Token;
        var expression = BinaryExpression(machine, operation, operation == "^" ? 2 : 1, BudgetBytes * 8);

        Assert.Throws<OperationCanceledException>(() => ArithmeticEvaluator.Evaluate(machine, expression, BudgetBytes));
    }

    private static Cell BinaryExpression(Machine machine, string name, long left, long right) =>
        machine.CreateStructure(machine.Symbols.InternFunctor(name, 2), [Cell.Integer60(left), Cell.Integer60(right)]);
}
