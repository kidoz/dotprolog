# Chapter 3 — Rules

Chapter 2 ended with a question that found Jim's mother without the database ever mentioning
mothers. The question worked, but you had to spell out *parent and female* every time. In this
chapter you will teach Prolog new words — *mother*, *grandparent*, *sibling* — so that it can
reason its way from the facts it has to conclusions you never wrote down. The statements that do
this are called **rules**, and they are the other half of Prolog. Facts and rules together are
the whole language, more or less.

Keep working in `family.pl` from chapter 2 — everything here builds on the same facts.

## Head if body

A rule looks like this:

```prolog
mother(X, Y) :- parent(X, Y), female(X).
```

The `:-` reads as **if**. The part before it is the **head**; the part after it is the
**body**. So, aloud: *X is the mother of Y if X is a parent of Y and X is female*. The comma is
the same *and* you met in chapter 2, and the full stop closes the whole rule, head and body
together.

Where a fact declares something true outright, a rule declares something true *on a condition*.
Prolog can now answer `mother` questions it was never given `mother` facts for: to prove the
head, it proves the body. Add the rule to `family.pl` and ask last chapter's question the short
way:

```text
?- mother(M, jim).
```

```prolog
main :- forall(mother(M, jim), writeln(M)).
```

```text
pat
```

Same answer as before, but the knowledge now lives in the program, not in the question. Every
question you ask from now on can say `mother` and mean it.

Notice that the rule mentions `X` and `Y`, but the question said `M` and `jim`. That is fine.
The variables in a rule are private to that rule — they are the rule's own blanks, and Prolog
matches them up with whatever the question supplies. You will see exactly how in
[chapter 4](04-unification-and-search.md).

## Printing whole answers

Add the obvious companion rule:

```prolog
father(X, Y) :- parent(X, Y), male(X).
```

Now ask for every father-child pair in the family. The question has two blanks, and it would be
good to see them side by side. A neat trick: print the question itself, with its blanks filled
in.

```text
?- father(F, C).
```

```prolog
main :- forall(father(F, C), writeln(father(F, C))).
```

```text
father(tom,bob)
father(tom,liz)
father(bob,ann)
father(bob,pat)
```

Each line is the question with that answer's values in place. The book uses this trick whenever
an answer has more than one part; tidier output is a matter for
[chapter 10](10-words-and-text.md).

## Reaching further: grandparent

Rules can reach across the tree. A grandparent is a parent of a parent:

```prolog
grandparent(G, C) :- parent(G, P), parent(P, C).
```

Aloud: *G is a grandparent of C if G is a parent of some P, and that P is a parent of C*. The
variable `P` appears only in the body — it is a go-between, the person in the middle, and the
question never needs to mention it. Ask for Tom's grandchildren:

```text
?- grandparent(tom, C).
```

```prolog
main :- forall(grandparent(tom, C), writeln(C)).
```

```text
ann
pat
```

Prolog found them by chaining two facts through Bob. You wrote no fact connecting Tom to Ann;
the rule built the connection.

## Siblings, and a bug

Two people are siblings if they share a parent. The rule almost writes itself:

```prolog
sibling(X, Y) :- parent(P, X), parent(P, Y).
```

Ask for every sibling pair:

```prolog
main :- forall(sibling(X, Y), writeln(sibling(X, Y))).
```

```text
sibling(bob,bob)
sibling(bob,bob)
sibling(bob,liz)
sibling(liz,bob)
sibling(liz,liz)
sibling(ann,ann)
sibling(ann,pat)
sibling(pat,ann)
sibling(pat,pat)
sibling(jim,jim)
```

According to this program, everyone is their own sibling — Bob twice over. Look at the rule
again and you can see why: it asks for a `P` who is a parent of `X` and a parent of `Y`, and
nothing stops both blanks being filled by the *same* person. Bob shares a parent with Bob —
twice, in fact, once through Pam and once through Tom. The rule says exactly what you wrote,
not what you meant.

The fix is to add the missing condition — the two people must be different:

```prolog
sibling(X, Y) :- parent(P, X), parent(P, Y), X \= Y.
```

`X \= Y` reads *X and Y must be different*. Chapter 4 explains precisely what it does; for now,
*must be different* is the truth. Run the question again:

```text
sibling(bob,liz)
sibling(liz,bob)
sibling(ann,pat)
sibling(pat,ann)
```

Better. Each pair still appears both ways round — Bob is Liz's sibling and Liz is Bob's — which
is fair, since the relationship is symmetric and the question had two blanks to fill. This kind
of bug, where a rule is *too* general because a condition went unstated, is the most common bug
in Prolog. The cure is always the same: read the rule aloud and ask whether you would accept the
sentence as true.

## Several clauses mean or

A predicate may be defined by more than one rule. Everyone in our database is a person, and
there are two ways to be one:

```prolog
person(X) :- male(X).
person(X) :- female(X).
```

Two rules, same head shape. Together they say: *X is a person if X is male, **or** if X is
female*. Where the comma inside a body means *and*, separate rules for the same predicate mean
*or* — Prolog tries the first, and also the second. Facts and rules for one predicate are
collectively called its **clauses**; our `parent/2` has six clauses that happen to be facts,
and `person/1` has two that happen to be rules.

```text
?- person(X).
```

```prolog
main :- forall(person(X), writeln(X)).
```

```text
tom
bob
jim
pam
liz
ann
pat
```

All seven people — the men first, because the `male` clause is tried first, and within each
clause the answers follow the order of the facts. Order is a story for the next chapter.

## Rules built on rules

The body of a rule can use any predicate, including ones you defined yourself. A grandmother is
a mother of a parent:

```prolog
grandmother(X, Z) :- mother(X, Y), parent(Y, Z).
```

```prolog
main :- forall(grandmother(G, C), writeln(grandmother(G, C))).
```

```text
grandmother(pam,ann)
grandmother(pam,pat)
```

To prove `grandmother`, Prolog needed `mother`; to prove `mother`, it needed `parent` and
`female` — three layers deep, ending at plain facts. All reasoning in Prolog has this shape:
rules lean on rules lean on facts. You define each layer in one honest sentence, and Prolog
does the stacking.

## Reading and writing programs

By now a useful habit should be forming: **read every clause aloud as a sentence**. `:-` is
*if*, a comma is *and*, a fresh clause with the same head is *or*, and the full stop is a full
stop. If the sentence sounds wrong, the clause is wrong — the `sibling` bug was audible long
before it was visible, since *X and Y are siblings if some P is a parent of both* says nothing
about X and Y being different people.

A few layout conventions make programs easier to read aloud. None of them change the meaning —
Prolog does not care about spaces or line breaks, only full stops — but this book follows them,
and so does most Prolog you will meet:

- Keep all clauses of one predicate together, and put a blank line between predicates.
- A short rule fits on one line. When a body grows past that, put the head and `:-` on the
  first line and each goal of the body on its own indented line — like `main` in chapter 1.
- Choose variable names that read well in the sentence: `parent(P, X)` is fine when the rule is
  three words long, but `Child` beats `C` in anything subtle.

## Exercises

1. Define `son(X, Y)` — X is a son of Y — and print all son-of pairs.
2. Define `grandfather/2` two ways: once from `parent` and `male` directly, and once on top of
   `father`. Check they give the same answers.
3. Define `aunt(A, C)`: an aunt is a sibling of a parent, and female. You will need the fixed
   `sibling`. Check every pair it finds against the family tree.
4. Define `brother(X, Y)` and read your rule aloud before running it. Is anyone their own
   brother?
5. Our `sibling` counts half-siblings — one shared parent is enough — and lists Bob and Liz even
   though they share only Tom. Is that what *sibling* should mean? There is no single right
   answer; decide what you think, and write a comment above the rule recording your decision.
   Programs are full of such choices.
6. Add a rule `child(X, Y)` meaning X is a child of Y, and use it to print all of Bob's
   children. Note that you do not need any new facts.

---

Next: [Chapter 4 — Unification and search](04-unification-and-search.md), where you find out
what Prolog is actually doing when it fills in a blank — and why answers come out in the order
they do.
