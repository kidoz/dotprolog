using System.Text;
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
        string maxAritySource = $"f({string.Join(",", Enumerable.Repeat("a", 256))})";

        if (!ExerciseSystemErrors())
        {
            return 1;
        }

        if (!Report(engine.ConsultText(Embedded, "embedded"), "embedded library"))
        {
            return 1;
        }

        // Everything from here on is decided at run time, inside a fully native executable.
        string script = $$"""
            :- op(650, xfx, native_quoted).
            :- initialization(main).

            native_noncallable_body :- 4.
            native_unreached_noncallable_body :- fail, 4.
            native_quoted_fact(left 'native_quoted' right).

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

                % ISO repeat/0 retains its infinite retry point in the native runtime.
                assertz(native_repeat_count(0)),
                repeat,
                retract(native_repeat_count(RepeatCount)),
                NextRepeatCount is RepeatCount + 1,
                assertz(native_repeat_count(NextRepeatCount)),
                NextRepeatCount >= 3,
                !,
                retractall(native_repeat_count(_)),
                write(repeat_control), nl,

                % Compiled non-callable bodies raise at execution and remain catchable.
                catch(
                    native_noncallable_body,
                    error(type_error(callable, 4), _),
                    NonCallableCaught = true),
                NonCallableCaught == true,
                \+ native_unreached_noncallable_body,
                write(compiled_goal_errors), nl,

                % Meta-control validates executed goals and the resulting call/N arity.
                catch(once(_), error(instantiation_error, _), OnceGoalCaught = true),
                OnceGoalCaught == true,
                catch(
                    catch(4, never, true),
                    error(type_error(callable, 4), _),
                    CatchGoalCaught = true),
                CatchGoalCaught == true,
                catch(
                    catch(throw(native_ball), native_ball, 4),
                    error(type_error(callable, 4), _),
                    RecoveryGoalCaught = true),
                RecoveryGoalCaught == true,
                functor(NativeCallFact, native_call_limit, 255),
                assertz(NativeCallFact),
                functor(NativeAllowedCall, native_call_limit, 248),
                call(NativeAllowedCall, a, b, c, d, e, f, g),
                abolish(native_call_limit/255),
                functor(NativeOversizedCall, native_call_limit, 249),
                catch(
                    call(NativeOversizedCall, a, b, c, d, e, f, g),
                    error(representation_error(max_arity), _),
                    CallArityCaught = true),
                CallArityCaught == true,
                write(control_error_audit), nl,

                % Predicate enumeration uses explicit program metadata, never reflection.
                assertz(inspected(value)),
                current_predicate(inspected/1), \+ current_predicate(write/1),
                write(predicate_info), nl,

                % ISO flags own runtime state explicitly and continue to work after trimming.
                current_prolog_flag(bounded, true),
                findall(FlagName, current_prolog_flag(FlagName, _), FlagNames),
                length(FlagNames, 9),
                current_prolog_flag(max_integer, NativeMaxInteger),
                catch(
                    set_prolog_flag(max_integer, NativeMaxInteger),
                    error(permission_error(modify, flag, max_integer), _),
                    MaxIntegerFlagCaught = true),
                MaxIntegerFlagCaught == true,
                catch(
                    set_prolog_flag(max_arity, 255),
                    error(permission_error(modify, flag, max_arity), _),
                    MaxArityFlagCaught = true),
                MaxArityFlagCaught == true,
                set_prolog_flag(unknown, fail), \+ native_missing_predicate,
                set_prolog_flag(unknown, error),
                set_prolog_flag(double_quotes, atom),
                atom_codes(Quoted, [34, 110, 97, 116, 105, 118, 101, 34]),
                read_term_from_atom(Quoted, native, []),
                set_prolog_flag(double_quotes, codes),
                write(prolog_flags), nl,

                % Character conversion owns explicit versioned state and feeds runtime term parsing.
                char_conversion(z, x), current_char_conversion(z, x),
                set_prolog_flag(char_conversion, on),
                atom_codes(ConvertedSource, [102, 105, 122, 122]),
                read_term_from_atom(ConvertedSource, fixx, []),
                char_code(SingleQuote, 39), char_code(DoubleQuote, 34),
                char_conversion(SingleQuote, x), char_conversion(DoubleQuote, x),
                atom_codes(QuotedSource, [39, 122, 39]),
                read_term_from_atom(QuotedSource, z, []),
                set_prolog_flag(double_quotes, atom),
                atom_codes(DoubleQuotedSource, [34, 122, 34]),
                read_term_from_atom(DoubleQuotedSource, z, []),
                set_prolog_flag(double_quotes, codes),
                char_conversion(a, x),
                atom_codes(CharacterCodeSource, [48, 39, 97]),
                read_term_from_atom(CharacterCodeSource, 97, []),
                set_prolog_flag(char_conversion, off),
                char_conversion(z, z), \+ current_char_conversion(z, _),
                char_conversion(SingleQuote, SingleQuote),
                char_conversion(DoubleQuote, DoubleQuote),
                char_conversion(a, a),
                write(character_conversion), nl,

                % ISO read options retain source order, sharing, and named-singleton identity.
                read_term_from_atom('f(A,B,A,_C,_)', ReadOptionsTerm,
                    [variables(ReadVariables), singletons(ReadSingletons)]),
                ReadOptionsTerm = f(ReadA, ReadB, ReadA, ReadC, ReadAnonymous),
                ReadVariables = [ReadA, ReadB, ReadC, ReadAnonymous],
                ReadSingletons = ['B'=ReadB, '_C'=ReadC],
                write(read_options), nl,

                % Invalid read options are rejected before they can consume the next stream term.
                open('dotprolog-aot-read-options.tmp', write, ReadOptionOut),
                write(ReadOptionOut, 'first .'), close(ReadOptionOut),
                open('dotprolog-aot-read-options.tmp', read, ReadOptionIn),
                catch(
                    read_term(ReadOptionIn, _, [nonsense(x)]),
                    error(domain_error(read_option, nonsense(x)), _),
                    true),
                read(ReadOptionIn, first), close(ReadOptionIn),
                write(read_option_validation), nl,

                % Runtime reading preserves bounded integer representation errors after trimming.
                catch(
                    read_term_from_atom('999999999999999999999999999999', _, []),
                    error(representation_error(max_integer), _),
                    MaxIntegerCaught = true),
                MaxIntegerCaught == true,
                catch(
                    read_term_from_atom('-999999999999999999999999999999', _, []),
                    error(representation_error(min_integer), _),
                    MinIntegerCaught = true),
                MinIntegerCaught == true,
                write(integer_representation_errors), nl,

                % Runtime reading enforces the same maximum arity in the native image.
                catch(
                    read_term_from_atom('{{maxAritySource}}', _, []),
                    error(representation_error(max_arity), _),
                    MaxArityCaught = true),
                MaxArityCaught == true,
                write(max_arity_representation_error), nl,

                % Oversized float literals remain finite-only runtime input after trimming.
                catch(
                    read_term_from_atom('1e9999', _, []),
                    error(syntax_error(float_overflow), _),
                    FloatOverflowCaught = true),
                FloatOverflowCaught == true,
                read_term_from_atom('1e-9999', Underflow, []), Underflow =:= 0.0,
                write(float_read_limits), nl,

                % halt/1 validates its ISO input mode before it can stop the native process.
                catch(halt(_), error(instantiation_error, _), HaltVariableCaught = true),
                HaltVariableCaught == true,
                catch(halt(stopped), error(type_error(integer, stopped), _), HaltTypeCaught = true),
                HaltTypeCaught == true,
                write(halt_status_errors), nl,

                % compare/3 validates the ISO order domain in the native image.
                catch(compare(1, a, b), error(type_error(atom, 1), _), CompareTypeCaught = true),
                CompareTypeCaught == true,
                catch(compare(foo, a, b), error(domain_error(order, foo), _), CompareDomainCaught = true),
                CompareDomainCaught == true,
                write(compare_order_errors), nl,

                % arg/3 preserves ISO instantiation and negative-index errors after trimming.
                catch(arg(1, _, _), error(instantiation_error, _), ArgTermCaught = true),
                ArgTermCaught == true,
                catch(
                    arg(-1, native_arg(value), _),
                    error(domain_error(not_less_than_zero, -1), _),
                    ArgIndexCaught = true),
                ArgIndexCaught == true,
                write(arg_errors), nl,

                % Term construction enforces list shape, atomic heads, and the advertised arity.
                catch(
                    (_ =.. [native | invalid_tail]),
                    error(type_error(list, [native | invalid_tail]), _),
                    UnivListCaught = true),
                UnivListCaught == true,
                catch(
                    (_ =.. [native(value)]),
                    error(type_error(atomic, native(value)), _),
                    UnivHeadCaught = true),
                UnivHeadCaught == true,
                functor(NativeMaximum, native_functor, 255),
                functor(NativeMaximum, native_functor, NativeArity),
                NativeArity =:= 255,
                catch(
                    functor(_, native_functor, 256),
                    error(representation_error(max_arity), _),
                    FunctorArityCaught = true),
                FunctorArityCaught == true,
                length(NativeUnivArgs, 256),
                catch(
                    (_ =.. [native_univ | NativeUnivArgs]),
                    error(representation_error(max_arity), _),
                    UnivArityCaught = true),
                UnivArityCaught == true,
                write(term_construction_errors), nl,

                % current_op/3 validates bound ISO filter domains before enumeration.
                catch(
                    current_op(a, _, _),
                    error(domain_error(operator_priority, a), _),
                    CurrentOpPriorityCaught = true),
                CurrentOpPriorityCaught == true,
                catch(current_op(_, _, 1), error(type_error(atom, 1), _), CurrentOpNameCaught = true),
                CurrentOpNameCaught == true,
                write(current_op_filter_errors), nl,

                % Operator enumeration retains the table version from its first solution.
                op(333, xfx, native_snapshot_old),
                findall(
                    OldName,
                    (current_op(_, _, OldName), op(0, xfx, native_snapshot_old)),
                    OldOperatorNames),
                member(native_snapshot_old, OldOperatorNames),
                findall(
                    NewName,
                    (current_op(_, _, NewName), op(333, xfx, native_snapshot_new)),
                    NewOperatorNames),
                \+ member(native_snapshot_new, NewOperatorNames),
                op(0, xfx, native_snapshot_new),
                write(current_op_snapshot), nl,

                % Quoting an operator name does not suppress its ISO operator role.
                native_quoted_fact(native_quoted(left, right)),
                read_term_from_atom(
                    'left ''native_quoted'' right',
                    native_quoted(left, right),
                    []),
                write(quoted_operator_syntax), nl,

                % Database inspection and removal preserve ISO procedure permissions after trimming.
                catch(
                    clause(greeting(_), _),
                    error(permission_error(access, private_procedure, greeting/1), _),
                    ClausePrivateCaught = true),
                ClausePrivateCaught == true,
                catch(
                    clause(native_absent(_), 4),
                    error(type_error(callable, 4), _),
                    ClauseBodyCaught = true),
                ClauseBodyCaught == true,
                catch(
                    retract(greeting(_)),
                    error(permission_error(modify, static_procedure, greeting/1), _),
                    RetractStaticCaught = true),
                RetractStaticCaught == true,
                write(database_permission_errors), nl,

                % Open streams and their ISO metadata are explicit runtime state, not reflection.
                current_input(NativeInput), current_stream(NativeInput),
                stream_property(NativeInput, mode(read)),
                stream_property(NativeInput, input),
                stream_property(NativeInput, type(text)),
                stream_property(NativeInput, reposition(false)),
                write(stream_properties), nl,

                % Bound current-stream queries reject non-stream terms in the native image too.
                catch(
                    (current_output(not_a_stream), CurrentStreamDomainCaught = false),
                    error(domain_error(stream, not_a_stream), _),
                    CurrentStreamDomainCaught = true),
                CurrentStreamDomainCaught == true,
                write(current_stream_domains), nl,

                % Character-code I/O shares the stream path and must preserve peek and EOF in AOT.
                open('dotprolog-aot-code.tmp', write, CodeOut),
                put_code(CodeOut, 65), put_code(CodeOut, 79), put_code(CodeOut, 84), close(CodeOut),
                open('dotprolog-aot-code.tmp', read, CodeIn),
                get_code(CodeIn, 65), peek_code(CodeIn, 79),
                get_code(CodeIn, 79), get_code(CodeIn, 84), get_code(CodeIn, -1), close(CodeIn),
                write(code_io), nl,

                % EOF policy and close options are explicit stream state in the native image.
                open('dotprolog-aot-code.tmp', read, ErrorEof, [eof_action(error)]),
                get_code(ErrorEof, 65), get_code(ErrorEof, 79), get_code(ErrorEof, 84), get_code(ErrorEof, -1),
                catch(
                    (get_code(ErrorEof, _), EofErrorCaught = false),
                    error(permission_error(input, past_end_of_stream, _), _),
                    EofErrorCaught = true),
                EofErrorCaught == true,
                close(ErrorEof, [force(true)]),
                open('dotprolog-aot-code.tmp', read, ResetEof, [eof_action(reset)]),
                get_code(ResetEof, 65), get_code(ResetEof, 79), get_code(ResetEof, 84), get_code(ResetEof, -1),
                stream_property(ResetEof, end_of_stream(past)),
                peek_code(ResetEof, -1),
                stream_property(ResetEof, end_of_stream(at)),
                close(ResetEof, [force(false)]),
                write(stream_eof_actions), nl,

                % Open rejects bound outputs and alias collisions before touching another file.
                catch(
                    (open('dotprolog-aot-invalid.tmp', write, already_bound), OpenOutputCaught = false),
                    error(uninstantiation_error(already_bound), _),
                    OpenOutputCaught = true),
                OpenOutputCaught == true,
                open('dotprolog-aot-alias.tmp', write, AliasOut, [alias(native_shared)]),
                catch(
                    (open('dotprolog-aot-duplicate.tmp', write, _, [alias(native_shared)]),
                        AliasErrorCaught = false),
                    error(permission_error(open, source_sink, alias(native_shared)), _),
                    AliasErrorCaught = true),
                AliasErrorCaught == true,
                close(AliasOut, [force(true)]),
                write(stream_open_errors), nl,

                % Source/sink is an ISO domain, including after the runtime is trimmed.
                catch(
                    (open(source(file), read, _), SourceSinkDomainCaught = false),
                    error(domain_error(source_sink, source(file)), _),
                    SourceSinkDomainCaught = true),
                SourceSinkDomainCaught == true,
                write(source_sink_domains), nl,

                % OS-invalid path atoms stay inside the ISO source/sink error boundary.
                atom_codes(InvalidSourceSink, [0]),
                catch(
                    (open(InvalidSourceSink, read, _), InvalidSourceSinkCaught = false),
                    error(domain_error(source_sink, InvalidSourceSink), _),
                    InvalidSourceSinkCaught = true),
                InvalidSourceSinkCaught == true,
                write(invalid_source_sink_paths), nl,

                % Option shape, stream domains, and option semantics follow ISO error priority.
                catch(
                    (open(1, sideways, bound, []), OpenPriorityCaught = false),
                    error(uninstantiation_error(bound), _),
                    OpenPriorityCaught = true),
                OpenPriorityCaught == true,
                catch(
                    (close(no_such_stream, [bad]), ClosePriorityCaught = false),
                    error(domain_error(close_option, bad), _),
                    ClosePriorityCaught = true),
                ClosePriorityCaught == true,
                catch(
                    (read_term(no_such_stream, _, [bad]), ReadPriorityCaught = false),
                    error(domain_error(read_option, bad), _),
                    ReadPriorityCaught = true),
                ReadPriorityCaught == true,
                catch(
                    (write_term(f(1), x, atom), WritePriorityCaught = false),
                    error(type_error(list, atom), _),
                    WritePriorityCaught = true),
                WritePriorityCaught == true,
                write(stream_option_error_priority), nl,

                % Permission culprits stay as stream designators, and malformed handles never wrap.
                catch(
                    (set_input(user_output), StreamPermissionCaught = false),
                    error(permission_error(input, stream, user_output), _),
                    StreamPermissionCaught = true),
                StreamPermissionCaught == true,
                catch(
                    (set_input('$stream'(-1)), StreamDomainCaught = false),
                    error(domain_error(stream_or_alias, '$stream'(-1)), _),
                    StreamDomainCaught = true),
                StreamDomainCaught == true,
                write(stream_error_terms), nl,

                % Bound character-input modes are validated before the stream is consumed.
                open('dotprolog-aot-code.tmp', read, CharacterMode),
                catch(
                    (get_char(CharacterMode, invalid), CharacterModeCaught = false),
                    error(type_error(in_character, invalid), _),
                    CharacterModeCaught = true),
                CharacterModeCaught == true,
                get_char(CharacterMode, 'A'),
                close(CharacterMode),
                write(character_input_modes), nl,

                % ISO value checks precede stream lookup, while an explicit stream variable wins first.
                catch(
                    (get_char(no_such_stream, bad), CharacterPriorityCaught = false),
                    error(type_error(in_character, bad), _),
                    CharacterPriorityCaught = true),
                CharacterPriorityCaught == true,
                catch(
                    (put_byte(_, 256), BytePriorityCaught = false),
                    error(instantiation_error, _),
                    BytePriorityCaught = true),
                BytePriorityCaught == true,
                write(primitive_io_error_priority), nl,
                write(stream_system_errors), nl,

                % ISO write_term/3 and numbervars options stay explicitly registered after trimming.
                with_output_to(atom(NumberedOutput),
                    write_term('$VAR'(27), [numbervars(true), quoted(true)])),
                NumberedOutput == 'B1',
                catch(
                    (write_term(x, [quoted(on)]), WriteOptionCaught = false),
                    error(domain_error(write_option, quoted(on)), _),
                    WriteOptionCaught = true),
                WriteOptionCaught == true,
                open('dotprolog-aot-write-term.tmp', write, WriteTermOut),
                write_term(WriteTermOut, '$VAR'(26), [numbervars(true)]),
                close(WriteTermOut),
                open('dotprolog-aot-write-term.tmp', read, WriteTermIn),
                get_char(WriteTermIn, 'A'), get_char(WriteTermIn, '1'),
                get_char(WriteTermIn, end_of_file), close(WriteTermIn),
                write(term_write_options), nl,

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

    private static bool ExerciseSystemErrors()
    {
        var engine = new PrologEngine { Input = new FailingReader() };
        if (
            engine.RunGoal("catch(get_char(_), error(system_error, _), true)", out IReadOnlyList<Diagnostic> inputDiagnostics)
                != RunResult.Success
            || inputDiagnostics.Count != 0
        )
        {
            return false;
        }

        engine.Output = new FailingWriter();
        if (
            engine.RunGoal("catch(write(native), error(system_error, _), true)", out IReadOnlyList<Diagnostic> outputDiagnostics)
                != RunResult.Success
            || outputDiagnostics.Count != 0
        )
        {
            return false;
        }

        engine.Output = new DisposedWriter();
        return engine.RunGoal(
                "catch(close(user_output, [force(false)]), error(system_error, _), true), " + "close(user_output, [force(true)])",
                out IReadOnlyList<Diagnostic> closeDiagnostics
            ) == RunResult.Success
            && closeDiagnostics.Count == 0;
    }

    private sealed class FailingReader : TextReader
    {
        public override int Read() => throw new IOException("native input failure");
    }

    private sealed class FailingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value) => throw new IOException("native output failure");
    }

    private sealed class DisposedWriter : StringWriter
    {
        public override void Flush() => throw new ObjectDisposedException(nameof(DisposedWriter));
    }
}
