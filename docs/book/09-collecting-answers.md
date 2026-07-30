# Chapter 9 — Collecting answers

Backtracking hands you answers one at a time: ask `parent(bob, C)` and Prolog offers `ann`, then
`pat`, then stops. That is fine for printing, but you cannot *do* anything with answers that
arrive and vanish one by one. How many are there? What is their total? Which is largest? For
that, you want all the answers in one place — in a list, where chapter 6's toolkit is waiting.

This chapter is about gathering answers, and then about its mirror image: teaching a running
program to remember new facts and forget old ones.

As before, the family database from chapter 2 sits at the top of each program, and this chapter
extends it with everyone's age:

```prolog
parent(pam, bob).
parent(tom, bob).
parent(tom, liz).
parent(bob, ann).
parent(bob, pat).
parent(pat, jim).

age(pam, 72).
age(tom, 78).
age(bob, 50).
age(liz, 48).
age(ann, 25).
age(pat, 27).
age(jim, 2).
```

## All the answers at once: `findall/3`

`findall(Template, Goal, List)` runs `Goal` every way it can and collects one copy of `Template`
for each success into `List`. Who are all of bob's children?

```prolog
:- initialization(main).

main :-
    findall(C, parent(bob, C), Children),
    write(Children), nl.
```

```text
[ann,pat]
```

Read the three arguments left to right: *what to collect*, *how to find it*, *where to put it*.
Here the template is just the variable `C`, so the list holds each child in turn, in the order
backtracking found them.

The goal can be as elaborate as any rule body. Everyone under thirty:

```prolog
main :-
    findall(P, (age(P, A), A < 30), Young),
    write(Young), nl.
```

```text
[ann,pat,jim]
```

Two things happened there. The goal was a conjunction in brackets — find an age, check it — and
only the successes contributed to the list. And the template stayed a bare variable, but it need
not: later in this chapter a template builds a whole pair per answer.

A goal with no solutions is not an error — you simply get the empty list:

```prolog
main :-
    findall(C, parent(jim, C), L),
    write(L), nl.
```

```text
[]
```

Remember the question chapter 8 postponed — *who are all the childless people?* With `person/1`
and `childless/1` defined as they were there, it is one line now:

```prolog
main :-
    findall(P, childless(P), L),
    write(L), nl.
```

```text
[jim,liz,ann]
```

## Counting and totalling

Once the answers are in a list, everything you learned in chapters 6 and 7 applies. How many
children does tom have? Collect, then measure:

```prolog
main :-
    findall(C, parent(tom, C), Cs),
    length(Cs, N),
    write(N), nl.
```

```text
2
```

What is the family's combined age, and who is oldest? Collect, then `sum_list` and `max_list`:

```prolog
main :-
    findall(A, age(_, A), Ages),
    write(Ages), nl,
    sum_list(Ages, Total),
    write(Total), nl,
    max_list(Ages, Oldest),
    write(Oldest), nl.
```

```text
[72,78,50,48,25,27,2]
302
78
```

This two-step shape — `findall`, then a list predicate — answers a huge range of questions. It
is worth pausing to appreciate: the database knows nothing about totals, and `sum_list` knows
nothing about families, yet gluing them together takes one line.

## Tidier: `aggregate_all/3`

Counting and totalling are so common that Prolog offers a shortcut which skips the visible list.
`aggregate_all(Spec, Goal, Result)` runs the goal and combines the answers according to the
spec — `count`, `sum(X)`, `max(X)`, or `min(X)`:

```prolog
main :-
    aggregate_all(count, parent(_, _), N), write(N), nl,
    aggregate_all(sum(A), age(_, A), Total), write(Total), nl,
    aggregate_all(max(A), age(_, A), Max), write(Max), nl,
    aggregate_all(min(A), age(_, A), Min), write(Min), nl.
```

```text
6
302
78
2
```

Six parent facts, a combined age of 302, an oldest of 78, a youngest of 2. Use whichever reads
better: `findall` plus a list predicate when you want the list too, `aggregate_all` when you only
want the number.

## Putting answers in order

Two close cousins sort a list. `msort/2` sorts; `sort/2` sorts *and removes duplicates*:

```prolog
main :-
    msort([pear, apple, plum, apple], M), write(M), nl,
    sort([pear, apple, plum, apple], S), write(S), nl.
```

```text
[apple,apple,pear,plum]
[apple,pear,plum]
```

Watch that difference — reaching for `sort` when you meant `msort` silently loses repeated
answers, which is either exactly what you want or a baffling bug.

For sorting one thing *by* another, remember the pairs of chapter 6: `Key-Value` terms like
`pam-72`. `keysort/2` sorts a list of pairs by their keys, so to order the family by age you make
age the key:

```prolog
main :-
    findall(A-Name, age(Name, A), Pairs),
    keysort(Pairs, ByAge),
    write(ByAge), nl.
```

```text
[2-jim,25-ann,27-pat,48-liz,50-bob,72-pam,78-tom]
```

The template `A-Name` builds a pair per answer; `keysort` does the rest. Youngest to oldest, in
one breath.

!!! note "bagof and setof"
    Prolog has two more collectors, `bagof/3` and `setof/3`. They differ from `findall` in two
    ways: they *fail* rather than return `[]` when there are no solutions, and they group
    answers by any variables of the goal you leave free — which is genuinely useful and
    genuinely confusing on a first meeting. This book sticks to `findall` and `aggregate_all`;
    when you want the grouping behaviour, the [language guide](../language-guide.md) shows how
    `bagof` and `setof` carve up their solutions.

## `forall/2`, properly at last

Since chapter 2 you have been printing answers with a spell:

```prolog
main :- forall(parent(P, bob), (write(P), nl)).
```

Time to explain it. `forall(Goal, Action)` succeeds when *every* solution of `Goal` makes
`Action` succeed. In the spell, the action — write and new line — always succeeds, so the effect
is *for each answer, print it*. But `forall` is really a checker, and it shines as one:

```prolog
main :-
    forall(parent(P, bob), (write(P), nl)),
    ( forall(member(X, [2, 4, 6]), 0 =:= X mod 2)
    -> write('all even') ; write('not all even') ), nl,
    ( forall(member(Y, [2, 3, 6]), 0 =:= Y mod 2)
    -> write('all even') ; write('not all even') ), nl.
```

```text
pam
tom
all even
not all even
```

Note what `forall` does *not* do: it collects nothing and binds nothing you can use afterwards —
each solution is checked and let go. Collecting is `findall`'s job; checking every case, or doing
something once per case, is `forall`'s.

## Remembering things

So far the database is frozen at the moment the file loads. But programs often need to remember:
a game needs to know what you have picked up, a shop what is in the basket. Prolog lets a running
program add and remove facts — with four predicates and one declaration.

The declaration is `:- dynamic task/1.` — it announces that the predicate `task/1` is allowed to
change while the program runs. Then `assertz(Fact)` adds a fact at the end of the predicate —
the *z* is the mnemonic, and its sibling `asserta/1` adds at the beginning — `retract(Fact)`
removes the first matching fact, and `retractall(Fact)` removes every match.

Here is a small to-do list, complete:

```prolog
:- initialization(main).
:- dynamic task/1.

add(Task) :-
    assertz(task(Task)),
    write('added: '), write(Task), nl.

finish(Task) :-
    retract(task(Task)),
    write('finished: '), write(Task), nl.

show :-
    write('--- to do ---'), nl,
    forall(task(T), (write(T), nl)).

main :-
    add('feed the cat'),
    add('water the plants'),
    add('write to grandma'),
    show,
    finish('feed the cat'),
    show,
    retractall(task(_)),
    show.
```

```text
added: feed the cat
added: water the plants
added: write to grandma
--- to do ---
feed the cat
water the plants
write to grandma
finished: feed the cat
--- to do ---
water the plants
write to grandma
--- to do ---
```

Everything in it is familiar: `task/1` facts are queried with `forall` exactly like `parent/2`
facts — the only novelty is that the facts were not in the file; the program made them up as it
ran.

Two details worth knowing. First, `retract` *fails* if nothing matches, so a careful `finish`
would say so instead:

```prolog
main :-
    assertz(task(one)),
    ( retract(task(two)) -> write(removed) ; write('not on the list') ), nl.
```

```text
not on the list
```

Second — and this surprises everyone once — nothing persists. Every run starts from the facts in
the file; `assertz` writes to the program's memory, not to disk. When the program ends, the
remembered facts are gone. That is exactly right for a game or a basket, and chapter 11 builds a
whole text adventure on this machinery.

## Exercises

1. Using `findall`, collect all of tom's grandchildren into a list. (You wrote a `grandparent`
   rule in chapter 3; a fresh `parent(tom, C), parent(C, G)` goal works too.)
2. Use `aggregate_all` to count how many people in the family are female.
3. Compute the average family age: total the ages, count the people, and divide with `/`. Chapter
   7 tells you what to expect from the division.
4. Collect all children of anyone — `parent(_, C)` — with `findall`, and look at the result. Bob
   appears twice. Which of `sort/2` and `msort/2` fixes that, and why?
5. Extend the to-do program with `count/0`, which prints how many tasks remain, using
   `aggregate_all`.
6. Change the to-do program to store `task(Priority, Name)` with a number for the priority. Make
   `show` print tasks most-urgent-first: collect `Priority-Name` pairs with `findall`, then
   `keysort`.

---

Next: [Chapter 10 — Words and text](10-words-and-text.md), where atoms come apart into letters,
printing gets tidy, and your programs finally listen as well as speak.
