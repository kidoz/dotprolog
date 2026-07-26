namespace Prolog.Compiler;

/// <summary>
/// The library predicates that are themselves written in Prolog, loaded into every engine after
/// <see cref="BootstrapLibrary"/>.
/// </summary>
/// <remarks>
/// <para>
/// A predicate belongs here unless it needs something only the runtime can reach — the heap, the
/// symbol table, the output writer. <c>sort/2</c> is native because comparing terms is; <c>append/3</c>
/// is not, because it is two clauses of ordinary Prolog and reads better as such.
/// </para>
/// <para>
/// A consulted file that defines one of these replaces it outright, because loading a predicate
/// redefines it rather than adding to it. A program is therefore free to write its own
/// <c>member/2</c> without inheriting extra solutions from this one.
/// </para>
/// </remarks>
internal static class StandardLibrary
{
    /// <summary>The library source, consulted by <see cref="PrologEngine"/> at construction.</summary>
    internal const string Source = """
        % --- Meta-call -------------------------------------------------------------
        % call/2..8 append their extra arguments to the goal and meta-call the result.
        % The cut deviation call/1 documents applies to these too.

        call(G, A) :- '$add_args'(G, [A], Goal), call(Goal).
        call(G, A, B) :- '$add_args'(G, [A, B], Goal), call(Goal).
        call(G, A, B, C) :- '$add_args'(G, [A, B, C], Goal), call(Goal).
        call(G, A, B, C, D) :- '$add_args'(G, [A, B, C, D], Goal), call(Goal).
        call(G, A, B, C, D, E) :- '$add_args'(G, [A, B, C, D, E], Goal), call(Goal).
        call(G, A, B, C, D, E, F) :- '$add_args'(G, [A, B, C, D, E, F], Goal), call(Goal).
        call(G, A, B, C, D, E, F, H) :- '$add_args'(G, [A, B, C, D, E, F, H], Goal), call(Goal).

        once(G) :- call(G), !.
        ignore(G) :- ( call(G) -> true ; true ).

        % --- Lists -----------------------------------------------------------------

        append([], L, L).
        append([H|T], L, [H|R]) :- append(T, L, R).

        member(X, [X|_]).
        member(X, [_|T]) :- member(X, T).

        memberchk(X, [Y|T]) :- ( X = Y -> true ; memberchk(X, T) ).

        % is_list/2 first so that the common call leaves no choice point behind; the
        % third clause is what makes length(L, N) enumerate lists of growing length.
        length(L, N) :- is_list(L), !, '$length'(L, 0, N).
        length(L, N) :- integer(N), !, N >= 0, '$length_make'(N, L).
        length(L, N) :- '$length'(L, 0, N).

        '$length'([], N, N).
        '$length'([_|T], S0, N) :- S is S0 + 1, '$length'(T, S, N).

        '$length_make'(0, []) :- !.
        '$length_make'(N, [_|T]) :- M is N - 1, '$length_make'(M, T).

        reverse(L, R) :- '$reverse'(L, [], R).

        '$reverse'([], R, R).
        '$reverse'([H|T], A, R) :- '$reverse'(T, [H|A], R).

        nth0(I, L, E) :- integer(I), !, I >= 0, '$nth'(I, L, E).
        nth0(I, L, E) :- '$nth_search'(L, E, 0, I).

        nth1(I, L, E) :- integer(I), !, I >= 1, J is I - 1, '$nth'(J, L, E).
        nth1(I, L, E) :- '$nth_search'(L, E, 1, I).

        '$nth'(0, [E|_], E) :- !.
        '$nth'(N, [_|T], E) :- N > 0, M is N - 1, '$nth'(M, T, E).

        '$nth_search'([E|_], E, I, I).
        '$nth_search'([_|T], E, I0, I) :- I1 is I0 + 1, '$nth_search'(T, E, I1, I).

        last([H|T], Last) :- '$last'(T, H, Last).

        '$last'([], Last, Last).
        '$last'([H|T], _, Last) :- '$last'(T, H, Last).

        select(X, [X|T], T).
        select(X, [H|T], [H|R]) :- select(X, T, R).

        selectchk(X, L, R) :- select(X, L, R), !.

        subtract([], _, []).
        subtract([H|T], L, R) :-
            ( memberchk(H, L) -> R = R1 ; R = [H|R1] ),
            subtract(T, L, R1).

        intersection([], _, []).
        intersection([H|T], L, R) :-
            ( memberchk(H, L) -> R = [H|R1] ; R = R1 ),
            intersection(T, L, R1).

        union([], L, L).
        union([H|T], L, R) :-
            ( memberchk(H, L) -> R = R1 ; R = [H|R1] ),
            union(T, L, R1).

        % An element is deleted when it unifies, but the test must not bind anything,
        % which is what \= gives and =/2 inside a negation would not.
        delete([], _, []).
        delete([H|T], X, R) :-
            ( H \= X -> R = [H|R1] ; R = R1 ),
            delete(T, X, R1).

        exclude(_, [], []).
        exclude(G, [H|T], R) :-
            ( call(G, H) -> R = R1 ; R = [H|R1] ),
            exclude(G, T, R1).

        include(_, [], []).
        include(G, [H|T], R) :-
            ( call(G, H) -> R = [H|R1] ; R = R1 ),
            include(G, T, R1).

        partition(_, [], [], []).
        partition(G, [H|T], I, E) :-
            ( call(G, H) -> I = [H|I1], E = E1 ; I = I1, E = [H|E1] ),
            partition(G, T, I1, E1).

        maplist(_, []).
        maplist(G, [A|As]) :- call(G, A), maplist(G, As).

        maplist(_, [], []).
        maplist(G, [A|As], [B|Bs]) :- call(G, A, B), maplist(G, As, Bs).

        maplist(_, [], [], []).
        maplist(G, [A|As], [B|Bs], [C|Cs]) :- call(G, A, B, C), maplist(G, As, Bs, Cs).

        maplist(_, [], [], [], []).
        maplist(G, [A|As], [B|Bs], [C|Cs], [D|Ds]) :-
            call(G, A, B, C, D),
            maplist(G, As, Bs, Cs, Ds).

        foldl(G, L, V0, V) :- '$foldl'(L, G, V0, V).

        '$foldl'([], _, V, V).
        '$foldl'([H|T], G, V0, V) :- call(G, H, V0, V1), '$foldl'(T, G, V1, V).

        foldl(G, L1, L2, V0, V) :- '$foldl'(L1, L2, G, V0, V).

        '$foldl'([], [], _, V, V).
        '$foldl'([H1|T1], [H2|T2], G, V0, V) :-
            call(G, H1, H2, V0, V1),
            '$foldl'(T1, T2, G, V1, V).

        numlist(L, H, R) :- L =< H, '$numlist'(L, H, R).

        '$numlist'(H, H, [H]) :- !.
        '$numlist'(L, H, [L|T]) :- L1 is L + 1, '$numlist'(L1, H, T).

        sum_list(L, S) :- '$sum_list'(L, 0, S).
        sumlist(L, S) :- sum_list(L, S).

        '$sum_list'([], S, S).
        '$sum_list'([H|T], A, S) :- A1 is A + H, '$sum_list'(T, A1, S).

        max_list([H|T], M) :- '$max_list'(T, H, M).

        '$max_list'([], M, M).
        '$max_list'([H|T], A, M) :- ( H > A -> A1 = H ; A1 = A ), '$max_list'(T, A1, M).

        min_list([H|T], M) :- '$min_list'(T, H, M).

        '$min_list'([], M, M).
        '$min_list'([H|T], A, M) :- ( H < A -> A1 = H ; A1 = A ), '$min_list'(T, A1, M).

        % max_member/2 and min_member/2 order terms, not numbers, so they use @>= and
        % @=< rather than arithmetic comparison.
        max_member(M, [H|T]) :- '$max_member'(T, H, M).

        '$max_member'([], M, M).
        '$max_member'([H|T], A, M) :- ( H @> A -> A1 = H ; A1 = A ), '$max_member'(T, A1, M).

        min_member(M, [H|T]) :- '$min_member'(T, H, M).

        '$min_member'([], M, M).
        '$min_member'([H|T], A, M) :- ( H @< A -> A1 = H ; A1 = A ), '$min_member'(T, A1, M).

        list_to_set(L, S) :- '$list_to_set'(L, [], S).

        '$list_to_set'([], _, []).
        '$list_to_set'([H|T], Seen, R) :-
            ( '$memberchk_eq'(H, Seen) -> R = R1 ; R = [H|R1] ),
            '$list_to_set'(T, [H|Seen], R1).

        '$memberchk_eq'(X, [Y|T]) :- ( X == Y -> true ; '$memberchk_eq'(X, T) ).

        permutation([], []).
        permutation(L, [H|T]) :- select(H, L, R), permutation(R, T).

        flatten(L, F) :- '$flatten'(L, [], F0), F = F0.

        '$flatten'(V, T, [V|T]) :- var(V), !.
        '$flatten'([], T, T) :- !.
        '$flatten'([H|R], T, F) :- !, '$flatten'(R, T, F1), '$flatten'(H, F1, F).
        '$flatten'(A, T, [A|T]).

        pairs_keys_values([], [], []).
        pairs_keys_values([K-V|T], [K|Ks], [V|Vs]) :- pairs_keys_values(T, Ks, Vs).

        pairs_keys([], []).
        pairs_keys([K-_|T], [K|Ks]) :- pairs_keys(T, Ks).

        pairs_values([], []).
        pairs_values([_-V|T], [V|Vs]) :- pairs_values(T, Vs).

        % --- Sorting with a user-supplied order ------------------------------------
        % A merge sort, because an insertion sort would call the comparison predicate
        % a quadratic number of times and that predicate is the expensive part.

        predsort(P, L, Sorted) :- '$predsort'(L, P, Sorted).

        '$predsort'([], _, []) :- !.
        '$predsort'([X], _, [X]) :- !.
        '$predsort'(L, P, Sorted) :-
            '$halve'(L, A, B),
            '$predsort'(A, P, SA),
            '$predsort'(B, P, SB),
            '$predmerge'(SA, SB, P, Sorted).

        '$halve'([], [], []).
        '$halve'([X], [X], []).
        '$halve'([X, Y|T], [X|A], [Y|B]) :- '$halve'(T, A, B).

        '$predmerge'([], L, _, L) :- !.
        '$predmerge'(L, [], _, L) :- !.
        '$predmerge'([A|As], [B|Bs], P, R) :-
            call(P, Order, A, B),
            '$predmerge_step'(Order, A, As, B, Bs, P, R).

        '$predmerge_step'('<', A, As, B, Bs, P, [A|R]) :- '$predmerge'(As, [B|Bs], P, R).
        '$predmerge_step'('>', A, As, B, Bs, P, [B|R]) :- '$predmerge'([A|As], Bs, P, R).
        '$predmerge_step'('=', A, As, _, Bs, P, [A|R]) :- '$predmerge'(As, Bs, P, R).

        % --- bagof/3 and setof/3 ---------------------------------------------------
        % Unlike findall/3 these fail when the goal has no solutions, and they group
        % the solutions by the goal's free variables, offering one group per binding
        % of those variables on backtracking.

        % V^Goal called directly is just Goal; the qualifier only means something to
        % the free-variable walk below.
        ^(_, Goal) :- call(Goal).

        bagof(Template, Goal, Bag) :-
            term_variables(Template, Bound),
            '$free_variables'(Goal, Bound, [], Reversed),
            reverse(Reversed, Witness),
            '$bagof'(Witness, Template, Goal, Bag).

        setof(Template, Goal, Set) :-
            bagof(Template, Goal, Bag),
            sort(Bag, Set).

        % With nothing free, there is one group and no binding to report.
        '$bagof'([], Template, Goal, Bag) :-
            !,
            findall(Template, Goal, Bag),
            Bag \== [].

        % Otherwise each solution is collected under its witness, and keysort brings
        % equal witnesses together so that one pass can group them.
        '$bagof'(Witness, Template, Goal, Bag) :-
            findall(Witness-Template, Goal, Pairs),
            Pairs \== [],
            keysort(Pairs, Sorted),
            '$group_pairs'(Sorted, Groups),
            member(Witness-Bag, Groups).

        '$group_pairs'([], []).
        '$group_pairs'([K-V|T], [K-[V|Vs]|Groups]) :-
            '$same_key'(K, T, Vs, Rest),
            '$group_pairs'(Rest, Groups).

        '$same_key'(K, [K1-V|T], Vs, Rest) :-
            K == K1,
            !,
            Vs = [V|Vs1],
            '$same_key'(K, T, Vs1, Rest).
        '$same_key'(_, Rest, [], Rest).

        % The free variables of a goal are those a solution can bind and the caller
        % can therefore see grouped. Control constructs are walked into so that a
        % variable's position inside one is what decides. A variable occurring only
        % under \+ is not free: negation proves a goal, it never binds anything.
        '$free_variables'(Goal, _, Free, Free) :- var(Goal), !.
        '$free_variables'(Quantified^Goal, Bound, Free0, Free) :-
            !,
            term_variables(Quantified, Vars),
            append(Vars, Bound, Bound1),
            '$free_variables'(Goal, Bound1, Free0, Free).
        '$free_variables'(\+ _, _, Free, Free) :- !.
        '$free_variables'((A, B), Bound, Free0, Free) :-
            !,
            '$free_variables'(A, Bound, Free0, Free1),
            '$free_variables'(B, Bound, Free1, Free).
        '$free_variables'((A ; B), Bound, Free0, Free) :-
            !,
            '$free_variables'(A, Bound, Free0, Free1),
            '$free_variables'(B, Bound, Free1, Free).
        '$free_variables'((A -> B), Bound, Free0, Free) :-
            !,
            '$free_variables'(A, Bound, Free0, Free1),
            '$free_variables'(B, Bound, Free1, Free).
        '$free_variables'(Goal, Bound, Free0, Free) :-
            term_variables(Goal, Vars),
            '$add_free'(Vars, Bound, Free0, Free).

        '$add_free'([], _, Free, Free).
        '$add_free'([V|Vs], Bound, Free0, Free) :-
            (   '$memberchk_eq'(V, Bound) -> Free1 = Free0
            ;   '$memberchk_eq'(V, Free0) -> Free1 = Free0
            ;   Free1 = [V|Free0]
            ),
            '$add_free'(Vs, Bound, Free1, Free).

        % --- Aggregation -----------------------------------------------------------

        aggregate_all(count, Goal, Count) :-
            findall(x, Goal, Xs),
            length(Xs, Count).

        aggregate_all(count(T), Goal, Count) :-
            findall(T, Goal, Xs),
            length(Xs, Count).

        aggregate_all(bag(T), Goal, Bag) :-
            findall(T, Goal, Bag).

        aggregate_all(set(T), Goal, Set) :-
            findall(T, Goal, Bag),
            sort(Bag, Set).

        aggregate_all(sum(E), Goal, Sum) :-
            findall(E, Goal, Es),
            sum_list(Es, Sum).

        aggregate_all(max(E), Goal, Max) :-
            findall(E, Goal, Es),
            Es \== [],
            max_list(Es, Max).

        aggregate_all(min(E), Goal, Min) :-
            findall(E, Goal, Es),
            Es \== [],
            min_list(Es, Min).
        """;
}
