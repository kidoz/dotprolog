# Chapter 8 — Making decisions

Everything you have written so far treats all answers equally: a rule describes what is true, and
Prolog finds every way to make it true. Real programs also need to *choose* — do this if that
holds, otherwise do something else; stop after the first answer; give up politely when something
goes wrong. This chapter is about those choices: *or*, if-then-else, negation, the cut, and
errors you can catch.

The examples use the family database from chapter 2. Keep those facts at the top of each program
in this chapter:

```prolog
parent(pam, bob).
parent(tom, bob).
parent(tom, liz).
parent(bob, ann).
parent(bob, pat).
parent(pat, jim).

female(pam).
female(liz).
female(ann).
female(pat).
male(tom).
male(bob).
male(jim).
```

## Saying *or*

You have been saying *or* since chapter 3 without noticing. Two rules with the same head mean
*either will do*:

```prolog
person(X) :- male(X).
person(X) :- female(X).
```

Someone is a person if they are male *or* female. Add these two rules to the family database with
`main :- forall(person(P), (write(P), nl)).` and every family member appears:

```text
tom
bob
jim
pam
liz
ann
pat
```

Prolog also has an *or* you can write inside a single rule: the semicolon, `;`. Here is a small
weather program:

```prolog
:- initialization(main).

today(cloudy).

can_see_sky :- today(sunny) ; today(cloudy).

main :-
    can_see_sky,
    write('we can see the sky'), nl.
```

```text
we can see the sky
```

Read `;` as *or*: we can see the sky if today is sunny or today is cloudy. Prolog tries the left
side first; only when it fails does it try the right. Since `today(sunny)` fails and
`today(cloudy)` succeeds, `can_see_sky` succeeds.

A word of style: when a predicate simply lists alternatives, separate clauses — like `person/1`
above — usually read better than one clause full of semicolons. Where the semicolon truly earns
its keep is in what comes next.

## If, then, else

Often you want *or* with a decision attached: if this condition holds do one thing, otherwise do
the other. Prolog writes that with an arrow inside brackets:

```prolog
:- initialization(main).

sign(N, S) :-
    ( N < 0 ->
        S = negative
    ;
        S = 'zero or positive'
    ).

main :-
    sign(-7, A), write(A), nl,
    sign(3, B), write(B), nl.
```

```text
negative
zero or positive
```

The shape is `( Condition -> Then ; Else )`. Prolog tries the condition. If it succeeds, only the
*then* part runs; if it fails, only the *else* part runs. One or the other, never both — and
unlike a plain `;`, Prolog will not come back later to try the branch it skipped. The brackets
are part of the idiom: always write them.

## Chains of conditions

Conditions chain naturally. Suppose a loyalty scheme sorts customers by points:

```prolog
:- initialization(main).

tier(Score, T) :-
    ( Score >= 1000 -> T = gold
    ; Score >= 500  -> T = silver
    ; T = bronze
    ).

main :-
    tier(1200, A), write(A), nl,
    tier(700, B), write(B), nl,
    tier(80, C), write(C), nl.
```

```text
gold
silver
bronze
```

Read it top to bottom, like a list of cases: 1000 or more is gold; otherwise 500 or more is
silver; otherwise bronze. The first condition that succeeds wins, and the last line — the
catch-all — has no arrow at all. This chain is one of the most useful shapes in Prolog, and you
will see it again in the project chapter.

## What cannot be proved: `\+`

Back in chapter 2 you met Prolog's closed world: if a fact is not in the database and no rule can
derive it, Prolog answers *false*. Not *unknown* — false. The operator `\+` turns that idea into
something you can use inside a rule: `\+ Goal` succeeds exactly when `Goal` cannot be proved.
Read it as *there is no way to show that…*

Jim has no children in our database, and `\+` can say so:

```prolog
main :-
    ( \+ parent(jim, _) ->
        write('jim has no children')
    ;
        write('jim has children')
    ), nl.
```

```text
jim has no children
```

Now a warning, and it is worth a whole paragraph: **`\+` never binds a variable.** You might hope
to find someone who is *not* a parent of bob by asking `\+ parent(X, bob)`:

```prolog
main :-
    ( \+ parent(X, bob) -> write(X) ; write('no answer') ), nl.
```

```text
no answer
```

Why? `X` is unbound, so `parent(X, bob)` *can* be proved — pam is a parent of bob — and
therefore `\+ parent(X, bob)` fails. `\+` only ever asks *can this be proved or not*; it proves
nothing itself and so has no bindings to hand back. It is a test, not a question.

The fix is a pattern you will use constantly: *generate, then test*. First name a candidate with
an ordinary goal, then use `\+` to filter:

```prolog
childless(X) :- person(X), \+ parent(X, _).
```

Here `person(X)` proposes each family member in turn, and `\+ parent(X, _)` keeps only those with
no children. Check a couple of people:

```prolog
main :-
    ( childless(jim) -> write('jim is childless') ; write('jim has a child') ), nl,
    ( childless(bob) -> write('bob is childless') ; write('bob has a child') ), nl.
```

```text
jim is childless
bob has a child
```

Collecting *everyone* who is childless into one list is a job for the next chapter.

## Just one answer: `once/1`

Sometimes any single answer will do. The query `parent(P, bob)` has two answers, pam and tom;
wrapping it in `once/1` takes the first and closes the door on the rest:

```prolog
main :-
    once(parent(P, bob)),
    write(P), nl.
```

```text
pam
```

Use `once` when a rule only needs *some* answer — a proof that a solution exists — and extra
answers on backtracking would be a nuisance rather than a feature.

## The cut

Prolog has an older, sharper tool for cutting off alternatives: the cut, written `!`. As a goal
it always succeeds — and as a side effect it *commits* to the choices made so far in the current
clause. After a cut, Prolog will not try other clauses of this predicate, nor other answers for
the goals to the cut's left.

The classic example is finding the larger of two numbers:

```prolog
:- initialization(main).

max(X, Y, X) :- X >= Y, !.
max(_, Y, Y).

main :-
    max(3, 7, A), write(A), nl,
    max(9, 2, B), write(B), nl.
```

```text
7
9
```

The first clause says: if `X` is at least `Y`, the answer is `X` — and the `!` adds *and stop
looking*. Without the cut, the two clauses would both apply: asking for every answer to
`max(9, 2, M)` would give `9` and then, absurdly, `2`, because the second clause has no idea the
first one succeeded. The cut makes the second clause mean *otherwise*.

The cut is powerful and famously easy to misuse — placed carelessly it silently throws away
answers you wanted. The honest advice for now: when you mean *if-then-else, say if-then-else*.
This version of `max` behaves the same and wears its logic on its sleeve:

```prolog
max(X, Y, M) :- ( X >= Y -> M = X ; M = Y ).
```

You will meet cuts in other people's code, so you need to read them; you will rarely need to
write one in this book.

## When things go wrong on purpose

In chapter 1, exercise 3, you misspelled `write` as `wrote` and met this:

```text
error: existence_error(procedure, wrote/1)
```

The program stopped. But here is the good news this section is about: an error in Prolog is not
necessarily the end. Errors are thrown, and anything thrown can be caught.

Two predicates do all the work. `throw(Term)` abandons what the program was doing and throws
`Term` outward. `catch(Goal, Pattern, Recovery)` runs `Goal`; if something is thrown while it
runs and it matches `Pattern`, the `Recovery` goal runs instead of the program stopping.

You can throw anything you like:

```prolog
:- initialization(main).

bake(_) :-
    throw(oven_too_hot).

main :-
    catch(bake(cake), oven_too_hot, (write('turn the oven down'), nl)).
```

```text
turn the oven down
```

The errors Prolog itself throws all share one shape, fixed by the ISO standard:
`error(WhatWentWrong, Context)`. The first argument says what happened —
`existence_error(procedure, wrote/1)` in the message above — and the second holds extra context
you can usually ignore. Knowing the shape, you can catch chapter 1's error and carry on:

```prolog
:- initialization(main).

main :-
    catch(wrote(hello),
          error(existence_error(procedure, What), _),
          (write('missing predicate: '), write(What), nl)),
    write('still running'), nl.
```

```text
missing predicate: wrote/1
still running
```

Notice that `What` in the pattern is a variable: catching unifies the thrown term with the
pattern, so the pattern tells you *which* predicate was missing.

Arithmetic gives the other error every beginner meets. Dividing by zero:

```prolog
main :-
    X is 1 / 0,
    write(X), nl.
```

```text
error: evaluation_error(zero_divisor)
```

Wrapped in `catch`, division becomes safe:

```prolog
:- initialization(main).

safe_divide(X, Y, R) :-
    catch(R is X / Y, error(evaluation_error(zero_divisor), _), R = undefined).

main :-
    safe_divide(10, 4, A), write(A), nl,
    safe_divide(10, 0, B), write(B), nl.
```

```text
2.5
undefined
```

When the division works, `catch` is invisible. When it throws, the recovery goal `R = undefined`
runs, and the caller gets the atom `undefined` instead of a crash. Catch errors where you can do
something sensible about them; everywhere else, let them fly — the message with the file and the
error term is exactly what you want when debugging.

!!! note "Failure and errors are different things"
    A goal that *fails* is normal Prolog life: it just means *not provable*, and backtracking
    carries on. An *error* is thrown past all of that, abandoning goals until a `catch` stops it.
    `\+` handles failure; `catch` handles errors. Keeping the two ideas apart will save you real
    confusion.

## Exercises

1. Extend `tier/2` with a `platinum` tier for scores of 2000 and above, and check that scores of
   2500, 1200, 700, and 80 land in the right tiers.
2. Write `abs_value(N, A)` using if-then-else, so that `A` is `N` without its minus sign:
   `abs_value(-5, A)` should give `5` and `abs_value(3, A)` should give `3`.
3. Write `has_son(X)`, true when `X` has a male child. Then, using `\+` and the
   generate-then-test pattern, write `sonless(X)`, true when `X` is a person with no sons. Check
   tom and bob individually, the way this chapter checked `childless`.
4. Write `min/3` in the style of `max/3`, first with a cut and then with if-then-else. Convince
   yourself both give one answer for `min(4, 9, M)`.
5. Write `ticket(Age)` that writes `child` for ages under 13 and `adult` otherwise — but throws
   the term `bad_age` if `Age` is negative. In `main`, call it inside `catch` so that
   `ticket(-3)` prints a polite complaint instead of stopping the program.
6. What does `once(parent(tom, C))` bind `C` to? Guess from the order of the facts, then check.

---

Next: [Chapter 9 — Collecting answers](09-collecting-answers.md), where whole sets of answers
land in a single list — and programs finally get a memory.
