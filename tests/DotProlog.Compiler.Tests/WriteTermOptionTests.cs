namespace DotProlog.Compiler.Tests;

/// <summary>ISO term-output options, numbered variables, explicit streams, and exact option errors.</summary>
public sealed class WriteTermOptionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-write-term-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "A1")]
    [InlineData(27, "B1")]
    [InlineData(52, "A2")]
    public void NumbervarsWritesIsoVariableNames(long number, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"write_term('$VAR'({number}), [numbervars(true)])"));

    [Theory]
    [InlineData("write('$VAR'(27))", "B1")]
    [InlineData("writeq('$VAR'(27))", "B1")]
    [InlineData("writeln('$VAR'(27))", "B1\n")]
    [InlineData("write_canonical('$VAR'(27))", "'$VAR'(27)")]
    [InlineData("write_term('$VAR'(27), [numbervars(false), quoted(true)])", "'$VAR'(27)")]
    [InlineData("write_term('$VAR'(-1), [numbervars(true), quoted(true)])", "'$VAR'(-1)")]
    public void StandardWritersSelectNumbervarsAsSpecified(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void RightmostWriteOptionsWin()
    {
        Assert.Equal(
            "A\n",
            PrologTestHost.RunGoal(
                "write_term('$VAR'(0), [numbervars(false), numbervars(true), quoted(false), quoted(true)]), nl"
            )
        );
    }

    [Fact]
    public void WriteTermThreeWritesToTheSelectedStream()
    {
        string path = Path("explicit.txt");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"open('{path}', write, S), write_term(S, '$VAR'(26), [numbervars(true)]), " + "close(S), write(yes), nl"
            )
        );
        Assert.Equal("A1", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("write_term(x, [quoted(_)])", "instantiation_error")]
    [InlineData("write_term(x, [numbervars(_)])", "instantiation_error")]
    [InlineData("write_term(x, [quoted(on)])", "domain_error(write_option,quoted(on))")]
    [InlineData("write_term(x, [ignore_ops(1)])", "domain_error(write_option,ignore_ops(1))")]
    [InlineData("write_term(x, [numbervars(off)])", "domain_error(write_option,numbervars(off))")]
    [InlineData("write_term(user_input, x, [])", "permission_error(output,stream,user_input)")]
    [InlineData("write_term(_, x, [])", "instantiation_error")]
    public void ReportsIsoWriteOptionAndStreamErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("write_term(_, x, atom)", "instantiation_error")]
    [InlineData("write_term(f(1), x, [_])", "instantiation_error")]
    [InlineData("write_term(f(1), x, atom)", "type_error(list,atom)")]
    [InlineData("write_term(no_such_stream, x, [bad])", "domain_error(write_option,bad)")]
    [InlineData("write_term(user_input, x, [bad])", "domain_error(write_option,bad)")]
    public void ReportsIsoWriteTermErrorPriority(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));
}
