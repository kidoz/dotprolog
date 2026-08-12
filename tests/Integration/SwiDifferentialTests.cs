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
        "( setup_call_cleanup(fail, true, write(never)) -> true ; write(setup_failed) )"
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
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };

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
