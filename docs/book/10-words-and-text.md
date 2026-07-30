# Chapter 10 — Words and text

Your programs have been full of words from the very first page — `hello`, `bob`, `gold`,
`'feed the cat'` — but so far words have been indivisible tokens: good for matching, useless for
anything else. This chapter opens them up. You will measure words, glue them together, take them
apart letter by letter, print them in tidy columns, and — at last — read what the person at the
keyboard types back. Along the way, one of this book's quiet mysteries gets solved: why every
piece of text so far has worn single quotes, never double ones.

## Atoms are Prolog's text

Prolog's name for a word is an *atom*. You have made thousands: any name starting with a
lowercase letter — `bob`, `gold`, `cloudy` — is an atom. But an atom starting with a capital
would be mistaken for a variable, and an atom with spaces would be mistaken for several things;
single quotes fix both:

```prolog
:- initialization(main).

capital(england, 'London').
capital(france, 'Paris').

main :-
    X = 'New York', write(X), nl,
    atom_length('New York', L), write(L), nl,
    capital(france, C), write(C), nl.
```

```text
New York
8
Paris
```

Quoted or not, an atom is one indivisible value — `'London'` is exactly as much a single thing
as `london`. The quotes are spelling, not substance. In DotProlog, atoms *are* the text type:
whenever this book says *text*, it means an atom.

## Measuring and joining

Two predicates you will use constantly. `atom_length(Atom, Length)` counts characters — you just
saw it report 8 for `'New York'`, space included. `atom_concat(A, B, C)` glues two atoms into
one:

Here, a character means one UTF-16 code unit. A character outside the Basic Multilingual Plane,
such as 😀, therefore contributes two to `atom_length/2`.

```prolog
main :-
    atom_concat(rain, bow, A), write(A), nl,
    atom_concat('good ', morning, B), write(B), nl.
```

```text
rainbow
good morning
```

For gluing a whole list of pieces, `atomic_list_concat/2` joins them all, and
`atomic_list_concat/3` puts a separator between each pair — numbers are welcome too:

```prolog
main :-
    atomic_list_concat([good, ' ', morning], A), write(A), nl,
    atomic_list_concat([red, green, blue], ', ', B), write(B), nl,
    atomic_list_concat([2026, 7, 30], '-', C), write(C), nl.
```

```text
good morning
red, green, blue
2026-7-30
```

## Looking inside: `sub_atom/5`

`sub_atom(Atom, Before, Length, After, Sub)` is the magnifying glass: `Sub` is the piece of
`Atom` with `Before` characters before it, `Length` characters in it, and `After` characters
after it. Five arguments sounds like a lot, but you rarely fill them all in — you state what you
know and ask for the rest. First letter: nothing before, length one. Last letter: length one,
nothing after:

```prolog
main :-
    sub_atom(prolog, 0, 1, _, First), write(First), nl,
    sub_atom(prolog, _, 1, 0, Last), write(Last), nl,
    ( sub_atom(bookshelf, _, _, _, shelf) -> write(yes) ; write(no) ), nl,
    ( sub_atom(bookshelf, _, _, _, cat) -> write(yes) ; write(no) ), nl.
```

```text
p
g
yes
no
```

The last two lines show the *contains* trick: leave everything unknown except the piece you are
looking for, and `sub_atom` succeeds exactly when it fits somewhere.

## Words as lists of letters

Sometimes you want the letters themselves, as a list you can work on with all of chapter 6.
`atom_chars/2` converts both ways — atom to letters, letters to atom — which makes word games
one-liners:

```prolog
main :-
    atom_chars(cat, Cs), write(Cs), nl,
    atom_chars(stressed, Ss), reverse(Ss, Rs), atom_chars(Word, Rs), write(Word), nl,
    char_code(a, Code), write(Code), nl,
    char_code(Ch, 98), write(Ch), nl.
```

```text
[c,a,t]
desserts
97
b
```

Each single letter is itself a tiny atom. Beneath every character sits a number — its *character
code* — and `char_code/2` converts between the two; `a` is 97, and 98 is `b`. Codes will matter
in a moment.

Two more conversions round out the kit. `upcase_atom/2` and `downcase_atom/2` shout and whisper,
and `atom_number/2` bridges text and arithmetic — essential when a number arrives dressed as
text:

```prolog
main :-
    upcase_atom('Hello there', U), write(U), nl,
    downcase_atom('LOUD', D), write(D), nl,
    atom_number('42', N), M is N * 2, write(M), nl.
```

```text
HELLO THERE
loud
84
```

## The truth about double quotes

Every other language you will ever meet writes text as `"hello"`. This book has carefully never
done that, and you have earned the explanation. Watch:

```prolog
main :-
    write("abc"), nl,
    X = "hello", write(X), nl,
    number_codes(N, "427"), Double is N * 2, write(Double), nl.
```

```text
[97,98,99]
[104,101,108,108,111]
854
```

In Prolog, `"abc"` is not an atom at all — it is *the list of character codes* `[97,98,99]`,
the numbers you just met with `char_code`. That is occasionally exactly what you want: the last
line uses `number_codes/2`, which converts between a number and its codes, so `"427"` is a
handy way to write those three digit codes. The grammar rules in DotProlog's
[language guide](../language-guide.md) put code lists to serious use.

But for a beginner it is a trap: `write("abc")` printing `[97,98,99]` has ruined many an
afternoon, and different Prolog systems disagree about what double quotes should mean — some
make them a string type that DotProlog deliberately does not have. Hence this book's rule, which
you can now adopt knowingly: **text is atoms, in single quotes when needed; double quotes mean a
list of codes.**

## Tidier printing: `format`

You have printed with `write` and `nl` for nine chapters, and messages like
`write('added: '), write(Task), nl` work but creak. `format/2` takes a template and a list of
values and does it all in one go:

```prolog
main :-
    format('Hello, ~w!~n', [ada]),
    format('~w plus ~w is ~d~n', [2, 3, 5]),
    format('~a~n', ['New York']),
    format('~q~n', ['New York']),
    format('name: ~w, age: ~d~n', [jim, 2]).
```

```text
Hello, ada!
2 plus 3 is 5
New York
'New York'
name: jim, age: 2
```

Everything ordinary in the template is printed as-is; each `~` directive consumes the next value
from the list. The ones you need:

- `~w` — write the value, like `write/1`
- `~a` — an atom, printed bare
- `~d` — an integer
- `~q` — write *quoted*, so what prints could be read back as Prolog (note `'New York'` kept
  its quotes)
- `~n` — new line

`format` can even lay out columns: `~t` inserts stretchy padding and `~12|` says *the column
ends here, at position 12*. One example, because tidy tables are worth having:

```prolog
main :-
    forall(member(Name-Qty, [apples-12, pears-3, plums-140]),
           format('~w~t~12|~t~d~4+~n', [Name, Qty])).
```

```text
apples        12
pears          3
plums        140
```

Names pushed left, numbers pushed right — a report in two lines of code.

## Reading what the user types

Until now, information flowed one way. `read(Term)` turns your program into a conversation: it
pauses, waits for the user to type a Prolog term, and binds the variable to it. Here is a
greeter:

```prolog
:- initialization(main).

main :-
    write('What is your name? '),
    read(Name),
    format('Hello, ~w!~n', [Name]).
```

Run it, and when the prompt appears type `ada.` — with the full stop — and press Enter:

```text
What is your name? ada.
Hello, ada!
```

The full stop matters: `read` reads a *term*, and just as in your program files, a term is not
finished until its full stop arrives. Type only `ada` and press Enter, and `read` simply keeps
waiting — it assumes the term continues on the next line. If the input runs out entirely with no
full stop in sight, that is an error, and by now you know its family:

```text
error: syntax_error(unexpected_end_of_file)
```

You can also feed answers in from the command line, which is how you test interactive programs
without typing at them. In a POSIX shell on macOS or Linux, `printf` supplies the text a user
would have typed:

```console
printf 'ada.\n' | dotnet run --project src/DotProlog.Tool -- run greet.pl
```

In PowerShell on Windows:

```powershell
"ada." | dotnet run --project src/DotProlog.Tool -- run greet.pl
```

```text
What is your name? Hello, ada!
```

(The greeting lands on the same line as the prompt because the *user's* Enter key, which usually
moves the cursor down, was piped in rather than typed.)

What is read is a real term, so a number can go straight into arithmetic:

```prolog
:- initialization(main).

main :-
    write('How old are you? '),
    read(Age),
    Next is Age + 1,
    format('Next year you will be ~d.~n', [Next]).
```

```console
printf '41.\n' | dotnet run --project src/DotProlog.Tool -- run age.pl
```

In PowerShell, use `"41." | dotnet run --project src/DotProlog.Tool -- run age.pl`.

```text
How old are you? Next year you will be 42.
```

And that is the last ingredient. A program that can print with `format`, read with `read`,
decide with if-then-else, and remember with `assertz` is a program that can hold a conversation
— which is precisely what the next chapter builds: a game you can walk around in, one typed
command at a time.

## Exercises

1. Write `initial(Name, I)` that gives the first letter of a name, using `sub_atom/5`. Check
   that `initial(gertrude, I)` gives `g`.
2. Write `shout(Atom)` that prints the atom in upper case with an exclamation mark after it, so
   `shout(hooray)` prints `HOORAY!`. (`upcase_atom` and either `atom_concat` or `format` will
   do it.)
3. Write `palindrome(Word)` that succeeds when a word reads the same backwards — `atom_chars`
   and `reverse` are all you need. Try it on `level`, `rotor`, and `prolog`.
4. Using `atomic_list_concat/3`, turn `[monday, tuesday, wednesday]` into the single atom
   `'monday, tuesday, wednesday'`, and `[jim, is, 2]` into `'jim is 2'`.
5. Extend the greeter: after asking the name, ask for a favourite colour, then reply using one
   `format` call with both values, e.g. `ada, the adventure begins in glorious green!`. Test it
   by piping the two answers into the program, using the same shell technique as above.
6. Write a program that prints a two-column table of the family's names and ages from chapter 9,
   using the column directives from this chapter.

---

Next: [Chapter 11 — A real project: text adventure](11-project-text-adventure.md), where every
chapter so far — facts, rules, decisions, memory, and conversation — becomes one playable game.
