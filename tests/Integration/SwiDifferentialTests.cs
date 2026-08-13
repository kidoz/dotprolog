using DotProlog.Compiler;
using DotProlog.Runtime;

namespace Integration.Tests;

/// <summary>
/// Runs one goal corpus against both DotProlog and a locally installed SWI-Prolog and compares
/// what each writes. SWI-Prolog is the differential oracle the scope document sanctions: optional
/// and never a dependency, so the suite is opt-in and skips when <c>swipl</c> is absent.
/// </summary>
/// <remarks>
/// Corpus goals must be deterministic, write their entire observable result, and stay inside the
/// surface the SWI compatibility ledger claims: no double-quoted text (the systems' defaults
/// differ), no error contexts (only the formal <c>error/2</c> argument is portable), and no
/// SWI-only spellings.
/// </remarks>
public sealed class SwiDifferentialTests
{
    private const string OptInVariable = "DOTPROLOG_RUN_SWI_DIFFERENTIAL_TESTS";

    public static readonly TheoryData<string> Corpus = new(
        "ord_union([a, c], [b, c, d], U), write(U)",
        "ord_subtract([a, b, c], [b], D), write(D)",
        "ord_intersection([a, b, c], [b, d], I), write(I)",
        "list_to_ord_set([c, a, b, a], S), write(S)",
        "( ord_memberchk(b, [a, b, c]) -> write(yes) ; write(no) )",
        "list_to_assoc([b-2, a-1, c-3], A), assoc_to_list(A, L), write(L)",
        "list_to_assoc([b-2, a-1], A), put_assoc(c, A, 3, A1), get_assoc(c, A1, V), write(V)",
        "list_to_assoc([a-1, b-2, c-3], A), del_assoc(b, A, V, A1), assoc_to_keys(A1, K), write(V-K)",
        "transpose_pairs([b-2, a-1], T), write(T)",
        "msort([b, a, c, a], L), write(L)",
        "aggregate_all(count, member(_, [a, b]), C), write(C)",
        "aggregate_all(sum(X), member(X, [1, 2, 3]), S), write(S)",
        "catch(must_be(integer, foo), error(E, _), true), write(E)",
        "catch(must_be(positive_integer, 0), error(E, _), true), write(E)",
        "catch(must_be(oneof([a, b]), c), error(E, _), true), write(E)",
        "( is_of_type(negative_integer, -2) -> write(yes) ; write(no) )",
        "char_type(a, to_upper(U)), write(U)",
        "code_type(53, digit(W)), write(W)",
        "format('~16r', [255])",
        "format('~2R', [5])",
        "between(1, inf, X), X > 3, !, write(X)",
        "succ(3, S), write(S)",
        "numbervars(f(X, Y, X), 0, E), write(f(X, Y, X)-E)",
        "findall(X, member(X, [a, b]), L, [z]), write(L)",
        "atom_to_term('foo(X, Y)', T, _), functor(T, N, A), write(N/A)",
        "( variant(f(X, Y), f(_, _)) -> write(yes) ; write(no) )",
        "current_output(S), write(a), tab(S, 2), write(b)",
        "nb_setval(k, f(1, a)), nb_getval(k, V), write(V)",
        "b_setval(k, 1), ( b_setval(k, 2), fail ; b_getval(k, V) ), write(V)",
        "nb_setval(k, base), ( b_setval(k, temp), fail ; true ), nb_getval(k, V), write(V)",
        "setup_call_cleanup(true, write(g), write(c)), write(k)",
        "( setup_call_cleanup(true, fail, write(c)) -> true ; write(f) )",
        "catch(setup_call_cleanup(true, throw(a), throw(b)), E, true), write(E)",
        "catch(setup_call_cleanup(true, true, throw(c)), E, true), write(E)",
        "findall(X, setup_call_cleanup(true, member(X, [1, 2]), write(c)), L), write(L)",
        "setup_call_cleanup(true, once(member(X, [1, 2])), write(c)), write(X)",
        "( setup_call_cleanup(fail, true, write(never)) -> true ; write(setup_failed) )",
        "T = f(a, b), setarg(1, T, x), write(T)",
        "T = f(a), ( setarg(1, T, x), fail ; true ), write(T)",
        "T = f(a), call((setarg(1, T, x), true)), write(T)",
        "T = counter(0), forall(member(_, [a, b, c]), ( arg(1, T, N), N1 is N + 1, nb_setarg(1, T, N1) )), arg(1, T, C), write(C)",
        "set_prolog_flag(occurs_check, true), ( X = f(X) -> write(cycled) ; write(failed) )",
        "set_prolog_flag(occurs_check, true), ( p(X, a) = p(f(X), a) -> write(cycled) ; write(failed) )",
        "set_prolog_flag(occurs_check, error), catch(X = f(X), error(occurs_check(_, _), _), write(caught))",
        "( ord_seteq([a, b], [a, b]) -> write(yes) ; write(no) )",
        "( ord_seteq([a, b], [a, c]) -> write(yes) ; write(no) )",
        "ord_symdiff([a, b, c], [b, d], D), write(D)",
        "aggregate_all(max(X, W), member(W-X, [a-1, b-3, c-2]), M), write(M)",
        "aggregate_all(min(X, W), member(W-X, [a-1, b-3, c-2]), M), write(M)",
        "nb_setval(diff_key, 42), ( nb_current(diff_key, V) -> write(V) ; write(unset) )",
        "( nb_current(diff_never_set, _) -> write(set) ; write(unset) )",
        "list_to_assoc([b-2, a-1, c-3], A), findall(K-V, gen_assoc(K, A, V), L), write(L)",
        "list_to_assoc([b-2, a-1], A), get_assoc(b, A, Old, A2, 9), assoc_to_list(A2, L), write(Old-L)",
        "list_to_assoc([b-2, a-1, c-3], A), del_min_assoc(A, K, V, A2), assoc_to_list(A2, L), write(K-V-L)",
        "list_to_assoc([b-2, a-1, c-3], A), del_max_assoc(A, K, V, A2), assoc_to_list(A2, L), write(K-V-L)",
        "( is_assoc(t) -> write(yes) ; write(no) ), ( is_assoc(foo) -> write(yes) ; write(no) )",
        "aggregate_all(r(sum(X), count), member(X, [1, 2, 3]), R), write(R)",
        "aggregate_all(p(max(X, W), count), member(W-X, [a-1, b-2]), R), write(R)",
        "aggregate_all(p(count, bag(X)), (member(X, [1]), fail), R), write(R)",
        "catch(aggregate_all(k(sum(_), foo), true, _), error(E, _), write(E))",
        "format(\"a~@b\", [write(mid)])",
        "( format(\"x~@\", [fail]) -> write(yes) ; write(no) )",
        "format(\"~W\", [f('A b'), [quoted(true)]])",
        "findall(C, char_type(C, digit(3)), L), write(L)",
        "findall(C, char_type(C, upper(a)), L), write(L)",
        "aggregate_all(count, char_type(_, digit(_)), N), write(N)",
        "with_output_to(atom(A), portray_clause((foo(V, W) :- bar(V), baz(W)))), write(A)",
        "with_output_to(atom(A), portray_clause((p :- (a -> b ; c)))), write(A)",
        "with_output_to(atom(A), portray_clause((p :- q, (a, b ; c)))), write(A)",
        "with_output_to(atom(A), portray_clause(f('A b', [1, 2]))), write(A)",
        "atom_string(ab, S), ( string(S) -> write(yes) ; write(no) ), writeq(S)",
        "atom_string(b, S), msort([f(x), a, S, 1], L), writeq(L)",
        "atom_string(ab, S), ( S == ab -> write(eq) ; write(neq) ), ( atom(S) -> write(a) ; write(na) ), ( atomic(S) -> write(t) ; write(nt) )",
        "string_concat(ab, cd, R), writeq(R)",
        "atom_string(abcd, W), string_concat(ab, B, W), writeq(B)",
        "atom_string(ab, W), findall(A-B, string_concat(A, B, W), L), writeq(L)",
        "string_to_atom(S, foo), writeq(S), string_to_atom(baz, B), writeq(B)",
        "string_chars(S, [a, b]), writeq(S), string_codes(S2, [99, 100]), writeq(S2)",
        "atom_string('42', T), number_string(N, T), writeq(N), number_string(2.5, S), writeq(S)",
        "atom_string(abc, T), ( number_string(_, T) -> write(yes) ; write(no) )",
        "split_string('a,b,,c', ',', '', L), writeq(L)",
        "split_string('/a//b/', '/', '/', L), writeq(L)",
        "split_string('  hi  ', '', ' ', L), writeq(L)",
        "sub_string(abcde, 1, 3, A, S), writeq(S-A)",
        "aggregate_all(count, sub_string(abc, _, _, _, _), N), writeq(N)",
        "string_code(2, abc, C), writeq(C)",
        "string_lower('AbC', L), string_upper('AbC', U), writeq(L), writeq(U)",
        "string_length(foo, N), writeq(N)",
        "with_output_to(string(S), write(hello)), writeq(S)",
        "format(string(S), '~w', [42]), writeq(S)",
        "atom_string(hi, S), format('~s', [S])",
        "atom_string(ab, S), writeq(S), nl, write(S)",
        "catch(must_be(string, foo), error(E, _), true), writeq(E)",
        // print_message/2 writes to user_error; the leading catch drops SWI's thread
        // decoration (the flag does not exist here, and the catch swallows that error).
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(type_error(atom, 1), context(foo/2, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(type_error(atom, []), context(foo/2, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(type_error(atom, f(x)), context(foo/2, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(type_error(evaluable, foo/0), context((is)/2, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(instantiation_error, context(foo/2, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(domain_error(order, x), context(compare/3, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(existence_error(procedure, foo/2), context(bar/1, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(existence_error(stream, mystream), context(close/1, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(permission_error(modify, static_procedure, foo/2), context(assertz/1, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(permission_error(input, stream, user_output), context(get_char/2, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(evaluation_error(zero_divisor), context((is)/2, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(representation_error(max_arity), context(f/1, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(resource_error(memory), context(f/1, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(syntax_error(operator_expected), context(read/1, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(uninstantiation_error(bound), context(open/4, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(weird_error(a, b), context(f/1, _)))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, error(type_error(atom, 1), context(foo/2, 'extra note')))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(warning, format('hello ~w', [world]))",
        "catch(set_prolog_flag(message_context, []), _, true), catch(set_prolog_flag(verbose, normal), _, true), print_message(informational, format('fyi ~w', [3]))",
        "catch(set_prolog_flag(message_context, []), _, true), print_message(error, hello_world(42))",
        "print_message(silent, format('never', [])), write(done)",
        // Unbounded integers: promotion, demotion, literals, and helpers agree with GMP.
        "X is 2 ^ 100, write(X)",
        "X is 10 ^ 30 + 10 ^ 30, write(X)",
        "X is 999999999999 * 999999999999, write(X)",
        "X is -(10 ^ 30) mod 7, write(X)",
        "X is (10 ^ 30) // 999, write(X)",
        "X is (10 ^ 40) >> 100, write(X)",
        "X is 1 << 100, write(X)",
        "X is 2 ^ 100 - 2 ^ 100 + 7, write(X)",
        "X is truncate(1.0e25), write(X)",
        "X is min(10 ^ 30, 5), Y is max(10 ^ 30, 5), write(X-Y)",
        "X = 123456789012345678901234567890, Y = 123456789012345678901234567890, ( X == Y -> write(eq) ; write(ne) )",
        "atom_number('123456789012345678901234567890', X), Y is X + 1, write(Y)",
        "number_codes(X, [57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57]), write(X)",
        // No floats beside integers: value-ordering numbers across kinds is SWI's documented
        // ISO deviation, which DotProlog deliberately does not follow.
        "msort([100000000000000000000000000000, 1, foo, 99999999999999999999999999999], L), write(L)",
        "( integer(123456789012345678901234567890) -> write(yes) ; write(no) )",
        "format('~d', [123456789012345678901234567890])",
        "format('~16r', [123456789012345678901234567890])",
        "succ(99999999999999999999999999, X), write(X)",
        "between(99999999999999999999999999, inf, X), !, write(X)",
        "current_prolog_flag(bounded, B), write(B)",
        "X is 10 ^ 21, writeq(X), nl, write(X)"
    );

    // The corpus stays runnable on DotProlog alone, so a corpus typo or a regression in the
    // claimed surface fails everywhere and not only where swipl happens to be installed.
    [Theory]
    [MemberData(nameof(Corpus))]
    public void CorpusGoalsRunOnDotProlog(string goal) => Assert.NotEqual(string.Empty, RunDotProlog(goal));

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task DotPrologAgreesWithSwiProlog(string goal)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the SWI differential suite."
        );
        var swipl = FindSwipl();
        Assert.SkipWhen(swipl is null, "swipl was not found on PATH.");

        var ours = RunDotProlog(goal);

        // -f none keeps a user's init file from changing flags or loading libraries.
        (var exitCode, var log) = await ChildProcess.RunAsync(
            swipl!,
            ["-f", "none", "-q", "-g", goal, "-t", "halt"],
            Path.GetTempPath()
        );

        Assert.True(exitCode == 0, $"swipl failed on {goal}: {log}");
        Assert.Equal(log.Trim(), ours.Trim());
    }

    private static string RunDotProlog(string goal)
    {
        // One writer for both streams, because the swipl side of the comparison is also
        // stdout and stderr concatenated. A corpus goal therefore writes to one stream only.
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Error = output };

        LoadResult loaded = engine.ConsultText($":- initialization(({goal})).", "differential.pl");
        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        return output.ToString();
    }

    private static string? FindSwipl()
    {
        var name = OperatingSystem.IsWindows() ? "swipl.exe" : "swipl";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
