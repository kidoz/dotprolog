# Chapter 4 — Unification and search

Two chapters of questions and rules have leant on two ideas this book has so far waved past:
what it means for a question to *match* a fact, and the order in which Prolog hunts for
answers. This chapter opens the bonnet. Nothing here adds new powers — but after it, Prolog
stops feeling like magic and starts feeling like a machine you can predict.

## Matching, not assignment

Meet the humblest-looking symbol in Prolog:

```prolog
:- initialization(main).

main :- X = tom, write(X), nl.
```

```text
tom
```

If you know another programming language, `X = tom` looks like assignment — *put `tom` in the
box called X*. It is not. It is a question, like every other goal in Prolog: *can `X` and `tom`
be made the same?* Since `X` is a blank, the answer is yes — by making `X` stand for `tom`.
This making-the-same is called **unification**, and it is what happens every single time a
question meets a fact or a rule head. When it succeeds, a variable that received a value is
said to be **bound**.

Because it is a question, it can fail. Two different atoms cannot be made the same:

```prolog
main :- tom = bob, writeln(matched).
```

```text
Warning: initialization goal failed.
```

`tom = bob` failed, `main` failed with it, and the `writeln` never ran — the same *no* you met
in chapter 2.

## Both sides at once

Assignment in other languages has a direction: value on the right, box on the left. Unification
has none. Both sides may contain blanks, and both sides can receive:

```prolog
main :-
    point(X, 2) = point(1, Y),
    write(X), nl,
    write(Y), nl.
```

```text
1
2
```

`point(X, 2)` and `point(1, Y)` are **compound terms** — structures with a name and parts,
built exactly like the facts you have been writing. Two compound terms unify if they have the
same name, the same arity, and each pair of corresponding parts unifies. Here that means
`X = 1` and `2 = Y`: one blank on the left got its value from the right, and one on the right
got its value from the left, in a single step. Change the `2` to a `3` on one side and the
whole unification fails — one mismatched part is enough.

Two blanks can even unify with each other:

```prolog
main :- X = Y, Y = liz, write(X), nl.
```

```text
liz
```

After `X = Y`, neither has a value, but they are now the *same* blank — so when `Y` later
becomes `liz`, `X` already is. A variable is not a box being filled and refilled; it is a
placeholder that, at most once, becomes something.

This is exactly what connects a question to a rule. When chapter 3's question `mother(M, jim)`
met the head `mother(X, Y)`, Prolog unified them: `M = X`, `Y = jim`. That is why the rule's
private names never mattered — unification stitched them to yours.

## Must be different

`\=` is the opposite question: *is it true that these two can **not** be made the same?*

```prolog
main :- tom \= bob, writeln(different).
```

```text
different
```

`tom \= bob` succeeds precisely because `tom = bob` would fail. This is the *must be different*
from chapter 3's `sibling` fix, now with its full meaning: `X \= Y` succeeds when `X` and `Y`
cannot be unified. By the time `sibling` reaches that goal, `X` and `Y` are already bound to
particular people, so it simply asks whether they are different people.

## How Prolog searches

Unification says whether one question can match one clause. A program has many clauses, and a
question has many goals — so Prolog needs a marching order. It has exactly two habits, and
knowing them lets you predict everything it does:

- **Clauses are tried top to bottom**, in the order they appear in the file.
- **Goals are tried left to right**, in the order you wrote them.

And one manoeuvre: when a goal fails — or when you ask for more answers — Prolog goes *back* to
the most recent goal that succeeded and asks it for a different answer. This is called
**backtracking**.

Watch it happen. Back in `family.pl`, ask for Bob's daughters, as in chapter 2:

```prolog
main :- forall((parent(bob, X), female(X)), writeln(X)).
```

```text
ann
pat
```

Here is the search behind those two lines, written as a trace — indentation shows Prolog
working on a sub-question. Simplified, but honest:

```text
parent(bob, X), female(X)

parent(bob, X)?
   try parent(pam, bob).   no — bob does not unify with pam
   try parent(tom, bob).   no
   try parent(tom, liz).   no
   try parent(bob, ann).   yes — X = ann
      female(ann)?
         try female(pam).  no
         try female(liz).  no
         try female(ann).  yes
      answer: X = ann
   more answers wanted — back to parent(bob, X)
   try parent(bob, pat).   yes — X = pat
      female(pat)?         yes, eventually
      answer: X = pat
   more answers wanted — back to parent(bob, X)
   try parent(pat, jim).   no
   no clauses left — the search is over
```

Every question in this book so far has been answered by exactly this procedure: march down the
clauses, march across the goals, and when stuck or asked for more, back up and try the next
thing. It is thorough, it is mechanical, and it never forgets where it was.

## Order matters

Since the search follows the file, changing the file changes the search. Reorder the first two
facts of `family.pl` so that Tom comes first:

```prolog
parent(tom, bob).
parent(pam, bob).
```

and ask again who Bob's parents are:

```prolog
main :- forall(parent(P, bob), writeln(P)).
```

```text
tom
pam
```

Chapter 2 got `pam` first. Same answers, different order — clause order decides the order
answers arrive. (Put the file back the way it was before going on.)

Goal order inside a rule matters too. Take chapter 3's `grandparent`, and ask for every pair:

```prolog
grandparent(G, C) :- parent(G, P), parent(P, C).

main :- forall(grandparent(G, C), writeln(grandparent(G, C))).
```

```text
grandparent(pam,ann)
grandparent(pam,pat)
grandparent(tom,ann)
grandparent(tom,pat)
grandparent(bob,jim)
```

Now swap the two goals in the body — the rule still says the same true thing, only back to
front:

```prolog
grandparent(G, C) :- parent(P, C), parent(G, P).
```

```text
grandparent(pam,ann)
grandparent(tom,ann)
grandparent(pam,pat)
grandparent(tom,pat)
grandparent(bob,jim)
```

The same five grandparent-of pairs, in a different order. The first version walks grandparents
first — all of Pam's grandchildren, then Tom's. The second walks grandchildren first — all of
Ann's grandparents, then Pat's. Both rules are equally *true*; they describe different
*searches*. For small programs the difference is only cosmetic. Later, when questions grow
expensive, goal order becomes a matter of putting the most restrictive question first — and in
the next chapter it can decide whether a search finishes at all.

## The nameless variable

Sometimes a blank is genuinely none of your business. To ask *who is a parent?* — of anyone,
never mind whom — use the **anonymous variable**, written as a single underscore:

```text
?- parent(X, _).
```

```prolog
main :- forall(parent(X, _), writeln(X)).
```

```text
pam
tom
tom
bob
bob
pat
```

The `_` means *someone, and I am not even giving them a name*. Note the repeats: Tom and Bob
each appear once per child, because the question has one answer per matching fact, and the
trace-style search reports each one. Prolog is answering exactly what was asked. (A tidy list
without repeats is a job for [chapter 9](09-collecting-answers.md).) Each `_` you write is a
fresh blank — `parent(_, _)` does not require the same person twice, which is precisely why
`_` is the right way to say *don't care*.

The underscore also earns its keep inside rules. Suppose you write:

```prolog
is_a_parent(X) :- parent(X, Y).
```

It works — but read it with a critical eye. `Y` is given a name and then never used again. A
variable that appears exactly once in a clause is called a **singleton**, and it is usually a
typo: a misspelt name that was *meant* to match another variable will silently become a
singleton, and the rule will quietly mean the wrong thing. Many Prolog systems print a warning
for named singletons; DotProlog currently runs the clause without comment, which makes the
discipline yours to keep. The convention: if you mean *don't care*, say so.

```prolog
is_a_parent(X) :- parent(X, _).
```

Now anyone reading the rule — including you, next month — knows the second blank is
intentionally ignored.

## Where this leads

Look once more at `grandparent`, and imagine going further up the tree. A great-grandparent is
a parent of a grandparent; a great-great-grandparent is a parent of that. You could keep
writing rules — one more `parent` goal each time — but the family tree does not come with a
height limit, and you cannot write infinitely many rules. What you want is one rule that says:
your *ancestor* is your parent, or a parent of your ancestor. A rule that leans on itself. That
is recursion, it is the single most important idea in this book, and it is next.

## Exercises

1. For each of these, decide whether the unification succeeds, and what the variables become if
   so — then check each one by putting it in a `main` that prints the variables:
   `a = a`, `f(X) = f(3)`, `f(X, 1) = f(2, Y)`, `point(1, 2) = point(X, 3)`.
2. Reorder the four `female` facts in `family.pl` and predict what the *Bob's daughters* query
   from this chapter will print, and in what order, before running it. Restore the file after.
3. Swap the two goals in the body of `mother/2` from chapter 3. Does `mother(M, jim)` still
   find Pat? Sketch the trace for the swapped version by hand, in the style of this chapter,
   then explain in one sentence why the answer had to be the same.
4. Using `_`, print everyone who *has* a parent. Which family member is missing from the
   output, and why?
5. In the trace for Bob's daughters, count how many unifications were attempted in total,
   including the failed ones. Machines are patient.

---

Next: [Chapter 5 — Recursion](05-recursion.md), where a rule uses itself, and six facts about
parents become answers about ancestors any number of generations away.
