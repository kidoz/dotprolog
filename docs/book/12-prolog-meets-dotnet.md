# Chapter 12 — Prolog meets .NET

Every program in this book has run on .NET without you needing to think about it. .NET is the
platform underneath C#, F#, and Visual Basic: a huge, well-tended world of libraries, tools, and
running programs, used everywhere from small scripts to large companies. DotProlog is Prolog
built *on* that platform — and that is not a technical footnote. It means the rules you now know
how to write can become an ordinary part of bigger programs: a C# web application can ask your
Prolog a question the way it calls any other code, your Prolog can be tested by the same test
runner as everything else, and a finished program can be shipped as a single file. This last
chapter is a tour of those doors, not a manual for them — its job is to show you what your new
skill is connected to, and then point you onward.

## Calling Prolog from C#

Here is a complete C# fragment that loads some facts and asks a question. If you have never seen
C#, read it the way you would a foreign newspaper: you will not catch every word, but you will
spot the Prolog inside it straight away.

```csharp
var engine = new PrologEngine();
engine.ConsultText("colour(red).  colour(green).  colour(blue).");

foreach (PrologSolution solution in engine.Query("colour(C)").Solutions())
{
    Console.WriteLine(solution["C"]);      // red, green, blue
}

bool ok = engine.Query("1 < 2").Prove();
```

The middle of the first line is three Prolog facts, exactly as you would write them in a file.
The query `colour(C)` is a query like any in this book, and the `foreach` walks its solutions
one at a time — `red`, `green`, `blue` — the way backtracking offered them to you in chapter 4.
`Prove()` asks only *yes or no*, like a query with no variables. Everything you learned about
how Prolog answers still holds; the only new thing is who is asking.

Solutions arrive lazily, so a C# program can take the first few answers from a query with
very many and simply stop — `between(1, 1000000000, X)` is no threat to anyone. The full
story, including calling a specific predicate directly with .NET values, is in the
[.NET integration guide](../dotnet-integration.md).

Think about what this makes possible. The adventure game from chapter 11 kept its whole world —
rooms, doors, locks — as Prolog facts and rules. A C# program with windows and a drawn map could
consult exactly the same file and ask it `exit(hall, Direction, Room)` whenever the player
clicks. The Prolog stays the brain; another language becomes the face.

## Testing Prolog with `dotnet test`

You have been testing programs all book by running them and reading the output. .NET has a more
disciplined habit: a *test runner* that runs many small checks and reports which passed.
DotProlog plugs Prolog into it with a convention you can learn in one sentence: **any zero-arity
predicate whose name starts with `test_` is a test, and it passes if it can be proved.**

```prolog
test_discount_reduces_price :-
    R is 100 - (100 * 15 / 100),
    R =:= 85.

test_tier_boundaries :-
    tier(1000, gold),
    tier(999, silver),
    tier(0, bronze).

tier(Total, gold)   :- Total >= 1000, !.
tier(Total, silver) :- Total >= 500, !.
tier(_, bronze).
```

No test framework to learn: a test is a rule, and proving it is passing it. Each test runs in a
fresh engine, so tests cannot disturb one another through the dynamic database. The repository
carries a real example in `samples/PricingTests` — run it yourself:

```console
dotnet test --project samples/PricingTests/PricingTests.dplproj
```

```text
Test run summary: Passed!
  total: 7
  failed: 0
  succeeded: 7
  skipped: 0
  duration: 429ms
```

Try it on your own code: put a few `test_` predicates for the adventure game's `exit/3` rule
into a copy of that project and watch them turn up in the summary.

## Prolog projects and typed facades

.NET programs are organised into *projects*, and DotProlog gives Prolog its own kind:
a `.dplproj`. It holds ordinary, portable Prolog — nothing .NET-flavoured — plus a small
*contract* file that declares which predicates the .NET world may call and what types their
arguments carry. From `samples/PricingRules`:

```prolog
:- clr_module('Pricing').
:- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
:- clr_export(in_catalogue/1, semidet, [in(item, atom)]).
```

Read the second line as: *`discount/3` always succeeds exactly once (`det`), takes a price and a
percentage in, and gives a result out.* You already know these characters from asking queries
yourself: `det` is a question with exactly one answer, `semidet` is a yes-or-no that may fail —
like `in_catalogue/1` here — and `nondet` is a question that backtracks through many answers,
which on the .NET side becomes an ordinary sequence to loop over.

During the build, DotProlog turns the contract into a typed *facade* — generated C# — so that a
C# program can write `pricing.Discount(100.0, 15)` and never mention an engine, a query, or a
term. F# and Visual Basic get the same for free. Your Prolog stays pure Prolog; the contract is
the border post. The [.NET integration guide](../dotnet-integration.md) tells the full story,
and `samples/PricingConsole` is a working consumer you can both read and run from the
repository root with `dotnet run --project samples/PricingConsole`.

## One native file

.NET can compile a program *ahead of time* into a single native executable — no .NET
installation needed on the machine that runs it. This is called NativeAOT, and DotProlog is
built to survive it: a compiled game or rule engine still consults brand-new `.pl` files at run
time, because runtime-loaded predicates run on DotProlog's own bytecode machine rather than
being turned into new machine code. Your text adventure from chapter 11 could be one file you
hand to a friend, and it could still load extra rooms from a `rooms.pl` beside it.

!!! note "An honest word about status"
    DotProlog is young. Nothing is published to NuGet, .NET's package registry, yet — which is
    why this book had you clone the repository and run everything from inside it, and why that
    remains the right way to work today. The pieces in this chapter are real and exercised by
    the project's tests, but expect edges. When something surprises you,
    [open an issue](https://github.com/kidoz/dotprolog/issues); early users shape a young
    project more than they usually realise.

## Where next

For DotProlog itself:

- The [language guide](../language-guide.md) lists everything the implementation supports — you
  have met perhaps half of it, and the rest now sits within reach.
- The [.NET integration guide](../dotnet-integration.md) covers embedding, contracts, and
  facades properly.
- The [architecture notes](../architecture/index.md) explain how the engine works inside, for
  the reader who has started wondering what backtracking looks like from underneath.

For Prolog the language, two classics, both free to read online: *Learn Prolog Now* by
Blackburn, Bos, and Striegnitz is a friendly second pass over the ground this book covered, and
it goes further; *The Art of Prolog* by Sterling and Shapiro is the deep book, the one that
shows what the language is truly capable of. Read them with a terminal open. Type things in.

And that is the book. Twelve chapters ago you installed an SDK; now you can teach a computer
facts, give it rules to reason with, and ask it questions it answers by logic — and you have a
game to prove it. That way of thinking stays with you in every language you use from here.
Thank you for reading, and happy questioning.

```prolog
knows(you, prolog).        % as of today, a fact
```
