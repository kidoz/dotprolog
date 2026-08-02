namespace DotProlog.Compiler.Tests;

/// <summary>ISO maximum-arity representation limits raised while runtime terms are read.</summary>
public sealed class MaxArityRepresentationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-max-arity-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Fact]
    public void RuntimeTermInputAcceptsMaximumSupportedArity()
    {
        var source = Compound(255);

        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal($"read_term_from_atom('{source}', Term, []), functor(Term, f, 255), write(yes)")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeTermInputRaisesCatchableMaxArityRepresentationError(bool nested)
    {
        var source = nested ? $"g({Compound(256)})" : Compound(256);

        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                $"catch(read_term_from_atom('{source}', _, []), " + "error(representation_error(max_arity), _), write(yes))"
            )
        );
    }

    [Fact]
    public void MaxArityErrorConsumesOnlyTheRejectedStreamTerm()
    {
        var path = Path("max-arity.pl");
        File.WriteAllText(path, $"{Compound(256)}. next.");

        Assert.Equal(
            "next\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S), "
                    + "catch(read(S, _), error(representation_error(max_arity), _), true), "
                    + "read(S, Next), close(S), write(Next), nl"
            )
        );
    }

    private static string Compound(int arity) => $"f({string.Join(",", Enumerable.Repeat("a", arity))})";
}
