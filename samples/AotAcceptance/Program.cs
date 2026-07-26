using Prolog.Compiler;
using Prolog.Runtime;
using Prolog.Syntax;

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

                % An operator declared at run time, in a native image, changing how a term prints.
                op(700, xfx, likes),
                write(likes(alice, bob)), nl,
                write_canonical(1 + 2 * 3), nl,

                % A grammar consulted from the external file, translated inside the native image.
                phrase(number(N), "427"), write(N), nl,

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
