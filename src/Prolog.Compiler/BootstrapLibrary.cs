namespace Prolog.Compiler;

/// <summary>
/// The Prolog definitions every engine loads before user code.
/// </summary>
/// <remarks>
/// <para>
/// The compiler expands <c>,/2</c>, <c>;/2</c>, <c>-&gt;/2</c>, <c>*-&gt;/2</c>, and <c>\+/1</c> in
/// place when they appear directly in a clause body, which is the fast path and the one with correct
/// cut scoping. A goal built at run time and reached through <c>call/1</c> cannot be expanded that
/// way, so the same constructs also exist here as ordinary predicates, defined in terms of the
/// inline forms.
/// </para>
/// <para>
/// The source is a constant rather than an embedded resource so that nothing has to be loaded
/// through reflection, which keeps the startup path trim-safe and AOT-safe.
/// </para>
/// <para>
/// Known deviation: a cut inside a meta-called goal is local to that goal and prunes nothing in the
/// meta-call itself, so <c>call((a, !, b))</c> behaves as <c>call((a, b))</c>. Reaching full ISO
/// behaviour needs a call barrier the engine does not have yet.
/// </para>
/// </remarks>
internal static class BootstrapLibrary
{
    /// <summary>The library source, consulted by <see cref="PrologEngine"/> at construction.</summary>
    internal const string Source = """
        % Control constructs, reachable when a goal is assembled at run time and reached
        % through call/1. Every body below uses the inline form the compiler expands, so
        % none of these predicates is recursive.

        ','(A, B) :- call(A), call(B).

        ';'('->'(C, T), E) :- !, ( call(C) -> call(T) ; call(E) ).
        ';'('*->'(C, T), E) :- !, ( call(C) *-> call(T) ; call(E) ).
        ';'(A, _) :- call(A).
        ';'(_, B) :- call(B).

        '->'(C, T) :- ( call(C) -> call(T) ).
        '*->'(C, T) :- ( call(C) *-> call(T) ).

        \+(G) :- \+ call(G).
        not(G) :- \+ call(G).

        % call(!) is defined to behave as true: the cut is local to the call, and there is
        % nothing inside it to prune.
        !.
        """;
}
