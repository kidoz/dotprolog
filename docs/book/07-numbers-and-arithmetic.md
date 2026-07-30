# Chapter 7 — Numbers and arithmetic

Six chapters in, and we have barely added two and two. That was deliberate: arithmetic in Prolog
has a small surprise at its centre, and it is easier to meet once you know how terms and
unification behave. The surprise is this: **Prolog does not do sums unless you ask it to.**
Understand that one sentence and everything in this chapter falls into place.

## 2 + 3 is a term, not a five

Numbers themselves are ordinary terms — you have been quietly using them since the lists chapter.
`7` is a term, `3.14` is a term. But what about `2 + 3`? Try it:

```prolog
:- initialization(main).

main :-
    write(2 + 3), nl.
```

```text
2+3
```

Not `5`. To Prolog, `2 + 3` is a *structure* — a term named `+` with two parts, just as
`parent(pam, bob)` is a term named `parent` with two parts. The pretty operator notation is only
notation. Nobody has calculated anything, because nobody asked.

## is: asking for the answer

The asking is done by `is/2`. Its right-hand side is an expression to *evaluate*; its left-hand
side unifies with the result:

```prolog
:- initialization(main).

main :-
    X is 2 + 3,
    write(X), nl.
```

```text
5
```

Read `X is 2 + 3` as *X is the value of 2 + 3*. The expression may use `+`, `-`, `*` and friends,
as deeply nested as you like, and any variables in it must already hold numbers. That last part
matters, and we will return to it — Prolog is strict about it.

## is is not =

Here is where the surprise bites. Chapter 4 taught you `=` — unification, the matching of terms.
Since `2 + 3` is a term named `+`, and `5` is a number, they are *differently shaped terms*, and
unification wants nothing to do with the pair of them:

```prolog
:- initialization(main).

main :- 5 = 2 + 3, write(they_match), nl.
```

```text
Warning: initialization goal failed.
```

`main` could not be proved — the goal `5 = 2 + 3` simply fails, the same quiet failure you met
when a query had no answers. No error, no sum: `=` compares shapes, and a `+` structure is not
shaped like a `5`. When you want *numerical* equality — evaluate both sides, then compare the
values — the operator is `=:=` :

```prolog
:- initialization(main).

main :- 5 =:= 2 + 3, write(yes), nl.
```

```text
yes
```

So keep the three tools apart in your mind. `=` matches terms without evaluating anything. `is`
evaluates its right side and unifies the result leftwards. `=:=` evaluates *both* sides and
compares the numbers. Most beginner arithmetic bugs are one of these doing another one's job.

## Comparing numbers

Alongside `=:=` live the other comparisons, and like it, they evaluate both sides first:

| Goal | Reads as |
|---|---|
| `X < Y` | X is less than Y |
| `X > Y` | X is greater than Y |
| `X =< Y` | X is at most Y |
| `X >= Y` | X is at least Y |
| `X =:= Y` | X and Y have equal values |
| `X =\= Y` | X and Y have different values |

```prolog
:- initialization(main).

main :-
    2 + 2 =:= 4,   write(sum_checks_out), nl,
    7 > 3,         write(seven_is_bigger), nl,
    10 =< 10,      write(ten_is_at_most_ten), nl,
    1 =\= 2,       write(one_is_not_two), nl.
```

```text
sum_checks_out
seven_is_bigger
ten_is_at_most_ten
one_is_not_two
```

!!! note "It is =<, not <="
    *At most* is written `=<` and *at least* is written `>=`. Prolog chose `=<` so that no
    operator looks like an arrow — you will see why arrows are precious when `->` appears in
    [chapter 8](08-making-decisions.md). Type `<=` and you will get a syntax error, which is at
    least a polite way to learn.

## Integers, floats, and division

Prolog keeps two kinds of number: *integers* (whole numbers, like `7`) and *floats* (numbers with
a decimal point, like `3.5`). They mix freely in arithmetic, but they are different terms: `3` and
`3.0` are numerically equal yet not the same shape, so `3 =:= 3.0` succeeds while `3 = 3.0`
fails. Division is where the difference shows most:

```prolog
:- initialization(main).

main :-
    A is 7 / 2,   write(A), nl,
    B is 7 // 2,  write(B), nl,
    C is 7 mod 2, write(C), nl,
    D is 6 / 2,   write(D), nl.
```

```text
3.5
3
1
3.0
```

`/` is ordinary division and gives a float — even `6 / 2` comes back as `3.0`. `//` is *integer*
division: divide and throw the remainder away. `mod` is the remainder itself. The pair `//` and
`mod` belong together: `7` is `2 * 3 + 1`, which is exactly what the two answers `3` and `1` say.

## Counting with between

You met `forall/2` as *for every answer, do this*. Give it `between/3` — true when a number lies
in a range, and happy to generate every number in the range — and you have counting:

```prolog
:- initialization(main).

main :- forall(between(1, 5, N), (write(N), nl)).
```

```text
1
2
3
4
5
```

`between(1, 5, N)` offers N = 1, then 2, and so on to 5, through backtracking — it is a fact-like
generator, no different in spirit from `parent/2` offering each parent in turn.

## Recursion meets numbers

Chapter 5's two commandments work on numbers just as well as on family trees, with one habit to
learn: the recursive clause makes progress by computing a *smaller number* with `is`, then
recursing on it. A countdown first:

```prolog
:- initialization(main).

countdown(0) :- write(liftoff), nl.
countdown(N) :-
    N > 0,
    write(N), nl,
    M is N - 1,
    countdown(M).

main :- countdown(5).
```

```text
5
4
3
2
1
liftoff
```

Base case: zero, where we announce liftoff and stop. Progress: `M is N - 1` steps towards zero,
and the guard `N > 0` keeps the second clause from marching past it into negative numbers. Note
that we *must* say `M is N - 1` and then use `M`; writing `countdown(N - 1)` would pass the
unevaluated term `5-1`, then `5-1-1` — structures, not numbers, growing instead of shrinking.

The most famous example of number recursion is the factorial — the product
`N * (N-1) * … * 2 * 1`:

```prolog
:- initialization(main).

factorial(0, 1).
factorial(N, F) :-
    N > 0,
    M is N - 1,
    factorial(M, G),
    F is N * G.

main :- factorial(6, F), write(F), nl.
```

```text
720
```

Read the second clause with chapter 5 eyes: to get the factorial of N, trust the recursion to
produce G, the factorial of N - 1, then one multiplication finishes the job. The factorial of
zero is one, and every question steps down towards it.

## Summing a list

Numbers and lists together, in the pattern you built in chapter 6 — peel the head, recurse on
the tail:

```prolog
:- initialization(main).

my_sum([], 0).
my_sum([H|T], Sum) :-
    my_sum(T, Rest),
    Sum is H + Rest.

main :- my_sum([3, 8, 4], S), write(S), nl.
```

```text
15
```

*The sum of the empty list is zero. The sum of a list is its head plus the sum of its tail.* Two
clauses, and by now you can probably write them faster than read about them.

And, as in chapter 6, a confession follows the exercise: the library already has `sum_list/2`,
along with some friends worth knowing — `max_list/2` and `min_list/2` for the largest and
smallest element, and `numlist/3`, which builds a list of consecutive integers:

```prolog
:- initialization(main).

main :-
    sum_list([3, 8, 4], S),    write(S), nl,
    max_list([3, 8, 4], Max),  write(Max), nl,
    min_list([3, 8, 4], Min),  write(Min), nl,
    numlist(1, 6, L),          write(L), nl.
```

```text
15
8
3
[1,2,3,4,5,6]
```

As before: write your own once to learn the shape, then use the library's.

## A practical example: averaging marks

Put the pieces together — `sum_list/2`, `length/2` from chapter 6, and one `is`:

```prolog
:- initialization(main).

average(Marks, Average) :-
    sum_list(Marks, Sum),
    length(Marks, Count),
    Average is Sum / Count.

main :- average([72, 85, 90, 65], A), write(A), nl.
```

```text
78.0
```

A float, because `/` always gives one. Three lines of definition, each one a plain English
sentence: sum the marks, count them, divide. This is what Prolog programs feel like once lists,
recursion, and arithmetic are all in your hands — less like instructions, more like a definition
of what the answer *is*.

## When is has nothing to work with

One warning to close on. `is` evaluates its right-hand side, so every variable in it must already
hold a number *at that moment*. Give it a variable still unbound and Prolog cannot fail politely —
failing would suggest the sum came out false, which is not what happened. Instead it throws an
error:

```prolog
:- initialization(main).

main :- X is Y + 1, write(X), nl.
```

```text
error: instantiation_error
```

An *instantiation error*: a variable was not instantiated — not yet bound to a value — where a
number was needed. When you see it, read your clause left to right and ask: by the time execution
reaches the `is`, has every variable on its right actually been given a number? Usually one goal
is out of order, and moving the `is` after the goal that supplies the value cures it. Errors like
this can also be caught and handled rather than ending the program — that story starts in
[chapter 8](08-making-decisions.md).

## Exercises

1. Write `fahrenheit_to_celsius(F, C)` using the formula `C is (F - 32) * 5 / 9`, and check that
   212 comes out as 100.0 and 32 as 0.0.
2. Using `forall/2` and `between/3`, print the seven times table: each line showing the product
   of 7 with the numbers 1 to 10. You will need an `is` inside the `forall`.
3. Write `my_product(List, P)`, true when P is the result of multiplying all the elements
   together. Only one thing differs from `my_sum/2` besides the operator — the base case. Why
   can it not be 0?
4. Predict what `X is 10 // 3` and `X is 10 mod 3` each give, then check. Then try `X is -10 // 3`
   and `X is -10 mod 3` — the answers may surprise you. Look at them until you can state each
   one's rule: which way does `//` round, and whose sign does the result of `mod` follow?
5. In the factorial program, the guard `N > 0` looks removable — the base case already handles
   zero, after all. Remove it, run `factorial(6, F)`, and all seems well. Now ask for more
   answers: `forall(factorial(6, F), (write(F), nl))`. Explain what you observe using chapter 5's
   two commandments. (Ctrl+C is your friend.)
6. Write `evens_up_to(N, L)` giving the even numbers from 2 to N — for example
   `evens_up_to(10, L)` should give `[2,4,6,8,10]`. One tidy route: build the full range with
   `numlist/3` and select from it recursively with `mod`; or count upwards by twos yourself.

---

Next: [Chapter 8 — Making decisions](08-making-decisions.md), where programs choose between
alternatives on purpose — if-then-else, saying no, and Prolog's famous cut.
