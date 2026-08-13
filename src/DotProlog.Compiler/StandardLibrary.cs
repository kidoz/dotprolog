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

        % --- Cleanup goals -----------------------------------------------------------
        % setup_call_cleanup/3 runs Cleanup exactly once when Goal's outcome is decided:
        % a deterministic success (including the redo that exhausts the alternatives),
        % failure, or a thrown ball. A surrounding cut that discards Goal's pending
        % alternatives does not reach the deferred cleanup — write once(Goal) when
        % commit semantics are wanted. The SWI rules this follows: once(Setup) runs
        % first; a ball from Goal outranks anything Cleanup throws; Cleanup's failure
        % is ignored while its ball propagates when nothing else is pending.

        setup_call_cleanup(Setup, Goal, Cleanup) :-
            once(Setup),
            catch('$call_cleanup'(Goal, Cleanup), Ball, '$cleanup_recover'(Cleanup, Ball)).

        call_cleanup(Goal, Cleanup) :-
            setup_call_cleanup(true, Goal, Cleanup).

        % Equal depths around the meta-call mean a deterministic exit: the cut then
        % discards the failure clause so cleanup cannot run a second time.
        '$call_cleanup'(Goal, Cleanup) :-
            '$choice_points'(B0),
            call(Goal),
            '$choice_points'(B1),
            (   B1 =:= B0
            ->  !,
                '$cleanup_once'(Cleanup)
            ;   true
            ).
        '$call_cleanup'(_, Cleanup) :-
            '$cleanup_once'(Cleanup),
            fail.

        % A ball the cleanup threw after already running is marked, so the recovery
        % can rethrow it without running the cleanup again.
        '$cleanup_once'(Cleanup) :-
            catch(ignore(Cleanup), Ball, throw('$cleanup_ball'(Ball))).

        '$cleanup_recover'(_, '$cleanup_ball'(Ball)) :- !, throw(Ball).
        '$cleanup_recover'(Cleanup, Ball) :-
            catch(ignore(Cleanup), _, true),
            throw(Ball).

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
        '$sink'(string(S), Text) :- !, atom_string(Text, S).
        '$sink'(Sink, _) :- throw(error(domain_error(output_sink, Sink), with_output_to/2)).

        % --- Formatted output -------------------------------------------------------
        % format/1,2,3 wrap the native '$format' engine so that ~@ can run its goal,
        % which a native builtin cannot: each goal runs here, once, with its output
        % captured, and the directive is rewritten to ~a over the captured atom. The
        % scan mirrors the engine's argument consumption so later directives keep
        % their arguments; text without a ~@ passes through unchanged.

        format(Text) :- format(Text, []).

        format(Text, Arguments) :-
            '$format_expand'(Text, Arguments, Text2, Arguments2, Status),
            '$format_emit'(Status, Text2, Arguments2).

        '$format_emit'(ok, Text, Args) :- '$format'(Text, Args).
        '$format_emit'(stopped(fail), Text, Args) :- '$format_prefix_emit'(Text, Args), fail.
        '$format_emit'(stopped(throw(Ball)), Text, Args) :- '$format_prefix_emit'(Text, Args), throw(Ball).

        % An empty prefix writes nothing: the empty code list would otherwise be
        % read back as the atom [].
        '$format_prefix_emit'(Text, Args) :- ( Text == [] -> true ; '$format'(Text, Args) ).

        format(Sink, Text, Arguments) :-
            '$format_expand'(Text, Arguments, Text2, Arguments2, Status),
            (   Status == ok
            ->  '$format'(Sink, Text2, Arguments2)
            ;   '$format_capture_sink'(Sink)
            ->  '$format_stop'(Status)
            ;   ( Text2 == [] -> true ; '$format'(Sink, Text2, Arguments2) ),
                '$format_stop'(Status)
            ).

        '$format_capture_sink'(Sink) :-
            nonvar(Sink),
            ( Sink = atom(_) ; Sink = codes(_) ; Sink = chars(_) ; Sink = string(_) ).

        '$format_stop'(stopped(fail)) :- fail.
        '$format_stop'(stopped(throw(Ball))) :- throw(Ball).

        '$format_expand'(Text, Arguments, Text2, Arguments2, Status) :-
            (   '$format_codes'(Text, Codes),
                memberchk(0'@, Codes)
            ->  ( is_list(Arguments) -> Args = Arguments ; Args = [Arguments] ),
                '$format_scan'(Codes, Args, [], [], Text2, Arguments2, Status)
            ;   Text2 = Text,
                Arguments2 = Arguments,
                Status = ok
            ).

        '$format_codes'(Text, Codes) :-
            (   atom(Text) -> atom_codes(Text, Codes)
            ;   string(Text) -> string_codes(Text, Codes)
            ;   is_list(Text) -> '$format_list_codes'(Text, Codes)
            ;   fail
            ).

        '$format_list_codes'([], []).
        '$format_list_codes'([H|T], [C|Cs]) :-
            ( integer(H) -> C = H ; atom(H), char_code(H, C) ),
            '$format_list_codes'(T, Cs).

        % The rewritten text and consumed arguments accumulate in reverse, so a
        % ~@ goal that fails or throws can still hand back the prefix scanned
        % before it: SWI streams directives, and the text before the goal has
        % already been emitted when the goal stops the format.
        '$format_scan'([], Args, RevOut, RevArgs, Out, OutArgs, ok) :-
            reverse(RevOut, Out),
            '$format_close_args'(RevArgs, Args, OutArgs).
        '$format_scan'([0'~|T], Args, RevOut, RevArgs, Out, OutArgs, Status) :- !,
            '$format_after'(T, Args, [0'~|RevOut], RevArgs, Out, OutArgs, Status).
        '$format_scan'([C|T], Args, RevOut, RevArgs, Out, OutArgs, Status) :-
            '$format_scan'(T, Args, [C|RevOut], RevArgs, Out, OutArgs, Status).

        '$format_close_args'([], Tail, Tail).
        '$format_close_args'([A|R], Tail, Args) :- '$format_close_args'(R, [A|Tail], Args).

        % The optional prefix before the directive character: a backquoted fill
        % character (code 96 spelled numerically — a backquote literal would read
        % as a backquoted atom), a * count taken from the arguments, or digits.
        '$format_after'(Codes, Args, RevOut, RevArgs, Out, OutArgs, Status) :-
            (   Codes = [96, F|T]
            ->  '$format_apply'(T, Args, [F, 96|RevOut], RevArgs, Out, OutArgs, Status)
            ;   Codes = [0'*|T]
            ->  (   Args = [N|Args1]
                ->  '$format_apply'(T, Args1, [0'*|RevOut], [N|RevArgs], Out, OutArgs, Status)
                ;   '$format_apply'(T, Args, [0'*|RevOut], RevArgs, Out, OutArgs, Status)
                )
            ;   '$format_take_digits'(Codes, T, RevOut, RevOut1)
            ->  '$format_apply'(T, Args, RevOut1, RevArgs, Out, OutArgs, Status)
            ;   '$format_apply'(Codes, Args, RevOut, RevArgs, Out, OutArgs, Status)
            ).

        '$format_take_digits'([C|T], Rest, RevOut, RevOutFinal) :-
            C >= 0'0, C =< 0'9,
            (   '$format_take_digits'(T, Rest, [C|RevOut], RevOutFinal)
            ->  true
            ;   Rest = T, RevOutFinal = [C|RevOut]
            ).

        '$format_apply'([], Args, RevOut, RevArgs, Out, OutArgs, ok) :-
            reverse(RevOut, Out),
            '$format_close_args'(RevArgs, Args, OutArgs).
        '$format_apply'([0'@|T], [Goal|Args], RevOut, RevArgs, Out, OutArgs, Status) :- !,
            (   catch(with_output_to(atom(Captured), once(Goal)), Ball, true)
            ->  (   var(Ball)
                ->  '$format_scan'(T, Args, [0'a|RevOut], [Captured|RevArgs], Out, OutArgs, Status)
                ;   '$format_stopped'(RevOut, RevArgs, Out, OutArgs), Status = stopped(throw(Ball))
                )
            ;   '$format_stopped'(RevOut, RevArgs, Out, OutArgs), Status = stopped(fail)
            ).
        '$format_apply'([0'W|T], Args, RevOut, RevArgs, Out, OutArgs, Status) :- !,
            (   Args = [A, B|Args1]
            ->  '$format_scan'(T, Args1, [0'W|RevOut], [B, A|RevArgs], Out, OutArgs, Status)
            ;   '$format_scan'(T, Args, [0'W|RevOut], RevArgs, Out, OutArgs, Status)
            ).
        '$format_apply'([D|T], Args, RevOut, RevArgs, Out, OutArgs, Status) :-
            (   '$format_one_arg'(D),
                Args = [A|Args1]
            ->  '$format_scan'(T, Args1, [D|RevOut], [A|RevArgs], Out, OutArgs, Status)
            ;   '$format_scan'(T, Args, [D|RevOut], RevArgs, Out, OutArgs, Status)
            ).

        % Everything scanned before the directive's own '~' becomes the prefix.
        '$format_stopped'(RevOut, RevArgs, Out, OutArgs) :-
            '$format_strip_directive'(RevOut, RevPrefix),
            reverse(RevPrefix, Out),
            '$format_close_args'(RevArgs, [], OutArgs).

        '$format_strip_directive'([0'~|Rest], Rest) :- !.
        '$format_strip_directive'([_|T], Rest) :- '$format_strip_directive'(T, Rest).

        '$format_one_arg'(0'w).
        '$format_one_arg'(0'p).
        '$format_one_arg'(0'q).
        '$format_one_arg'(0'a).
        '$format_one_arg'(0'd).
        '$format_one_arg'(0'D).
        '$format_one_arg'(0'e).
        '$format_one_arg'(0'f).
        '$format_one_arg'(0'g).
        '$format_one_arg'(0'r).
        '$format_one_arg'(0'R).
        '$format_one_arg'(0's).
        '$format_one_arg'(0'c).
        '$format_one_arg'(0'i).

        % --- Clause pretty-printing (portray_clause/1,2) ------------------------------
        % SWI's listing layout: the head, ' :-', one goal per line four spaces in,
        % and control blocks bracketed with (   / ;   / ->  / ) at the enclosing
        % indent. Named variables print as A, B, ...; singletons as underscore.

        portray_clause(Clause) :-
            '$portray_names'(Clause, Names),
            '$portray_clause'('$portray_current', Clause, Names).

        portray_clause(Stream, Clause) :-
            '$portray_names'(Clause, Names),
            '$portray_clause'(Stream, Clause, Names).

        % The layout writes through these so portray_clause/1 reaches the current
        % output — which may be a with_output_to capture with no addressable
        % handle — while portray_clause/2 targets its explicit stream.
        '$portray_write'('$portray_current', Text) :- !, write(Text).
        '$portray_write'(S, Text) :- write(S, Text).

        '$portray_nl'('$portray_current') :- !, nl.
        '$portray_nl'(S) :- nl(S).

        '$portray_tab'('$portray_current', N) :- !, tab(N).
        '$portray_tab'(S, N) :- tab(S, N).

        '$portray_clause'(S, Clause, Names) :-
            (   nonvar(Clause), Clause = (Head :- Body)
            ->  (   Body == true
                ->  '$portray_goal'(S, Head, Names)
                ;   '$portray_goal'(S, Head, Names),
                    '$portray_write'(S, ' :-'), '$portray_nl'(S), '$portray_tab'(S, 4),
                    '$portray_body'(S, Body, 4, Names)
                )
            ;   '$portray_goal'(S, Clause, Names)
            ),
            '$portray_write'(S, '.'), '$portray_nl'(S).

        % The cursor is already at the goal column when a body is written.
        '$portray_body'(S, Goal, Col, Names) :-
            (   var(Goal)
            ->  '$portray_goal'(S, Goal, Names)
            ;   Goal = (A, B)
            ->  '$portray_body'(S, A, Col, Names),
                '$portray_write'(S, ','), '$portray_nl'(S), '$portray_tab'(S, Col),
                '$portray_body'(S, B, Col, Names)
            ;   Goal = (_ ; _)
            ->  '$portray_block'(S, Goal, Col, Names)
            ;   Goal = (_ -> _)
            ->  '$portray_block'(S, Goal, Col, Names)
            ;   Goal = (_ *-> _)
            ->  '$portray_block'(S, Goal, Col, Names)
            ;   Goal = (\+ A)
            ->  '$portray_write'(S, '\\+ '),
                '$portray_body'(S, A, Col, Names)
            ;   '$portray_goal'(S, Goal, Names)
            ).

        '$portray_block'(S, Goal, Col, Names) :-
            Inner is Col + 4,
            '$portray_write'(S, '(   '),
            '$portray_branches'(S, Goal, Col, Inner, Names),
            '$portray_nl'(S), '$portray_tab'(S, Col), '$portray_write'(S, ')').

        % A right-nested ;-chain becomes sibling branches at one indent, so an
        % else-if ladder stays flat the way SWI lists it.
        '$portray_branches'(S, Goal, Col, Inner, Names) :-
            (   nonvar(Goal), Goal = (A ; B)
            ->  '$portray_branch'(S, A, Col, Inner, Names),
                '$portray_nl'(S), '$portray_tab'(S, Col), '$portray_write'(S, ';   '),
                '$portray_branches'(S, B, Col, Inner, Names)
            ;   '$portray_branch'(S, Goal, Col, Inner, Names)
            ).

        '$portray_branch'(S, Goal, Col, Inner, Names) :-
            (   nonvar(Goal), Goal = (C -> T)
            ->  '$portray_body'(S, C, Inner, Names),
                '$portray_nl'(S), '$portray_tab'(S, Col), '$portray_write'(S, '->  '),
                '$portray_body'(S, T, Inner, Names)
            ;   nonvar(Goal), Goal = (C *-> T)
            ->  '$portray_body'(S, C, Inner, Names),
                '$portray_nl'(S), '$portray_tab'(S, Col), '$portray_write'(S, '*-> '),
                '$portray_body'(S, T, Inner, Names)
            ;   '$portray_body'(S, Goal, Inner, Names)
            ).

        '$portray_goal'('$portray_current', Goal, Names) :- !,
            write_term(Goal, [quoted(true), numbervars(true), spacing(next_argument), variable_names(Names)]).
        '$portray_goal'(S, Goal, Names) :-
            write_term(S, Goal, [quoted(true), numbervars(true), spacing(next_argument), variable_names(Names)]).

        % Variable names by first appearance: A, B, ... with a numeric suffix past
        % Z; a variable occurring once prints as plain underscore.
        '$portray_names'(Term, Names) :-
            '$portray_occurrences'(Term, [], Occurrences),
            term_variables(Term, Variables),
            '$portray_name_each'(Variables, 0, Occurrences, Names).

        '$portray_occurrences'(Term, Acc0, Acc) :-
            (   var(Term) -> Acc = [Term|Acc0]
            ;   compound(Term)
            ->  Term =.. [_|Arguments],
                '$portray_occurrences_list'(Arguments, Acc0, Acc)
            ;   Acc = Acc0
            ).

        '$portray_occurrences_list'([], Acc, Acc).
        '$portray_occurrences_list'([A|As], Acc0, Acc) :-
            '$portray_occurrences'(A, Acc0, Acc1),
            '$portray_occurrences_list'(As, Acc1, Acc).

        '$portray_name_each'([], _, _, []).
        '$portray_name_each'([V|Vs], N, Occurrences, [Name = V|Names]) :-
            '$portray_count'(Occurrences, V, Count),
            (   Count =:= 1
            ->  Name = '_', N1 = N
            ;   '$portray_letter'(N, Name), N1 is N + 1
            ),
            '$portray_name_each'(Vs, N1, Occurrences, Names).

        '$portray_count'([], _, 0).
        '$portray_count'([O|Os], V, Count) :-
            '$portray_count'(Os, V, Count0),
            ( O == V -> Count is Count0 + 1 ; Count = Count0 ).

        '$portray_letter'(N, Name) :-
            Letter is 0'A + N mod 26,
            Index is N // 26,
            char_code(L, Letter),
            (   Index =:= 0
            ->  Name = L
            ;   number_codes(Index, Digits),
                atom_codes(Suffix, Digits),
                atom_concat(L, Suffix, Name)
            ).

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

        % --- Strings ------------------------------------------------------
        % The nondeterministic string predicates enumerate through between/3 over the
        % native slicing primitives; bound arguments filter by converted content, so
        % an atom or number is accepted wherever SWI accepts one.

        string_concat(Left, Right, Whole) :-
            (   '$string_text'(Left), '$string_text'(Right)
            ->  '$string_concat'(Left, Right, Whole)
            ;   '$string_text'(Whole)
            ->  string_length(Whole, Total),
                between(0, Total, Cut),
                Length is Total - Cut,
                '$string_slice'(Whole, 0, Cut, LeftSlice),
                '$string_slice'(Whole, Cut, Length, RightSlice),
                '$string_part'(Left, LeftSlice),
                '$string_part'(Right, RightSlice)
            ;   instantiation_error(Whole)
            ).

        sub_string(String, Before, Length, After, Sub) :-
            string_length(String, Total),
            between(0, Total, Before),
            Rest is Total - Before,
            between(0, Rest, Length),
            After is Total - Before - Length,
            '$string_slice'(String, Before, Length, Slice),
            '$string_part'(Sub, Slice).

        string_code(Index, String, Code) :-
            string_length(String, Length),
            between(1, Length, Index),
            '$string_code'(Index, String, Code).

        term_string(Term, String) :-
            (   '$string_text'(String)
            ->  string_to_atom(String, Atom),
                term_to_atom(Term, Atom)
            ;   term_to_atom(Term, Atom),
                atom_string(Atom, String)
            ).

        '$string_text'(Text) :- nonvar(Text), ( string(Text) ; atom(Text) ; number(Text) ), !.

        '$string_part'(Given, Slice) :-
            (   var(Given)
            ->  Given = Slice
            ;   '$as_string'(Given, Slice)
            ).

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

        aggregate_all(Template, _, _) :-
            var(Template),
            instantiation_error(Template).
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

        % The witnessed specs answer max(Value, Witness) / min(Value, Witness),
        % comparing values arithmetically; a tie keeps the first solution.
        aggregate_all(max(E, W), Goal, max(Max, Witness)) :-
            findall(E-W, Goal, Pairs),
            Pairs \== [],
            '$aggregate_max_pair'(Pairs, Max, Witness).

        aggregate_all(min(E, W), Goal, min(Min, Witness)) :-
            findall(E-W, Goal, Pairs),
            Pairs \== [],
            '$aggregate_min_pair'(Pairs, Min, Witness).

        % A compound template such as r(sum(X), count) aggregates each argument,
        % which must itself be a spec; the result keeps the template's shape. An
        % invalid template raises the error SWI raises, even before the goal runs.
        aggregate_all(Template, Goal, Result) :-
            '$aggregate_template_args'(Template, Name, Specs),
            '$aggregate_tuple'(Specs, Tuple),
            findall(Tuple, Goal, Tuples),
            '$aggregate_columns'(Specs, Tuples, Results),
            Result =.. [Name|Results].

        % aggregate/3 is aggregate_all/3 with bagof/3 underneath: the goal's free
        % variables group the solutions, one aggregate per group on backtracking,
        % and no solutions at all is failure rather than a zero.
        aggregate(Template, _, _) :-
            var(Template),
            instantiation_error(Template).
        aggregate(count, Goal, Count) :- bagof(x, Goal, Xs), length(Xs, Count).
        aggregate(count(T), Goal, Count) :- bagof(T, Goal, Xs), length(Xs, Count).
        aggregate(bag(T), Goal, Bag) :- bagof(T, Goal, Bag).
        aggregate(set(T), Goal, Set) :- setof(T, Goal, Set).
        aggregate(sum(E), Goal, Sum) :- bagof(E, Goal, Es), sum_list(Es, Sum).
        aggregate(max(E), Goal, Max) :- bagof(E, Goal, Es), max_list(Es, Max).
        aggregate(min(E), Goal, Min) :- bagof(E, Goal, Es), min_list(Es, Min).
        aggregate(max(E, W), Goal, max(Max, Witness)) :-
            bagof(E-W, Goal, Pairs),
            '$aggregate_max_pair'(Pairs, Max, Witness).
        aggregate(min(E, W), Goal, min(Min, Witness)) :-
            bagof(E-W, Goal, Pairs),
            '$aggregate_min_pair'(Pairs, Min, Witness).
        aggregate(Template, Goal, Result) :-
            '$aggregate_template_args'(Template, Name, Specs),
            '$aggregate_tuple'(Specs, Tuple),
            bagof(Tuple, Goal, Tuples),
            '$aggregate_columns'(Specs, Tuples, Results),
            Result =.. [Name|Results].

        % The /4 forms count each distinct discriminator-template pair once. The
        % aggregate/4 family groups by free variables through setof/3; the
        % aggregate_all/4 family deduplicates with sort/2 over all solutions.
        aggregate(Template, _, _, _) :-
            var(Template),
            instantiation_error(Template).
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
        aggregate(max(E, W), D, Goal, max(Max, Witness)) :-
            setof(D-(E-W), Goal, Ps),
            pairs_values(Ps, Pairs),
            '$aggregate_max_pair'(Pairs, Max, Witness).
        aggregate(min(E, W), D, Goal, min(Min, Witness)) :-
            setof(D-(E-W), Goal, Ps),
            pairs_values(Ps, Pairs),
            '$aggregate_min_pair'(Pairs, Min, Witness).
        aggregate(Template, D, Goal, Result) :-
            '$aggregate_template_args'(Template, Name, Specs),
            '$aggregate_tuple'(Specs, Tuple),
            setof(D-Tuple, Goal, Ps),
            pairs_values(Ps, Tuples),
            '$aggregate_columns'(Specs, Tuples, Results),
            Result =.. [Name|Results].

        aggregate_all(Template, _, _, _) :-
            var(Template),
            instantiation_error(Template).
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
        aggregate_all(max(E, W), D, Goal, max(Max, Witness)) :-
            findall(D-(E-W), Goal, Ps0),
            Ps0 \== [],
            sort(Ps0, Ps),
            pairs_values(Ps, Pairs),
            '$aggregate_max_pair'(Pairs, Max, Witness).
        aggregate_all(min(E, W), D, Goal, min(Min, Witness)) :-
            findall(D-(E-W), Goal, Ps0),
            Ps0 \== [],
            sort(Ps0, Ps),
            pairs_values(Ps, Pairs),
            '$aggregate_min_pair'(Pairs, Min, Witness).
        aggregate_all(Template, D, Goal, Result) :-
            '$aggregate_template_args'(Template, Name, Specs),
            '$aggregate_tuple'(Specs, Tuple),
            findall(D-Tuple, Goal, Ps0),
            sort(Ps0, Ps),
            pairs_values(Ps, Tuples),
            '$aggregate_columns'(Specs, Tuples, Results),
            Result =.. [Name|Results].

        % Decomposes a compound template into its spec arguments, failing for the
        % simple and witnessed specs (their own clauses handle those) and raising
        % instantiation, domain, or type errors for anything else, as SWI does.
        '$aggregate_template_args'(Template, Name, Specs) :-
            (   var(Template) -> instantiation_error(Template)
            ;   '$aggregate_spec'(Template) -> fail
            ;   compound(Template)
            ->  Template =.. [Name|Specs],
                '$aggregate_specs_check'(Specs)
            ;   atom(Template) -> domain_error(aggregate_template, Template)
            ;   type_error(aggregate_template, Template)
            ).

        '$aggregate_spec'(count).
        '$aggregate_spec'(count(_)).
        '$aggregate_spec'(sum(_)).
        '$aggregate_spec'(max(_)).
        '$aggregate_spec'(min(_)).
        '$aggregate_spec'(bag(_)).
        '$aggregate_spec'(set(_)).
        '$aggregate_spec'(max(_, _)).
        '$aggregate_spec'(min(_, _)).

        '$aggregate_specs_check'([]).
        '$aggregate_specs_check'([Spec|Specs]) :-
            (   var(Spec) -> instantiation_error(Spec)
            ;   '$aggregate_spec'(Spec) -> true
            ;   callable(Spec) -> domain_error(aggregate_template, Spec)
            ;   type_error(aggregate_template, Spec)
            ),
            '$aggregate_specs_check'(Specs).

        % What one solution records for each spec: the counted marker, the summed
        % expression, or the value-witness pair.
        '$aggregate_tuple'([], []).
        '$aggregate_tuple'([Spec|Specs], [E|Es]) :-
            '$aggregate_spec_expression'(Spec, E),
            '$aggregate_tuple'(Specs, Es).

        '$aggregate_spec_expression'(count, x).
        '$aggregate_spec_expression'(count(E), E).
        '$aggregate_spec_expression'(sum(E), E).
        '$aggregate_spec_expression'(max(E), E).
        '$aggregate_spec_expression'(min(E), E).
        '$aggregate_spec_expression'(bag(E), E).
        '$aggregate_spec_expression'(set(E), E).
        '$aggregate_spec_expression'(max(E, W), E-W).
        '$aggregate_spec_expression'(min(E, W), E-W).

        '$aggregate_columns'([], _, []).
        '$aggregate_columns'([Spec|Specs], Tuples, [Result|Results]) :-
            '$aggregate_heads'(Tuples, Column, Rests),
            '$aggregate_one'(Spec, Column, Result),
            '$aggregate_columns'(Specs, Rests, Results).

        '$aggregate_heads'([], [], []).
        '$aggregate_heads'([[X|Xs]|Tuples], [X|Column], [Xs|Rests]) :-
            '$aggregate_heads'(Tuples, Column, Rests).

        '$aggregate_one'(count, Values, Count) :- length(Values, Count).
        '$aggregate_one'(count(_), Values, Count) :- length(Values, Count).
        '$aggregate_one'(sum(_), Values, Sum) :- sum_list(Values, Sum).
        '$aggregate_one'(max(_), Values, Max) :- Values \== [], max_list(Values, Max).
        '$aggregate_one'(min(_), Values, Min) :- Values \== [], min_list(Values, Min).
        '$aggregate_one'(bag(_), Values, Values).
        '$aggregate_one'(set(_), Values, Set) :- sort(Values, Set).
        '$aggregate_one'(max(_, _), Pairs, max(Max, Witness)) :-
            Pairs \== [],
            '$aggregate_max_pair'(Pairs, Max, Witness).
        '$aggregate_one'(min(_, _), Pairs, min(Min, Witness)) :-
            Pairs \== [],
            '$aggregate_min_pair'(Pairs, Min, Witness).

        '$aggregate_max_pair'([E-W|Pairs], Max, Witness) :-
            '$aggregate_max_pair'(Pairs, E, W, Max, Witness).

        '$aggregate_max_pair'([], Max, Witness, Max, Witness).
        '$aggregate_max_pair'([E-W|Pairs], E0, W0, Max, Witness) :-
            (   E > E0
            ->  '$aggregate_max_pair'(Pairs, E, W, Max, Witness)
            ;   '$aggregate_max_pair'(Pairs, E0, W0, Max, Witness)
            ).

        '$aggregate_min_pair'([E-W|Pairs], Min, Witness) :-
            '$aggregate_min_pair'(Pairs, E, W, Min, Witness).

        '$aggregate_min_pair'([], Min, Witness, Min, Witness).
        '$aggregate_min_pair'([E-W|Pairs], E0, W0, Min, Witness) :-
            (   E < E0
            ->  '$aggregate_min_pair'(Pairs, E, W, Min, Witness)
            ;   '$aggregate_min_pair'(Pairs, E0, W0, Min, Witness)
            ).

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
        '$has_type'(string, X) :- string(X).
        '$has_type'(symbol, X) :- atom(X).
        '$has_type'(text, X) :-
            (   atom(X) -> true
            ;   string(X) -> true
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
        '$known_type'(string).
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

        % Ordered sets are canonical, so equal sets are the same list.
        ord_seteq(Set1, Set2) :- Set1 == Set2.

        ord_symdiff([], Set2, Set2).
        ord_symdiff([H|T], Set2, Difference) :- '$ord_symdiff'(Set2, H, T, Difference).

        '$ord_symdiff'([], H, T, [H|T]).
        '$ord_symdiff'([H2|T2], H, T, Difference) :-
            compare(O, H, H2),
            '$ord_symdiff_step'(O, H, T, H2, T2, Difference).

        '$ord_symdiff_step'('<', H, T, H2, T2, [H|Difference]) :-
            ord_symdiff(T, [H2|T2], Difference).
        '$ord_symdiff_step'('=', _, T, _, T2, Difference) :- ord_symdiff(T, T2, Difference).
        '$ord_symdiff_step'('>', H, T, H2, T2, [H2|Difference]) :- '$ord_symdiff'(T2, H, T, Difference).

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

        del_min_assoc(Assoc, Key, Value, Rest) :- '$assoc_del_min'(Assoc, Key, Value, Rest).

        del_max_assoc(Assoc, Key, Value, Rest) :- '$assoc_del_max'(Assoc, Key, Value, Rest).

        '$assoc_del_max'(t(K, V, _, L, t), K, V, L) :- !.
        '$assoc_del_max'(t(K0, V0, _, L, R), K, V, Assoc) :-
            '$assoc_del_max'(R, K, V, NewRight),
            '$assoc_rebalance'(K0, V0, L, NewRight, Assoc).

        % gen_assoc/3 enumerates pairs on backtracking in ascending key order; a
        % bound key reads directly instead of enumerating, the way SWI's does.
        gen_assoc(Key, Assoc, Value) :-
            (   ground(Key)
            ->  get_assoc(Key, Assoc, Value)
            ;   '$gen_assoc'(Assoc, Key, Value)
            ).

        '$gen_assoc'(t(K, V, _, L, R), Key, Value) :-
            (   '$gen_assoc'(L, Key, Value)
            ;   Key = K, Value = V
            ;   '$gen_assoc'(R, Key, Value)
            ).

        % get_assoc/5 replaces one key's value. The shape is untouched, so no
        % rebalancing and the stored heights carry over.
        get_assoc(Key, t(K, V, H, L, R), Value0, Assoc, Value) :-
            compare(O, Key, K),
            '$get_assoc5_step'(O, Key, K, V, H, L, R, Value0, Assoc, Value).

        '$get_assoc5_step'('=', _, K, V, H, L, R, V, t(K, Value, H, L, R), Value).
        '$get_assoc5_step'('<', Key, K, V, H, L, R, Value0, t(K, V, H, NewLeft, R), Value) :-
            get_assoc(Key, L, Value0, NewLeft, Value).
        '$get_assoc5_step'('>', Key, K, V, H, L, R, Value0, t(K, V, H, L, NewRight), Value) :-
            get_assoc(Key, R, Value0, NewRight, Value).

        map_assoc(_, t).
        map_assoc(Goal, t(_, V, _, L, R)) :-
            map_assoc(Goal, L),
            call(Goal, V),
            map_assoc(Goal, R).

        map_assoc(_, t, t).
        map_assoc(Goal, t(K, V, H, L, R), t(K, V2, H, L2, R2)) :-
            map_assoc(Goal, L, L2),
            call(Goal, V, V2),
            map_assoc(Goal, R, R2).

        % is_assoc/1 validates the AVL shape: stored heights consistent, every
        % balance factor within one, and the in-order keys strictly ascending.
        is_assoc(Assoc) :-
            nonvar(Assoc),
            '$is_assoc_shape'(Assoc, _),
            assoc_to_keys(Assoc, Keys),
            '$assoc_keys_ascending'(Keys).

        '$is_assoc_shape'(Tree, _) :- var(Tree), !, fail.
        '$is_assoc_shape'(t, 0).
        '$is_assoc_shape'(t(_, _, H, L, R), H) :-
            integer(H),
            '$is_assoc_shape'(L, HL),
            '$is_assoc_shape'(R, HR),
            Difference is HL - HR,
            Difference >= -1,
            Difference =< 1,
            ( HL >= HR -> H =:= HL + 1 ; H =:= HR + 1 ).

        '$assoc_keys_ascending'([]).
        '$assoc_keys_ascending'([_]).
        '$assoc_keys_ascending'([K1, K2|Keys]) :-
            K1 @< K2,
            '$assoc_keys_ascending'([K2|Keys]).
        """;
}
