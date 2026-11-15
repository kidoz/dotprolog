using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>Open-stream enumeration, ISO properties, filtering, and EOF-state transitions.</summary>
public sealed class StreamPropertyTests : IDisposable
{
    [Fact]
    public void NestedPropertySnapshotsSurviveBacktrackingAndCut()
    {
        using var engine = new PrologEngine();

        Assert.True(
            engine
                .Query(
                    "findall(A-B, (stream_property(_, alias(A)), "
                        + "once(stream_property(_, mode(_))), stream_property(_, alias(B))), Pairs), "
                        + "Pairs = [user_input-user_input,user_input-user_output,user_input-user_error,"
                        + "user_output-user_input,user_output-user_output,user_output-user_error,"
                        + "user_error-user_input,user_error-user_output,user_error-user_error]"
                )
                .Prove()
        );
        Assert.True(engine.Query("catch((stream_property(_, _), throw(done)), done, true)").Prove());
        Assert.True(engine.Query("findall(A, stream_property(_, alias(A)), [user_input,user_output,user_error])").Prove());
    }

    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-properties-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Fact]
    public void EnumeratesTheThreeStandardStreamAliases()
    {
        Assert.Equal(
            "[user_input,user_output,user_error]\n",
            PrologTestHost.RunGoal("findall(A, stream_property(_, alias(A)), Aliases), write(Aliases), nl")
        );
    }

    [Fact]
    public void StandardStreamsReportTheirStandardMetadata()
    {
        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                """
                stream_property(user_input, mode(read)),
                  stream_property(user_input, eof_action(reset)),
                  \+ stream_property(user_input, file_name(_)),
                  stream_property(user_output, mode(append)),
                  stream_property(user_output, eof_action(reset)),
                  \+ stream_property(user_output, file_name(_)),
                  stream_property(user_error, mode(append)),
                  stream_property(user_error, eof_action(reset)),
                  \+ stream_property(user_error, file_name(_)),
                write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void ReportsTruthfulPropertiesOfAnOpenedStream()
    {
        var path = Path("append.txt");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', append, S, [alias(log)]),
                  stream_property(S, file_name('{path}')),
                  stream_property(S, mode(append)),
                  stream_property(S, output),
                  stream_property(S, alias(log)),
                  stream_property(S, type(text)),
                  stream_property(S, reposition(true)),
                  \+ stream_property(S, input),
                  stream_property(S, eof_action(eof_code)),
                  stream_property(S, position(Position)), ground(Position),
                close(S),
                write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void CurrentStreamTracksOpenAndClosedHandles()
    {
        var path = Path("current.txt");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                findall(Before, current_stream(Before), Initial), length(Initial, 3),
                open('{path}', write, S),
                findall(During, current_stream(During), Open), length(Open, 4),
                current_stream(S),
                close(S),
                \+ current_stream(S),
                findall(After, current_stream(After), Final), length(Final, 3),
                write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void PropertyFilteringAndSharedVariablesLeaveNoBindingsBehind()
    {
        Assert.Equal(
            "[read] yes\n",
            PrologTestHost.RunGoal(
                "current_input(Input), "
                    + "findall(M, stream_property(Input, mode(M)), Modes), "
                    + "\\+ stream_property(Same, alias(Same)), var(Same), "
                    + "write(Modes), write(' yes'), nl"
            )
        );
    }

    [Fact]
    public void EndOfStreamMovesFromNotToAtToPast()
    {
        var path = Path("eof.txt");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out), put_code(Out, 97), close(Out),
                open('{path}', read, In),
                  stream_property(In, end_of_stream(not)),
                  get_code(In, 97),
                  stream_property(In, end_of_stream(at)),
                  peek_code(In, -1),
                  stream_property(In, end_of_stream(at)),
                  get_code(In, -1),
                  stream_property(In, end_of_stream(past)),
                close(In),
                write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void PropertyEnumerationRemainsStableWhenTheSelectedStreamCloses()
    {
        var path = Path("close-during-enumeration.txt");
        File.WriteAllText(path, "a");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, S),
                  findall(P, (
                    stream_property(S, P),
                    (\+ nb_current(stream_test, _) -> nb_setval(stream_test, closed), close(S) ; true)
                  ), Properties),
                  length(Properties, 8),
                  member(mode(read), Properties),
                  member(end_of_stream(not), Properties),
                write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void PropertyEnumerationDoesNotAdmitStreamsOpenedDuringEnrollment()
    {
        var first = Path("initial.txt");
        var second = Path("added.txt");
        File.WriteAllText(first, string.Empty);
        File.WriteAllText(second, string.Empty);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{first}', read, Original),
                  findall(S, (
                    stream_property(S, file_name(_)),
                    (S == Original -> open('{second}', read, Added), close(Added) ; true)
                  ), Streams),
                  Streams = [Original],
                close(Original), write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void AClosedStreamHandleHasNoProperties()
    {
        var path = Path("closed.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S), close(S), \\+ stream_property(S, _), "
                    + "\\+ stream_property('$stream'(999), _), write(yes), nl"
            )
        );
    }

    [Theory]
    [InlineData("current_input(foo)", "domain_error(stream,foo)")]
    [InlineData("current_output(1)", "domain_error(stream,1)")]
    [InlineData("current_input('$stream'(-1))", "domain_error(stream,$stream(-1))")]
    [InlineData("current_output('$stream'(999))", "domain_error(stream,$stream(999))")]
    [InlineData("current_stream(foo)", "domain_error(stream,foo)")]
    [InlineData("stream_property(1, _)", "domain_error(stream,1)")]
    [InlineData("stream_property(no_such_alias, _)", "domain_error(stream,no_such_alias)")]
    [InlineData("stream_property(_, nonsense)", "domain_error(stream_property,nonsense)")]
    [InlineData("stream_property(_, mode(1))", "domain_error(stream_property,mode(1))")]
    [InlineData("stream_property(_, alias(1))", "domain_error(stream_property,alias(1))")]
    [InlineData("stream_property(_, position(foo))", "domain_error(stream_property,position(foo))")]
    public void ReportsIsoStreamPropertyErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(({goal}), error(E, _), write(E))"));
}
