namespace DotProlog.Compiler.Tests;

/// <summary>Finite floating-point limits raised while runtime terms are read.</summary>
public sealed class FloatRepresentationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-float-limits-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Theory]
    [InlineData("1.0e9999")]
    [InlineData("-1.0e9999")]
    [InlineData("f(1.0e9999)")]
    public void RuntimeTermInputRaisesCatchableFloatOverflowSyntaxError(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                $"catch(read_term_from_atom('{source}', _, []), " + "error(syntax_error(float_overflow), _), write(yes))"
            )
        );
    }

    [Fact]
    public void MaximumFiniteFloatStillReadsSuccessfully() =>
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal("read_term_from_atom('1.7976931348623157e308', Value, []), float(Value), write(yes)")
        );

    [Theory]
    [InlineData("1.0e-9999")]
    [InlineData("-1.0e-9999")]
    public void FloatUnderflowRoundsToZero(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Assert.Equal("yes", PrologTestHost.RunGoal($"read_term_from_atom('{source}', Value, []), Value =:= 0.0, write(yes)"));
    }

    [Fact]
    public void FloatOverflowConsumesOnlyTheRejectedStreamTerm()
    {
        string path = Path("float-overflow.pl");
        File.WriteAllText(path, "1.0e9999. next.");

        Assert.Equal(
            "next\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S), "
                    + "catch(read(S, _), error(syntax_error(float_overflow), _), true), "
                    + "read(S, Next), close(S), write(Next), nl"
            )
        );
    }

    [Fact]
    public void ExponentRequiresAFractionAcrossTermAndNumberConversion()
    {
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "catch(read_term_from_atom('1e2', _, []), error(syntax_error(_), _), ReadCaught = true), "
                    + "ReadCaught == true, "
                    + "\\+ atom_number('1e2', _), "
                    + "catch(number_chars(_, ['1',e,'2']), "
                    + "error(syntax_error(illegal_number), _), NumberCaught = true), "
                    + "NumberCaught == true, write(yes)"
            )
        );
    }
}
