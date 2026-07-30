# Chapter 11 — A real project: text adventure

You have spent ten chapters collecting tools: facts and rules, recursion, lists, arithmetic,
decisions, a database you can change while the program runs, and text in and out of the
terminal. Each arrived in a small program built to show off that one tool. This chapter is
different. You will build a single program, stage by stage, until it is a game you can actually
play.

The game is a *text adventure* — the oldest kind of computer game there is. Before graphics,
games were made of sentences. The computer described where you were; you typed what you wanted
to do; the computer told you what happened. Prolog is a natural fit for this: the world is a set
of facts, and the things you can do are rules.

Our game is called *The Silver Crown*. There are four rooms, three objects, one locked door, and
one goal: find the crown. By the end of this chapter the whole thing fits in about a hundred
lines — and every one of those lines uses something you already know.

Work in one file, `adventure.pl`, and grow it as the chapter goes. Every stage runs, so you can
check your progress after each section with the usual command:

```console
dotnet run --project src/DotProlog.Tool -- run adventure.pl
```

## The world as facts

A world is a set of true statements, and true statements are facts — chapter 2. Start with the
rooms. Each room is an atom, and each has a description, which is also an atom: a quoted piece
of text, as in [chapter 10](10-words-and-text.md).

```prolog
description(hall,    'You are in a dusty hall. A grandfather clock ticks somewhere.').
description(kitchen, 'You are in the kitchen. It smells faintly of bread.').
description(garden,  'You are in a walled garden. Bees drift between the roses.').
description(cellar,  'You are in the cellar. Cobwebs everywhere - and a glint of silver.').
```

Rooms need doorways. A doorway joins two rooms in a direction:

```prolog
door(hall, kitchen, north).
door(hall, garden, south).
door(hall, cellar, east).
```

Read the first fact as *there is a door from the hall to the kitchen, on the north side*. But a
door works both ways: if the kitchen is north of the hall, then the hall is south of the
kitchen. We could write every doorway twice — or we could state each one once and let a rule do
the reasoning, which is exactly what rules are for (chapter 3):

```prolog
opposite(north, south).
opposite(south, north).
opposite(east, west).
opposite(west, east).

exit(From, Direction, To) :- door(From, To, Direction).
exit(From, Direction, To) :- door(To, From, Opposite), opposite(Direction, Opposite).
```

The first `exit` clause says: you can leave `From` in some `Direction` if there is a door that
way. The second says: you can also leave through a door stated the *other* way round, by going
in the opposite direction. Three `door` facts now describe six passages.

Finally, put some objects in the rooms:

```prolog
at(loaf, kitchen).
at(key, garden).
at(crown, cellar).
```

That is the entire world. To check it, add a temporary `main` that asks about the exits — the
`forall` is from [chapter 9](09-collecting-answers.md):

```prolog
:- initialization(main).

main :-
    forall(exit(hall, Direction, Room),
           format('From the hall you can go ~w to the ~w.~n', [Direction, Room])),
    forall(exit(garden, Direction, Room),
           format('From the garden you can go ~w to the ~w.~n', [Direction, Room])).
```

```text
From the hall you can go north to the kitchen.
From the hall you can go south to the garden.
From the hall you can go east to the cellar.
From the garden you can go north to the hall.
```

The hall has three exits from three `door` facts; the garden has one, derived by the second
`exit` clause. The world reasons about itself already, and we have not written a single line of
game yet.

## The player's state

The world stands still; the player does not. Three things change during play: where you are,
what is lying on each floor, and what you are carrying. Changing facts are the dynamic database
from [chapter 9](09-collecting-answers.md) — declare them `dynamic`, and move them with
`assertz` and `retract`:

```prolog
:- dynamic here/1.
here(hall).

:- dynamic at/2.
at(loaf, kitchen).
at(key, garden).
at(crown, cellar).

:- dynamic holding/1.
```

Note the pattern: a `dynamic` declaration followed by ordinary facts. The facts in the file are
the *starting* state — the game begins with you in the hall, the key in the garden, and your
hands empty. `holding/1` has no facts at all, which is fine: a dynamic predicate is allowed to
start empty. (`at/2` was static in the last section; now that things can be picked up, it moves
into the dynamic column.)

With state in place we can write the game's most important predicate: `look`, which describes
wherever you are.

```prolog
look :-
    here(Here),
    description(Here, Text),
    writeln(Text),
    write('Exits:'),
    forall(exit(Here, Direction, _), format(' ~w', [Direction])),
    nl,
    forall(at(Item, Here), format('There is a ~w here.~n', [Item])).
```

Read it aloud: find out where *here* is, print its description, print each exit, print each item
lying about. Two `forall` calls do the listing — *for every exit, print it; for every item
here, print it*.

To see the state actually change, try a `main` that looks, teleports the player by hand, and
looks again:

```prolog
main :-
    look,
    retract(here(hall)),
    assertz(here(garden)),
    look.
```

```text
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
You are in a walled garden. Bees drift between the roses.
Exits: north
There is a key here.
```

The `retract`–`assertz` pair is how the player will move for the rest of the game: forget the
old location, remember the new one.

## Commands

A player should not have to teleport by editing the program. Time for verbs. Each command is a
predicate, and each follows one design rule worth stating out loud: **a command always succeeds,
and always says something** — even when what the player asked for cannot be done. The game loop
we build next relies on that.

`go/1` is the `retract`–`assertz` move wrapped in the if-then-else from
[chapter 8](08-making-decisions.md). If there is an exit that way, move and look; if not, say
so:

```prolog
go(Direction) :-
    here(Here),
    (   exit(Here, Direction, There)
    ->  retract(here(Here)),
        assertz(here(There)),
        look
    ;   writeln('You cannot go that way.')
    ).
```

`take/1` and `drop/1` move an item between the floor and your hands — between `at/2` and
`holding/1`:

```prolog
take(Item) :-
    here(Here),
    (   at(Item, Here)
    ->  retract(at(Item, Here)),
        assertz(holding(Item)),
        format('You take the ~w.~n', [Item])
    ;   format('There is no ~w here.~n', [Item])
    ).

drop(Item) :-
    (   holding(Item)
    ->  retract(holding(Item)),
        here(Here),
        assertz(at(Item, Here)),
        format('You drop the ~w.~n', [Item])
    ;   format('You are not holding a ~w.~n', [Item])
    ).
```

And `inventory` lists what you carry, with a kinder message for empty hands:

```prolog
inventory :-
    (   holding(_)
    ->  writeln('You are carrying:'),
        forall(holding(Item), format('  a ~w~n', [Item]))
    ;   writeln('You are carrying nothing.')
    ).
```

A scripted `main` exercises the lot — including the polite failures:

```prolog
main :-
    look,
    go(south),
    take(key),
    inventory,
    go(west),
    take(loaf).
```

```text
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
You are in a walled garden. Bees drift between the roses.
Exits: north
There is a key here.
You take the key.
You are carrying:
  a key
You cannot go that way.
There is no loaf here.
```

Walking into a wall and grabbing at a loaf that is not there both produce sentences, not
failures. That is the design rule doing its job.

## The game loop

Now the player takes over from the script. A game loop does three things, forever: show a
prompt, read a command, obey it. *Read a command* is `read/1` from
[chapter 10](10-words-and-text.md) — the player types a Prolog term ending in a full stop, such
as `go(north).` — and *forever* is not a loop keyword, because Prolog has none. It is recursion,
straight from [chapter 5](05-recursion.md): the loop obeys one command, then calls itself.

Obeying is a small dispatch predicate, `do/1`, with one clause per command:

```prolog
do(look)       :- look.
do(go(D))      :- go(D).
do(take(X))    :- take(X).
do(drop(X))    :- drop(X).
do(inventory)  :- inventory.
do(Command)    :- format('I do not know how to ~w.~n', [Command]).
```

Prolog tries the clauses top to bottom (chapter 4), and the term the player typed unifies with
the matching head — `take(loaf)` finds the `take(X)` clause with `X = loaf`. The last clause
matches anything, so it must come last: it is the fallback for commands the game does not know.
And because every command keeps our always-succeed promise, the first clause that matches is the
only one that runs.

The loop itself, with `quit` handled before dispatch:

```prolog
loop :-
    write('> '),
    read(Command),
    (   Command = quit
    ->  writeln('Thanks for playing. Goodbye!')
    ;   do(Command),
        loop
    ).
```

Replace the scripted `main` with the real thing:

```prolog
main :-
    look,
    loop.
```

Run it and play. Here is a session — the lines after each `>` are what the player typed, full
stops and all:

```text
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> go(north).
You are in the kitchen. It smells faintly of bread.
Exits: south
There is a loaf here.
> take(loaf).
You take the loaf.
> sing.
I do not know how to sing.
> go(south).
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> quit.
Thanks for playing. Goodbye!
```

!!! note "Testing a game without playing it"
    You met this trick in [chapter 10](10-words-and-text.md): pipe a scripted session into the
    program instead of typing it. It works just as well for a whole game. In a POSIX shell:

    ```console
    printf 'go(north).\ntake(loaf).\nsing.\ngo(south).\nquit.\n' | dotnet run --project src/DotProlog.Tool -- run adventure.pl
    ```

    In PowerShell:

    ```powershell
    @("go(north).", "take(loaf).", "sing.", "go(south).", "quit.") |
        dotnet run --project src/DotProlog.Tool -- run adventure.pl
    ```

    Piped input is not echoed, so the transcript looks barer than an interactive one — the
    responses appear straight after each `>` — but it is the same game. Keep a winning script
    around and you can re-test the game after every change in one command.

## Locking the cellar, and winning

A game needs something to want. The crown is in the cellar; let us lock the cellar, and hide the
key in the garden. Whether a door is locked is state — it changes once, when you unlock it — so
it is another dynamic predicate:

```prolog
:- dynamic locked/1.
locked(cellar).
```

Entering a room now takes a little judgement, so we split it out of `go/1`. The chain of
conditions is the multi-way if-then-else from [chapter 8](08-making-decisions.md), and the
`\+` is negation from the same chapter — *locked, and you are not holding the key*:

```prolog
go(Direction) :-
    here(Here),
    (   exit(Here, Direction, There)
    ->  enter(There)
    ;   writeln('You cannot go that way.')
    ).

enter(There) :-
    (   locked(There), \+ holding(key)
    ->  writeln('The door is locked. Perhaps a key would help.')
    ;   locked(There)
    ->  retract(locked(There)),
        writeln('You unlock the door with the key.'),
        move_to(There)
    ;   move_to(There)
    ).

move_to(There) :-
    retract(here(_)),
    assertz(here(There)),
    look.
```

Three cases, top to bottom: locked and no key — turned away; locked and you have the key —
unlock (by retracting the `locked` fact, so the door stays open) and go in; not locked — just go
in. The moving itself now lives in `move_to/1`, so it is written once.

Winning is the simplest predicate in the game:

```prolog
won :- holding(crown).
```

The loop checks it after every command, and ends the recursion with a fanfare instead of another
prompt:

```prolog
loop :-
    write('> '),
    read(Command),
    (   Command = quit
    ->  writeln('Thanks for playing. Goodbye!')
    ;   do(Command),
        (   won
        ->  nl,
            writeln('The silver crown is yours. You win!')
        ;   loop
        )
    ).
```

Finally, a proper opening. `main` announces the game, teaches the commands, and hands over:

```prolog
main :-
    writeln('THE SILVER CROWN'),
    writeln('Somewhere in this house is a silver crown. Find it.'),
    writeln('Commands: look. go(north). take(key). drop(key). inventory. quit.'),
    nl,
    look,
    loop.
```

## The whole game

Here is the complete program, exactly as it runs — the same clauses you built, gathered in one
place:

```prolog
% The Silver Crown — a small text adventure.
:- initialization(main).

% ----- The world -----

description(hall,    'You are in a dusty hall. A grandfather clock ticks somewhere.').
description(kitchen, 'You are in the kitchen. It smells faintly of bread.').
description(garden,  'You are in a walled garden. Bees drift between the roses.').
description(cellar,  'You are in the cellar. Cobwebs everywhere - and a glint of silver.').

door(hall, kitchen, north).
door(hall, garden, south).
door(hall, cellar, east).

opposite(north, south).
opposite(south, north).
opposite(east, west).
opposite(west, east).

exit(From, Direction, To) :- door(From, To, Direction).
exit(From, Direction, To) :- door(To, From, Opposite), opposite(Direction, Opposite).

% ----- The player's state -----

:- dynamic here/1.
here(hall).

:- dynamic at/2.
at(loaf, kitchen).
at(key, garden).
at(crown, cellar).

:- dynamic holding/1.

:- dynamic locked/1.
locked(cellar).

% ----- Commands -----

look :-
    here(Here),
    description(Here, Text),
    writeln(Text),
    write('Exits:'),
    forall(exit(Here, Direction, _), format(' ~w', [Direction])),
    nl,
    forall(at(Item, Here), format('There is a ~w here.~n', [Item])).

go(Direction) :-
    here(Here),
    (   exit(Here, Direction, There)
    ->  enter(There)
    ;   writeln('You cannot go that way.')
    ).

enter(There) :-
    (   locked(There), \+ holding(key)
    ->  writeln('The door is locked. Perhaps a key would help.')
    ;   locked(There)
    ->  retract(locked(There)),
        writeln('You unlock the door with the key.'),
        move_to(There)
    ;   move_to(There)
    ).

move_to(There) :-
    retract(here(_)),
    assertz(here(There)),
    look.

take(Item) :-
    here(Here),
    (   at(Item, Here)
    ->  retract(at(Item, Here)),
        assertz(holding(Item)),
        format('You take the ~w.~n', [Item])
    ;   format('There is no ~w here.~n', [Item])
    ).

drop(Item) :-
    (   holding(Item)
    ->  retract(holding(Item)),
        here(Here),
        assertz(at(Item, Here)),
        format('You drop the ~w.~n', [Item])
    ;   format('You are not holding a ~w.~n', [Item])
    ).

inventory :-
    (   holding(_)
    ->  writeln('You are carrying:'),
        forall(holding(Item), format('  a ~w~n', [Item]))
    ;   writeln('You are carrying nothing.')
    ).

% ----- Dispatch: one clause per command, a fallback for the rest -----

do(look)       :- look.
do(go(D))      :- go(D).
do(take(X))    :- take(X).
do(drop(X))    :- drop(X).
do(inventory)  :- inventory.
do(Command)    :- format('I do not know how to ~w.~n', [Command]).

% ----- Winning -----

won :- holding(crown).

% ----- The game loop -----

loop :-
    write('> '),
    read(Command),
    (   Command = quit
    ->  writeln('Thanks for playing. Goodbye!')
    ;   do(Command),
        (   won
        ->  nl,
            writeln('The silver crown is yours. You win!')
        ;   loop
        )
    ).

% ----- Start -----

main :-
    writeln('THE SILVER CROWN'),
    writeln('Somewhere in this house is a silver crown. Find it.'),
    writeln('Commands: look. go(north). take(key). drop(key). inventory. quit.'),
    nl,
    look,
    loop.
```

And a full winning playthrough — again, the lines after each `>` are the player's:

```text
THE SILVER CROWN
Somewhere in this house is a silver crown. Find it.
Commands: look. go(north). take(key). drop(key). inventory. quit.

You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> look.
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> go(east).
The door is locked. Perhaps a key would help.
> go(south).
You are in a walled garden. Bees drift between the roses.
Exits: north
There is a key here.
> take(key).
You take the key.
> inventory.
You are carrying:
  a key
> go(north).
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> go(east).
You unlock the door with the key.
You are in the cellar. Cobwebs everywhere - and a glint of silver.
Exits: west
There is a crown here.
> take(crown).
You take the crown.
The silver crown is yours. You win!
```

Step back and look at what this program is made of. The world is facts (chapter 2) reasoned over
by rules (chapter 3). Commands are found by unification against clause heads, tried in order
(chapter 4). The loop is recursion (chapter 5). Errors and choices are if-then-else and negation
(chapter 8). The changing world is the dynamic database, and the listings are `forall`
(chapter 9). The words in and out are atoms, `format`, and `read` (chapter 10). Nothing new was
needed — a real program is just the small ideas, kept tidy and stacked up.

## Exercises

Every one of these extends the game. After each change, replay your winning script — the
`printf` pipe from earlier — to make sure the game still works.

1. **More house.** Add a study to the west of the hall, with a description and something on the
   desk worth taking. One `door` fact, one `description` fact, one `at` fact — the rest of the
   game adjusts itself. Then add a landing and a stair; there is no rule that directions must be
   compass points.
2. **Examining things.** Add `item_description/2` facts — for instance, that the key is small
   and made of brass — and a `look(Item)` command that prints the description if the item is
   here or in your hands, and something polite otherwise. You will need a new `do/1` clause;
   think about why `do(look)` and `do(look(X))` do not collide.
3. **A dark cellar.** Put a lamp in the kitchen, and make the cellar dark: if you enter without
   the lamp, `look` prints only `'It is pitch dark.'` — no description, no exits, no glint of
   silver. Chapter 8 has everything you need.
4. **Full hands.** Give the player only two hands: `take/1` should refuse when you are already
   carrying two items. Count with `aggregate_all(count, holding(_), N)` from chapter 9.
5. **A proper heist.** Taking the crown should not be enough — you must carry it back out to
   the garden. Change `won` (and the victory message) so the game is only won when you are
   holding the crown *and* standing in the garden. Notice that nothing else in the program needs
   to change.
6. **A saved game.** `assertz` and `retract` know the whole story of a playthrough. Add a
   `score` command that reports how many items you are carrying and how many are still lying
   about the house — two `aggregate_all` calls and a `format`.

---

Next: [Chapter 12 — Prolog meets .NET](12-prolog-meets-dotnet.md), where the game you just wrote
turns out to live in a much larger world — one where your Prolog can be called from other
languages, tested like any professional code, and shipped as a real application.
