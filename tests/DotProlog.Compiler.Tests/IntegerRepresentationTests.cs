namespace DotProlog.Compiler.Tests;

/// <summary>ISO integer representation limits raised while runtime input is converted to heap terms.</summary>
public sealed class IntegerRepresentationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-integer-limits-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Theory]
    [InlineData("576460752303423488", "max_integer")]
    [InlineData("-576460752303423489", "min_integer")]
    [InlineData("999999999999999999999999999999", "max_integer")]
    [InlineData("-999999999999999999999999999999", "min_integer")]
    [InlineData("0xffffffffffffffffffffffffffffffff", "max_integer")]
    [InlineData("-0xffffffffffffffffffffffffffffffff", "min_integer")]
    [InlineData("f(999999999999999999999999999999)", "max_integer")]
    public void RuntimeTermInputRaisesCatchableRepresentationError(string source, string limit)
    {
        ArgumentNullException.ThrowIfNull(source);

        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                $"catch(read_term_from_atom('{source}', _, []), " + $"error(representation_error({limit}), _), write(yes))"
            )
        );
    }

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
    public void RepresentationErrorConsumesOnlyTheRejectedStreamTerm()
    {
        string path = Path("limits.pl");
        File.WriteAllText(path, "999999999999999999999999999999. next.");

        Assert.Equal(
            "next\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S), "
                    + "catch(read(S, _), error(representation_error(max_integer), _), true), "
                    + "read(S, Next), close(S), write(Next), nl"
            )
        );
    }
}
