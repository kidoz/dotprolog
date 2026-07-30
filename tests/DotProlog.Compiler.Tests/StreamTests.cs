using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>
/// Streams: reading terms and characters, opening and closing files, choosing the current stream,
/// and capturing output.
/// </summary>
public sealed class StreamTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-streams-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Runs a goal with standard input supplied and standard output captured.</summary>
    private static string RunWithInput(string source, string input)
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = new StringReader(input) };

        engine.ConsultOrThrow(source, "test.pl");
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        return output.ToString();
    }

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Fact]
    public void ReadsTermsFromStandardInput() =>
        Assert.Equal(
            "foo(bar)\n'a b'\n",
            RunWithInput(
                """
                main :- read(T), ( T == end_of_file -> true ; writeq(T), nl, main ).
                :- initialization(main).
                """,
                "foo(bar).\n'a b'.\n"
            )
        );

    [Fact]
    public void AClauseMaySpanSeveralLines() =>
        Assert.Equal("long(a,b)\n", RunWithInput(":- initialization((read(T), writeq(T), nl)).", "long(\n  a,\n  b).\n"));

    [Fact]
    public void SeveralTermsMayShareALine() =>
        Assert.Equal(
            "a\nb\n",
            RunWithInput(
                """
                main :- read(T), ( T == end_of_file -> true ; writeq(T), nl, main ).
                :- initialization(main).
                """,
                "a. b.\n"
            )
        );

    [Fact]
    public void ReadingATermLeavesFollowingCharactersUntouched() =>
        Assert.Equal(
            "yes\n",
            RunWithInput(":- initialization((read(a), get_char(' '), get_char(t), write(yes), nl)).", "a. tail")
        );

    [Fact]
    public void ReadingPastTheEndGivesEndOfFile() =>
        Assert.Equal("end_of_file\n", RunWithInput(":- initialization((read(T), writeq(T), nl)).", ""));

    [Fact]
    public void AFullStopInsideAQuotedAtomDoesNotEndTheClause() =>
        Assert.Equal("f('a. b')\n", RunWithInput(":- initialization((read(T), writeq(T), nl)).", "f('a. b').\n"));

    [Fact]
    public void AFullStopInAFloatDoesNotEndTheClause() =>
        Assert.Equal("f(3.14)\n", RunWithInput(":- initialization((read(T), writeq(T), nl)).", "f(3.14).\n"));

    [Fact]
    public void ASymbolicAtomEndingInAFullStopDoesNotEndTheClause() =>
        Assert.Equal("_G1=..[a]\n", RunWithInput(":- initialization((read(T), writeq(T), nl)).", "X =.. [a].\n"));

    [Fact]
    public void AnIncompleteClauseAtEndOfInputIsASyntaxError() =>
        Assert.Equal(
            "syntax_error(unexpected_end_of_file)\n",
            RunWithInput(":- initialization(catch(read(_), error(E, _), (writeq(E), nl))).", "oops(\n")
        );

    [Fact]
    public void ReadTermReportsVariableNames() =>
        Assert.Equal(
            "['World'=_G6]\n",
            RunWithInput(":- initialization((read_term(_, [variable_names(V)]), writeq(V), nl)).", "hello(World).\n")
        );

    [Fact]
    public void ReadTermReportsAllVariablesAndNamedSingletonsInSourceOrder()
    {
        Assert.Equal(
            "yes\n",
            RunWithInput(
                """
                :- initialization((
                    read_term(T, [variable_names(Names), variables(Vars), singletons(Singles)]),
                    T = f(A, B, A, C, Anonymous),
                    Names = ['A'=A, 'B'=B, '_C'=C],
                    Vars = [A, B, C, Anonymous],
                    Singles = ['B'=B, '_C'=C],
                    write(yes), nl
                )).
                """,
                "f(A, B, A, _C, _).\n"
            )
        );
    }

    [Fact]
    public void ReadTermFromAtomSupportsSingletons()
    {
        Assert.Equal(
            "yes",
            PrologTestHost.RunGoal(
                "read_term_from_atom('pair(Left, Right)', pair(L, R), " + "[singletons(['Left'=L, 'Right'=R])]), write(yes)"
            )
        );
    }

    [Fact]
    public void EndOfFileHasNoVariablesOrSingletons() =>
        Assert.Equal(
            "yes",
            RunWithInput(":- initialization((read_term(end_of_file, [variables([]), singletons([])]), write(yes))).", "")
        );

    [Fact]
    public void AnUnknownReadOptionIsReported() =>
        Assert.Equal(
            "domain_error(read_option,nonsense(x))\n",
            RunWithInput(":- initialization(catch(read_term(_, [nonsense(x)]), error(E, _), (writeq(E), nl))).", "a.\n")
        );

    [Theory]
    [InlineData("[nonsense(x)]", "domain_error(read_option, nonsense(x))")]
    [InlineData("[_]", "instantiation_error")]
    [InlineData("[variables(_)|tail]", "type_error(list, [variables(_)|tail])")]
    public void InvalidReadOptionsDoNotConsumeTheNextTerm(string options, string formal)
    {
        string path = Path("read-options.pl");
        File.WriteAllText(path, "first. second.");

        Assert.Equal(
            "first\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S), "
                    + $"catch(read_term(S, _, {options}), error({formal}, _), true), "
                    + "read(S, First), close(S), write(First), nl"
            )
        );
    }

    [Theory]
    [InlineData("read_term(_, _, atom)", "instantiation_error")]
    [InlineData("read_term(f(1), _, atom)", "domain_error(stream_or_alias,f(1))")]
    [InlineData("read_term(no_such_stream, _, [_])", "instantiation_error")]
    [InlineData("read_term(no_such_stream, _, [bad])", "domain_error(read_option,bad)")]
    [InlineData("read_term(user_output, _, [bad])", "domain_error(read_option,bad)")]
    public void ReportsIsoReadTermErrorPriority(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Fact]
    public void WritesAndReadsBackAFile()
    {
        string path = Path("round-trip.pl");

        Assert.Equal(
            "[first,'a b',end_of_file]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S),
                  writeq(S, first), write(S, ' .'), nl(S),
                  writeq(S, 'a b'), write(S, ' .'), nl(S),
                close(S),
                open('{path}', read, R), read(R, T1), read(R, T2), read(R, T3), close(R),
                writeq([T1, T2, T3]), nl
                """
            )
        );
    }

    [Fact]
    public void AppendAddsToWhatIsThere()
    {
        string path = Path("append.pl");

        Assert.Equal(
            "[one,two]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S1), write(S1, 'one .'), nl(S1), close(S1),
                open('{path}', append, S2), write(S2, 'two .'), nl(S2), close(S2),
                open('{path}', read, R), read(R, A), read(R, B), close(R),
                writeq([A, B]), nl
                """
            )
        );
    }

    [Fact]
    public void CharactersAreReadOneAtATime()
    {
        string path = Path("chars.txt");

        Assert.Equal(
            "a-b-b-c\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S), write(S, abc), close(S),
                open('{path}', read, R),
                  get_char(R, C1), peek_char(R, P), get_char(R, C2), get_char(R, C3),
                close(R),
                writeq(C1-P-C2-C3), nl
                """
            )
        );
    }

    [Fact]
    public void CharacterCodesAreWrittenReadAndPeeked()
    {
        string path = Path("codes.txt");

        Assert.Equal(
            "[97,98,98,-1]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S), put_code(S, 97), put_code(S, 98), close(S),
                open('{path}', read, R),
                  get_code(R, A), peek_code(R, P), get_code(R, B), get_code(R, Eof),
                close(R),
                write([A,P,B,Eof]), nl
                """
            )
        );
    }

    [Fact]
    public void OneArgumentCodeOperationsUseTheCurrentStreams() =>
        Assert.Equal(
            "[65,66]",
            PrologTestHost.RunGoal("with_output_to(codes(Codes), (put_code(65), put_code(66))), write(Codes)")
        );

    [Fact]
    public void GetCodeConsumesTextBufferedByATermRead()
    {
        Assert.Equal("32-90\n", RunWithInput(":- initialization((read(a), get_code(A), peek_code(B), write(A-B), nl)).", "a. Z"));
    }

    [Fact]
    public void ReadingPastTheEndOfAFileGivesEndOfFile()
    {
        string path = Path("empty.txt");

        Assert.Equal(
            "end_of_file-end_of_file\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S), close(S),
                open('{path}', read, R), get_char(R, C), read(R, T), close(R),
                writeq(C-T), nl
                """
            )
        );
    }

    [Fact]
    public void AtEndOfStreamReportsWhereTheReaderIs()
    {
        string path = Path("at-end.txt");

        Assert.Equal(
            "false-true\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S), write(S, ab), close(S),
                open('{path}', read, R),
                  ( at_end_of_stream(R) -> B1 = true ; B1 = false ),
                  get_char(R, _), get_char(R, _),
                  ( at_end_of_stream(R) -> B2 = true ; B2 = false ),
                close(R),
                writeq(B1-B2), nl
                """
            )
        );
    }

    [Fact]
    public void SetOutputRedirectsWritesWithNoStreamArgument()
    {
        string path = Path("redirect.txt");

        Assert.Equal(
            "before-after\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S),
                  set_output(S), write(hidden), write(' .'), set_output(user_output),
                close(S),
                open('{path}', read, R), read(R, T), close(R),
                write(before), write(-), write(after), nl,
                T == hidden
                """
            )
        );
    }

    [Fact]
    public void AnAliasNamesAStream()
    {
        string path = Path("alias.txt");

        Assert.Equal(
            "aliased\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, _, [alias(notes)]), write(notes, 'aliased .'), close(notes),
                open('{path}', read, R), read(R, T), close(R),
                writeq(T), nl
                """
            )
        );
    }

    [Fact]
    public void CurrentOutputNamesAStreamThatCanBeWrittenTo()
    {
        // current_output/1 gives a stream term rather than the alias atom, and that term has to be
        // usable wherever a stream is expected.
        Assert.Equal(
            "through the current stream",
            PrologTestHost.RunGoal("current_output(S), write(S, 'through the current stream')")
        );
    }

    [Theory]
    [InlineData("open('/no/such/place/file.pl', read, _)", "existence_error(source_sink,/no/such/place/file.pl)")]
    [InlineData("open(_, read, _)", "instantiation_error")]
    [InlineData("open(1, read, _)", "domain_error(source_sink,1)")]
    [InlineData("open(f, sideways, _)", "domain_error(io_mode,sideways)")]
    [InlineData("close(nowhere)", "existence_error(stream,nowhere)")]
    [InlineData("read(nowhere, _)", "existence_error(stream,nowhere)")]
    [InlineData("read('$stream'(-1), _)", "domain_error(stream_or_alias,$stream(-1))")]
    [InlineData("write('$stream'(4294967299), x)", "domain_error(stream_or_alias,$stream(4294967299))")]
    [InlineData("get_code('$stream'(foo), _)", "domain_error(stream_or_alias,$stream(foo))")]
    [InlineData("set_input('$stream'(4294967299))", "domain_error(stream_or_alias,$stream(4294967299))")]
    [InlineData("write(user_input, x)", "permission_error(output,stream,user_input)")]
    [InlineData("read(user_output, _)", "permission_error(input,stream,user_output)")]
    public void ReportsBadStreamArguments(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("get_code(a)", "type_error(integer,a)")]
    [InlineData("get_code(-2)", "representation_error(in_character_code)")]
    [InlineData("get_code(65536)", "representation_error(in_character_code)")]
    [InlineData("put_code(_)", "instantiation_error")]
    [InlineData("put_code(a)", "type_error(integer,a)")]
    [InlineData("put_code(-1)", "representation_error(character_code)")]
    [InlineData("put_code(65536)", "representation_error(character_code)")]
    [InlineData("get_code(f(1), _)", "domain_error(stream_or_alias,f(1))")]
    [InlineData("put_code(user_input, 97)", "permission_error(output,stream,user_input)")]
    public void ReportsCharacterCodeErrors(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("get_char(_, bad)", "instantiation_error")]
    [InlineData("get_char(no_such_stream, bad)", "type_error(in_character,bad)")]
    [InlineData("get_code(no_such_stream, bad)", "type_error(integer,bad)")]
    [InlineData("get_code(user_output, -2)", "permission_error(input,stream,user_output)")]
    [InlineData("put_char(user_input, _)", "instantiation_error")]
    [InlineData("put_char(no_such_stream, bad)", "type_error(character,bad)")]
    [InlineData("put_code(no_such_stream, bad)", "type_error(integer,bad)")]
    [InlineData("put_code(user_input, -1)", "permission_error(output,stream,user_input)")]
    public void ReportsIsoCharacterIoErrorPriority(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("get_char(1)", "type_error(in_character,1)")]
    [InlineData("get_char(foo)", "type_error(in_character,foo)")]
    [InlineData("peek_char('')", "type_error(in_character,'')")]
    public void ReportsInputCharacterErrorsWithoutReading(string operation, string expected)
    {
        string path = Path("character-errors.txt");
        File.WriteAllText(path, "a");

        Assert.Equal(
            $"{expected} yes\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S), set_input(S), "
                    + $"catch({operation}, error(E, _), writeq(E)), "
                    + "get_char(a), set_input(user_input), close(S), write(' yes'), nl"
            )
        );
    }

    [Fact]
    public void EndOfFileIsAValidBoundInputCharacter()
    {
        string path = Path("empty.txt");
        File.WriteAllText(path, string.Empty);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"open('{path}', read, S), get_char(S, end_of_file), " + "peek_char(S, end_of_file), close(S), write(yes), nl"
            )
        );
    }

    [Fact]
    public void AClosedStreamIsGoneRatherThanReused()
    {
        string path = Path("closed.txt");

        Assert.Equal(
            "existence_error(stream,$stream(3))",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, S), close(S),
                catch(write(S, x), error(E, _), write(E))
                """
            )
        );
    }

    [Theory]
    [InlineData("with_output_to(atom(A), write(1+2)), writeq(A)", "'1+2'")]
    [InlineData("with_output_to(codes(C), write(ab)), write(C)", "[97,98]")]
    [InlineData("with_output_to(chars(C), write(ab)), write(C)", "[a,b]")]
    [InlineData("with_output_to(atom(A), (write(a), write(b))), writeq(A)", "ab")]
    [InlineData("with_output_to(atom(A), true), writeq(A)", "''")]
    public void CapturesOutput(string goal, string expected) => Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void CaptureIsUndoneWhenTheGoalFails() =>
        Assert.Equal(
            "visible",
            PrologTestHost.RunGoal("( with_output_to(atom(_), (write(hidden), fail)) -> true ; true ), write(visible)")
        );

    [Fact]
    public void CaptureIsUndoneWhenTheGoalThrows() =>
        Assert.Equal(
            "caught-visible",
            PrologTestHost.RunGoal(
                "catch(with_output_to(atom(_), (write(hidden), throw(oops))), oops, write(caught)), write(-), write(visible)"
            )
        );

    [Fact]
    public void CapturesNest() =>
        Assert.Equal(
            "outer(inner)",
            PrologTestHost.RunGoal(
                "with_output_to(atom(A), (write(outer), write('('), with_output_to(atom(B), write(inner)), "
                    + "write(B), write(')'))), write(A)"
            )
        );

    [Fact]
    public void AnUnknownSinkIsReported() =>
        Assert.Equal(
            "domain_error(output_sink,file(x))",
            PrologTestHost.RunGoal("catch(with_output_to(file(x), true), error(E, _), write(E))")
        );

    [Theory]
    [InlineData("term_to_atom(foo(a, b), A), writeq(A)", "'foo(a,b)'")]
    [InlineData("term_to_atom(T, 'baz(1)'), writeq(T)", "baz(1)")]
    [InlineData("term_to_atom(1+2, A), writeq(A)", "'+(1,2)'")]
    [InlineData("read_term_from_atom('hello(X)', T, []), writeq(T)", "hello(_G2)")]
    public void ConvertsBetweenTermsAndAtoms(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal(goal));

    [Fact]
    public void HaltFlushesAndClosesWhatAProgramOpened()
    {
        // A file written and then left to halt must still be on disk, which means the close cannot
        // be left to a finalizer.
        string path = Path("halted.txt");
        var engine = new PrologEngine { Output = TextWriter.Null };

        engine.ConsultOrThrow($":- initialization((open('{path}', write, S), write(S, 'kept .'), halt)).", "halt.pl");
        Assert.Equal(RunResult.Halted, engine.RunPendingGoals());
        Assert.Equal("kept .", File.ReadAllText(path));
    }
}
