% Loaded from disk at run time, after the executable has already started.
% Nothing here is known when the program is published.

:- dynamic stored/1.

colour(red).
colour(green).
colour(blue).

stored(first).

describe(C, Text) :- colour(C), atom_or_self(C, Text).

atom_or_self(X, X).

% A grammar, to prove the loader's DCG translation runs inside a native image. Double-quoted
% text reads as characters, so the grammar walks characters.
number(N)     --> digits(Ds), { number_chars(N, Ds) }.
digits([D|T]) --> digit(D), digits(T).
digits([D])   --> digit(D).
digit(D)      --> [D], { D @>= '0', D @=< '9' }.
