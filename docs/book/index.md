# A Gentle Introduction to Prolog

*A free book for people learning to program, using DotProlog on .NET.*

Most programming languages ask you to write down **steps**: do this, then that, then loop until
done. Prolog asks for something different. You write down **facts** about your problem and
**rules** for reasoning about them — and then you ask questions. Prolog finds the answers itself.

That makes Prolog a wonderful first or second language. It is small enough to learn in a few
evenings, different enough to change how you think about every other language, and old enough —
it was born in 1972 — to have a rich tradition of beautiful ideas behind it.

This book teaches Prolog from zero using [DotProlog](../index.md), a Prolog implementation for
.NET. You do not need to know C#, .NET, or any Prolog. You need a computer, a text editor, and
curiosity.

> Эта книга также доступна [на русском языке](ru/index.md).

## Who this book is for

You, if any of these sound familiar:

- You are new to programming and want a language where small programs do interesting things.
- You know a little of some language — Python, C#, JavaScript — and want to see a genuinely
  different way of thinking.
- You have heard the words *logic programming* and want to know what the fuss is about.

No mathematics beyond school arithmetic is assumed. When a term of art appears — *unification*,
*backtracking* — the book explains it before using it.

## How to use this book

Read the chapters in order; each builds on the previous one. Every chapter is short, ends with
exercises, and every example in it is a real program you can run. Chapter 1 shows you how to run
them and introduces the one command reused throughout the book.

Type the examples in rather than copying them. Your fingers learn faster than your eyes.

## The chapters

1. [Getting ready](01-getting-ready.md) — install the prerequisites, run your first program.
2. [Facts and questions](02-facts-and-questions.md) — teach Prolog what is true, then ask.
3. [Rules](03-rules.md) — teach Prolog how to reason.
4. [Unification and search](04-unification-and-search.md) — how Prolog actually finds answers.
5. [Recursion](05-recursion.md) — rules that use themselves.
6. [Lists](06-lists.md) — Prolog's favourite data structure.
7. [Numbers and arithmetic](07-numbers-and-arithmetic.md) — calculating, comparing, counting.
8. [Making decisions](08-making-decisions.md) — if-then-else, negation, and the cut.
9. [Collecting answers](09-collecting-answers.md) — gathering solutions and remembering things.
10. [Words and text](10-words-and-text.md) — atoms, characters, and tidy output.
11. [A real project: text adventure](11-project-text-adventure.md) — everything you learned, in one game.
12. [Prolog meets .NET](12-prolog-meets-dotnet.md) — where DotProlog goes further, and where you go next.

## About DotProlog

DotProlog is an open-source Prolog for .NET 10, written in C#. The same rules you write in this
book can later be called from C#, F#, or Visual Basic programs, tested with `dotnet test`, and
shipped inside self-contained native executables. None of that is needed to learn Prolog — but it is
waiting for you in [chapter 12](12-prolog-meets-dotnet.md) and the
[.NET integration guide](../dotnet-integration.md) when you are ready.

This book, like DotProlog itself, is free under the MIT licence. If you find a mistake or a
confusing paragraph, please [open an issue](https://github.com/kidoz/dotprolog/issues) — readers
after you will be grateful.
