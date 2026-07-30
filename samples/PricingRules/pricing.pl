% A small rule set, written as ordinary ISO Prolog. Nothing here is .NET-specific:
% the C# surface is declared separately, in pricing.dpli.

:- module(pricing, [discount/3, tier/2, in_catalogue/1, bundle/2, stock_level/2, compiled_runtime/1]).

discount(Price, Percent, Result) :-
    Result is Price - (Price * Percent / 100).

tier(Total, gold)   :- Total >= 1000, !.
tier(Total, silver) :- Total >= 500, !.
tier(_, bronze).

stock_level(widget, 7).
stock_level(gadget, 0).

in_catalogue(widget).
in_catalogue(gadget).
in_catalogue(sprocket).

% Used by the NativeAOT acceptance sample to prove a generated-C# predicate can call a predicate
% that did not exist until the process consulted it.
compiled_runtime(Value) :- runtime_value(Value).

% Every way of splitting a catalogue into two bundles.
bundle([], []).
bundle([H|T], [H|R]) :- bundle(T, R).
bundle([_|T], R) :- bundle(T, R).
