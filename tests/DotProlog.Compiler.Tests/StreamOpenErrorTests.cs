namespace DotProlog.Compiler.Tests;

/// <summary>ISO open errors that must be detected without opening, truncating, or replacing state.</summary>
public sealed class StreamOpenErrorTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-open-errors-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Theory]
    [InlineData("bound")]
    [InlineData("f(bound)")]
    [InlineData("1")]
    public void BoundOutputRaisesBeforeOpeningTheFile(string target)
    {
        string path = Path("must-not-exist.txt");

        Assert.Equal(
            $"uninstantiation_error({target})",
            PrologTestHost.RunGoal($"catch(open('{path}', write, {target}), error(E, _), write(E))")
        );
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DuplicateAliasDoesNotReplaceTheOpenStreamOrTouchTheSecondFile()
    {
        string first = Path("first.txt");
        string second = Path("must-not-exist.txt");

        Assert.Equal(
            "permission_error(open,source_sink,alias(shared)) yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{first}', write, Original, [alias(shared)]),
                  catch(open('{second}', write, _, [alias(shared)]), error(E, _), write(E)),
                  stream_property(Original, alias(shared)),
                  write(shared, 'kept'),
                close(Original),
                write(' yes'), nl
                """
            )
        );
        Assert.Equal("kept", File.ReadAllText(first));
        Assert.False(File.Exists(second));
    }

    [Fact]
    public void StandardAliasesCannotBeReplaced()
    {
        string path = Path("must-not-exist.txt");

        Assert.Equal(
            "permission_error(open,source_sink,alias(user_output))",
            PrologTestHost.RunGoal($"catch(open('{path}', write, _, [alias(user_output)]), error(E, _), write(E))")
        );
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ExistingSourceThatCannotBeOpenedReportsPermissionError()
    {
        string directory = _directory.Replace("\\", "/", StringComparison.Ordinal);

        Assert.Equal(
            $"permission_error(open,source_sink,'{directory}')",
            PrologTestHost.RunGoal($"catch(open('{directory}', read, _), error(E, _), writeq(E))")
        );
    }

    [Fact]
    public void RightmostAliasIsTheOnlyOneApplied()
    {
        string path = Path("rightmost.txt");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S, [alias(user_output), alias(fresh_alias)]),
                  stream_property(S, alias(fresh_alias)),
                  \+ stream_property(S, alias(user_output)),
                close(S), write(yes), nl
                """
            )
        );
    }
}
