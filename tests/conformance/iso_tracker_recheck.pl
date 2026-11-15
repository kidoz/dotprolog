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

% StrictIso stream reads currently reject these valid quoted-token forms with
% syntax_error('DPL0003'). Keep them visible until the runtime reader is fixed.
tracker_case(backquoted_atom,
    (tracker_read_atom('`hello`', Term), Term == hello), ok, solutions([ok])).
tracker_case(backquoted_operator,
    (tracker_read_atom('left `is` right', Term), Term == is(left,right)), ok, solutions([ok])).
tracker_case(backquoted_numeric_escape,
    (atom_codes(Source, [96,92,49,55,55,92,96]), atom_codes(Delete, [127]),
     tracker_read_atom(Source, Term), Term == Delete), ok, solutions([ok])).
tracker_case(quoted_prefix_operator,
    (atom_codes(Source, [39,100,121,110,97,109,105,99,39,32,112,114,101,100,105,99,97,116,101]),
     tracker_read_atom(Source, Term), Term == dynamic(predicate)), ok, solutions([ok])).

tracker_read_atom(Text, Term) :-
    open('iso_tracker_input.tmp', write, Output),
    write(Output, Text),
    write(Output, '.\n'),
    close(Output),
    open('iso_tracker_input.tmp', read, Input),
    catch(read_term(Input, Read, []), Ball, (close(Input), throw(Ball))),
    close(Input),
    Term = Read.

tracker_member(X, [X|_]).
tracker_member(X, [_|Xs]) :- tracker_member(X, Xs).
