% A small rule set, written as ordinary ISO Prolog. Nothing here is .NET-specific:
% the C# surface is declared separately, in pricing.dpli.

:- module(pricing, [discount/3, tier/2, in_catalogue/1, bundle/2]).

discount(Price, Percent, Result) :-
    Result is Price - (Price * Percent / 100).

tier(Total, gold)   :- Total >= 1000, !.
tier(Total, silver) :- Total >= 500, !.
tier(_, bronze).

in_catalogue(widget).
in_catalogue(gadget).
in_catalogue(sprocket).

% Every way of splitting a catalogue into two bundles.
bundle([], []).
bundle([H|T], [H|R]) :- bundle(T, R).
bundle([_|T], R) :- bundle(T, R).
