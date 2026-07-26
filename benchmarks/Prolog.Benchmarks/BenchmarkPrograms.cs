namespace Prolog.Benchmarks;

/// <summary>Prolog sources the benchmarks consult, kept in one place so numbers stay comparable.</summary>
internal static class BenchmarkPrograms
{
    /// <summary>Naive reverse — the traditional Prolog throughput benchmark, plus a list builder.</summary>
    internal const string NaiveReverse = """
        app([], L, L).
        app([H|T], L, [H|R]) :- app(T, L, R).

        nrev([], []).
        nrev([H|T], R) :- nrev(T, RT), app(RT, [H], R).

        mklist(0, []) :- !.
        mklist(N, [N|T]) :- M is N - 1, mklist(M, T).
        """;

    /// <summary>A deterministic tail-recursive countdown, which measures dispatch and last-call cost.</summary>
    internal const string Countdown = """
        count(0) :- !.
        count(N) :- M is N - 1, count(M).
        """;

    /// <summary>A fact table scanned by backtracking, which measures choice-point and trail cost.</summary>
    internal const string FactTable = """
        item(a). item(b). item(c). item(d). item(e).
        item(f). item(g). item(h). item(i). item(j).
        item(k). item(l). item(m). item(n). item(o).
        item(p). item(q). item(r). item(s). item(t).

        find(X) :- item(X), X = t.
        """;
}
