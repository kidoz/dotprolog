% Original regression cases for ISO/IEC 13211-1:1995 and Cor.1:2007 / Cor.2:2012, plus TS 13211-3:2025.
% This fixture uses only standard predicates and runs with StrictIso enabled.
% Each goal must succeed exactly once; errors must actually be raised, and cases
% about nondeterminism collect all answers before checking their order and count.

run_iso_review_case(Name) :- iso_review_case(Name, Goal), iso_review_invoke(Goal).
iso_review_invoke(Goal) :- call(Goal).
iso_review_error(Goal, Expected) :-
    catch((call(Goal), Outcome = succeeded), error(Error, _), Outcome = error(Error)),
    Outcome = error(Expected).

:- dynamic(review_dynamic/1).
:- dynamic(review_clause/2).
review_static(fixed).
review_seven(a, b, c, d, e, f, g).
review_clause(f(X), X).
review_clause(g(X), pair(X, X)).

% Cor.1 8.16.4/5: a bound atom unifies with a partial output list.
iso_review_case(atom_chars_partial, (atom_chars(dog, [d|Tail]), Tail == [o,g])).
iso_review_case(atom_codes_partial, (atom_codes(dog, [100|Tail]), Tail == [111,103])).
iso_review_case(atom_chars_variables, (atom_chars(aba, [X,Y,X]), X == a, Y == b)).
iso_review_case(atom_codes_variables, (atom_codes(aba, [X,Y,X]), X == 97, Y == 98)).
iso_review_case(atom_chars_mismatch, \+ atom_chars(dog, [d,o,t])).
iso_review_case(atom_codes_mismatch, \+ atom_codes(dog, [100,111,116])).
iso_review_case(atom_chars_length, \+ atom_chars(dog, [_,_])).
iso_review_case(atom_codes_length, \+ atom_codes(dog, [_,_])).

% Cor.2 8.16.4/5: validation also applies with a bound first argument,
% beyond a mismatching prefix, and beyond a variable element.
iso_review_case(atom_chars_nonlist, iso_review_error(atom_chars(dog, bad), type_error(list,bad))).
iso_review_case(atom_codes_nonlist, iso_review_error(atom_codes(dog, bad), type_error(list,bad))).
iso_review_case(atom_chars_improper, iso_review_error(atom_chars(dog, [d|bad]), type_error(list,[d|bad]))).
iso_review_case(atom_codes_improper, iso_review_error(atom_codes(dog, [100|bad]), type_error(list,[100|bad]))).
iso_review_case(atom_chars_bad_character, iso_review_error(atom_chars(dog, [dog]), type_error(character,dog))).
iso_review_case(atom_codes_bad_integer, iso_review_error(atom_codes(dog, [d]), type_error(integer,d))).
iso_review_case(atom_codes_negative, iso_review_error(atom_codes(dog, [-2]), representation_error(character_code))).
iso_review_case(atom_chars_bad_after_mismatch, iso_review_error(atom_chars(dog, [x,oo]), type_error(character,oo))).
iso_review_case(atom_codes_bad_after_mismatch, iso_review_error(atom_codes(dog, [120,1.5]), type_error(integer,1.5))).
iso_review_case(atom_chars_bad_after_variable, iso_review_error(atom_chars(dog, [_,oo]), type_error(character,oo))).
iso_review_case(atom_codes_bad_after_variable, iso_review_error(atom_codes(dog, [_,-2]), representation_error(character_code))).
iso_review_case(atom_chars_empty_bad_output, iso_review_error(atom_chars('', [long]), type_error(character,long))).
iso_review_case(atom_codes_empty_bad_output, iso_review_error(atom_codes('', [bad]), type_error(integer,bad))).
iso_review_case(atom_chars_unbound_partial, iso_review_error(atom_chars(_, [d|_]), instantiation_error)).
iso_review_case(atom_codes_unbound_partial, iso_review_error(atom_codes(_, [100|_]), instantiation_error)).
iso_review_case(atom_chars_rollback, ((atom_chars(dog, [X,X,_]); X=untouched), X == untouched)).
iso_review_case(atom_codes_rollback, ((atom_codes(dog, [X,X,_]); X=untouched), X == untouched)).

% 8.16.7/8 and Cor.2: numeric lists are parsed, even with a bound number.
iso_review_case(number_chars_alternate, number_chars(42, ['0','4','2'])).
iso_review_case(number_codes_alternate, number_codes(42, [48,52,50])).
iso_review_case(number_chars_float, number_chars(12.0, ['1','2','.','0','0'])).
iso_review_case(number_codes_float, number_codes(12.0, [49,50,46,48,48])).
iso_review_case(number_chars_type_mismatch, \+ number_chars(12, ['1','2','.','0'])).
iso_review_case(number_codes_type_mismatch, \+ number_codes(12, [49,50,46,48])).
iso_review_case(number_chars_partial, (number_chars(42, ['4'|Tail]), Tail == ['2'])).
iso_review_case(number_codes_partial, (number_codes(42, [52|Tail]), Tail == [50])).
iso_review_case(number_chars_bad_element, iso_review_error(number_chars(42, [_,bad]), type_error(character,bad))).
iso_review_case(number_codes_bad_element, iso_review_error(number_codes(42, [_,bad]), type_error(integer,bad))).
iso_review_case(number_codes_bad_code, iso_review_error(number_codes(42, [_,-2]), representation_error(character_code))).
iso_review_case(number_chars_bad_tail, iso_review_error(number_chars(42, ['4'|bad]), type_error(list,['4'|bad]))).
iso_review_case(number_codes_bad_tail, iso_review_error(number_codes(42, [52|bad]), type_error(list,[52|bad]))).

% Cor.2 8.2.4 and 8.5.5: no bindings from subsumption; first-occurrence
% variable order is computed before unification with the output list.
iso_review_case(subsumes_no_binding, (subsumes_term(pair(X,Y), pair(a,b)), var(X), var(Y), X \== Y)).
iso_review_case(subsumes_specific_rigid, (\+ subsumes_term(pair(X,X), pair(A,B)), var(X), A \== B)).
iso_review_case(subsumes_shared_rigid, (\+ subsumes_term(pair(X,Y), pair(Y,a)), var(X), var(Y))).
iso_review_case(subsumes_aliases, (subsumes_term(pair(X,Y), pair(Z,Z)), var(X), var(Y), var(Z), X \== Y)).
iso_review_case(term_variables_order, (T=pair(C,pair(A,C)), term_variables(T, Vs), Vs == [C,A])).
iso_review_case(term_variables_output_alias, (term_variables(pair(A,pair(B,A)), [B|Tail]), A == B, Tail == [B])).
iso_review_case(term_variables_repeat, (term_variables(pair(A,B), V1), term_variables(pair(B,A), V2), V1 == [A,B], V2 == [B,A])).
iso_review_case(term_variables_bad_tail, iso_review_error(term_variables(ground, [a|bad]), type_error(list,[a|bad]))).

% Cor.1 8.8.1 / 8.14.4 and Cor.2 8.4: bindings and complete answer sets.
iso_review_case(clause_fresh_variables, (clause(review_clause(f(X), Y), true), X == Y, var(X))).
iso_review_case(clause_order, (findall(H, clause(review_clause(H,_), true), [f(A),g(B)]), var(A), var(B), A \== B)).
iso_review_case(current_op_bindings, (findall(P-S, current_op(P,S,div), Ops), Ops == [400-yfx])).
iso_review_case(compare_output_alias, (compare(Order, Order, z), Order == (<))).
iso_review_case(sort_output_unification, (sort([X,9], [9,9]), X == 9)).
iso_review_case(sort_types, (sort([1,8.0,a,2.0,1], L), L == [2.0,8.0,1,a])).
iso_review_case(keysort_stability, (keysort([b-1,a-2,b-3,a-2], L), L == [a-2,a-2,b-1,b-3])).
iso_review_case(keysort_output_alias, (keysort([X-a,4-b], [8-a,4-b]), X == 8)).
iso_review_case(keysort_bad_output, iso_review_error(keysort([], [bad]), type_error(pair,bad))).
iso_review_case(keysort_output_variables, (keysort([b-1,a-2], [X|Xs]), X == a-2, Xs == [b-1])).

% Cor.2 7.8.3/9 and 8.15.4/5: goal conversion, caught errors, call/N.
iso_review_case(catch_variable_goal, (catch(_, error(E,_), true), E == instantiation_error)).
iso_review_case(catch_noncallable_goal, (catch(17, error(E,_), true), E == type_error(callable,17))).
iso_review_case(call_append_seven, call(review_seven, a,b,c,d,e,f,g)).
iso_review_case(call_compound_closure, call(review_seven(a,b,c), d,e,f,g)).
iso_review_case(call_nested, (call(call(atom_concat, dot), net, A), A == dotnet)).
iso_review_case(call_disjunction_order, (findall(X, call(';', X=left, X=right), L), L == [left,right])).
iso_review_case(call_condition_commit, \+ call(';', (true->fail), true)).
iso_review_case(call_bad_closure, iso_review_error(call(17, a), type_error(callable,17))).
iso_review_case(false_fails, \+ false).

% Cor.2 8.9.3/5: static errors and preservation of an empty dynamic predicate.
iso_review_case(retract_static, iso_review_error(retract(review_static(_)), permission_error(modify,static_procedure,review_static/1))).
iso_review_case(retractall_static, iso_review_error(retractall(review_static(_)), permission_error(modify,static_procedure,review_static/1))).
iso_review_case(retractall_preserves_dynamic, (assertz(review_dynamic(a)), retractall(review_dynamic(X)), var(X), current_predicate(review_dynamic/1), \+ review_dynamic(_))).

% Cor.2 6.3.4 and 8.14.3: reserved operator names and list-form validation.
iso_review_case(op_empty_curly, iso_review_error(op(650,xfy,[{}]), permission_error(create,operator,{}))).
iso_review_case(op_empty_list, iso_review_error(op(650,xfy,[[]]), permission_error(create,operator,[]))).
iso_review_case(op_bar_low, iso_review_error(op(999,xfy,['|']), permission_error(create,operator,'|'))).
iso_review_case(op_bar_prefix, iso_review_error(op(1150,fy,'|'), permission_error(create,operator,'|'))).
iso_review_case(op_bar_boundary, (op(1001,xfy,'|'), current_op(1001,xfy,'|'), op(0,xfy,'|'), \+ current_op(_,_, '|'))).

% Cor.1 4.1.3.5 / 7.9.2 and Cor.2 clause 9: values, types, and domains.
iso_review_case(sqrt_zero, (X is sqrt(0), X == 0.0)).
iso_review_case(unary_plus_integer, (X is + (3*7), X == 21)).
iso_review_case(unary_plus_float, (X is + 2.5, X == 2.5)).
iso_review_case(div_signs, (A is -8 div 3, B is 8 div -3, C is -8 div -3, [A,B,C] == [-3,-3,2])).
iso_review_case(div_truncate_difference, (A is -8 // 3, B is -8 div 3, [A,B] == [-2,-3])).
iso_review_case(div_zero, iso_review_error(_ is 7 div 0, evaluation_error(zero_divisor))).
iso_review_case(div_float_error, iso_review_error(_ is (1.0+2.0) div 2, type_error(integer,3.0))).
iso_review_case(floor_integer_error, iso_review_error(_ is floor(2+3), type_error(float,5))).
iso_review_case(xor_value, (X is xor(9,5), X == 12)).
iso_review_case(xor_float_error, iso_review_error(_ is xor(2,1.0+2.0), type_error(integer,3.0))).
iso_review_case(bitwise_unevaluable, iso_review_error(_ is unknown_number /\ 3, type_error(evaluable,unknown_number/0))).
iso_review_case(power_zero_zero, (X is 0^0, X == 1)).
iso_review_case(power_integer, (X is 4^3, X == 64)).
iso_review_case(power_float, (X is 4^3.0, X == 64.0)).
iso_review_case(asin_domain, iso_review_error(_ is asin(-2), evaluation_error(undefined))).
iso_review_case(acos_domain, iso_review_error(_ is acos(2), evaluation_error(undefined))).
iso_review_case(atan2_origin, iso_review_error(_ is atan2(0,0), evaluation_error(undefined))).
iso_review_case(atan2_quadrants, (A is atan2(1,-1), B is atan2(-1,-1), A > pi/2, A < pi, B < -pi/2, B > -pi)).
iso_review_case(trig_endpoints, (A is asin(0), B is acos(1), C is tan(0), [A,B,C] == [0.0,0.0,0.0])).

% Cor.1 6.3.7: atom-valued double quotes take part in the grammar.
:- set_prolog_flag(double_quotes, atom).
iso_review_case(double_quoted_infix, (1 "+" 2 * 3 == 1 + 2 * 3)).
iso_review_case(double_quoted_prefix, ("-"7 == -(7))).
iso_review_case(double_quoted_functor, ("pair"(left,right) == pair(left,right))).
iso_review_case(double_quoted_operator_functor, ("+"(4,5) == +(4,5))).


% Part 3 7.13.5 and 8.18.1: nested variables execute after earlier grammar goals.
review_delayed --> {B=[a]}, B.
review_unreachable --> {fail}, _.
review_variable_choice --> {B=[a]}, (B ; [b]).
review_cut --> ([a] ; [b]), !.
iso_review_case(phrase_delayed_variable, phrase(({B=[a]}, B), [a])).
iso_review_case(phrase_delayed_static, phrase(review_delayed, [a])).
iso_review_case(phrase_unreachable_variable, \+ phrase(({fail}, _), [])).
iso_review_case(phrase_unreachable_static, \+ phrase(review_unreachable, [])).
iso_review_case(phrase_unreachable_else, phrase(([] -> [] ; _), [])).
iso_review_case(phrase_variable_disjunction, phrase(({B=[a]}, (B ; [b])), [a])).
iso_review_case(phrase_variable_bar, phrase(({B=[a]}, (B | [b])), [a])).
iso_review_case(phrase_variable_negation, phrase(({B=[a]}, \+ B), [])).
iso_review_case(phrase_variable_condition, phrase(({B=[a]}, (B -> [b] ; [c])), [a,b])).
iso_review_case(phrase_variable_then, phrase(({B=[b]}, ([a] -> B ; [c])), [a,b])).
iso_review_case(phrase_variable_else, phrase(({B=[c]}, ([a] -> [b] ; B)), [c])).
iso_review_case(phrase_variable_rest, (phrase(({B=[a]}, B), [a,b], R), R == [b])).
iso_review_case(phrase_variable_answers, (findall(L, phrase(({B=[a]}, (B ; [b])), L), Ls), Ls == [[a],[b]])).
iso_review_case(phrase_variable_static_answers, (findall(L, phrase(review_variable_choice, L), Ls), Ls == [[a],[b]])).
iso_review_case(phrase_variable_rollback, (findall(B, phrase(({B=[a]}, B ; {B=[b]}, B), [_]), Bs), Bs == [[a],[b]])).
iso_review_case(phrase_variable_error, iso_review_error(phrase(([], _), []), instantiation_error)).
iso_review_case(phrase_top_variable_error, iso_review_error(phrase(_, []), instantiation_error)).
iso_review_case(phrase_variable_callable_error, iso_review_error(phrase(({B=17}, B), []), type_error(callable,17))).
iso_review_case(phrase_cut_answers, (findall(L, phrase((([a] ; [b]), !), L), Ls), Ls == [[a]])).
iso_review_case(phrase_cut_static_answers, (findall(L, phrase(review_cut, L), Ls), Ls == [[a]])).
iso_review_case(phrase_nested_cut_opaque, (findall(X, ((X=left;X=right), phrase(({B= !}, B), [])), Xs), Xs == [left,right])).
iso_review_case(phrase_delayed_exception, (catch(phrase(({B= {throw(ball)}}, B), []), Ball, true), Ball == ball)).

% Part 1 7.8, 8.5.4 and 8.10, reconciled with Annex A's execution model.
iso_review_case(catch_continuation_scope, (catch((catch(true, inner, fail), throw(outer)), E, true), E == outer)).
iso_review_case(catch_redo_scope, (findall(X, catch((X=first;throw(redo)), redo, X=recovered), Xs), Xs == [first,recovered])).
iso_review_case(throw_copies_variables, (catch(throw(pair(X,X)), T, true), T=pair(A,B), A == B, X \== A)).
iso_review_case(copy_term_aliases, (copy_term(pair(X,X), pair(A,B)), A == B, A \== X)).
iso_review_case(findall_fresh_answers, (findall(X, (true;true), [A,B]), var(X), A \== B, A \== X, B \== X)).
iso_review_case(bagof_witness_alias, (bagof(X, (X=Y;X=Y), L), L == [Y,Y], var(Y))).


% Part 1 8.17.2.1: enumeration retains its initial values while effects persist.
iso_review_case(flag_snapshot, (set_prolog_flag(debug,off), findall(V, (current_prolog_flag(F,V), set_prolog_flag(debug,on), F==debug), Vs), Vs==[off], current_prolog_flag(debug,on), set_prolog_flag(debug,off))).
