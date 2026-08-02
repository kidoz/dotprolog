using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>The complete predefined predicate and evaluable-functor inventory from ISO/IEC 13211-1.</summary>
public sealed class IsoPartOneInventoryTests
{
    private static readonly (string Name, int Arity)[] Predicates =
    [
        ("true", 0),
        ("fail", 0),
        ("false", 0),
        ("repeat", 0),
        ("halt", 0),
        ("var", 1),
        ("atom", 1),
        ("integer", 1),
        ("float", 1),
        ("atomic", 1),
        ("compound", 1),
        ("nonvar", 1),
        ("number", 1),
        ("callable", 1),
        ("ground", 1),
        ("acyclic_term", 1),
        ("current_predicate", 1),
        ("asserta", 1),
        ("assertz", 1),
        ("retract", 1),
        ("abolish", 1),
        ("retractall", 1),
        ("current_input", 1),
        ("current_output", 1),
        ("set_input", 1),
        ("set_output", 1),
        ("flush_output", 0),
        ("flush_output", 1),
        ("at_end_of_stream", 0),
        ("at_end_of_stream", 1),
        ("get_char", 1),
        ("get_code", 1),
        ("peek_char", 1),
        ("peek_code", 1),
        ("put_char", 1),
        ("put_code", 1),
        ("nl", 0),
        ("nl", 1),
        ("get_byte", 1),
        ("peek_byte", 1),
        ("put_byte", 1),
        ("read", 1),
        ("write", 1),
        ("writeq", 1),
        ("write_canonical", 1),
        ("once", 1),
        ("halt", 1),
        ("=", 2),
        ("unify_with_occurs_check", 2),
        ("\\=", 2),
        ("subsumes_term", 2),
        ("==", 2),
        ("\\==", 2),
        ("@<", 2),
        ("@=<", 2),
        ("@>", 2),
        ("@>=", 2),
        ("sort", 2),
        ("keysort", 2),
        ("=..", 2),
        ("copy_term", 2),
        ("term_variables", 2),
        ("is", 2),
        ("=:=", 2),
        ("=\\=", 2),
        ("<", 2),
        ("=<", 2),
        (">", 2),
        (">=", 2),
        ("clause", 2),
        ("stream_property", 2),
        ("set_stream_position", 2),
        ("char_conversion", 2),
        ("current_char_conversion", 2),
        ("set_prolog_flag", 2),
        ("current_prolog_flag", 2),
        ("atom_length", 2),
        ("atom_chars", 2),
        ("atom_codes", 2),
        ("char_code", 2),
        ("number_chars", 2),
        ("number_codes", 2),
        ("close", 1),
        ("close", 2),
        ("get_char", 2),
        ("get_code", 2),
        ("peek_char", 2),
        ("peek_code", 2),
        ("put_char", 2),
        ("put_code", 2),
        ("get_byte", 2),
        ("peek_byte", 2),
        ("put_byte", 2),
        ("read", 2),
        ("write", 2),
        ("writeq", 2),
        ("write_canonical", 2),
        ("read_term", 2),
        ("write_term", 2),
        ("compare", 3),
        ("functor", 3),
        ("arg", 3),
        ("findall", 3),
        ("bagof", 3),
        ("setof", 3),
        ("op", 3),
        ("current_op", 3),
        ("atom_concat", 3),
        ("catch", 3),
        ("open", 3),
        ("read_term", 3),
        ("write_term", 3),
        ("open", 4),
        ("sub_atom", 5),
        ("call", 2),
        ("call", 3),
        ("call", 4),
        ("call", 5),
        ("call", 6),
        ("call", 7),
        ("call", 8),
    ];

    [Fact]
    public void EveryPartOnePredefinedPredicateIsInstalled()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);

        foreach ((var name, var arity) in Predicates)
        {
            var functor = engine.Program.Symbols.InternFunctor(name, arity);
            Assert.True(
                engine.Program.Builtins.TryGetId(functor, out _) || engine.Program.IsDefined(functor),
                $"ISO/IEC 13211-1 predicate {name}/{arity} is not installed."
            );
        }
    }

    [Fact]
    public void EveryPartOneControlConstructCompilesInStrictMode()
    {
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);
        LoadResult loaded = engine.ConsultText(
            """
            controls :-
                true,
                (fail ; true),
                call(true),
                !,
                (true -> true),
                (true -> true ; fail),
                \+ fail,
                catch(throw(caught), caught, true).
            """
        );

        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunGoal("controls", out _));
    }

    [Fact]
    public void EveryPartOneEvaluableFunctorExecutesInStrictMode()
    {
        var output = new StringWriter();
        var engine = new PrologEngine(PrologLanguageMode.StrictIso) { Output = output };
        const string goal =
            "_ is pi, _ is +1, _ is -1, _ is abs(-1), _ is sign(-1), "
            + "_ is float_integer_part(1.5), _ is float_fractional_part(1.5), _ is float(1), "
            + "_ is floor(1.5), _ is truncate(-1.5), _ is round(1.5), _ is ceiling(1.5), "
            + "_ is sqrt(4.0), _ is sin(0.0), _ is cos(0.0), _ is tan(0.0), "
            + "_ is asin(0.0), _ is acos(1.0), _ is atan(0.0), _ is exp(0.0), _ is log(1.0), "
            + "_ is \\1, _ is 1 + 2, _ is 3 - 2, _ is 2 * 3, _ is 4 // 2, _ is 4 / 2, "
            + "_ is 5 rem 2, _ is 5 mod 2, _ is 5 div 2, _ is 2 ** 2, _ is 2 ^ 2, "
            + "_ is max(1, 2), _ is min(1, 2), _ is atan2(0.0, 1.0), "
            + "_ is 4 >> 1, _ is 1 << 2, _ is 3 /\\ 1, _ is 1 \\/ 2, _ is xor(1, 3), "
            + "write(ok), nl";

        Assert.Equal(RunResult.Success, engine.RunGoal(goal, out _));
        Assert.Equal("ok\n", output.ToString().ReplaceLineEndings("\n"));
    }
}
