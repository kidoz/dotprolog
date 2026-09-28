% Original Modern tracker recheck cases. Run in a fresh process in Modern.
% These are audit observations, not additions to the passing ISO corpus.
:- include('report.pl').
:- initialization(main).

main :-
    tracker_report(default_double_quotes, current_prolog_flag(double_quotes, V), V),
    tracker_report(rational_quoted_codes, number_codes(N, "1r3"), N),
    tracker_report(rational_explicit_codes, number_codes(N, [49,114,51]), N),
    tracker_report(bagof_existential, bagof(X, Y^(X=beta;X=alpha), L), L),
    tracker_report(setof_existential, setof(X, Y^(X=beta;X=alpha), L), L),
    tracker_report(bagof_nested_caret, bagof(X, (true,Y^(X=beta;X=alpha)), L), L),
    tracker_report(setof_nested_caret, setof(X, (true,Y^(X=beta;X=alpha)), L), L),
    tracker_report(bagof_variable, bagof(_, _, _), ok),
    tracker_report(bagof_quantified_noncallable, bagof(_, _^23, _), ok),
    tracker_report(bagof_output_atom, bagof(X, X=kept, invalid), ok),
    tracker_report(bagof_output_tail, bagof(X, X=kept, [kept|invalid]), ok),
    tracker_report(bagof_nested_variable, bagof(_, _^_^_, _), ok),
    tracker_report(bagof_empty_bad_output, bagof(_, fail, [kept|invalid]), ok),
    tracker_report(setof_variable, setof(_, _, _), ok),
    tracker_report(setof_quantified_variable, setof(_, _^_, _), ok),
    tracker_report(setof_noncallable, setof(_, 23, _), ok),
    tracker_report(setof_quantified_noncallable, setof(_, _^23, _), ok),
    tracker_report(setof_output_atom, setof(X, X=kept, invalid), ok),
    tracker_report(setof_output_tail, setof(X, X=kept, [kept|invalid]), ok).
