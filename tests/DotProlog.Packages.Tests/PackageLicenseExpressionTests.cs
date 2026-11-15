namespace DotProlog.Packages.Tests;

public sealed class PackageLicenseExpressionTests
{
    [Theory]
    [InlineData("MIT", new[] { "MIT" }, true)]
    [InlineData("GPL-3.0", new[] { "MIT" }, false)]
    [InlineData("MIT WITH LLVM-exception", new[] { "MIT WITH LLVM-exception" }, true)]
    [InlineData("MIT WITH LLVM-exception", new[] { "MIT", "LLVM-exception" }, false)]
    [InlineData("MIT OR GPL-3.0 AND BSD-2-Clause", new[] { "MIT" }, true)]
    [InlineData("(MIT OR GPL-3.0) AND BSD-2-Clause", new[] { "MIT" }, false)]
    [InlineData("(MIT OR GPL-3.0) AND BSD-2-Clause", new[] { "GPL-3.0", "BSD-2-Clause" }, true)]
    public void Valid_expressions_keep_their_semantics(string expression, string[] allowed, bool expected) =>
        Assert.Equal(expected, PackageLicenseExpression.Allows(expression, allowed.ToHashSet(StringComparer.Ordinal)));

    [Fact]
    public void Deeply_nested_expression_is_parsed_and_evaluated_iteratively()
    {
        const int depth = 4096;
        var expression = new string('(', depth) + "MIT" + new string(')', depth);

        Assert.True(PackageLicenseExpression.Allows(expression, Allowed("MIT")));
    }

    [Fact]
    public void Long_flat_conjunction_is_evaluated_iteratively()
    {
        var expression = string.Join(" AND ", Enumerable.Repeat("MIT", 4096));

        Assert.True(PackageLicenseExpression.Allows(expression, Allowed("MIT")));
    }

    [Fact]
    public void Long_flat_disjunction_is_evaluated_iteratively()
    {
        var expression = string.Join(" OR ", Enumerable.Repeat("GPL-3.0", 4095).Append("MIT"));

        Assert.True(PackageLicenseExpression.Allows(expression, Allowed("MIT")));
    }

    [Fact]
    public void Deeply_nested_malformed_expression_reports_invalid_data()
    {
        var expression = new string('(', 4096) + "MIT";

        var error = Assert.Throws<InvalidDataException>(() => PackageLicenseExpression.Allows(expression, Allowed("MIT")));
        Assert.Contains("Invalid license expression", error.Message, StringComparison.Ordinal);
    }

    private static HashSet<string> Allowed(params string[] licenses) => new(licenses, StringComparer.Ordinal);
}
