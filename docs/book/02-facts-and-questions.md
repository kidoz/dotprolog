# Chapter 2 — Facts and questions

In this chapter you will teach Prolog its first facts and ask it your first questions. By the
end you will have a small database describing a family, and you will be able to ask it who is
whose parent — including questions you never wrote the answers to.

## A statement of truth

A Prolog program is, at heart, a list of statements that you declare to be true. Each statement
is called a **fact**, and it looks like this:

```prolog
parent(tom, bob).
```

Read it aloud: *tom is a parent of bob*. The word before the brackets, `parent`, names a
relationship. The words inside, `tom` and `bob`, name the people it relates. The full stop ends
the statement, exactly as it ends this sentence — forget it and Prolog will complain, as you saw
in chapter 1.

Names that begin with a lowercase letter, like `tom` and `parent`, are called **atoms**. An atom
names one particular thing: this person, this relationship. Names that begin with a capital
letter, like `X` or `Who`, are **variables** — blanks that Prolog can fill in. Keep that
distinction in mind; it matters more in Prolog than in most languages, and
[chapter 4](04-unification-and-search.md) looks at it closely. For now: lowercase means *this
exact thing*, capitalised means *something, to be determined*.

Notice what a fact does not say. `parent(tom, bob).` does not say how to compute anything, or
what to do, or when. It simply states that something is true. Programs in this book are built
almost entirely out of such statements.

## A family

Here is the database this book will use for the next few chapters. Create a file called
`family.pl` with exactly this content:

```prolog
% family.pl — a small family database

parent(pam, bob).
parent(tom, bob).
parent(tom, liz).
parent(bob, ann).
parent(bob, pat).
parent(pat, jim).

female(pam). female(liz). female(ann). female(pat).
male(tom). male(bob). male(jim).
```

The first line is a **comment**: everything from a `%` to the end of the line is for human
readers, and Prolog ignores it. Use comments to leave notes to your future self.

The facts describe this family tree:

```text
   pam     tom
     \     / \
      \   /   \
       bob    liz
       / \
      /   \
    ann   pat
           |
          jim
```

Pam and Tom are Bob's parents; Tom is also Liz's father. Bob has two daughters, Ann and Pat, and
Pat has a son, Jim. The `female` and `male` facts record who is who. That is everything Prolog
will know about this family — no more and, as you will see at the end of this chapter, no less.

Three relationships appear here: `parent`, `female`, and `male`. Prolog books have a compact way
of naming them: `parent/2`, `female/1`, `male/1`. The number after the slash is the **arity** —
how many things the relationship connects. `parent/2` relates two people; `female/1` describes
one. The name-and-arity pair is how Prolog itself identifies a relationship, and it is how error
messages will refer to them, so the notation is worth learning early. A relationship named this
way is called a **predicate**.

## Asking yes or no

A database is only interesting once you can question it. The simplest question presents a
complete statement and asks: *is this true?* In the `?-` notation from chapter 1:

```text
?- parent(tom, liz).
```

To run it, add this to the end of `family.pl`:

```prolog
:- initialization(main).

main :- parent(tom, liz), write(yes), nl.
```

Then run the file as always:

```console
dotnet run --project src/DotProlog.Tool -- run family.pl
```

```text
yes
```

Read `main` aloud: *main succeeds if tom is a parent of liz — and then write `yes`*. Prolog
looked through your facts, found `parent(tom, liz).`, and so the question succeeded and the
`write` ran.

Now ask something false. Change the question round to `parent(liz, tom)` — *is liz a parent of
tom?* — and run again:

```text
Warning: initialization goal failed.
```

No `yes` this time. Prolog searched the facts, found nothing saying liz is a parent of tom, and
the question **failed** — so `main` never reached its `write`. The warning is DotProlog telling
you that `main` as a whole could not be proved. In a Prolog conversation, failure is not an
error; it is simply the answer *no*.

## Questions with a blank to fill

Yes-or-no questions only confirm what you already suspect. The real power arrives when you put a
variable — a capitalised blank — into the question:

```text
?- parent(tom, C).
```

*Tom is a parent of — whom?* Prolog does not just answer yes; it fills in the blank. Put the
question into `main` and print the result:

```prolog
main :- parent(tom, C), write(C), nl.
```

```text
bob
```

Prolog searched top to bottom, found `parent(tom, bob).`, and made `C` stand for `bob`. But
look at the tree: Tom has two children. Where is Liz? The answer is that `main` only asked for
*an* answer. As soon as one was found, the `write` ran and `main` finished. The other answer
exists — you just have not asked to see it.

## All the answers

Here is the shape this book uses whenever it wants every answer to a question:

```prolog
main :- forall(parent(P, bob), (write(P), nl)).
```

Read it as: *for every answer to `parent(P, bob)`, write it and take a new line*. Run it:

```text
pam
tom
```

Both of Bob's parents, one per line, in the order their facts appear in the file. `forall` takes
two things: a question, and something to do for each answer. There is more to say about it, and
[chapter 9](09-collecting-answers.md) says it; until then, *for every answer, print it* is all
you need.

Writing `write` followed by `nl` is so common that Prolog offers `writeln`, which does both in
one step. The book uses whichever reads better in each example; with `writeln` the idiom becomes:

```prolog
main :- forall(parent(P, bob), writeln(P)).
```

```text
pam
tom
```

Try it on another question — *who are Tom's children?*

```text
?- parent(tom, C).
```

```prolog
main :- forall(parent(tom, C), writeln(C)).
```

```text
bob
liz
```

There is Liz. Same question as before; this time you asked for everything.

## Asking two things at once

Questions can have more than one part, joined by a comma. The comma reads as *and*, just as it
did inside `main`. Suppose you want Bob's daughters — the people who have Bob as a parent *and*
are female:

```text
?- parent(bob, C), female(C).
```

An answer must satisfy both parts with the *same* `C`. Run it:

```prolog
main :- forall((parent(bob, C), female(C)), writeln(C)).
```

```text
ann
pat
```

Note the extra brackets: the whole two-part question, comma and all, is wrapped in `(...)` so
that `forall` sees it as one question. Without them, `forall` would read the comma as splitting
its own two pieces, and the program would not mean what you meant.

One more, and it is worth pausing on. *Who is Jim's mother?*

```text
?- parent(X, jim), female(X).
```

```prolog
main :- forall((parent(X, jim), female(X)), writeln(X)).
```

```text
pat
```

Nothing in `family.pl` mentions mothers. Yet the question found Jim's mother, because *mother*
is just *parent and female* said in one word. You have effectively invented a new relationship
out of two existing ones — but only inside a single question. Teaching Prolog the word itself,
so you can simply ask `mother(X, jim)`, is exactly what the next chapter is about.

## What Prolog does not know

Ask about someone who is not in the database:

```text
?- parent(P, nina).
```

```prolog
main :- forall(parent(P, nina), writeln(P)).
```

Run it, and the program prints nothing at all. No answers, no error, no complaint — the question
`parent(P, nina)` simply has no answers, so there is nothing to do *for every answer*.

This is worth a moment's thought. Prolog did not say *I have never heard of nina*; it said, in
effect, *no one is a parent of nina*. A Prolog program treats its own facts as the whole world:
whatever it cannot find or work out from them, it treats as false. This is called the
**closed-world assumption**, and it is usually exactly what you want — a database of your
family should say *no* when asked about strangers. But it means the quality of Prolog's answers
is the quality of your facts. Prolog reasons faithfully from what you told it; it does not know
what you forgot to tell it.

!!! note "Failure is an answer"
    New Prolog programmers often read a silent result or a failed goal as something going
    wrong. It is not. *No* and *no answers* are ordinary, useful results — half the questions
    worth asking have them.

## Exercises

1. Using a yes-or-no question, ask whether Pam is a parent of Liz. Decide what the output will
   be before you run it.
2. Print all of Bob's children.
3. Print everyone who is male, using the `male/1` facts.
4. Print Tom's daughters. You will need a two-part question.
5. Add facts to `family.pl` making `nina` a parent of `pam`, and female. Now ask: who is Bob's
   grandmother? You cannot say *grandmother* yet — build the question from two `parent` parts
   and one `female` part, joined by commas.
6. Ask a question about someone you did not add to the database, and check that Prolog behaves
   the way this chapter claims.

---

Next: [Chapter 3 — Rules](03-rules.md), where Prolog learns the word *mother* — and starts
reasoning for itself.
