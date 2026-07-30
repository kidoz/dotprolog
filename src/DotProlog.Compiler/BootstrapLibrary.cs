namespace DotProlog.Compiler;

/// <summary>
/// The Prolog definitions every engine loads before user code.
/// </summary>
/// <remarks>
/// <para>
/// The compiler expands <c>,/2</c>, <c>;/2</c>, <c>-&gt;/2</c>, <c>*-&gt;/2</c>, and <c>\+/1</c> in
/// place when they appear directly in a clause body. A control term built at run time and reached
/// through <c>call/1</c> goes through the same lowering via
/// <see cref="DotProlog.Runtime.IRuntimeCompiler"/>.
/// </para>
/// <para>
/// The source is a constant rather than an embedded resource so that nothing has to be loaded
/// through reflection, which keeps the startup path trim-safe and AOT-safe.
/// </para>
/// </remarks>
internal static class BootstrapLibrary
{
    /// <summary>The library source, consulted by <see cref="PrologEngine"/> at construction.</summary>
    internal const string Source = """
        not(G) :- \+ call(G).

        % call(!) is defined to behave as true: the cut is local to the call, and there is
        % nothing inside it to prune.
        !.

        % findall/3 as a failure-driven loop. '$collect_begin' opens a solution buffer that
        % survives backtracking, '$collect_add' copies one solution into it, and
        % '$collect_end' materialises the lot as a list. The loop reaches '$collect_end'
        % through the ';' branch, because the first branch always fails.
        findall(Template, Goal, Bag) :-
            '$validate_callable'(Goal),
            '$validate_partial_list'(Bag),
            '$collect_begin',
            ( call(Goal), '$collect_add'(Template), fail ; true ),
            '$collect_end'(Bag).

        forall(Condition, Action) :-
            \+ ( call(Condition), \+ call(Action) ).
        """;
}
