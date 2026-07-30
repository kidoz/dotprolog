# Chapter 6 — Lists

So far every piece of data has stood alone: one atom, one name, one city. Real programs deal in
*collections* — a shopping list, a queue of passengers, the letters of a word. Prolog's collection
is the **list**, and it is so central that the language gives it its own notation. Better still,
lists are built exactly the way chapter 5 taught you to think: one step at a time. Everything you
learned about recursion is about to pay off.

## What a list is

A list is a sequence of terms between square brackets, separated by commas:

```prolog
[apple, banana, cherry]
[1, 2, 3]
[pam, tom, bob]
[]
```

The last one, `[]`, is the *empty list* — a list with nothing in it. It is a perfectly ordinary
term, and it matters as much as zero matters to counting. A list can hold any terms: atoms,
numbers, even other lists — we will get to that.

## Head and tail

Here is the secret that makes lists work with recursion: a non-empty list is really two things.
Its **head** — the first element — and its **tail** — the list of everything after the first
element. The notation `[H|T]` means *a list whose head is H and whose tail is T*. That vertical
bar is the important character; everything before it is elements, everything after it is the rest
of the list.

Unification, from chapter 4, does the taking-apart for you. Save this as `headtail.pl`:

```prolog
:- initialization(main).

main :-
    [H|T] = [apple, banana, cherry],
    write(H), nl,
    write(T), nl.
```

```console
dotnet run --project src/DotProlog.Tool -- run headtail.pl
```

```text
apple
[banana,cherry]
```

The head is `apple`, one element. The tail is `[banana, cherry]` — *still a list*, just a shorter
one. That is the crucial point. Take the tail of the tail and you get `[cherry]`; take the tail of
that and you get `[]`. Every list is a head and a shorter list, all the way down to the empty
list. A list is a staircase, and `[H|T]` takes one step.

Patterns can be as specific as you like. Watch a few unifications at once:

```prolog
:- initialization(main).

main :-
    [X, Y, Z] = [red, green, blue],
    write(Y), nl,
    [First|Rest] = [1, 2, 3, 4],
    write(First), nl,
    write(Rest), nl,
    [A, B|Tail] = [mon, tue, wed, thu],
    write(A), write(' '), write(B), nl,
    write(Tail), nl.
```

```text
green
1
[2,3,4]
mon tue
[wed,thu]
```

`[X, Y, Z]` insists on *exactly three* elements and names each one. `[A, B|Tail]` peels off the
first two and keeps the rest. And one more shape to know: `[H|T]` refuses to unify with `[]` —
the empty list has no head to give. That refusal is not a nuisance; it is what will stop our
recursions, the way running out of `parent` facts stopped `ancestor/2`.

!!! note "Double quotes are not text — yet"
    You might guess that `"hello"` is how to write text. Not in this book, not yet: in Prolog,
    double quotes make a *list of numbers* — character codes — for reasons
    [chapter 10](10-words-and-text.md) explains and puts to good use. Until then, text is always
    an atom in single quotes: `'hello'`, `'New York'`. If output ever shows a burst of numbers
    where you expected words, a stray double quote is almost certainly why.

## Writing my_member: is it in the list?

Time to write our first recursive predicate over lists: `my_member(X, List)`, true when X is one
of the elements of List. Use chapter 5's recipe. *Smallest problem:* X is the head — then we are
done, no searching needed. *Peeling one step off:* if X is not the head, the question becomes
whether X is in the *tail*, a shorter list. Base case and progress, both present:

```prolog
:- initialization(main).

my_member(X, [X|_]).
my_member(X, [_|T]) :- my_member(X, T).

main :- forall(my_member(Colour, [red, green, blue]), (write(Colour), nl)).
```

```text
red
green
blue
```

Read the clauses aloud. *X is a member of a list whose head is X* — the anonymous variable `_`
from chapter 4 says we do not care what the tail is. *X is a member of a list if X is a member of
its tail* — and this time we do not care about the head. Neither clause mentions `[]`, so a query
against the empty list fails, which is exactly right: nothing is a member of the empty list.

Run backwards through the query in your head: `my_member(Colour, [red, green, blue])` first
unifies Colour with the head, red. On backtracking, the second clause drops to the tail and finds
green; then blue; then the tail is `[]`, no clause fits, and the search ends. Membership,
backtracking, and *generating* the elements one by one — all from two lines.

## Writing my_append: joining lists

Next, a classic: `my_append(A, B, C)`, true when C is list A followed by list B. The recipe again.
*Smallest problem:* appending B to the empty list — the answer is just B. *One step:* to append B
to `[H|T]`, put H at the front of whatever appending B to T gives.

```prolog
:- initialization(main).

my_append([], L, L).
my_append([H|T], L, [H|R]) :- my_append(T, L, R).

main :-
    my_append([a, b], [c, d], Joined),
    write(Joined), nl.
```

```text
[a,b,c,d]
```

The second clause deserves a slow read: *appending L to a list with head H and tail T gives a list
with head H and tail R, where R is T appended to L*. The head is carried across; the tails are the
smaller problem. Trust the recursion — `my_append(T, L, R)` will do its job — and the clause is
obviously true.

If these two definitions felt satisfying to write, good: that feeling is chapter 5 becoming muscle
memory. Every list predicate you will ever write is a variation on these moves.

## The standard library was here all along

Now for a small confession. Prolog already provides both of these, and more. DotProlog's standard
library includes, among others:

- `member(X, List)` — what we just wrote as `my_member/2`
- `append(A, B, C)` — our `my_append/3`
- `length(List, N)` — N is the number of elements
- `reverse(List, Reversed)` — the same elements, back to front
- `nth1(N, List, X)` — X is the Nth element, counting from 1
- `last(List, X)` — X is the final element

We wrote our own first because *writing* them is the lesson; from here on, use the built-in ones.
A taste of the newcomers:

```prolog
:- initialization(main).

main :-
    length([a, b, c, d], N),   write(N), nl,
    reverse([a, b, c], R),     write(R), nl,
    nth1(2, [red, green, blue], E), write(E), nl,
    last([spring, summer, autumn], L), write(L), nl.
```

```text
4
[c,b,a]
green
autumn
```

## Append backwards: the wow moment

Here is something no ordinary language will do for you. `append/3` is a *relation*, not a
function — chapter 2's lesson about questions running in any direction applies to it in full. We
used it to join two lists. But nothing stops you giving it the *answer* and asking for the
questions:

```prolog
:- initialization(main).

main :- forall(append(Front, Back, [a, b, c]), (write(Front), write(' + '), write(Back), nl)).
```

```text
[] + [a,b,c]
[a] + [b,c]
[a,b] + [c]
[a,b,c] + []
```

Every way of *splitting* `[a, b, c]` into two pieces. We wrote no splitting code — the same two
clauses that join lists, run with different arguments known, take a list apart. This trick is
everywhere in real Prolog: finding what comes before or after an element, checking prefixes,
carving a sequence at every possible point. One relation, many programs.

## Lists inside lists

List elements can themselves be lists. A noughts-and-crosses board, say, as a list of rows:

```prolog
:- initialization(main).

main :-
    Board = [[x, o, x], [o, o, x], [x, x, o]],
    nth1(2, Board, Row),
    write(Row), nl,
    length(Board, N),
    write(N), nl.
```

```text
[o,o,x]
3
```

To the outer list, each row is simply one element — `length` says the board has three elements,
and `nth1` hands you a whole row. Nothing new is needed; the bracket notation nests as far as you
like.

## Pairs

One more shape you will meet constantly. Prolog programmers write a *pair* of related values with
a dash between them: `alice-34` is the name alice paired with the number 34. It is not subtraction
happening — it is just a term with two parts, a convenient shape everyone agrees on. A list of
pairs makes a tidy little table:

```prolog
:- initialization(main).

main :-
    Ages = [alice-34, bob-27, carol-41],
    forall(member(Name-Age, Ages), (write(Name), write(' is '), write(Age), nl)).
```

```text
alice is 34
bob is 27
carol is 41
```

Notice `member(Name-Age, Ages)`: the pattern in the first argument takes each pair apart as it is
found, one unification doing two jobs. Keep this shape in mind — [chapter 9](09-collecting-answers.md)
builds on it when we start collecting and organising answers.

## Exercises

1. Write `my_last(X, List)`, true when X is the last element, without using the built-in
   `last/2`. Hint: what is the smallest list that *has* a last element? It is not `[]`.
2. Using `append/3` with a pattern, find what comes immediately after `c` in
   `[a, b, c, d, e]`. Hint: the query `append(_, [c, After|_], [a, b, c, d, e])` is nearly the
   whole answer — read it aloud and finish the program around it.
3. What does `reverse([[a, b], [c, d], [e]], R)` give? In particular, what happens to the inner
   lists? Guess first, then run it.
4. Given `Ages = [alice-34, bob-27, carol-41]`, write a query using `member/2` that finds bob's
   age and prints it — without mentioning 27 anywhere in your program.
5. What do `[H|T] = [only]` and then `[H|T] = []` each do? Predict both, then test them one at a
   time, and note what the program prints when `main` cannot be proved — you will meet that
   message again.
6. Write `my_prefix(P, List)`, true when list P is the front part of List — so
   `my_prefix([a, b], [a, b, c])` holds. You can write it in one short clause using `append/3`,
   or recursively without it. Try both if you are feeling strong.

---

Next: [Chapter 7 — Numbers and arithmetic](07-numbers-and-arithmetic.md), where Prolog finally
does some sums — and is surprisingly picky about how.
