# Chapter 5 — Recursion

The rules you wrote in chapter 3 all reach a fixed distance. A grandparent rule finds people two
steps apart. If you wanted great-grandparents, you could write a longer rule with three `parent`
goals in it; for great-great-grandparents, four. But what about *ancestors* in general — a parent,
or a grandparent, or a great-grandparent, or anything further up, however far the family stretches?
No rule of fixed length can say that. The rule you need has to reach any distance.

The trick is one of the loveliest ideas in programming: a rule that uses **itself**. It is called
*recursion*, and it is the engine behind almost everything interesting you will write from here on.

## A rule that uses itself

Here is the family database from chapters 2 to 4 again, with two new clauses at the bottom. Save
the whole thing as `ancestor.pl`:

```prolog
:- initialization(main).

parent(pam, bob).
parent(tom, bob).
parent(tom, liz).
parent(bob, ann).
parent(bob, pat).
parent(pat, jim).

female(pam). female(liz). female(ann). female(pat).
male(tom). male(bob). male(jim).

ancestor(X, Z) :- parent(X, Z).
ancestor(X, Z) :- parent(X, Y), ancestor(Y, Z).

main :- forall(ancestor(A, jim), (write(A), nl)).
```

Two clauses define `ancestor/2`, and as chapter 3 taught, two clauses mean *or*. Read them aloud:

- *X is an ancestor of Z if X is a parent of Z.*
- *Or: X is an ancestor of Z if X is a parent of some Y, and Y is an ancestor of Z.*

The second clause mentions `ancestor` while it is busy defining `ancestor`. That is the whole
trick. It is not circular reasoning, though it looks alarmingly like it — read the English again.
Your ancestors are your parents, plus the ancestors of your parents. That is simply true, and it
is a complete definition, because every chain of ancestors eventually ends at a plain parent.

Run it:

```console
dotnet run --project src/DotProlog.Tool -- run ancestor.pl
```

```text
pat
pam
tom
bob
```

Four ancestors of jim, at every distance: pat is his parent, bob his grandparent, pam and tom his
great-grandparents. Two short clauses reached the whole tree.

## Watching it work

How does Prolog get there? Ask a narrower question — is pam an ancestor of jim? — and follow along.
Change `main` to:

```prolog
main :- ancestor(pam, jim), write(yes), nl.
```

Prolog tries the first `ancestor` clause: is there a fact `parent(pam, jim)`? No. So it tries the
second clause: find a Y with `parent(pam, Y)`. The only fact is `parent(pam, bob)`, so Y is bob,
and the remaining goal is `ancestor(bob, jim)`.

Notice what just happened: the question shrank. *Is pam an ancestor of jim?* became *is bob an
ancestor of jim?* — the same kind of question, one generation closer to jim. Prolog now answers it
the same way. `parent(bob, jim)`? No. So: `parent(bob, Y)` gives ann, but ann leads nowhere —
`ancestor(ann, jim)` fails, because ann has no children in our database. Prolog backtracks, exactly
as in chapter 4, and tries bob's other child: pat. `ancestor(pat, jim)`? This time the *first*
clause works — `parent(pat, jim)` is a fact — and the whole chain of pending questions succeeds at
once. Run it and Prolog prints `yes`.

The chain it found was pam → bob → pat → jim: each link a `parent` fact, glued together by a rule
that asked itself ever-smaller questions until one was small enough to answer from a fact.

## The two commandments

Every recursive definition you ever write must obey two rules, and when a recursive program
misbehaves, one of these is what it broke.

**First: have a base case.** A clause that does *not* use the predicate it defines — a way for the
questions to stop. For `ancestor/2` it is the first clause, the plain `parent` one. Without it,
every question would only ever produce another question, and no answer could ever come back.

**Second: make progress towards it.** The recursive clause must ask a question that is *closer to
the base case* than the one it was asked. Our second clause moves one generation down the family
tree before asking again, so the questions cannot go on forever — the tree runs out.

Keep these two in mind as you read the next example, and check them off. It becomes a habit
quickly, and it is the habit that makes recursion safe.

## Another example: train routes

Recursion is not about families. The same shape fits any relation that chains: a small railway
network, for instance. Save this as `routes.pl`:

```prolog
:- initialization(main).

connected(london, paris).
connected(paris, berlin).
connected(paris, zurich).
connected(berlin, warsaw).
connected(zurich, milan).

route(A, B) :- connected(A, B).
route(A, B) :- connected(A, C), route(C, B).

main :- forall(route(london, City), (write(City), nl)).
```

The facts say which cities have a direct line between them, in the direction of travel. The two
`route/2` clauses should look familiar — they are `ancestor/2` wearing a different hat:

- *There is a route from A to B if they are directly connected.*
- *Or: there is a route from A to B if A is directly connected to some C, and there is a route
  from C to B.*

Base case: a direct line. Progress: each recursive question starts one city further along. Both
commandments obeyed. Run it:

```console
dotnet run --project src/DotProlog.Tool -- run routes.pl
```

```text
paris
berlin
zurich
warsaw
milan
```

Every city you can reach from london, at any number of changes. Notice that the *pattern* of the
two clauses is identical to `ancestor/2` — only the names changed. You will meet this pattern so
often it deserves a name; Prolog folk call it *transitive closure*, but you can just think of it
as *reachable by chaining*.

## When recursion goes wrong

Now let us break a commandment on purpose, so you recognise the symptoms. Go back to
`ancestor.pl` and replace the two `ancestor` clauses with these — the same clauses, but with the
recursive one first, and the recursive call moved to the *front* of its body:

```prolog
ancestor(X, Z) :- ancestor(X, Y), parent(Y, Z).
ancestor(X, Z) :- parent(X, Z).
```

As logic, this is still perfectly true: an ancestor of a parent of Z is an ancestor of Z. Prolog
does not object to the file at all. But run `main :- forall(ancestor(A, jim), (write(A), nl)).`
against it and something has gone badly wrong: the program prints nothing, and it never finishes.
We let it run for ten seconds and then had to stop it ourselves — it would have carried on all
day.

Why? Remember from chapter 4 that Prolog works strictly left to right and top to bottom. To answer
`ancestor(A, jim)`, it takes the first clause, whose first goal is `ancestor(A, Y)`. So before
consulting a single `parent` fact, Prolog must first answer… `ancestor` of *something unknown* —
a question exactly as large as the one it started with. And to answer *that*, the first clause
sends it straight back to another `ancestor` question. It never reaches the base case, because it
never reaches `parent` at all. The questions are not shrinking; they are not even changing. That
is the second commandment broken: no progress.

This shape — a clause whose first goal is a recursive call on unchanged arguments — is called
*left recursion*, and it is the classic way recursion fails in Prolog. The fix is what we had in
the first place: do a step of real work first (`parent(X, Y)`), *then* recurse on the smaller
problem. When a program of yours hangs silently, this is the first thing to look for.

!!! note "The order of clauses matters twice over"
    Chapter 4 showed that clause order and goal order decide the order of answers. Recursion
    raises the stakes: now they can decide whether answers arrive *at all*. Base case first,
    recursive case second, real work before the recursive call — that arrangement rarely lets
    you down.

## Trust the recursion

There is a mental habit that makes writing recursive rules easy, and its absence that makes them
feel impossible: **do not try to follow the whole chain in your head.** When you wrote

```prolog
ancestor(X, Z) :- parent(X, Y), ancestor(Y, Z).
```

you did not need to imagine every generation. You needed only two beliefs: that the clause is true
*for one step*, and that `ancestor(Y, Z)` — the smaller question — will be answered correctly.
Grant yourself the second belief. It feels like cheating; it is not. It works for the same reason
the tracing above worked: as long as the base case exists and every step makes progress, the
smaller question really will be answered, by the very rules you are writing.

So the recipe for any recursive predicate is short. Ask: *what is the smallest version of this
problem, and what is its answer?* Write that clause. Then ask: *how do I peel one step off a
bigger problem, leaving a smaller one of the same kind?* Write that clause, and in it, call the
predicate as though it already worked. Then stop. Trust the recursion.

In the next chapter this recipe meets Prolog's favourite data structure — the list — where
*peeling one step off* becomes something you can see.

## Exercises

1. In `ancestor.pl`, change the question to `ancestor(tom, A)` — everyone tom is an ancestor of.
   Predict the answers and their order before you run it, using what chapter 4 taught you about
   search order.
2. Define `descendant(X, Y)`, true when X is a descendant of Y. You can do it in one clause by
   reusing `ancestor/2` — no new recursion needed.
3. In `routes.pl`, change the question to find every city with a route *to* warsaw, rather than
   from london.
4. What does `forall(route(milan, City), (write(City), nl))` print, and does the program still
   succeed? Guess first, then run it. (Look carefully at the facts: does any line leave milan?)
5. Extend the railway: add `connected(warsaw, kyiv).` and `connected(milan, rome).`, then rerun
   the london question. Where do the new cities appear in the output, and why there?
6. A trickier one: add the fact `connected(milan, london).` — a line back to the start, making a
   circle. Run the london question again and describe what happens. Which commandment does the
   *data* now break, even though the rules are unchanged? (Stop the program with Ctrl+C when you
   have seen enough.)

---

Next: [Chapter 6 — Lists](06-lists.md), where recursion pays off: a data structure that is built
one step at a time and taken apart the same way.
