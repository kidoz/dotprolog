% Loaded from disk at run time, after the executable has already started.
% Nothing here is known when the program is published.

:- dynamic stored/1.

colour(red).
colour(green).
colour(blue).

stored(first).

describe(C, Text) :- colour(C), atom_or_self(C, Text).

atom_or_self(X, X).
