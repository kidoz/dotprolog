namespace DotProlog.Compiler.Tests;

/// <summary>ISO close options and per-stream behavior after an EOF marker has been returned.</summary>
public sealed class StreamEofActionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-eof-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Theory]
    [InlineData("error")]
    [InlineData("eof_code")]
    [InlineData("reset")]
    public void OpenReportsTheSelectedEofAction(string action)
    {
        string path = Path($"{action}.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S, [eof_action({action})]), "
                    + $"stream_property(S, eof_action({action})), "
                    + "close(S, [force(false)]), write(yes), nl"
            )
        );
    }

    [Fact]
    public void EofCodeReturnsTheMarkerRepeatedly()
    {
        string path = Path("eof-code.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, S, [eof_action(eof_code)]),
                  get_code(S, -1), get_code(S, -1),
                  stream_property(S, end_of_stream(past)),
                close(S), write(yes), nl
                """
            )
        );
    }

    [Theory]
    [InlineData("get_code(S, -1), get_code(S, _)")]
    [InlineData("read(S, end_of_file), read(S, _)")]
    public void ErrorRejectsTextInputPastTheEnd(string operations)
    {
        string path = Path("error.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            "permission_error(input,past_end_of_stream,$stream(3))",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S, [eof_action(error)]), "
                    + $"catch(({operations}), error(E, _), write(E)), "
                    + "close(S, [force(true)])"
            )
        );
    }

    [Fact]
    public void ErrorRejectsByteInputPastTheEnd()
    {
        string path = Path("error.bin");
        File.WriteAllBytes(path, []);

        Assert.Equal(
            "permission_error(input,past_end_of_stream,$stream(3))",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S, [type(binary), eof_action(error)]), "
                    + "get_byte(S, -1), "
                    + "catch(get_byte(S, _), error(E, _), write(E)), "
                    + "close(S, [force(true)])"
            )
        );
    }

    [Theory]
    [InlineData("get_char(S, bad)", "type_error(in_character,bad)")]
    [InlineData("get_code(S, bad)", "type_error(integer,bad)")]
    public void InvalidCharacterInputTakesPriorityOverPastEndPermission(string operation, string expected)
    {
        string path = Path("past-end-priority.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            expected,
            PrologTestHost.RunGoal(
                $"open('{path}', read, S, [eof_action(error)]), get_code(S, -1), "
                    + $"catch({operation}, error(E, _), write(E)), close(S, [force(true)])"
            )
        );
    }

    [Fact]
    public void InvalidByteInputTakesPriorityOverPastEndPermission()
    {
        string path = Path("past-end-priority.bin");
        File.WriteAllBytes(path, []);

        Assert.Equal(
            "type_error(in_byte,256)",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S, [type(binary), eof_action(error)]), get_byte(S, -1), "
                    + "catch(get_byte(S, 256), error(E, _), write(E)), close(S, [force(true)])"
            )
        );
    }

    [Fact]
    public void ResetRechecksTheSourceAfterPastEnd()
    {
        string path = Path("reset.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, S, [eof_action(reset)]),
                  get_code(S, -1), stream_property(S, end_of_stream(past)),
                  peek_code(S, -1), stream_property(S, end_of_stream(at)),
                  get_code(S, -1), stream_property(S, end_of_stream(past)),
                close(S), write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void RightmostEofActionWins()
    {
        string path = Path("rightmost.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            "reset\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S, [eof_action(error), eof_action(reset)]), "
                    + "stream_property(S, eof_action(Action)), close(S), write(Action), nl"
            )
        );
    }

    [Theory]
    [InlineData("eof_action(other)", "domain_error(stream_option,eof_action(other))")]
    [InlineData("eof_action(1)", "domain_error(stream_option,eof_action(1))")]
    [InlineData("eof_action(_)", "instantiation_error")]
    public void ReportsEofActionOptionErrors(string option, string expected)
    {
        string path = Path("option.txt");
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(open('{path}', read, _, [{option}]), error(E, _), write(E))"));
    }

    [Theory]
    [InlineData("close(user_output, _)", "instantiation_error")]
    [InlineData("close(user_output, [force(_)])", "instantiation_error")]
    [InlineData("close(user_output, [force(other)])", "domain_error(close_option,force(other))")]
    [InlineData("close(user_output, [force(1)])", "domain_error(close_option,force(1))")]
    [InlineData("close(user_output, [unknown(true)])", "domain_error(close_option,unknown(true))")]
    [InlineData("close(user_output, atom)", "type_error(list,atom)")]
    public void ReportsCloseOptionErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("close(_, atom)", "instantiation_error")]
    [InlineData("close(no_such_stream, [_])", "instantiation_error")]
    [InlineData("close(no_such_stream, atom)", "type_error(list,atom)")]
    [InlineData("close(f(1), [bad])", "domain_error(stream_or_alias,f(1))")]
    [InlineData("close(no_such_stream, [bad])", "domain_error(close_option,bad)")]
    public void ReportsIsoCloseErrorPriority(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void StandardOutputAcceptsForceCloseOptions(string force) =>
        Assert.Equal("yes\n", PrologTestHost.RunGoal($"close(user_output, [force({force})]), write(yes), nl"));
}
