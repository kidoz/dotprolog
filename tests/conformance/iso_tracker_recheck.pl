% Original probes for the ISO tracker source recheck (2026-09-28).
% Run directly with the CLI in strict-iso or modern mode. This is an audit
% report, not the passing regression corpus: known gaps remain visible.
% result(Id, Required, Observed) compares formal errors and complete answers.
% The writer probe prints its required and observed characters for inspection.

:- initialization(main).

main :-
    (tracker_case(Id, Goal, Template, Required),
     tracker_report(Id, Goal, Template, Required),
     fail ; true),
    write('writer_numbervars_ignore_ops: required=A; observed='),
    write_term('$VAR'(0), [numbervars(true), ignore_ops(true)]), nl.

tracker_report(Id, Goal, Template, Required) :-
    catch((findall(Template, Goal, Answers), Observed = solutions(Answers)),
          error(Formal, _), Observed = error(Formal)),
    writeq(result(Id, Required, Observed)), nl.

% Part 1 8.10.3.4 with Cor.3: retain the existential qualifier.
% The second case intentionally removes it to expose a changed test oracle.
tracker_case(setof_existential,
    setof(T, pair(U,V)^tracker_member(T, [V,U,box(U),box(V)]),
          [left,right,box(right),box(left)]), ok, solutions([ok])).
tracker_case(setof_unquantified,
    setof(T, tracker_member(T, [V,U,box(U),box(V)]),
          [left,right,box(right),box(left)]), ok, solutions([])).

% Cor.3 9.3.10.3 e; Part 1 9.1.5 and 8.7.1.
tracker_case(power_negative_integer, R is 2^(-1), R, error(type_error(float,2))).
tracker_case(mixed_division, R is 1/(10^400), R, error(evaluation_error(float_overflow))).
tracker_case(mixed_multiplication, R is 0.0*(10^400), R, error(evaluation_error(float_overflow))).
tracker_case(mixed_comparison, 10^400 =:= 1.0, ok, error(evaluation_error(float_overflow))).

% Part 1 9.3.4 does not impose the conversion rule for basic arithmetic.
% The interval is a coarse sanity check, not an ISO accuracy specification.
tracker_case(atan_large_integer,
    (R is atan(10^400), R > 1.5707, R < 1.5709), ok, solutions([ok])).
% Part 1 9.3.5.3 d concerns the nonzero mathematical result, not the input size.
tracker_case(exp_underflow, R is exp(-(10^400)), R, error(evaluation_error(underflow))).

tracker_member(X, [X|_]).
tracker_member(X, [_|Xs]) :- tracker_member(X, Xs).
