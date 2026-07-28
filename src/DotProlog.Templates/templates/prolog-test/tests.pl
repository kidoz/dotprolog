% Every zero-arity predicate named test_* is a test.

test_addition :-
    2 + 2 =:= 4.

test_lists :-
    append([a, b], [c], [a, b, c]).
