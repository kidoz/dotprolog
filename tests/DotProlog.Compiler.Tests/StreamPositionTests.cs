namespace DotProlog.Compiler.Tests;

/// <summary>ISO reposition options, opaque positions, restoration, buffering, and errors.</summary>
public sealed class StreamPositionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-position-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Fact]
    public void RestoresTextPositionsAcrossTermParserLookahead()
    {
        string path = Path("terms.pl");
        File.WriteAllText(path, "one. two.");

        Assert.Equal(
            "[one,two,two,one]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, In),
                  stream_property(In, reposition(true)),
                  stream_property(In, position(Start)),
                  read(In, First),
                  stream_property(In, position(SecondPosition)),
                  read(In, Second),
                  set_stream_position(In, SecondPosition), read(In, SecondAgain),
                  set_stream_position(In, Start), read(In, FirstAgain),
                close(In),
                write([First,Second,SecondAgain,FirstAgain]), nl
                """
            )
        );
    }

    [Fact]
    public void RestoresTextPositionsAcrossCrLfNormalization()
    {
        string path = Path("lines.pl");
        File.WriteAllText(path, "one.\r\ntwo.");

        Assert.Equal(
            "two-two\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, In),
                  read(In, one),
                  stream_property(In, position(Position)),
                  read(In, First),
                  set_stream_position(In, Position), read(In, Second),
                close(In),
                write(First-Second), nl
                """
            )
        );
    }

    [Fact]
    public void RestoresLogicalUnicodeCharacterPositions()
    {
        string path = Path("unicode.txt");
        File.WriteAllText(path, "αβγ");

        Assert.Equal(
            "α-β-γ-β\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, In),
                  get_char(In, Alpha),
                  stream_property(In, position(Position)),
                  get_char(In, Beta), get_char(In, Gamma),
                  set_stream_position(In, Position), get_char(In, BetaAgain),
                close(In),
                write(Alpha-Beta-Gamma-BetaAgain), nl
                """
            )
        );
    }

    [Fact]
    public void RestoringBinaryInputResetsEndOfStream()
    {
        string path = Path("input.bin");
        File.WriteAllBytes(path, [10, 20]);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, In, [type(binary)]),
                  get_byte(In, 10),
                  stream_property(In, position(Position)),
                  get_byte(In, 20), get_byte(In, -1),
                  stream_property(In, end_of_stream(past)),
                  set_stream_position(In, Position),
                  stream_property(In, end_of_stream(not)),
                  get_byte(In, 20),
                close(In),
                write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void RestoresBinaryOutputPositions()
    {
        string path = Path("output.bin");

        Assert.Equal(
            "[1,9,3]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [type(binary)]),
                  put_byte(Out, 1),
                  stream_property(Out, position(Position)),
                  put_byte(Out, 2), put_byte(Out, 3),
                  set_stream_position(Out, Position), put_byte(Out, 9),
                close(Out),
                open('{path}', read, In, [type(binary)]),
                  get_byte(In, A), get_byte(In, B), get_byte(In, C),
                close(In),
                write([A,B,C]), nl
                """
            )
        );
    }

    [Fact]
    public void RestoresTextOutputPositions()
    {
        string path = Path("output.txt");

        Assert.Equal(
            "azc\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out),
                  put_char(Out, a),
                  stream_property(Out, position(Position)),
                  put_char(Out, b), put_char(Out, c),
                set_stream_position(Out, Position), put_char(Out, z),
                close(Out),
                open('{path}', read, In),
                  get_char(In, A), get_char(In, Z), get_char(In, C),
                close(In),
                atom_chars(Text, [A,Z,C]),
                write(Text), nl
                """
            )
        );
    }

    [Fact]
    public void RightmostRepositionOptionWins()
    {
        string path = Path("rightmost.txt");

        Assert.Equal(
            "false\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [reposition(true), reposition(false)]),
                  stream_property(Out, reposition(Value)),
                close(Out),
                write(Value), nl
                """
            )
        );
    }

    [Fact]
    public void NonRepositionableFilesStillReportButCannotRestorePositions()
    {
        string path = Path("disabled.txt");

        Assert.Equal(
            "permission_error(reposition,stream,fixed)\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, _, [alias(fixed), reposition(false)]),
                  stream_property(fixed, position(Position)),
                  catch(set_stream_position(fixed, Position), error(E, _), write(E)),
                close(fixed), nl
                """
            )
        );
    }

    [Theory]
    [InlineData("set_stream_position(_, foo)", "instantiation_error")]
    [InlineData("set_stream_position(user_input, _)", "instantiation_error")]
    [InlineData("set_stream_position(user_input, foo)", "domain_error(stream_position,foo)")]
    [InlineData("set_stream_position(user_input, '$stream_position'(0,0,0,0))", "permission_error(reposition,stream,user_input)")]
    [InlineData("set_stream_position(no_such_stream, '$stream_position'(0,0,0,0))", "existence_error(stream,no_such_stream)")]
    [InlineData("set_stream_position(f(1), '$stream_position'(0,0,0,0))", "domain_error(stream_or_alias,f(1))")]
    [InlineData("set_stream_position(f(1), foo)", "domain_error(stream_or_alias,f(1))")]
    [InlineData("set_stream_position(no_such_stream, foo)", "domain_error(stream_position,foo)")]
    [InlineData(
        "set_stream_position(user_input, '$stream_position'(-1,0,0,0))",
        "domain_error(stream_position,$stream_position(-1,0,0,0))"
    )]
    public void ReportsStreamPositionErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("reposition(other)", "domain_error(stream_option,reposition(other))")]
    [InlineData("reposition(1)", "domain_error(stream_option,reposition(1))")]
    [InlineData("reposition(_)", "instantiation_error")]
    public void ReportsRepositionOptionErrors(string option, string expected)
    {
        string path = Path("option.txt");
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(open('{path}', write, _, [{option}]), error(E, _), write(E))"));
    }
}
