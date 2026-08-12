namespace DotProlog.Compiler;

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

        call(G, A) :- '$add_args'(G, [A], Goal), call(Goal).
        call(G, A, B) :- '$add_args'(G, [A, B], Goal), call(Goal).
        call(G, A, B, C) :- '$add_args'(G, [A, B, C], Goal), call(Goal).
        call(G, A, B, C, D) :- '$add_args'(G, [A, B, C, D], Goal), call(Goal).
        call(G, A, B, C, D, E) :- '$add_args'(G, [A, B, C, D, E], Goal), call(Goal).
        call(G, A, B, C, D, E, F) :- '$add_args'(G, [A, B, C, D, E, F], Goal), call(Goal).
        call(G, A, B, C, D, E, F, H) :- '$add_args'(G, [A, B, C, D, E, F, H], Goal), call(Goal).

        % Module:Goal, resolved when it is called rather than when it is compiled: the module named
        % may not have been loaded at the point the call was read.
        ':'(Module, Goal) :- '$qualify'(Module, Goal, Resolved), call(Resolved).

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

        foldl(G, L1, L2, L3, V0, V) :- '$foldl'(L1, L2, L3, G, V0, V).

        '$foldl'([], [], [], _, V, V).
        '$foldl'([H1|T1], [H2|T2], [H3|T3], G, V0, V) :-
            call(G, H1, H2, H3, V0, V1),
            '$foldl'(T1, T2, T3, G, V1, V).

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

        % transpose_pairs/2 swaps every Key-Value into Value-Key and key-sorts the
        % result, which is the cheapest stable way to invert a pairs list.
        transpose_pairs(Pairs, Transposed) :-
            '$swap_pairs'(Pairs, Swapped),
            keysort(Swapped, Transposed).

        '$swap_pairs'([], []).
        '$swap_pairs'([K-V|T], [V-K|R]) :- '$swap_pairs'(T, R).

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

        % --- Output capture ---------------------------------------------------------
        % with_output_to/2 runs the goal once with output diverted into a buffer, and
        % restores the previous output however the goal ends: success, failure, or a
        % thrown ball. Losing the restore would leave every later write in the buffer.

        with_output_to(Sink, Goal) :-
            '$capture_begin',
            (   catch(Goal, Error, true)
            ->  '$capture_end'(Text),
                ( nonvar(Error) -> throw(Error) ; true ),
                '$sink'(Sink, Text)
            ;   '$capture_end'(_),
                fail
            ).

        '$sink'(atom(A), Text) :- !, A = Text.
        '$sink'(codes(C), Text) :- !, atom_codes(Text, C).
        '$sink'(chars(C), Text) :- !, atom_chars(Text, C).
        '$sink'(Sink, _) :- throw(error(domain_error(output_sink, Sink), with_output_to/2)).

        % Reading and writing a term as an atom, which is what with_output_to/2 and
        % read_term_from_atom/3 make possible without a file anywhere in sight.
        term_to_atom(Term, Atom) :-
            var(Atom),
            !,
            with_output_to(atom(Atom), write_canonical(Term)).
        term_to_atom(Term, Atom) :-
            read_term_from_atom(Atom, Term, []).

        read_term_from_atom(Atom, Term, Options) :-
            atom_concat(Atom, ' .', Text),
            '$read_from_atom'(Text, Term, Options).

        atom_to_term(Atom, Term, Bindings) :-
            read_term_from_atom(Atom, Term, [variable_names(Bindings)]).

        % --- Term utilities ----------------------------------------------------------
        % numbervars/3 binds each distinct variable in Term to '$VAR'(N), the term the
        % writer already prints as A, B, ... when the numbervars(true) option applies.

        numbervars(Term, Start, End) :-
            must_be(integer, Start),
            term_variables(Term, Vars),
            '$numbervars'(Vars, Start, End).

        '$numbervars'([], N, N).
        '$numbervars'(['$VAR'(N0)|Vs], N0, N) :-
            N1 is N0 + 1,
            '$numbervars'(Vs, N1, N).

        % tab/2 is the native tab/1 with an explicit stream: the count is evaluated as
        % an arithmetic expression, and a non-positive count writes nothing.
        % Two terms are variants when each subsumes the other: equal up to a
        % renaming of variables.
        variant(X, Y) :- subsumes_term(X, Y), subsumes_term(Y, X).

        % X ?= Y holds when further instantiation cannot change whether X and Y
        % unify: they are already identical, or they cannot unify at all.
        '?='(X, Y) :- ( X == Y -> true ; \+ X = Y ).

        tab(Stream, Count) :-
            N is Count,
            ( integer(N) -> true ; type_error(integer, N) ),
            '$tab'(N, Stream).

        '$tab'(N, _) :- N =< 0, !.
        '$tab'(N, Stream) :-
            put_char(Stream, ' '),
            N1 is N - 1,
            '$tab'(N1, Stream).

        % --- Grammars ---------------------------------------------------------------
        % A grammar rule is translated into an ordinary clause when it is loaded. A body built at
        % run time is first expanded into one ordinary goal and then meta-called once. Building the
        % complete goal before calling it keeps grammar cuts transparent within that body, matching
        % the scope of cuts emitted by the static translator.

        phrase(Body, List) :- '$validate_terminal_sequence'(List), phrase(Body, List, []).

        % Rest is unified only after the grammar body has completed. Keeping the body's
        % output argument fresh makes phrase/3 steadfast when Rest is already instantiated.
        phrase(Body, List, Rest) :-
            '$phrase_goal'(Body, List, ActualRest, Goal),
            call(Goal),
            Rest = ActualRest.

        '$phrase_goal'(Body, _, _, _) :- var(Body), !, throw(error(instantiation_error, phrase/3)).
        '$phrase_goal'((A, B), S0, S, (GA, GB)) :- !,
            '$phrase_goal'(A, S0, S1, GA),
            '$phrase_goal'(B, S1, S, GB).
        % An if-then-else is one construct, not a disjunction of two goals, so it is matched
        % before the plain (A ; B) clause can split it.
        '$phrase_goal'((C -> T ; E), S0, S, (GC -> GT ; GE)) :- !,
            '$phrase_goal'(C, S0, S1, GC),
            '$phrase_goal'(T, S1, S, GT),
            '$phrase_goal'(E, S0, S, GE).
        '$phrase_goal'((C *-> T ; E), S0, S, (GC *-> GT ; GE)) :- '$grammar_soft_cut', !,
            '$phrase_goal'(C, S0, S1, GC),
            '$phrase_goal'(T, S1, S, GT),
            '$phrase_goal'(E, S0, S, GE).
        '$phrase_goal'((A ; B), S0, S, (GA ; GB)) :- !,
            '$phrase_goal'(A, S0, S, GA),
            '$phrase_goal'(B, S0, S, GB).
        '$phrase_goal'((A | B), S0, S, (GA ; GB)) :- !,
            '$phrase_goal'(A, S0, S, GA),
            '$phrase_goal'(B, S0, S, GB).
        '$phrase_goal'((A -> B), S0, S, (GA -> GB)) :- !,
            '$phrase_goal'(A, S0, S1, GA),
            '$phrase_goal'(B, S1, S, GB).
        '$phrase_goal'((A *-> B), S0, S, (GA *-> GB)) :- '$grammar_soft_cut', !,
            '$phrase_goal'(A, S0, S1, GA),
            '$phrase_goal'(B, S1, S, GB).
        '$phrase_goal'(\+ A, S0, S, (\+ GA, S = S0)) :- !, '$phrase_goal'(A, S0, _, GA).
        '$phrase_goal'(!, S0, S, (!, S0 = S)) :- !.
        '$phrase_goal'({Goal}, S0, S, (Goal, S0 = S)) :- !.
        '$phrase_goal'([], S0, S, S0 = S) :- !.
        '$phrase_goal'([H|T], S0, S, S0 = Terminals) :- !,
            '$validate_proper_list'([H|T]),
            '$terminal_sequence'([H|T], S, Terminals).
        '$phrase_goal'(Body, S0, S, Goal) :- '$add_args'(Body, [S0, S], Goal).

        '$terminal_sequence'([], Tail, Tail).
        '$terminal_sequence'([H|T], Tail, [H|R]) :- '$terminal_sequence'(T, Tail, R).

        % --- bagof/3 and setof/3 ---------------------------------------------------
        % Unlike findall/3 these fail when the goal has no solutions, and they group
        % the solutions by the goal's free variables, offering one group per binding
        % of those variables on backtracking.

        % V^Goal called directly is just Goal; the qualifier only means something to
        % the free-variable walk below.
        ^(_, Goal) :- call(Goal).

        bagof(Template, Goal, Bag) :-
            '$validate_callable'(Goal),
            '$validate_partial_list'(Bag),
            term_variables(Template, Bound),
            '$free_variables'(Goal, Bound, [], Reversed),
            reverse(Reversed, Witness),
            '$bagof'(Witness, Template, Goal, Bag).

        setof(Template, Goal, Set) :-
            '$validate_callable'(Goal),
            '$validate_partial_list'(Set),
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
            '$take_variant_keys'(K, T, Vs, Rest),
            '$group_pairs'(Rest, Groups).

        '$take_variant_keys'(_, [], [], []).
        '$take_variant_keys'(K, [K1-V|T], [V|Vs], Rest) :-
            subsumes_term(K, K1),
            subsumes_term(K1, K),
            !,
            K = K1,
            '$take_variant_keys'(K, T, Vs, Rest).
        '$take_variant_keys'(K, [Pair|T], Vs, [Pair|Rest]) :-
            '$take_variant_keys'(K, T, Vs, Rest).

        % Only a leading chain of ^/2 terms is existential syntax for bagof/3. A
        % caret nested inside a control construct is an ordinary callable goal, so
        % its variables remain free for grouping.
        '$free_variables'(Goal, _, Free, Free) :- var(Goal), !.
        '$free_variables'(Quantified^Goal, Bound, Free0, Free) :-
            !,
            term_variables(Quantified, Vars),
            append(Vars, Bound, Bound1),
            '$free_variables'(Goal, Bound1, Free0, Free).
        '$free_variables'(Goal, Bound, Free0, Free) :-
            '$free_goal_variables'(Goal, Bound, Free0, Free).

        % Control constructs are walked into so that a variable's position inside
        % one is what decides. A variable occurring only under \+ is not free:
        % negation proves a goal, it never binds anything.
        '$free_goal_variables'(Goal, _, Free, Free) :- var(Goal), !.
        '$free_goal_variables'(\+ _, _, Free, Free) :- !.
        '$free_goal_variables'((A, B), Bound, Free0, Free) :-
            !,
            '$free_goal_variables'(A, Bound, Free0, Free1),
            '$free_goal_variables'(B, Bound, Free1, Free).
        '$free_goal_variables'((A ; B), Bound, Free0, Free) :-
            !,
            '$free_goal_variables'(A, Bound, Free0, Free1),
            '$free_goal_variables'(B, Bound, Free1, Free).
        '$free_goal_variables'((A -> B), Bound, Free0, Free) :-
            !,
            '$free_goal_variables'(A, Bound, Free0, Free1),
            '$free_goal_variables'(B, Bound, Free1, Free).
        '$free_goal_variables'(Goal, Bound, Free0, Free) :-
            term_variables(Goal, Vars),
            '$add_free'(Vars, Bound, Free0, Free).

        '$add_free'([], _, Free, Free).
        '$add_free'([V|Vs], Bound, Free0, Free) :-
            (   '$memberchk_eq'(V, Bound) -> Free1 = Free0
            ;   '$memberchk_eq'(V, Free0) -> Free1 = Free0
            ;   Free1 = [V|Free0]
            ),
            '$add_free'(Vs, Bound, Free1, Free).

        % findall/4 is findall/3 with an explicit tail: the solutions are prefixed
        % onto Tail, so consecutive calls can grow one list without a final append.
        findall(Template, Goal, Bag, Tail) :-
            findall(Template, Goal, Solutions),
            append(Solutions, Tail, Bag).

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

        % aggregate/3 is aggregate_all/3 with bagof/3 underneath: the goal's free
        % variables group the solutions, one aggregate per group on backtracking,
        % and no solutions at all is failure rather than a zero.
        aggregate(count, Goal, Count) :- bagof(x, Goal, Xs), length(Xs, Count).
        aggregate(count(T), Goal, Count) :- bagof(T, Goal, Xs), length(Xs, Count).
        aggregate(bag(T), Goal, Bag) :- bagof(T, Goal, Bag).
        aggregate(set(T), Goal, Set) :- setof(T, Goal, Set).
        aggregate(sum(E), Goal, Sum) :- bagof(E, Goal, Es), sum_list(Es, Sum).
        aggregate(max(E), Goal, Max) :- bagof(E, Goal, Es), max_list(Es, Max).
        aggregate(min(E), Goal, Min) :- bagof(E, Goal, Es), min_list(Es, Min).

        % The /4 forms count each distinct discriminator-template pair once. The
        % aggregate/4 family groups by free variables through setof/3; the
        % aggregate_all/4 family deduplicates with sort/2 over all solutions.
        aggregate(count, D, Goal, Count) :- setof(D, Goal, Ds), length(Ds, Count).
        aggregate(count(T), D, Goal, Count) :- setof(D-T, Goal, Ps), length(Ps, Count).
        aggregate(bag(T), D, Goal, Bag) :- setof(D-T, Goal, Ps), pairs_values(Ps, Bag).
        aggregate(set(T), D, Goal, Set) :-
            setof(D-T, Goal, Ps),
            pairs_values(Ps, Vs),
            sort(Vs, Set).
        aggregate(sum(E), D, Goal, Sum) :-
            setof(D-E, Goal, Ps),
            pairs_values(Ps, Es),
            sum_list(Es, Sum).
        aggregate(max(E), D, Goal, Max) :-
            setof(D-E, Goal, Ps),
            pairs_values(Ps, Es),
            max_list(Es, Max).
        aggregate(min(E), D, Goal, Min) :-
            setof(D-E, Goal, Ps),
            pairs_values(Ps, Es),
            min_list(Es, Min).

        aggregate_all(count, D, Goal, Count) :-
            findall(D, Goal, Ds0),
            sort(Ds0, Ds),
            length(Ds, Count).
        aggregate_all(count(T), D, Goal, Count) :-
            findall(D-T, Goal, Ps0),
            sort(Ps0, Ps),
            length(Ps, Count).
        aggregate_all(bag(T), D, Goal, Bag) :-
            findall(D-T, Goal, Ps0),
            sort(Ps0, Ps),
            pairs_values(Ps, Bag).
        aggregate_all(set(T), D, Goal, Set) :-
            findall(D-T, Goal, Ps0),
            sort(Ps0, Ps),
            pairs_values(Ps, Vs),
            sort(Vs, Set).
        aggregate_all(sum(E), D, Goal, Sum) :-
            findall(D-E, Goal, Ps0),
            sort(Ps0, Ps),
            pairs_values(Ps, Es),
            sum_list(Es, Sum).
        aggregate_all(max(E), D, Goal, Max) :-
            findall(D-E, Goal, Ps0),
            Ps0 \== [],
            sort(Ps0, Ps),
            pairs_values(Ps, Es),
            max_list(Es, Max).
        aggregate_all(min(E), D, Goal, Min) :-
            findall(D-E, Goal, Ps0),
            Ps0 \== [],
            sort(Ps0, Ps),
            pairs_values(Ps, Es),
            min_list(Es, Min).

        % --- Type and mode validation (library(error)) -------------------------------
        % Raising an ISO error term as a call. The context argument of error/2 is left
        % unbound, matching the engine's convention for the errors it raises itself.

        instantiation_error(_) :- throw(error(instantiation_error, _)).
        uninstantiation_error(Culprit) :- throw(error(uninstantiation_error(Culprit), _)).
        type_error(Type, Culprit) :- throw(error(type_error(Type, Culprit), _)).
        domain_error(Domain, Culprit) :- throw(error(domain_error(Domain, Culprit), _)).
        existence_error(Type, Culprit) :- throw(error(existence_error(Type, Culprit), _)).
        permission_error(Action, Type, Culprit) :-
            throw(error(permission_error(Action, Type, Culprit), _)).
        representation_error(Flag) :- throw(error(representation_error(Flag), _)).
        resource_error(Resource) :- throw(error(resource_error(Resource), _)).
        syntax_error(Message) :- throw(error(syntax_error(Message), _)).

        % must_be/2 succeeds silently or raises the error SWI's library(error) would:
        % instantiation before type, uninstantiation for the var type, and an
        % existence error for a type name the table below does not know.
        must_be(Type, X) :-
            (   var(Type) -> instantiation_error(Type)
            ;   is_of_type(Type, X) -> true
            ;   '$is_not'(Type, X)
            ).

        '$is_not'(list, X) :- !, '$not_a_list'(list, X).
        '$is_not'(proper_list, X) :- !, '$not_a_list'(proper_list, X).
        '$is_not'(chars, X) :- !, '$not_a_list'(chars, X).
        '$is_not'(codes, X) :- !, '$not_a_list'(codes, X).
        '$is_not'(list_or_partial_list, X) :- !, type_error(list, X).
        '$is_not'(var, X) :- !, uninstantiation_error(X).
        '$is_not'(Type, X) :-
            (   \+ '$known_type'(Type) -> existence_error(type, Type)
            ;   var(X) -> instantiation_error(X)
            ;   Type == ground -> instantiation_error(X)
            ;   type_error(Type, X)
            ).

        % A list that ends in a variable is insufficiently instantiated rather than
        % of the wrong type, which decides between the two errors.
        '$not_a_list'(Type, X) :-
            (   '$ends_in_var'(X) -> instantiation_error(X)
            ;   type_error(Type, X)
            ).

        '$ends_in_var'(V) :- var(V), !.
        '$ends_in_var'([_|T]) :- '$ends_in_var'(T).

        is_of_type(Type, X) :- '$has_type'(Type, X).

        '$has_type'(any, _).
        '$has_type'(atom, X) :- atom(X).
        '$has_type'(atomic, X) :- atomic(X).
        '$has_type'(boolean, X) :- ( X == true -> true ; X == false ).
        '$has_type'(callable, X) :- callable(X).
        '$has_type'(char, X) :- atom(X), atom_length(X, 1).
        '$has_type'(chars, X) :- '$text_list'(X, char).
        '$has_type'(code, X) :- integer(X), X >= 0, X =< 65535.
        '$has_type'(codes, X) :- '$text_list'(X, code).
        '$has_type'(compound, X) :- compound(X).
        '$has_type'(constant, X) :- atomic(X).
        '$has_type'(float, X) :- float(X).
        '$has_type'(ground, X) :- ground(X).
        '$has_type'(integer, X) :- integer(X).
        '$has_type'(list, X) :- is_list(X).
        '$has_type'(proper_list, X) :- is_list(X).
        '$has_type'(list_or_partial_list, X) :- '$list_or_partial_list'(X).
        '$has_type'(negative_integer, X) :- integer(X), X < 0.
        '$has_type'(nonneg, X) :- integer(X), X >= 0.
        '$has_type'(number, X) :- number(X).
        '$has_type'(oneof(L), X) :- ground(X), memberchk(X, L).
        '$has_type'(pair, X) :- nonvar(X), X = _-_.
        '$has_type'(positive_integer, X) :- integer(X), X > 0.
        '$has_type'(symbol, X) :- atom(X).
        '$has_type'(text, X) :-
            (   atom(X) -> true
            ;   '$text_list'(X, char) -> true
            ;   '$text_list'(X, code)
            ).
        '$has_type'(var, X) :- var(X).
        '$has_type'(between(L, U), X) :-
            (   integer(L), integer(U) -> integer(X), X >= L, X =< U
            ;   number(X), X >= L, X =< U
            ).

        '$text_list'(X, _) :- var(X), !, fail.
        '$text_list'([], _).
        '$text_list'([H|T], Kind) :- '$text_element'(Kind, H), '$text_list'(T, Kind).

        '$text_element'(char, X) :- atom(X), atom_length(X, 1).
        '$text_element'(code, X) :- integer(X), X >= 0, X =< 65535.

        '$list_or_partial_list'(V) :- var(V), !.
        '$list_or_partial_list'([]) :- !.
        '$list_or_partial_list'([_|T]) :- '$list_or_partial_list'(T).

        '$known_type'(any).
        '$known_type'(atom).
        '$known_type'(atomic).
        '$known_type'(between(_, _)).
        '$known_type'(boolean).
        '$known_type'(callable).
        '$known_type'(char).
        '$known_type'(chars).
        '$known_type'(code).
        '$known_type'(codes).
        '$known_type'(compound).
        '$known_type'(constant).
        '$known_type'(float).
        '$known_type'(ground).
        '$known_type'(integer).
        '$known_type'(list).
        '$known_type'(list_or_partial_list).
        '$known_type'(negative_integer).
        '$known_type'(nonneg).
        '$known_type'(number).
        '$known_type'(oneof(_)).
        '$known_type'(pair).
        '$known_type'(positive_integer).
        '$known_type'(proper_list).
        '$known_type'(symbol).
        '$known_type'(text).
        '$known_type'(var).

        % --- Ordered sets (library(ordsets)) -----------------------------------------
        % An ordered set is a duplicate-free list sorted by the standard order, which
        % is exactly what sort/2 produces. Every operation below is one merge pass
        % driven by compare/3.

        list_to_ord_set(List, Set) :- sort(List, Set).

        ord_empty([]).

        ord_memberchk(X, [H|T]) :- compare(O, X, H), '$ord_memberchk_step'(O, X, T).

        '$ord_memberchk_step'('=', _, _).
        '$ord_memberchk_step'('>', X, T) :- ord_memberchk(X, T).

        ord_subset([], _).
        ord_subset([H|T], [H2|T2]) :- compare(O, H, H2), '$ord_subset_step'(O, H, T, T2).

        '$ord_subset_step'('=', _, T, T2) :- ord_subset(T, T2).
        '$ord_subset_step'('>', H, T, T2) :- ord_subset([H|T], T2).

        ord_disjoint([], _).
        ord_disjoint([_|_], []).
        ord_disjoint([H|T], [H2|T2]) :- compare(O, H, H2), '$ord_disjoint_step'(O, H, T, H2, T2).

        '$ord_disjoint_step'('<', _, T, H2, T2) :- ord_disjoint(T, [H2|T2]).
        '$ord_disjoint_step'('>', H, T, _, T2) :- ord_disjoint([H|T], T2).

        ord_union([], Set, Set).
        ord_union([H|T], Set2, Union) :- '$ord_union'(Set2, H, T, Union).

        '$ord_union'([], H, T, [H|T]).
        '$ord_union'([H2|T2], H, T, Union) :-
            compare(O, H, H2),
            '$ord_union_step'(O, H, T, H2, T2, Union).

        '$ord_union_step'('<', H, T, H2, T2, [H|Union]) :- ord_union(T, [H2|T2], Union).
        '$ord_union_step'('=', H, T, _, T2, [H|Union]) :- ord_union(T, T2, Union).
        '$ord_union_step'('>', H, T, H2, T2, [H2|Union]) :- '$ord_union'(T2, H, T, Union).

        ord_intersection([], _, []).
        ord_intersection([H|T], Set2, Intersection) :-
            '$ord_intersection'(Set2, H, T, Intersection).

        '$ord_intersection'([], _, _, []).
        '$ord_intersection'([H2|T2], H, T, Intersection) :-
            compare(O, H, H2),
            '$ord_intersection_step'(O, H, T, H2, T2, Intersection).

        '$ord_intersection_step'('<', _, T, H2, T2, Intersection) :-
            ord_intersection(T, [H2|T2], Intersection).
        '$ord_intersection_step'('=', H, T, _, T2, [H|Intersection]) :-
            ord_intersection(T, T2, Intersection).
        '$ord_intersection_step'('>', H, T, _, T2, Intersection) :-
            '$ord_intersection'(T2, H, T, Intersection).

        ord_subtract([], _, []).
        ord_subtract([H|T], Set2, Difference) :- '$ord_subtract'(Set2, H, T, Difference).

        '$ord_subtract'([], H, T, [H|T]).
        '$ord_subtract'([H2|T2], H, T, Difference) :-
            compare(O, H, H2),
            '$ord_subtract_step'(O, H, T, H2, T2, Difference).

        '$ord_subtract_step'('<', H, T, H2, T2, [H|Difference]) :-
            ord_subtract(T, [H2|T2], Difference).
        '$ord_subtract_step'('=', _, T, _, T2, Difference) :- ord_subtract(T, T2, Difference).
        '$ord_subtract_step'('>', H, T, _, T2, Difference) :- '$ord_subtract'(T2, H, T, Difference).

        ord_add_element([], Element, [Element]).
        ord_add_element([H|T], Element, Set) :-
            compare(O, Element, H),
            (   O == '<' -> Set = [Element, H|T]
            ;   O == '=' -> Set = [H|T]
            ;   Set = [H|Rest], ord_add_element(T, Element, Rest)
            ).

        ord_del_element([], _, []).
        ord_del_element([H|T], Element, Set) :-
            compare(O, Element, H),
            (   O == '<' -> Set = [H|T]
            ;   O == '=' -> Set = T
            ;   Set = [H|Rest], ord_del_element(T, Element, Rest)
            ).

        % The n-ary forms fold the binary operation over a list of sets. The union
        % of no sets is empty; the intersection of no sets is undefined and fails.
        ord_union([], []).
        ord_union([Set|Sets], Union) :- '$ord_union_all'(Sets, Set, Union).

        '$ord_union_all'([], Union, Union).
        '$ord_union_all'([Set|Sets], Acc, Union) :-
            ord_union(Acc, Set, Next),
            '$ord_union_all'(Sets, Next, Union).

        ord_intersection([Set|Sets], Intersection) :-
            '$ord_intersection_all'(Sets, Set, Intersection).

        '$ord_intersection_all'([], Intersection, Intersection).
        '$ord_intersection_all'([Set|Sets], Acc, Intersection) :-
            ord_intersection(Acc, Set, Next),
            '$ord_intersection_all'(Sets, Next, Intersection).

        % --- Association lists (library(assoc)) --------------------------------------
        % An assoc is an AVL tree: the atom t is the empty tree, and a node is
        % t(Key, Value, Height, Left, Right). Heights are stored rather than balance
        % atoms so rebalancing is plain arithmetic over the two child heights.

        empty_assoc(t).

        get_assoc(Key, t(K, V, _, L, R), Value) :-
            compare(O, Key, K),
            '$get_assoc_step'(O, Key, V, L, R, Value).

        '$get_assoc_step'('=', _, V, _, _, V).
        '$get_assoc_step'('<', Key, _, L, _, Value) :- get_assoc(Key, L, Value).
        '$get_assoc_step'('>', Key, _, _, R, Value) :- get_assoc(Key, R, Value).

        put_assoc(Key, t, Value, t(Key, Value, 1, t, t)).
        put_assoc(Key, t(K, V, _, L, R), Value, Tree) :-
            compare(O, Key, K),
            '$put_assoc_step'(O, Key, Value, K, V, L, R, Tree).

        '$put_assoc_step'('=', Key, Value, _, _, L, R, Tree) :-
            '$assoc_node'(Key, Value, L, R, Tree).
        '$put_assoc_step'('<', Key, Value, K, V, L, R, Tree) :-
            put_assoc(Key, L, Value, NewLeft),
            '$assoc_rebalance'(K, V, NewLeft, R, Tree).
        '$put_assoc_step'('>', Key, Value, K, V, L, R, Tree) :-
            put_assoc(Key, R, Value, NewRight),
            '$assoc_rebalance'(K, V, L, NewRight, Tree).

        '$assoc_height'(t, 0).
        '$assoc_height'(t(_, _, H, _, _), H).

        '$assoc_node'(K, V, L, R, t(K, V, H, L, R)) :-
            '$assoc_height'(L, HL),
            '$assoc_height'(R, HR),
            ( HL >= HR -> H is HL + 1 ; H is HR + 1 ).

        '$assoc_rebalance'(K, V, L, R, Tree) :-
            '$assoc_height'(L, HL),
            '$assoc_height'(R, HR),
            Difference is HL - HR,
            (   Difference =:= 2 -> '$assoc_rotate_right'(K, V, L, R, Tree)
            ;   Difference =:= -2 -> '$assoc_rotate_left'(K, V, L, R, Tree)
            ;   '$assoc_node'(K, V, L, R, Tree)
            ).

        % Left-heavy: one right rotation, or a left-right double rotation when the
        % left child leans right. The mirror clause handles the right-heavy case.
        '$assoc_rotate_right'(K, V, t(KL, VL, _, LL, LR), R, Tree) :-
            '$assoc_height'(LL, HLL),
            '$assoc_height'(LR, HLR),
            (   HLL >= HLR
            ->  '$assoc_node'(K, V, LR, R, NewRight),
                '$assoc_node'(KL, VL, LL, NewRight, Tree)
            ;   LR = t(KM, VM, _, ML, MR),
                '$assoc_node'(KL, VL, LL, ML, NewLeft),
                '$assoc_node'(K, V, MR, R, NewRight),
                '$assoc_node'(KM, VM, NewLeft, NewRight, Tree)
            ).

        '$assoc_rotate_left'(K, V, L, t(KR, VR, _, RL, RR), Tree) :-
            '$assoc_height'(RL, HRL),
            '$assoc_height'(RR, HRR),
            (   HRR >= HRL
            ->  '$assoc_node'(K, V, L, RL, NewLeft),
                '$assoc_node'(KR, VR, NewLeft, RR, Tree)
            ;   RL = t(KM, VM, _, ML, MR),
                '$assoc_node'(K, V, L, ML, NewLeft),
                '$assoc_node'(KR, VR, MR, RR, NewRight),
                '$assoc_node'(KM, VM, NewLeft, NewRight, Tree)
            ).

        list_to_assoc(List, Assoc) :-
            must_be(list, List),
            '$assoc_pairs_check'(List),
            msort(List, Sorted),
            '$assoc_unique_keys'(Sorted, List),
            length(Sorted, N),
            '$assoc_build'(N, Sorted, [], Assoc).

        ord_list_to_assoc(Sorted, Assoc) :-
            must_be(list, Sorted),
            '$assoc_pairs_check'(Sorted),
            '$assoc_ordered_keys'(Sorted, Sorted),
            length(Sorted, N),
            '$assoc_build'(N, Sorted, [], Assoc).

        '$assoc_pairs_check'([]).
        '$assoc_pairs_check'([Pair|T]) :-
            ( nonvar(Pair), Pair = _-_ -> true ; type_error(pair, Pair) ),
            '$assoc_pairs_check'(T).

        '$assoc_unique_keys'([], _).
        '$assoc_unique_keys'([_], _).
        '$assoc_unique_keys'([K1-_, K2-V2|T], List) :-
            ( K1 == K2 -> domain_error(unique_key_pairs, List) ; true ),
            '$assoc_unique_keys'([K2-V2|T], List).

        '$assoc_ordered_keys'([], _).
        '$assoc_ordered_keys'([_], _).
        '$assoc_ordered_keys'([K1-_, K2-V2|T], List) :-
            ( K1 @< K2 -> true ; domain_error(key_ordered_pairs, List) ),
            '$assoc_ordered_keys'([K2-V2|T], List).

        % Builds the AVL for N sorted pairs directly: the middle pair becomes the
        % root, so sibling heights differ by at most one without any rotation.
        '$assoc_build'(0, Rest, Rest, t) :- !.
        '$assoc_build'(N, List, Rest, Tree) :-
            LeftCount is (N - 1) // 2,
            RightCount is N - 1 - LeftCount,
            '$assoc_build'(LeftCount, List, [K-V|Middle], Left),
            '$assoc_build'(RightCount, Middle, Rest, Right),
            '$assoc_node'(K, V, Left, Right, Tree).

        assoc_to_list(Assoc, List) :- '$assoc_to_list'(Assoc, [], List).

        '$assoc_to_list'(t, List, List).
        '$assoc_to_list'(t(K, V, _, L, R), List0, List) :-
            '$assoc_to_list'(R, List0, List1),
            '$assoc_to_list'(L, [K-V|List1], List).

        assoc_to_keys(Assoc, Keys) :- assoc_to_list(Assoc, Pairs), pairs_keys(Pairs, Keys).

        assoc_to_values(Assoc, Values) :- assoc_to_list(Assoc, Pairs), pairs_values(Pairs, Values).

        min_assoc(t(K, V, _, L, _), Key, Value) :- '$min_assoc'(L, K, V, Key, Value).

        '$min_assoc'(t, Key, Value, Key, Value).
        '$min_assoc'(t(K, V, _, L, _), _, _, Key, Value) :- '$min_assoc'(L, K, V, Key, Value).

        max_assoc(t(K, V, _, _, R), Key, Value) :- '$max_assoc'(R, K, V, Key, Value).

        '$max_assoc'(t, Key, Value, Key, Value).
        '$max_assoc'(t(K, V, _, _, R), _, _, Key, Value) :- '$max_assoc'(R, K, V, Key, Value).

        % del_assoc/4 removes a key, answering its value; a missing key fails.
        % Deleting joins the two subtrees around the removed node and rebalances on
        % the way back up, so the tree stays an AVL.
        del_assoc(Key, t(K, V, _, L, R), Value, Assoc) :-
            compare(O, Key, K),
            '$del_assoc_step'(O, Key, K, V, L, R, Value, Assoc).

        '$del_assoc_step'('=', _, _, V, L, R, V, Assoc) :- '$assoc_join'(L, R, Assoc).
        '$del_assoc_step'('<', Key, K, V, L, R, Value, Assoc) :-
            del_assoc(Key, L, Value, NewLeft),
            '$assoc_rebalance'(K, V, NewLeft, R, Assoc).
        '$del_assoc_step'('>', Key, K, V, L, R, Value, Assoc) :-
            del_assoc(Key, R, Value, NewRight),
            '$assoc_rebalance'(K, V, L, NewRight, Assoc).

        '$assoc_join'(t, R, R) :- !.
        '$assoc_join'(L, t, L) :- !.
        '$assoc_join'(L, R, Assoc) :-
            '$assoc_del_min'(R, K, V, NewRight),
            '$assoc_rebalance'(K, V, L, NewRight, Assoc).

        '$assoc_del_min'(t(K, V, _, t, R), K, V, R) :- !.
        '$assoc_del_min'(t(K0, V0, _, L, R), K, V, Assoc) :-
            '$assoc_del_min'(L, K, V, NewLeft),
            '$assoc_rebalance'(K0, V0, NewLeft, R, Assoc).
        """;
}
