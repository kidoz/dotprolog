using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>ISO Prolog-state enumeration, mutation, reader integration, and undefined calls.</summary>
public sealed class PrologFlagTests
{
    [Fact]
    public void EnumeratesEveryIsoFlagInStableOrder()
    {
        Assert.Equal(
            "[bounded-true,max_integer-576460752303423487,min_integer- -576460752303423488,"
                + "integer_rounding_function-toward_zero,max_arity-255,char_conversion-off,debug-off,"
                + "double_quotes-codes,unknown-error,colon_sets_calling_context-true,occurs_check-false]\n",
            PrologTestHost.RunGoal("findall(F-V, current_prolog_flag(F, V), Flags), write(Flags), nl")
        );
    }

    [Fact]
    public void EnumerationHandlesBoundAndSharedArgumentsTransactionally()
    {
        Assert.Equal(
            "codes yes\n",
            PrologTestHost.RunGoal(
                "current_prolog_flag(double_quotes, Value), "
                    + "\\+ current_prolog_flag(Same, Same), var(Same), "
                    + "write(Value), write(' yes'), nl"
            )
        );
    }

    [Fact]
    public void ModulesSeeOnlyTheSameIsoFlagSet()
    {
        Assert.Equal(
            "[bounded,max_integer,min_integer,integer_rounding_function,max_arity,char_conversion,debug,double_quotes,unknown,"
                + "colon_sets_calling_context,occurs_check]\n",
            PrologTestHost.Run(
                """
                :- module(flag_scope, [flag_names/1]).

                flag_names(Names) :- findall(Name, current_prolog_flag(Name, _), Names).

                :- initialization((flag_names(Names), write(Names), nl)).
                """
            )
        );
    }

    [Fact]
    public void MutableFlagsCanBeChangedAndReadBack()
    {
        Assert.Equal(
            "on on chars fail\n",
            PrologTestHost.RunGoal(
                "set_prolog_flag(char_conversion, on), "
                    + "set_prolog_flag(debug, on), "
                    + "set_prolog_flag(double_quotes, chars), "
                    + "set_prolog_flag(unknown, fail), "
                    + "current_prolog_flag(char_conversion, C), "
                    + "current_prolog_flag(debug, D), "
                    + "current_prolog_flag(double_quotes, Q), "
                    + "current_prolog_flag(unknown, U), "
                    + "write(C), write(' '), write(D), write(' '), "
                    + "write(Q), write(' '), write(U), nl"
            )
        );
    }

    [Fact]
    public void DoubleQuotesDirectiveAffectsFollowingSourceText()
    {
        var output = PrologTestHost.Run(
            """
            :- set_prolog_flag(double_quotes, atom).
            atom_text("ab").

            :- set_prolog_flag(double_quotes, chars).
            chars_text("ab").

            :- set_prolog_flag(double_quotes, codes).
            codes_text("ab").

            :- initialization((
                atom_text(ab),
                chars_text([a,b]),
                codes_text([97,98]),
                current_prolog_flag(double_quotes, codes),
                write(yes), nl
            )).
            """
        );

        Assert.Equal("yes\n", output);
    }

    [Fact]
    public void DoubleQuotesFlagAffectsQueriesAndTermInput()
    {
        var engine = new PrologEngine { Output = new StringWriter() };

        Assert.Equal(RunResult.Success, engine.RunGoal("set_prolog_flag(double_quotes, atom)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("\"hi\" == hi", out _));
        Assert.Equal(
            RunResult.Success,
            engine.RunGoal("atom_codes(Text, [34,104,105,34]), read_term_from_atom(Text, hi, [])", out _)
        );

        Assert.Equal(RunResult.Success, engine.RunGoal("set_prolog_flag(double_quotes, chars)", out _));
        Assert.Equal(RunResult.Success, engine.RunGoal("\"hi\" == [h,i]", out _));
        Assert.Equal(
            RunResult.Success,
            engine.RunGoal("atom_codes(Text, [34,104,105,34]), read_term_from_atom(Text, [h,i], [])", out _)
        );
    }

    [Fact]
    public void UnknownFailAppliesToDirectMetaAndHostCalls()
    {
        var engine = new PrologEngine { Output = new StringWriter() };
        Assert.Equal(RunResult.Success, engine.RunGoal("set_prolog_flag(unknown, fail)", out _));

        Assert.Equal(RunResult.Failure, engine.RunGoal("not_defined_directly", out _));
        Assert.Equal(RunResult.Failure, engine.RunGoal("call(not_defined_through_meta)", out _));

        var host = new PrologHost(engine.Machine);
        PrologPredicate predicate = host.Bind("not_defined_through_host", 0);
        Assert.False(host.Prove(predicate));
    }

    [Fact]
    public void UnknownWarningWritesToUserErrorAndFails()
    {
        var warnings = new StringWriter();
        var engine = new PrologEngine { Output = new StringWriter(), Error = warnings };
        Assert.Equal(RunResult.Success, engine.RunGoal("set_prolog_flag(unknown, warning)", out _));

        Assert.Equal(RunResult.Failure, engine.RunGoal("missing_warning(1)", out _));
        Assert.Equal("Warning: undefined procedure missing_warning/1\n", warnings.ToString().ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("current_prolog_flag(1, _)", "type_error(atom,1)")]
    [InlineData("set_prolog_flag(_, codes)", "instantiation_error")]
    [InlineData("set_prolog_flag(double_quotes, _)", "instantiation_error")]
    [InlineData("set_prolog_flag(1, codes)", "type_error(atom,1)")]
    [InlineData("set_prolog_flag(not_a_flag, value)", "domain_error(prolog_flag,not_a_flag)")]
    [InlineData("set_prolog_flag(double_quotes, strings)", "domain_error(flag_value,double_quotes+strings)")]
    [InlineData("set_prolog_flag(bounded, false)", "domain_error(flag_value,bounded+false)")]
    [InlineData("set_prolog_flag(bounded, true)", "permission_error(modify,flag,bounded)")]
    [InlineData(
        "current_prolog_flag(max_integer, V), set_prolog_flag(max_integer, V)",
        "permission_error(modify,flag,max_integer)"
    )]
    [InlineData(
        "current_prolog_flag(min_integer, V), set_prolog_flag(min_integer, V)",
        "permission_error(modify,flag,min_integer)"
    )]
    [InlineData(
        "set_prolog_flag(integer_rounding_function, toward_zero)",
        "permission_error(modify,flag,integer_rounding_function)"
    )]
    [InlineData("set_prolog_flag(max_arity, 255)", "permission_error(modify,flag,max_arity)")]
    public void ReportsIsoFlagErrors(string goal, string expected)
    {
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(({goal}), error(E, _), write(E))"));
    }
}
