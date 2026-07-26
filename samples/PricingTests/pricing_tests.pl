% Tests written in plain Prolog. A zero-arity predicate named test_* is a test: it passes if it can
% be proved, and fails if it fails, throws, or halts.

test_discount_reduces_price :-
    R is 100 - (100 * 15 / 100),
    R =:= 85.

test_tier_boundaries :-
    tier(1000, gold),
    tier(999, silver),
    tier(0, bronze).

tier(Total, gold)   :- Total >= 1000, !.
tier(Total, silver) :- Total >= 500, !.
tier(_, bronze).

test_bundles_are_enumerated :-
    findall(B, bundle([a, b], B), Bundles),
    length_of(Bundles, 4).

bundle([], []).
bundle([H|T], [H|R]) :- bundle(T, R).
bundle([_|T], R) :- bundle(T, R).

length_of([], 0).
length_of([_|T], N) :- length_of(T, M), N is M + 1.

test_errors_are_catchable :-
    catch(_ is 1 // 0, error(evaluation_error(zero_divisor), _), true).
