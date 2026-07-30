# Chapter 1 — Getting ready

In this chapter you will install what you need, run your first Prolog program, and learn the one
command you will use throughout this book. There is no theory yet — the goal is simply that by
the end, when this book says *run this program*, you know exactly what to do.

## What you need

Two downloads, both free:

1. **The .NET 10 SDK** — from
   [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). Pick the SDK (not
   the runtime) for your operating system and follow the installer.
2. **Git** — from [git-scm.com](https://git-scm.com/downloads), if you do not already have it.

To check both are ready, open a terminal — PowerShell on Windows, Terminal on macOS or Linux —
and type:

```console
dotnet --version
git --version
```

If each prints a version number, you are set. Any text editor will do for writing programs;
[Visual Studio Code](https://code.visualstudio.com/) is a fine free choice.

## Get DotProlog

DotProlog lives in a Git repository. Downloading it is one command. In your terminal, go to a
folder where you keep projects and run:

```console
git clone https://github.com/kidoz/dotprolog.git
cd dotprolog
```

Everything else in this book happens inside this `dotprolog` folder. The first time you run a
program, the .NET SDK will download some packages and build DotProlog itself — that takes a
minute or two, once. After that, runs are quick.

## Your first program

The repository already contains a tiny Prolog program at `samples/HelloProlog/hello.pl`. Run it:

```console
dotnet run --project src/DotProlog.Tool -- run samples/HelloProlog/hello.pl
```

```text
Hello! World!
```

That long command is *the* command of this book: `dotnet run --project src/DotProlog.Tool -- run`
followed by the name of a Prolog file. It reads the file, checks it, and runs it. If you tire of
typing it, see the tip at the end of this chapter.

Here is the program it just ran:

```prolog
:- initialization(main).

main :-
    greeting(Greeting),
    write(Greeting),
    nl.

greeting('Hello! World!').
```

You do not need to understand this yet — that is what the rest of the book is for. But three
things are worth noticing even now:

- Prolog programs are made of short statements, each ending with a full stop, like sentences.
- The line `:- initialization(main).` means *when this file runs, start at `main`*.
- Reading `main` aloud almost works as English: *to run main, find a greeting, write it, then
  take a new line*.

## Write your own

Time to write a program yourself, in a file of your own. Create a file called `first.pl` —
anywhere you like, though inside the `dotprolog` folder is convenient — with exactly this
content:

```prolog
:- initialization(main).

main :-
    write(hello),
    nl,
    write(world),
    nl.
```

Save it, then run it (adjust the path if you saved it elsewhere):

```console
dotnet run --project src/DotProlog.Tool -- run first.pl
```

```text
hello
world
```

Congratulations — you have written and run a Prolog program. `write` prints something, `nl`
prints a *new line*, and the commas chain the steps together. The full stop at the very end
closes the whole `main` sentence.

## When things go wrong

Programs go wrong; that is normal, and Prolog tells you where. Delete the final full stop in
`first.pl`, save, and run it again:

```text
first.pl(8,1): error DPL0005: Expected '.' to end the clause but found end of file.
```

Read the message from the left: the file, then `(8,1)` — line 8, column 1 — then what was
expected. Put the full stop back and the program runs again. Whenever this book's examples do
not behave as printed, the first thing to check is that you typed them exactly, full stops
included.

## How this book shows questions

Prolog is a conversation: you state facts, then ask questions. Books traditionally write a
question — Prolog calls it a *query* — like this:

```text
?- write(hello).
```

The `?-` is a prompt, like the `$` of a terminal: *it is not part of the program* — it marks
what you ask. When this book shows a `?-` line, you can try it by putting the question inside a
`main` like the one you just wrote. From chapter 2 on, each example says exactly what to put
where. For now, the shape to remember is:

```prolog
:- initialization(main).

main :-
    <the question goes here>,
    nl.
```

## Tip: a shorter command

Typing the long command gets old. You can give it a short name once per terminal session.
On macOS and Linux:

```console
alias plrun='dotnet run --project src/DotProlog.Tool -- run'
plrun first.pl
```

On Windows PowerShell:

```powershell
function plrun { dotnet run --project src/DotProlog.Tool -- run $args }
plrun first.pl
```

The book keeps writing the full command so every example works everywhere, but feel free to use
your shortcut.

## Exercises

1. Change `first.pl` to print your own name instead of `world`, and run it.
2. Make the program print three lines instead of two.
3. Deliberately misspell `write` as `wrote`, run the program, and read the error message you
   get. You will meet this kind of message — an *existence error* — again in
   [chapter 8](08-making-decisions.md); errors are something Prolog programs can catch and
   handle.
4. What do you think happens if you remove the `nl` lines but keep the `write` lines? Guess
   first, then run it and check.

Answers are not printed in this book on purpose: the terminal tells you whether you are right,
and being told by the terminal teaches more than being told by a page.

---

Next: [Chapter 2 — Facts and questions](02-facts-and-questions.md), where Prolog starts doing
things no ordinary language does.
