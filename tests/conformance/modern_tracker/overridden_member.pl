% GAP-66: redefining the public member/2 must not discard answer groups from
% bagof/3 or setof/3. Keep this in a separate process from the other cases.
member(X, [X|_]) :- !.
member(X, [_|Xs]) :- member(X, Xs).
tracker_pair(a, 10).
tracker_pair(b, 20).
tracker_pair(a, 30).

:- include('report.pl').
:- initialization(main).
main :-
    tracker_report(bagof_overridden_member, bagof(V, tracker_pair(K,V), L), K-L),
    tracker_report(setof_overridden_member, setof(V, tracker_pair(K,V), L), K-L).
