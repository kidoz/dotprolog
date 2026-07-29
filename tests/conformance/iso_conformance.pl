% Conformance cases derived from ISO/IEC 13211-1:1995, clause 8 (built-in predicates).
%
% Each case names the clause it comes from, a goal, and what the standard says that goal does:
%
%   success            the goal succeeds
%   failure            the goal fails
%   error(Formal)      the goal raises error(Formal, _)
%
% This is our own encoding of the standard, not a third-party suite. It is written as ordinary
% Prolog so it can be run against another system for comparison.
%
% Every case is run through call/1, so a case involving cut measures the meta-call path rather
% than what the same goal would do written directly in a clause body. The two differ here, and
% COMPATIBILITY.md says how.


% --- Helpers used by the cases below -----------------------------------------
% Reading and writing are checked through atoms rather than files, so that a case
% stays a single goal and the suite needs nothing on disk.

reads_as(Text, Term) :- read_term_from_atom(Text, Read, []), Read == Term.

writeq_gives(Term, Expected) :- with_output_to(atom(Written), writeq(Term)), Written == Expected.

write_gives(Term, Expected) :- with_output_to(atom(Written), write(Term)), Written == Expected.

writeq_codes(Term, Expected) :- with_output_to(codes(Written), writeq(Term)), Written == Expected.

% --- 8.2 Unification ---------------------------------------------------------
iso_case('8.2.1', (_X = 1), success).
iso_case('8.2.1', (1 = 2), failure).
iso_case('8.2.1', (f(X1, b) = f(a, Y1)), success).
iso_case('8.2.1', (f(a) = g(a)), failure).
iso_case('8.2.2', unify_with_occurs_check(X2, f(X2)), failure).
iso_case('8.2.2', (unify_with_occurs_check(X3, f(a)), X3 == f(a)), success).
iso_case('8.2.2', (unify_with_occurs_check(X4, Y4), X4 == Y4), success).
iso_case('8.2.2', (\+ unify_with_occurs_check(f(X5, X5), f(a, b)), var(X5)), success).
iso_case('8.2.3', \+ (_X \= _Y), success).
iso_case('8.2.3', (1 \= 2), success).

% --- 8.3 Type testing --------------------------------------------------------
iso_case('8.3.1', var(_), success).
iso_case('8.3.1', var(foo), failure).
iso_case('8.3.2', atom(atom), success).
iso_case('8.3.2', atom([]), success).
iso_case('8.3.2', atom(1), failure).
iso_case('8.3.2', atom(f(a)), failure).
iso_case('8.3.3', integer(3), success).
iso_case('8.3.3', integer(3.3), failure).
iso_case('8.3.4', float(3.3), success).
iso_case('8.3.4', float(3), failure).
iso_case('8.3.5', atomic(atom), success).
iso_case('8.3.5', atomic(1), success).
iso_case('8.3.5', atomic(f(a)), failure).
iso_case('8.3.5', atomic(_), failure).
iso_case('8.3.6', compound(f(a)), success).
iso_case('8.3.6', compound([a]), success).
iso_case('8.3.6', compound(a), failure).
iso_case('8.3.7', nonvar(33.3), success).
iso_case('8.3.7', nonvar(_), failure).
iso_case('8.3.8', number(3), success).
iso_case('8.3.8', number(3.3), success).
iso_case('8.3.8', number(a), failure).
iso_case('8.3.9', callable(a), success).
iso_case('8.3.9', callable(f(a)), success).
iso_case('8.3.9', callable(1), failure).

% --- 8.4 Term comparison -----------------------------------------------------
iso_case('8.4.1', (1 == 1), success).
iso_case('8.4.1', (1 \== 1), failure).
iso_case('8.4.1', (aardvark @< zebra), success).
iso_case('8.4.1', (short @< short), failure).
iso_case('8.4.1', (foo(a, b) @< north(a)), failure).
iso_case('8.4.1', (1.0 @< 1), success).
iso_case('8.4.2', compare(<, 1, 2), success).
iso_case('8.4.2', compare(=, 1, 1), success).
iso_case('8.4.2', compare(>, 2, 1), success).

% --- 8.5 Term creation and decomposition -------------------------------------
iso_case('8.5.1', functor(foo(a, b, c), foo, 3), success).
iso_case('8.5.1', functor(foo, foo, 0), success).
iso_case('8.5.1', functor([_|_], '.', 2), success).
iso_case('8.5.1', functor(1, 1, 0), success).
iso_case('8.5.1', functor(_, _, _), error(instantiation_error)).
iso_case('8.5.1', functor(_, foo, _), error(instantiation_error)).
iso_case('8.5.1', functor(_, foo, a), error(type_error(integer, a))).
iso_case('8.5.1', functor(_, _, 1), error(instantiation_error)).
iso_case('8.5.1', functor(_, foo(a), 1), error(type_error(atomic, foo(a)))).
iso_case('8.5.2', arg(1, foo(a, b), a), success).
iso_case('8.5.2', arg(3, foo(a, b), _), failure).
iso_case('8.5.2', arg(0, foo(a, b), _), failure).
iso_case('8.5.2', arg(_, foo(a), _), error(instantiation_error)).
iso_case('8.5.2', arg(a, foo(a), _), error(type_error(integer, a))).
iso_case('8.5.2', arg(1, atom, _), error(type_error(compound, atom))).
iso_case('8.5.3', (foo(a, b) =.. [foo, a, b]), success).
iso_case('8.5.3', (foo =.. [foo]), success).
iso_case('8.5.3', (1 =.. [1]), success).
iso_case('8.5.3', ([a] =.. ['.', a, []]), success).
iso_case('8.5.3', (_ =.. _), error(instantiation_error)).
iso_case('8.5.3', (_ =.. [foo, a | _]), error(instantiation_error)).
iso_case('8.5.3', (_ =.. [_, a]), error(instantiation_error)).
iso_case('8.5.3', (_ =.. [foo(a), a]), error(type_error(atom, foo(a)))).
iso_case('8.5.3', (_ =.. []), error(domain_error(non_empty_list, []))).
iso_case('8.5.4', copy_term(_, _), success).
iso_case('8.5.4', copy_term(a, a), success).
iso_case('8.5.4', copy_term(a, b), failure).
iso_case('8.5.4', (copy_term(f(X2, X2), f(A2, B2)), A2 == B2), success).

% --- 8.6 Arithmetic evaluation -----------------------------------------------
iso_case('8.6.1', (X3 is 3 + 11, X3 =:= 14), success).
iso_case('8.6.1', (X4 is 3.5 - 0.5, X4 =:= 3.0), success).
iso_case('8.6.1', (_ is foo), error(type_error(evaluable, foo/0))).
iso_case('8.6.1', (_ is _), error(instantiation_error)).
iso_case('8.6.1', (_ is 1 + a), error(type_error(evaluable, a/0))).
iso_case('8.6.1', (_ is 1 // 0), error(evaluation_error(zero_divisor))).
iso_case('8.6.1', (foo is 1 + 1), failure).
iso_case('8.6.1', (X10 is 6 / 3, float(X10), X10 =:= 2.0), success).
iso_case('8.6.1', (_ is 1.0 / 0.0), error(evaluation_error(zero_divisor))).
iso_case('8.6.1', (_ is 0.0 / 0.0), error(evaluation_error(undefined))).
iso_case('8.6.1', (_ is 1 // 2.0), error(type_error(integer, 2.0))).
iso_case('8.6.1', (X11 is 2 ** 3, float(X11), X11 =:= 8.0), success).
iso_case('8.6.1', (_ is 2 ^ -1), error(type_error(float, 2))).
iso_case('8.6.1', (1 ^ (-1) =:= 1), success).
iso_case('8.6.1', ((-1) ^ (-2) =:= 1), success).
iso_case('8.6.1', (_ is 0 ^ -1), error(evaluation_error(zero_divisor))).
iso_case('8.6.1', (X12 is min(1.0, 1), integer(X12)), success).
iso_case('8.6.1', (X13 is max(1.0, 1), integer(X13)), success).
iso_case('8.6.1', (X14 is \ 1, X14 =:= -2), success).
iso_case('8.6.1', (_ is \ 1.0), error(type_error(integer, 1.0))).
iso_case('8.6.1', (1 << 2 =:= 4), success).
iso_case('8.6.1', (4 >> 2 =:= 1), success).
iso_case('8.6.1', (_ is 1 << 60), error(evaluation_error(int_overflow))).
iso_case('8.6.1', (_ is 1.0 << 2), error(type_error(integer, 1.0))).
iso_case('8.6.1', (X15 is pi, X15 > 3.14, X15 < 3.15), success).
iso_case('8.6.1', (X16 is sqrt(9), float(X16), X16 =:= 3.0), success).
iso_case('8.6.1', (_ is sqrt(-1)), error(evaluation_error(undefined))).
iso_case('8.6.1', (sin(0) =:= 0.0), success).
iso_case('8.6.1', (cos(0) =:= 1.0), success).
iso_case('8.6.1', (tan(0) =:= 0.0), success).
iso_case('8.6.1', (asin(0) =:= 0.0), success).
iso_case('8.6.1', (acos(1) =:= 0.0), success).
iso_case('8.6.1', (atan(0) =:= 0.0), success).
iso_case('8.6.1', (atan2(0, 1) =:= 0.0), success).
iso_case('8.6.1', (_ is atan2(0, 0)), error(evaluation_error(undefined))).
iso_case('8.6.1', (exp(0) =:= 1.0), success).
iso_case('8.6.1', (log(1) =:= 0.0), success).
iso_case('8.6.1', (_ is log(0)), error(evaluation_error(zero_divisor))).
iso_case('8.6.1', (X17 is float(3), float(X17), X17 =:= 3.0), success).
iso_case('8.6.1', (round(1.5) =:= 2), success).
iso_case('8.6.1', (round(-1.5) =:= -1), success).
iso_case('8.6.1', (truncate(-1.9) =:= -1), success).
iso_case('8.6.1', (floor(-1.1) =:= -2), success).
iso_case('8.6.1', (ceiling(-1.1) =:= -1), success).
iso_case('8.6.1', (_ is floor(1)), error(type_error(float, 1))).
iso_case('8.6.1', (float_integer_part(-1.25) =:= -1.0), success).
iso_case('8.6.1', (float_fractional_part(-1.25) =:= -0.25), success).
iso_case('8.6.1', (_ is float_integer_part(1)), error(type_error(float, 1))).
iso_case('8.6.1', (_ is max_tagged_integer + 1), error(evaluation_error(int_overflow))).

% --- 8.7 Arithmetic comparison -----------------------------------------------
iso_case('8.7.1', (0 =:= 0.0), success).
iso_case('8.7.1', (1 =\= 2), success).
iso_case('8.7.1', (1 < 2), success).
iso_case('8.7.1', (2 =< 2), success).
iso_case('8.7.1', (3 > 2), success).
iso_case('8.7.1', (3 >= 3), success).
iso_case('8.7.1', (_ < 1), error(instantiation_error)).
iso_case('8.7.1', (a < 1), error(type_error(evaluable, a/0))).

% --- 8.8 Clause retrieval and information ------------------------------------
iso_case('8.8.1', clause(_, _), error(instantiation_error)).
iso_case('8.8.1', clause(_, b), error(instantiation_error)).
iso_case('8.8.1', clause(4, _), error(type_error(callable, 4))).
iso_case('8.8.2', current_predicate(repeat_guard/1), success).
iso_case('8.8.2', current_predicate(write/1), failure).
iso_case('8.8.2', (current_predicate(repeat_guard/A10), A10 == 1), success).
iso_case('8.8.2', (retractall(iso_empty(_)), current_predicate(iso_empty/1)), success).
iso_case('8.8.2', (assertz(iso_gone(a)), abolish(iso_gone/1), \+ current_predicate(iso_gone/1)), success).
iso_case('8.8.2', current_predicate(4), error(type_error(predicate_indicator, 4))).
iso_case('8.8.2', current_predicate(foo/a), error(type_error(integer, a))).
iso_case('8.8.2', current_predicate(4/1), error(type_error(atom, 4))).
iso_case('8.8.2', current_predicate(foo/(-1)), error(domain_error(not_less_than_zero, -1))).
iso_case('8.8.2', current_predicate(foo/256), error(representation_error(max_arity))).

% --- 8.9 Clause creation and destruction -------------------------------------
iso_case('8.9.1', assertz(iso_scratch(1)), success).
iso_case('8.9.1', assertz(_), error(instantiation_error)).
iso_case('8.9.1', assertz(4), error(type_error(callable, 4))).
iso_case('8.9.1', assertz((iso_scratch(_) :- 4)), error(type_error(callable, 4))).
iso_case('8.9.2', asserta(iso_scratch(0)), success).
iso_case('8.9.2', asserta(_), error(instantiation_error)).
iso_case('8.9.3', retract(_), error(instantiation_error)).
iso_case('8.9.3', retract(iso_absent(_)), failure).

% --- 8.10 All solutions ------------------------------------------------------
iso_case('8.10.1', findall(X5, member(X5, [1, 2]), [1, 2]), success).
iso_case('8.10.1', findall(_, fail, []), success).
iso_case('8.10.1', findall(_, _, _), error(instantiation_error)).
iso_case('8.10.1', findall(_, 4, _), error(type_error(callable, 4))).
iso_case('8.10.2', bagof(X6, member(X6, [1, 2]), [1, 2]), success).
iso_case('8.10.2', bagof(_, fail, _), failure).
iso_case('8.10.3', setof(X7, member(X7, [2, 1, 2]), [1, 2]), success).
iso_case('8.10.3', setof(_, fail, _), failure).

% --- 8.11 Stream selection ---------------------------------------------------
iso_case('8.11.1', (current_output(S1), S1 == S1), success).
iso_case('8.11.2', (current_input(S2), S2 == S2), success).

% --- 8.14 Term input and output ----------------------------------------------
iso_case('8.14.2', write_canonical([1, 2, 3]), success).
iso_case('8.14.2', write_canonical('a b'), success).
iso_case('8.14.3', (op(30, xfy, iso_op), current_op(30, xfy, iso_op)), success).
iso_case('8.14.3', op(_, xfy, iso_bad), error(instantiation_error)).
iso_case('8.14.3', op(30, _, iso_bad), error(instantiation_error)).
iso_case('8.14.3', op(a, xfy, iso_bad), error(type_error(integer, a))).
iso_case('8.14.3', op(30, a, iso_bad), error(domain_error(operator_specifier, a))).
iso_case('8.14.3', op(1300, xfx, iso_bad), error(domain_error(operator_priority, 1300))).
iso_case('8.14.3', op(30, xfy, 0), error(type_error(atom, 0))).
iso_case('8.14.3', op(30, xfx, ','), error(permission_error(modify, operator, ','/2))).

% --- 8.15 Logic and control --------------------------------------------------
iso_case('8.15.1', \+ true, failure).
iso_case('8.15.1', \+ fail, success).
iso_case('8.15.1', \+ _, error(instantiation_error)).
iso_case('8.15.1', \+ 4, error(type_error(callable, 4))).
iso_case('8.15.2', once(!), success).
iso_case('8.15.2', once(fail), failure).
iso_case('8.15.3', (repeat_guard(0) -> true ; true), success).
iso_case('8.15.3', call(!), success).
iso_case('8.15.3', call(fail), failure).
iso_case('8.15.3', call(_), error(instantiation_error)).
iso_case('8.15.3', call(4), error(type_error(callable, 4))).
iso_case('8.15.3', call((fail, 4)), error(type_error(callable, (fail, 4)))).

% --- 8.16 Atomic term processing ---------------------------------------------
iso_case('8.16.1', atom_length(abcde, 5), success).
iso_case('8.16.1', atom_length('', 0), success).
iso_case('8.16.1', atom_length(_, _), error(instantiation_error)).
iso_case('8.16.1', atom_length(1.23, _), success).
iso_case('8.16.2', atom_concat(hello, ' world', 'hello world'), success).
iso_case('8.16.2', (atom_concat(T1, ' world', 'hello world'), T1 == hello), success).
iso_case('8.16.2', findall(_-_, atom_concat(_, _, abc), L1), success).
iso_case('8.16.2', atom_concat(_, _, _), error(instantiation_error)).
iso_case('8.16.3', sub_atom(abracadabra, 0, 5, _, abrac), success).
iso_case('8.16.3', (sub_atom(abracadabra, _, 5, 0, S3), S3 == dabra), success).
iso_case('8.16.3', sub_atom(_, _, _, _, _), error(instantiation_error)).
iso_case('8.16.4', atom_chars('', []), success).
iso_case('8.16.4', atom_chars([], ['[', ']']), success).
iso_case('8.16.4', atom_chars(abc, [a, b, c]), success).
iso_case('8.16.4', (atom_chars(A3, [a, b, c]), A3 == abc), success).
iso_case('8.16.4', atom_chars(_, _), error(instantiation_error)).
iso_case('8.16.5', atom_codes(abc, [0'a, 0'b, 0'c]), success).
iso_case('8.16.5', atom_codes(_, _), error(instantiation_error)).
iso_case('8.16.6', char_code(a, 0'a), success).
iso_case('8.16.6', (char_code(C1, 0'a), C1 == a), success).
iso_case('8.16.6', char_code(_, _), error(instantiation_error)).
iso_case('8.16.7', number_chars(33, ['3', '3']), success).
iso_case('8.16.7', (number_chars(N1, ['3', '3']), N1 == 33), success).
iso_case('8.16.7', number_chars(_, [a]), error(syntax_error(illegal_number))).
iso_case('8.16.8', number_codes(33, [0'3, 0'3]), success).
iso_case('8.16.8', number_codes(_, [0'a]), error(syntax_error(illegal_number))).

% --- 8.17 Implementation defined hooks ---------------------------------------
iso_case('8.17.1', (catch(throw(ball), Ball, true), Ball == ball), success).
iso_case('8.17.1', catch(true, _, fail), success).
iso_case('8.17.1', throw(_), error(instantiation_error)).
iso_case('8.17.2', current_prolog_flag(bounded, true), success).
iso_case('8.17.2', current_prolog_flag(integer_rounding_function, toward_zero), success).
iso_case('8.17.2', current_prolog_flag(max_arity, 255), success).
iso_case('8.17.2', current_prolog_flag(double_quotes, codes), success).
iso_case('8.17.2', current_prolog_flag(not_a_flag, _), failure).
iso_case('8.17.2', (\+ current_prolog_flag(F1, F1), var(F1)), success).
iso_case('8.17.2', current_prolog_flag(1, _), error(type_error(atom, 1))).
iso_case('8.17.3', set_prolog_flag(_, codes), error(instantiation_error)).
iso_case('8.17.3', set_prolog_flag(double_quotes, _), error(instantiation_error)).
iso_case('8.17.3', set_prolog_flag(1, codes), error(type_error(atom, 1))).
iso_case('8.17.3', set_prolog_flag(not_a_flag, value), error(domain_error(prolog_flag, not_a_flag))).
iso_case('8.17.3', set_prolog_flag(double_quotes, strings), error(domain_error(flag_value, double_quotes+strings))).
iso_case('8.17.3', set_prolog_flag(bounded, false), error(domain_error(flag_value, bounded+false))).
iso_case('8.17.3', set_prolog_flag(bounded, true), error(permission_error(modify, flag, bounded))).
iso_case('8.17.3', (set_prolog_flag(debug, on), current_prolog_flag(debug, on), set_prolog_flag(debug, off)), success).

% Used by 8.15.3; a goal that is defined so the case tests control flow, not existence.
repeat_guard(0).

% --- 7.1 Terms, read back from text ------------------------------------------
iso_case('7.1.1', reads_as('foo', foo), success).
iso_case('7.1.1', reads_as('[]', []), success).
iso_case('7.1.2', reads_as('123', 123), success).
iso_case('7.1.2', reads_as('-123', -123), success).
iso_case('7.1.2', reads_as('0''a', 97), success).
iso_case('7.1.2', reads_as('0x1f', 31), success).
iso_case('7.1.2', reads_as('0o17', 15), success).
iso_case('7.1.2', reads_as('0b101', 5), success).
iso_case('7.1.2', reads_as('1.0', 1.0), success).
iso_case('7.1.2', reads_as('1.0e2', 100.0), success).
iso_case('7.1.4', reads_as('f(a, b)', f(a, b)), success).
iso_case('7.1.6', reads_as('[a, b]', [a, b]), success).
iso_case('7.1.6', reads_as('[a|b]', [a|b]), success).
iso_case('7.1.6', reads_as('{a}', {a}), success).

% --- 7.2 Operators, and how priority decides structure ------------------------
iso_case('7.2.1', reads_as('1 + 2 * 3', +(1, *(2, 3))), success).
iso_case('7.2.1', reads_as('(1 + 2) * 3', *(+(1, 2), 3)), success).
iso_case('7.2.1', reads_as('1 - 2 - 3', -(-(1, 2), 3)), success).
iso_case('7.2.1', reads_as('1 ^ 2 ^ 3', ^(1, ^(2, 3))), success).
iso_case('7.2.1', reads_as('- 1', -(1)), success).
iso_case('7.2.1', reads_as('a :- b, c', ':-'(a, ','(b, c))), success).
iso_case('7.2.1', reads_as('\\+ a', \+(a)), success).
iso_case('7.2.1', reads_as('f(-)', f(-)), success).
iso_case('7.2.1', reads_as('[-]', [-]), success).

% --- 7.10 Writing terms -------------------------------------------------------
iso_case('7.10.5', writeq_gives(foo, foo), success).
iso_case('7.10.5', writeq_gives([], '[]'), success).
iso_case('7.10.5', writeq_gives(1 + 2, '1+2'), success).
iso_case('7.10.5', writeq_gives(1 + 2 * 3, '1+2*3'), success).
iso_case('7.10.5', writeq_gives((1 + 2) * 3, '(1+2)*3'), success).
iso_case('7.10.5', writeq_gives(1 - (2 - 3), '1-(2-3)'), success).
iso_case('7.10.5', writeq_gives(-(1), '- 1'), success).
iso_case('7.10.5', writeq_gives(f(-1), 'f(-1)'), success).
iso_case('7.10.5', writeq_gives([a, b], '[a,b]'), success).
iso_case('7.10.5', writeq_gives([a|b], '[a|b]'), success).
iso_case('7.10.5', writeq_gives({a}, '{a}'), success).
iso_case('7.10.5', writeq_gives(1.0, '1.0'), success).
iso_case('7.10.5', write_gives('a b', 'a b'), success).
% A quoted atom is written so that it reads back: quote, backslash, n, quote.
iso_case('7.10.5', writeq_codes('\n', [39, 92, 110, 39]), success).
iso_case('7.10.5', writeq_codes('a b', [39, 97, 32, 98, 39]), success).
iso_case('7.10.5', writeq_codes('', [39, 39]), success).

% --- 7.8 Control constructs ---------------------------------------------------
iso_case('7.8.1', true, success).
iso_case('7.8.2', fail, failure).
iso_case('7.8.3', call(true), success).
iso_case('7.8.4', (!, fail ; true), failure).
iso_case('7.8.5', (fail, _X8), failure).
iso_case('7.8.6', (true ; fail), success).
iso_case('7.8.7', (fail -> true ; true), success).
iso_case('7.8.7', (true -> fail ; true), failure).
iso_case('7.8.8', catch(true, _, true), success).
iso_case('7.8.9', catch(fail, _, true), failure).

% --- 8.11 Stream selection and control ----------------------------------------
iso_case('8.11.5', open(_, read, _), error(instantiation_error)).
iso_case('8.11.5', open(f, _, _), error(instantiation_error)).
iso_case('8.11.5', open(f, sideways, _), error(domain_error(io_mode, sideways))).
iso_case('8.11.5', open(1, read, _), error(type_error(atom, 1))).
iso_case('8.11.6', close(_), error(instantiation_error)).
iso_case('8.11.6', close(no_such_stream), error(existence_error(stream, no_such_stream))).
iso_case('8.11.7', (current_output(S4), \+ var(S4)), success).
iso_case('8.11.8', set_output(no_such_stream), error(existence_error(stream, no_such_stream))).
iso_case('8.11.8', set_input(no_such_stream), error(existence_error(stream, no_such_stream))).
iso_case('8.11.8', set_input(user_output), error(permission_error(input, stream, user_output/0))).
iso_case('8.11.8', set_output(user_input), error(permission_error(output, stream, user_input/0))).

% --- 8.12 Character input and output ------------------------------------------
iso_case('8.12.1', get_char(no_such_stream, _), error(existence_error(stream, no_such_stream))).
iso_case('8.12.1', get_char(user_output, _), error(permission_error(input, stream, user_output/0))).
iso_case('8.12.2', peek_char(no_such_stream, _), error(existence_error(stream, no_such_stream))).
iso_case('8.12.3', put_char(user_output, _), error(instantiation_error)).
iso_case('8.12.3', put_char(user_output, ab), error(type_error(character, ab))).
iso_case('8.12.3', put_char(user_input, a), error(permission_error(output, stream, user_input/0))).

% --- 8.14 Term input and output -----------------------------------------------
iso_case('8.14.1', read_term(no_such_stream, _, []), error(existence_error(stream, no_such_stream))).
iso_case('8.14.1', read_term(user_output, _, []), error(permission_error(input, stream, user_output/0))).
iso_case('8.14.2', write(no_such_stream, a), error(existence_error(stream, no_such_stream))).
iso_case('8.14.2', write_term(a, [nonsense(x)]), error(domain_error(write_option, nonsense(x)))).
iso_case('8.14.2', (with_output_to(atom(A4), write_term(1 + 2, [ignore_ops(true)])), A4 == '+(1,2)'), success).
iso_case('8.14.2', (with_output_to(atom(A5), write_term('a b', [quoted(true)])), atom_length(A5, 5)), success).
iso_case('8.14.4', (current_op(P1, xfx, ':-'), P1 =:= 1200), success).

% --- 8.16 Atomic term processing, remaining modes ------------------------------
iso_case('8.16.2', (atom_concat(A6, B6, ab), A6 == '', B6 == ab), success).
iso_case('8.16.2', findall(A7-B7, atom_concat(A7, B7, ab), [''-ab, a-b, ab-'']), success).
iso_case('8.16.3', findall(S5, sub_atom(abc, _, 1, _, S5), [a, b, c]), success).
iso_case('8.16.3', (sub_atom(abc, 1, 1, A8, S6), A8 =:= 1, S6 == b), success).
iso_case('8.16.3', sub_atom(abc, 0, 4, _, _), failure).
iso_case('8.16.4', atom_chars(1.0, ['1', '.', '0']), success).
iso_case('8.16.5', (atom_codes(A9, [0'a]), A9 == a), success).
iso_case('8.16.6', char_code(_, -1), error(representation_error(character_code))).
iso_case('8.16.7', (number_chars(N2, [' ', '3']), N2 == 3), success).
iso_case('8.16.7', number_chars(_, ['3', 'a']), error(syntax_error(illegal_number))).

% --- Sorting: not in clause 8, but standard in every system --------------------
iso_case('sort/2', sort([b, a, b], [a, b]), success).
iso_case('sort/2', sort([], []), success).
iso_case('msort/2', msort([b, a, b], [a, b, b]), success).
iso_case('keysort/2', keysort([b-1, a-2], [a-2, b-1]), success).
iso_case('keysort/2', keysort([a-1, a-2], [a-1, a-2]), success).
iso_case('sort/4', sort(0, @>=, [1, 2, 2], [2, 2, 1]), success).

% --- Runner ------------------------------------------------------------------
% Writes one line per case: PASS or FAIL with what was expected and what happened.
% Running the suite in Prolog keeps it portable to another system for comparison.

run_conformance :-
    forall(iso_case(Id, Goal, Expected), run_case(Id, Goal, Expected)),
    aggregate_all(count, iso_case(_, _, _), Total),
    format("cases ~d~n", [Total]).

run_case(Id, Goal, Expected) :-
    outcome(Goal, Actual),
    (   matches(Expected, Actual)
    ->  format("PASS ~w~n", [Id])
    ;   format("FAIL ~w | expected ~q | got ~q~n", [Id, Expected, Actual])
    ).

% A case is run once: a second solution is of no interest, and leaving a choice
% point behind would let a later case backtrack into this one.
% Whatever the goal writes is captured rather than left to interleave with the
% report, which would make a case like write_canonical/1 unreadable to a harness.
outcome(Goal, Actual) :-
    catch(
        ( with_output_to(atom(_), Goal) -> Actual = success ; Actual = failure ),
        Ball,
        ball_outcome(Ball, Actual)
    ).

ball_outcome(error(Formal, _), error(Formal)) :- !.
ball_outcome(Ball, thrown(Ball)).

% The context of an error is implementation defined, so only the formal part is
% compared; anything else has to match exactly.
matches(error(Formal), error(Actual)) :- !, Formal == Actual.
matches(Expected, Actual) :- Expected == Actual.
