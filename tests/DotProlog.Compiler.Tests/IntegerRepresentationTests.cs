namespace DotProlog.Compiler.Tests;

/// <summary>
/// Unbounded integers at the runtime input boundary: literals of any magnitude read
/// to their exact value, in either representation tier, and write back their decimal spelling.
/// </summary>
public sealed class IntegerRepresentationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-integer-limits-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Theory]
    [InlineData("576460752303423488", "576460752303423488")]
    [InlineData("-576460752303423489", "-576460752303423489")]
    [InlineData("999999999999999999999999999999", "999999999999999999999999999999")]
    [InlineData("-999999999999999999999999999999", "-999999999999999999999999999999")]
    [InlineData("0xffffffffffffffffffffffffffffffff", "340282366920938463463374607431768211455")]
    [InlineData("-0xffffffffffffffffffffffffffffffff", "-340282366920938463463374607431768211455")]
    public void RuntimeTermInputReadsTheExactValue(string source, string expected)
    {
        ArgumentNullException.ThrowIfNull(source);

        Assert.Equal(expected, PrologTestHost.RunGoal($"read_term_from_atom('{source}', X, []), write(X)"));
    }

    [Fact]
    public void ABigLiteralInsideACompoundReadsTheExactValue() =>
        Assert.Equal(
            "999999999999999999999999999999",
            PrologTestHost.RunGoal("read_term_from_atom('f(999999999999999999999999999999)', T, []), arg(1, T, A), write(A)")
        );

    [Fact]
    public void TaggedIntegerBoundariesStillReadSuccessfully() =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "read_term_from_atom('576460752303423487', 576460752303423487, []), "
                    + "read_term_from_atom('-576460752303423488', -576460752303423488, []), write(yes)"
            )
        );

    [Fact]
    public void ABigStreamTermDoesNotDisturbTheFollowingTerm()
    {
        var path = Path("limits.pl");
        File.WriteAllText(path, "999999999999999999999999999999. next.");

        Assert.Equal(
            "999999999999999999999999999999-next",
            PrologTestHost.RunGoal($"open('{path}', read, S), read(S, Big), read(S, Next), close(S), write(Big-Next)")
        );
    }
}
