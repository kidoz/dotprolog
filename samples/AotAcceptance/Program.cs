using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace AotAcceptance;

/// <summary>
/// The NativeAOT acceptance check from the product scope, as a runnable program.
/// </summary>
/// <remarks>
/// <para>
/// Published with <c>PublishAot=true</c>, this must run with no .NET runtime installed, load a
/// Prolog file it has never seen, compile it to bytecode, enumerate several solutions, change the
/// clause database, and exit cleanly — with no trimming or AOT warnings anywhere in the build.
/// </para>
/// <para>
/// One clause of the scope's acceptance list is not covered here: predicates compiled at build time
/// into generated C#. That path does not exist yet, so "ahead of the run" below means consulted from
/// an embedded source constant before the external file is read, not compiled to IL.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>Prolog consulted before the external file, standing in for a built-in library.</summary>
    private const string Embedded = """
        greeting('Hello from NativeAOT!').

        count_solutions(Goal, Template, N) :-
            findall(Template, Goal, L),
            length(L, N).
        """;

    private static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "acceptance.pl");
        var engine = new PrologEngine();

        if (!Report(engine.ConsultText(Embedded, "embedded"), "embedded library"))
        {
            return 1;
        }

        // Everything from here on is decided at run time, inside a fully native executable.
        string script = $$"""
            :- initialization(main).

            main :-
                greeting(G), write(G), nl,

                consult('{{path.Replace("\\", "\\\\", StringComparison.Ordinal)}}'),

                count_solutions(colour(_), _, Total),
                write(colours(Total)), nl,

                findall(C, colour(C), Cs), write(Cs), nl,

                % Meta-called control is lowered to VM bytecode after startup. Its cut must commit
                % inside the meta-call without relying on JIT compilation.
                findall(X, call((member(X, [first, second]), !)), Committed),
                write(Committed), nl,

                assertz(stored(second)),
                assertz(stored(third)),
                findall(S, stored(S), Before), write(Before), nl,

                retract(stored(second)),
                findall(S2, stored(S2), After), write(After), nl,

                catch(no_such_predicate, error(existence_error(procedure, _), _), write(caught)), nl,

                % The standard library is compiled at engine construction, so it has to survive
                % trimming and work in a native image like everything else here.
                msort([c, a, b], Sorted), write(Sorted), nl,
                maplist(upcase_atom, Sorted, Upper), write(Upper), nl,
                aggregate_all(sum(N), member(N, [1, 2, 3]), Sum),
                format("sum=~d~n", [Sum]),
                format(atom(Aligned), "~w~t~8|~d", [row, 7]), write(Aligned), nl,

                % ISO arithmetic uses the same evaluator after NativeAOT trimming.
                Rounded is round(-1.5), Quotient is 6 / 3, Sine is sin(0),
                format("arithmetic=~w,~w,~w~n", [Rounded, Quotient, Sine]),
                catch(_ is 0.0 / 0.0, error(evaluation_error(undefined), _), write(arithmetic_error)), nl,

                % Occurs-check unification is iterative and must survive NativeAOT trimming.
                \+ unify_with_occurs_check(Cycle, f(Cycle)), var(Cycle),
                write(occurs_check), nl,

                % Predicate enumeration uses explicit program metadata, never reflection.
                assertz(inspected(value)),
                current_predicate(inspected/1), \+ current_predicate(write/1),
                write(predicate_info), nl,

                % ISO flags own runtime state explicitly and continue to work after trimming.
                current_prolog_flag(bounded, true),
                set_prolog_flag(unknown, fail), \+ native_missing_predicate,
                set_prolog_flag(unknown, error),
                set_prolog_flag(double_quotes, atom),
                atom_codes(Quoted, [34, 110, 97, 116, 105, 118, 101, 34]),
                read_term_from_atom(Quoted, native, []),
                set_prolog_flag(double_quotes, codes),
                write(prolog_flags), nl,

                % ISO read options retain source order, sharing, and named-singleton identity.
                read_term_from_atom('f(A,B,A,_C,_)', ReadOptionsTerm,
                    [variables(ReadVariables), singletons(ReadSingletons)]),
                ReadOptionsTerm = f(ReadA, ReadB, ReadA, ReadC, ReadAnonymous),
                ReadVariables = [ReadA, ReadB, ReadC, ReadAnonymous],
                ReadSingletons = ['B'=ReadB, '_C'=ReadC],
                write(read_options), nl,

                % Open streams and their ISO metadata are explicit runtime state, not reflection.
                current_input(NativeInput), current_stream(NativeInput),
                stream_property(NativeInput, mode(read)),
                stream_property(NativeInput, input),
                stream_property(NativeInput, type(text)),
                stream_property(NativeInput, reposition(false)),
                write(stream_properties), nl,

                % Character-code I/O shares the stream path and must preserve peek and EOF in AOT.
                open('dotprolog-aot-code.tmp', write, CodeOut),
                put_code(CodeOut, 65), put_code(CodeOut, 79), put_code(CodeOut, 84), close(CodeOut),
                open('dotprolog-aot-code.tmp', read, CodeIn),
                get_code(CodeIn, 65), peek_code(CodeIn, 79),
                get_code(CodeIn, 79), get_code(CodeIn, 84), get_code(CodeIn, -1), close(CodeIn),
                write(code_io), nl,

                % Binary streams and byte predicates use raw storage without reflection or encoding.
                open('dotprolog-aot-byte.tmp', write, ByteOut, [type(binary)]),
                stream_property(ByteOut, type(binary)),
                put_byte(ByteOut, 0), put_byte(ByteOut, 128), put_byte(ByteOut, 255), close(ByteOut),
                open('dotprolog-aot-byte.tmp', read, ByteIn, [type(binary)]),
                get_byte(ByteIn, 0), peek_byte(ByteIn, 128),
                get_byte(ByteIn, 128), get_byte(ByteIn, 255), get_byte(ByteIn, -1), close(ByteIn),
                write(byte_io), nl,

                % Opaque positions restore seekable streams without depending on runtime metadata.
                open('dotprolog-aot-position.tmp', write, PositionOut, [type(binary)]),
                stream_property(PositionOut, reposition(true)),
                put_byte(PositionOut, 1),
                stream_property(PositionOut, position(SavedPosition)),
                put_byte(PositionOut, 2), put_byte(PositionOut, 3),
                set_stream_position(PositionOut, SavedPosition), put_byte(PositionOut, 9), close(PositionOut),
                open('dotprolog-aot-position.tmp', read, PositionIn, [type(binary)]),
                get_byte(PositionIn, 1), get_byte(PositionIn, 9), get_byte(PositionIn, 3), close(PositionIn),
                write(stream_position), nl,

                % An operator declared at run time, in a native image, changing how a term prints.
                op(700, xfx, likes),
                write(likes(alice, bob)), nl,
                write_canonical(1 + 2 * 3), nl,

                % A grammar consulted from the external file, translated inside the native image.
                phrase(number(N), "427"), write(N), nl,

                % Streams: a file written, read back as a term, and output captured — all of which
                % need the reader, which a native image has to carry with it.
                with_output_to(atom(Captured), write(captured(1+2))),
                open('dotprolog-aot.tmp', write, Out), write(Out, Captured), write(Out, ' .'), close(Out),
                open('dotprolog-aot.tmp', read, In), read(In, Read), close(In),
                write(Read), nl,

                write(done), nl.
            """;

        if (!Report(engine.ConsultText(script, "acceptance"), "acceptance script"))
        {
            return 1;
        }

        try
        {
            RunResult result = engine.RunPendingGoals();
            Console.Out.Flush();
            return result == RunResult.Success ? 0 : 2;
        }
        catch (PrologException error)
        {
            Console.Error.WriteLine($"uncaught: {error.Message}");
            return 3;
        }
    }

    private static bool Report(LoadResult loaded, string what)
    {
        foreach (Diagnostic diagnostic in loaded.Diagnostics)
        {
            Console.Error.WriteLine($"{what}: {diagnostic}");
        }

        return loaded.Success;
    }
}
