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
    length(Bundles, 4).

bundle([], []).
bundle([H|T], [H|R]) :- bundle(T, R).
bundle([_|T], R) :- bundle(T, R).

test_totals_are_aggregated :-
    aggregate_all(sum(Q), member(_-Q, [widget-12, gadget-3]), 15),
    aggregate_all(count, member(_, [a, b, c]), 3).

test_lines_are_sorted_by_quantity :-
    keysort([12-widget, 3-gadget], Sorted),
    pairs_values(Sorted, [gadget, widget]).

test_a_receipt_line_is_aligned :-
    format(atom(Line), "~w~t~10|~t~d~4+", [widget, 12]),
    atom_length(Line, 14),
    sub_atom(Line, 0, 6, _, widget).

test_errors_are_catchable :-
    catch(_ is 1 // 0, error(evaluation_error(zero_divisor), _), true).
