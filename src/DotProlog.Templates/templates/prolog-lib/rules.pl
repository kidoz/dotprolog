% Ordinary ISO Prolog. Nothing here is .NET-specific — the C# surface is declared in rules.dpli,
% which keeps this file loadable by any other Prolog system.

:- module(rules, [double/2, positive/1, upto/2]).

double(X, Y) :- Y is X * 2.

positive(X) :- X > 0.

upto(N, X) :- between(1, N, X).
