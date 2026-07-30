# DotProlog

A Prolog language implementation for .NET 10, written in C# 14.

<https://github.com/kidoz/dotprolog>

The goal is a first-class Prolog experience on the .NET SDK, the way C# and F# have one: `.dplproj` projects, a `plc` compiler, `dotnet prolog`, `dotnet new` templates, and NativeAOT publishing.

**Status: early, but usable.** `dotnet new prolog-console` through `dotnet publish -p:PublishAot=true`
works today, and `dotnet test` discovers Prolog tests under Microsoft.Testing.Platform. What is
missing: the `plc` compiler and generating IL for predicate bodies — a `.dplproj` currently embeds
its Prolog source and compiles it to bytecode at startup. Nothing is published to NuGet yet, though
the packages build: see [CHANGELOG.md](CHANGELOG.md) and [COMPATIBILITY.md](COMPATIBILITY.md).

## Hello, world

```prolog
% samples/HelloProlog/hello.pl
:- initialization(main).

main :-
    greeting(Greeting),
    write(Greeting),
    nl.

greeting('Hello! World!').
```

```console
$ dotnet run --project src/DotProlog.Tool -- run samples/HelloProlog/hello.pl
Hello! World!
```

Or, with [just](https://just.systems):

```console
$ just hello
```

## Calling Prolog from C#

```csharp
var engine = new PrologEngine();
engine.ConsultText("colour(red).  colour(green).  colour(blue).");

foreach (PrologSolution solution in engine.Query("colour(C)").Solutions())
{
    Console.WriteLine(solution["C"]);      // red, green, blue
}

bool ok = engine.Query("1 < 2").Prove();
```

Answers are produced on demand and marshalled into plain .NET objects as they arrive, so they stay valid after the query has moved on — and an unbounded goal is fine as long as you stop taking:

```csharp
var first = engine.Query("between(1, 1000000000, X)").Solutions().Take(4);
```

Predicates can also be called directly with .NET values. This is the surface the generated `.dplproj` facades sit on:

```csharp
var host = new PrologHost(engine.Machine);
PrologPredicate discount = host.Bind("discount", 3);

PrologValue[]? result = host.CallOnce(
    discount, PrologInput.Float(100.0), PrologInput.Integer(10), PrologInput.Output);
```

This works from F# and VB the same way. One engine runs one goal at a time and is not thread-safe.

## Prolog as a referenced project

`samples/PricingRules` is a `.dplproj` holding ordinary ISO Prolog plus a contract declaring its .NET
surface. `samples/PricingConsole` is a plain C# app that references it:

```xml
<ProjectReference Include="..\PricingRules\PricingRules.dplproj" />
```
```prolog
% pricing.dpli — modes and determinism live here, so pricing.pl stays portable
:- clr_module('Pricing').
:- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
:- clr_export(in_catalogue/1, semidet, [in(item, atom)]).
:- clr_export(bundle/2, nondet, [in(items, list(atom)), out(bundle, list(atom))]).
```
```csharp
IPricingModule pricing = PricingModule.Create();

pricing.Discount(100.0, 15);          // 85
pricing.InCatalogue("widget");        // true
foreach (var b in pricing.Bundle(["widget", "gadget"])) { /* streamed */ }
```

The facade is generated during the build, before the C# compiler runs. Nothing in the consuming code
mentions the engine, a goal, or a term — which is why the same reference works from F# and VB with no
extra work. That is checked rather than assumed: `samples/PricingFSharp` and
`samples/PricingVisualBasic` consume the same `.dplproj`, and the integration test builds and runs all
three.

The generated surface follows Microsoft's [F# component design
guidelines](https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/component-design-guidelines)
for "vanilla .NET" libraries: the `TryGetValue` pattern for `semidet` predicates with outputs, a named
record rather than a tuple for several outputs, collection interfaces rather than concrete types, null
checks at the boundary, and a `CancellationToken` on anything that streams. A predicate whose name
reads poorly in C# can be renamed in the contract, the equivalent of F#'s `[<CompiledName>]`:

```prolog
:- clr_export(nrev/2, det, [in(l, list(atom)), out(r, list(atom))], 'NaiveReverse').
```

```console
$ dotnet run --project samples/PricingConsole
100 less 15% = 85
total 1200 is gold
widget in catalogue: True
bundles of [widget, gadget]:
  [widget, gadget]
  [widget]
  [gadget]
  []
```

## Repository layout

| Path | What it is |
|---|---|
| `src/DotProlog.Syntax` | Lexer, ISO operator table, operator-precedence reader, diagnostics |
| `src/DotProlog.Runtime` | Tagged terms, heap, trail, choice points, bytecode VM, builtins |
| `src/DotProlog.Compiler` | Clause and directive lowering to bytecode, consult and embedding API |
| `src/DotProlog.CodeGen.CSharp` | `.dpli` contract reader and C# facade generator |
| `src/DotProlog.Build.Tasks` | MSBuild task that runs the generator |
| `src/DotProlog.Sdk` | The `DotProlog.Sdk` MSBuild SDK package |
| `src/DotProlog.Templates` | `dotnet new prolog-console`, `prolog-lib`, and `prolog-test` |
| `src/DotProlog.Testing` | Microsoft.Testing.Platform host for Prolog tests |
| `src/DotProlog.Tool` | The `dotnet prolog` command |
| `tests/` | Unit tests per component, plus end-to-end execution tests |
| `benchmarks/` | BenchmarkDotNet suite for the reader, compiler, and engine |
| `samples/HelloProlog` | The Hello World sample |
| `samples/PricingRules` | A `.dplproj`: Prolog rules plus their `.dpli` contract |
| `samples/PricingConsole`, `samples/PricingFSharp`, `samples/PricingVisualBasic` | C#, F#, and VB apps referencing it |
| `samples/GreetingApp` | A Prolog application built from a `.dplproj` |
| `samples/PricingTests` | Prolog tests in a `.dplproj`, run by `DotProlog.Testing` |
| `samples/AotAcceptance` | The NativeAOT acceptance sample |

## Common tasks

```console
just build          # build everything
just test           # run every test project
just format         # format all C# with CSharpier
just format-check   # fail if anything is unformatted
just check          # format-check + build + test
just run FILE.pl    # consult and run a Prolog file
just bench '*'      # run the benchmark suite
```

Without `just`, each recipe is a plain `dotnet` command — read the `justfile`.

## How it executes

Two paths are planned, and they are deliberately different:

```text
Build-time Prolog        : parser -> semantic IR -> generated C# -> Roslyn -> IL -> JIT/NativeAOT
Runtime consult / assert : parser -> semantic IR -> Prolog bytecode -> AOT-compatible bytecode VM
```

Only the second is implemented today. It never emits CLR IL, so it stays valid inside a NativeAOT process — runtime-loaded predicates execute as bytecode and are not turned into new machine code.

That is verified, not assumed. `samples/AotAcceptance` publishes to a self-contained native executable with no managed assemblies beside it, then at run time consults a `.pl` file it has never seen, enumerates solutions, asserts and retracts clauses, and catches an ISO error — with zero trimming or AOT warnings in the build. CI runs it on Windows, Linux, and macOS:

```console
$ DOTPROLOG_RUN_AOT_TESTS=1 dotnet test --project tests/Integration
```

Dynamic predicates use the logical update view, so a goal sees exactly the clauses that existed when it started:

```prolog
:- dynamic p/1.
p(1).
p(2).

% [1,2] — the asserted clauses are not visible to the goal that asserted them
?- findall(X, (p(X), assertz(p(9))), L).
```

The engine owns its control state: heap, trail, environment stack, choice-point stack, and argument registers are plain arrays, and Prolog calls are jumps inside a single dispatch loop. Prolog recursion depth therefore does not consume CLR stack, and failure is a return value rather than an exception. Last-call optimisation makes tail recursion run at constant stack depth.

## What the language supports today

| Area | Predicates |
|---|---|
| Terms | atoms, variables, integers, floats, double-quoted code lists, lists, structures |
| Control | `,/2`, `;/2`, `->/2`, `*->/2`, `\+/1`, `!/0`, `call/1..8`, `once/1`, `repeat/0`, `ignore/1`, `not/1`, `true/0`, `fail/0` |
| Exceptions | `throw/1`, `catch/3`, with catchable ISO `error/2` terms |
| All solutions | `findall/3`, `bagof/3`, `setof/3`, `forall/2`, `aggregate_all/3` (`count`, `bag`, `set`, `sum`, `max`, `min`) |
| Database | `assertz/1`, `asserta/1`, `retract/1`, `clause/2`, `retractall/1`, `abolish/1`, `:- dynamic` |
| Ranges | `between/3` |
| Loading | `consult/1`, `ensure_loaded/1` at run time |
| Unification | `=/2`, `\=/2` |
| Arithmetic | ISO-oriented integer and float evaluable functors; `is/2`, `=:=/2`, `=\=/2`, `</2`, `>/2`, `=</2`, `>=/2` |
| Standard order | `==/2`, `\==/2`, `@</2`, `@>/2`, `@=</2`, `@>=/2`, `compare/3` |
| Term inspection | `functor/3`, `arg/3`, `=../2`, `copy_term/2`, `term_variables/2` |
| Type tests | `var/1`, `nonvar/1`, `atom/1`, `number/1`, `integer/1`, `float/1`, `atomic/1`, `compound/1`, `callable/1`, `is_list/1`, `ground/1` |
| Text | `atom_length/2`, `atom_chars/2`, `atom_codes/2`, `number_chars/2`, `number_codes/2`, `char_code/2`, `atom_number/2`, `atom_concat/3`, `sub_atom/5`, `atomic_list_concat/2,3`, `upcase_atom/2`, `downcase_atom/2` |
| Lists | `length/2`, `append/3`, `member/2`, `memberchk/2`, `nth0/3`, `nth1/3`, `last/2`, `reverse/2`, `select/3`, `selectchk/3`, `subtract/3`, `intersection/3`, `union/3`, `delete/3`, `list_to_set/2`, `permutation/2`, `flatten/2`, `numlist/3`, `sum_list/2`, `max_list/2`, `min_list/2`, `max_member/2`, `min_member/2`, `pairs_keys_values/3`, `pairs_keys/2`, `pairs_values/2` |
| Higher order | `maplist/2..5`, `foldl/4,5`, `include/3`, `exclude/3`, `partition/4` |
| Sorting | `sort/2`, `sort/4`, `msort/2`, `keysort/2`, `predsort/3` |
| Integers | `succ/2`, `plus/3` |
| Output | `write/1,2`, `writeq/1,2`, `print/1,2`, `writeln/1`, `write_canonical/1,2`, `write_term/2,3`, `nl/0,1`, `format/1,2,3`, `tab/1` |
| Operators | `op/3`, `current_op/3` |
| Grammars | `-->/2` with `{}/1`, `!`, `\+`, pushback lists; `phrase/2`, `phrase/3` |
| Streams | `open/3,4` text and binary streams, `close/1,2`, configurable EOF actions, `current_stream/1`, `stream_property/2`, `set_stream_position/2`, current-stream selection, EOF inspection, flushing |
| Reading | term, character, character-code, and byte input/output; `read_term_from_atom/3`, `term_to_atom/2`; `char_conversion/2`, `current_char_conversion/2` |
| Modules | `:- module/2`, `use_module/1,2`, `:- meta_predicate/1`, `Module:Goal` |
| Directives | `:- Goal`, `:- initialization(Goal)`, `halt/0`, `halt/1` |

Control constructs are compiled in place inside a clause body, so cut scopes the way ISO specifies: opaque in the condition of if-then-else, transparent in its branches, clause-scoped elsewhere. A bootstrap library written in Prolog makes the same constructs reachable when a goal is assembled at run time and passed to `call/1`.

```prolog
sign(N, S) :- ( N < 0 -> S = negative ; N =:= 0 -> S = zero ; S = positive ).

safe_divide(X, Y, R) :-
    catch(R is X / Y, error(evaluation_error(zero_divisor), _), R = undefined).

item(1).
item(2).
item(3).

squares(L) :- findall(S, (item(N), S is N * N), L).   % L = [1,4,9]
```

Every error the engine raises is a catchable `error(Formal, Context)` term, so `existence_error`, `type_error`, `instantiation_error`, and `evaluation_error` can all be handled in Prolog rather than aborting the run.
`halt/1` validates its status before terminating: variables and non-integers remain catchable input
errors instead of being converted to exit code zero.
`compare/3` likewise distinguishes an invalid output type from an atom outside the order domain.
`arg/3` distinguishes an uninstantiated compound term and a negative index from its ordinary
zero-or-out-of-range failure cases.
`=../2` distinguishes partial lists from malformed list terms, requires an atomic one-element
construction list, and shares the advertised arity-255 limit with `functor/3`.
`current_op/3` validates bound priority, specifier, and name filters before enumeration.
Its solutions come from the operator-table snapshot current when the goal starts, even if `op/3`
changes the live table between solutions.
`clause/2` rejects static and built-in predicates as private procedures and validates a bound body
as callable. Attempts to retract a static procedure raise a catchable modification permission error.

```prolog
report(Rows) :-
    forall(member(Name-Qty, Rows), format("~w~t~12|~t~d~4+~n", [Name, Qty])).

total(Rows, Total) :- aggregate_all(sum(Q), member(_-Q, Rows), Total).

initials(Name, Initial) :- sub_atom(Name, 0, 1, _, Initial).
```

Terms are written in operator notation, and what `writeq/1` produces reads back as the same term —
brackets and spacing are decided by priority and by whether two adjacent tokens would otherwise lex
as one. `write_canonical/1` and `write_term/2` with `ignore_ops(true)` opt out.

```prolog
:- op(700, xfx, likes).

fact(alice likes bob).      % read with the operator declared just above

?- fact(F), write(F).       % alice likes bob
?- write_canonical(1+2*3).  % +(1,*(2,3))
```

An `:- op/3` directive takes effect while the file is still being read, so the file that declares an
operator can use it. The reader and the writer share one table per engine, so nothing leaks between
two engines in the same process.

Predicates written in Prolog rather than C# live in a standard library that every engine loads at
construction, which costs about 220 µs. A consulted file that defines one of them replaces it
outright, so a program is free to write its own `member/2` without inheriting extra solutions.

There is no string type: an atom is the only text term. The SWI-Prolog string predicates are absent
rather than aliased to their atom counterparts, because aliasing would let portable code compile
here and then behave differently.

`bagof/3` and `setof/3` group their solutions by whichever of the goal's variables are free —
those the caller can still see — and offer one group per binding of them. A variable is made
existential with `^/2`, and one occurring only under `\+` is never free, since negation proves a
goal but cannot bind anything. Both fail when the goal has no solutions, where `findall/3` returns
`[]`.

```prolog
class(peter, a).  class(ann, b).  class(pat, a).

?- bagof(N, class(N, C), L).     % C = a, L = [peter,pat] ;  C = b, L = [ann]
?- bagof(N, C^class(N, C), L).   % L = [peter,ann,pat]
```

A definite clause grammar is translated into ordinary clauses when it is loaded — each non-terminal
gains the list before it and the list after it — so nothing about the engine knows that grammars
exist. `{Goal}` escapes to a plain goal, `!` is a cut, `\+` consumes nothing, and a pushback list
rewrites the input that the rules after it will see.

```prolog
digits([D|T]) --> digit(D), digits(T).
digits([D])   --> digit(D).
digit(D)      --> [D], { D >= 0'0, D =< 0'9 }.

number(N)     --> digits(Ds), { number_codes(N, Ds) }.

?- phrase(number(N), "427", Rest).   % N = 427, Rest = []
```

`phrase/2` and `phrase/3` walk the control constructs themselves, so a body assembled at run time
works as well as one written as a rule. A rule asserted with `assertz/1` is translated too.

Text streams carry terms and characters, while binary streams carry bytes. `read/1` pulls text a
line at a time until the lexer finds a clause terminator, so a term can be read from a console as
soon as it is complete rather than after the input ends — a full stop inside `'a. b'`, inside
`3.14`, or inside `=..` is not one. An incomplete clause at end of input is a catchable
`syntax_error(unexpected_end_of_file)` rather than a silently truncated term. When the
`char_conversion` flag is on, mappings installed by `char_conversion/2` are applied to unquoted
input before tokenization; quoted text, escapes, and primitive character input remain raw. An input
stream's `eof_action(error|eof_code|reset)` controls reads after its first EOF marker, and
`close/2` supports forced best-effort cleanup. `open/3,4` rejects bound output arguments and alias
collisions before it touches the requested source/sink, and rejects non-source/sink terms with
`domain_error(source_sink, Culprit)`. Host-invalid pathname atoms remain inside that same catchable
domain boundary. Stream permission errors preserve that alias or handle as the culprit, and
malformed handles cannot wrap to another live stream. Bound character-input targets are validated
before any character is consumed. Character, code, and byte predicates apply ISO error priority
when their stream and value arguments are both invalid. Open, close, stream positioning, and term
I/O likewise validate option shape, domains, and option semantics before stream lookup in the
standard order. Host reader and writer failures remain recoverable through `catch/3` as
`system_error`; closing an already-disposed host stream follows the same policy, with `force(true)`
remaining best-effort. `write_term/2,3` supports the ISO `quoted`, `ignore_ops`, and `numbervars`
options with exact boolean validation. `read_term/2,3` rejects malformed options before consuming
the next term and reports bounded integer overflows as
`representation_error(max_integer|min_integer)`. It also enforces the advertised `max_arity` of
255 while reading, raising `representation_error(max_arity)` before constructing a larger compound.
Oversized float literals raise `syntax_error(float_overflow)` instead of becoming IEEE infinity;
underflow rounds to signed zero.

```prolog
main :-
    read_term(T, []),
    ( T == end_of_file -> true
    ; format("got ~q~n", [T]), main ).

?- with_output_to(atom(A), write(1+2)).      % A = '1+2'
?- open('data.pl', read, S), read(S, T), close(S).
```

`Machine.Output` remains what an embedding host sets, and it is `user_output` specifically — a
program that redirects itself with `set_output/1` or `with_output_to/2` cannot detach the host from
the stream it handed in. `halt/0` flushes and closes whatever the program opened.

A file that declares a module keeps to itself everything its export list omits. A predicate `p/1`
in module `m` is compiled under the name `m:p`, and a call inside `m` to something `m` defines is
rewritten to that name — so two files can each have a `helper/1` without ever meeting. Anything the
module does not define falls through to the global name, which is how the standard library and
`user` stay reachable.

```prolog
:- module(shapes, [describe/1]).

describe(N) :- helper(N).        % shapes' own helper/1
helper(N) :- write(area(N)).     % not exported, so not visible outside
```

```prolog
:- use_module(shapes).

helper(N) :- write(mine(N)).     % a different helper/1 entirely

?- describe(4).                  % area(4)
?- shapes:helper(9).             % area(9), reached by qualifying
```

An exported predicate is also given its plain name when nothing else has claimed it, which is what
lets a generated facade, `dotnet prolog run`, and an embedding host call it without knowing modules
exist. A goal that is only known at run time carries its module and is resolved when called, so a
closure handed to `maplist/3` finds the predicate it meant. `:- meta_predicate f(0, +, -)` says
which of a predicate's own arguments are goals.

Nothing in the engine knows modules exist: resolution is a rewrite performed while loading.

A control term assembled at run time and passed to `call/1` is lowered to VM bytecode through the
same control-construct compiler used for source clauses. Its cut is transparent within that
meta-called goal and opaque to the caller, as ISO specifies.
A non-callable term in a compiled clause body remains a runtime error: it is catchable if execution
reaches it, while normal control flow can leave it unevaluated.
`call/2..8` checks the arity of the resulting goal; arity 255 is supported and 256 raises the
catchable `representation_error(max_arity)`.

DotProlog does not claim ISO or SWI-Prolog compatibility. It runs 498 conformance cases encoded
from ISO/IEC 13211-1, all passing, but those are its own reading of the standard rather than an
independent suite — see [COMPATIBILITY.md](COMPATIBILITY.md), which also lists the known
differences.

## Diagnostics

Diagnostic identifiers are stable and product-specific: `DPL0xxx` from the reader, `DPL1xxx` from the compiler.

```text
hello.pl(4,12): error DPL0005: Expected '.' to end the clause but found 'b'.
```

## Starting a project

```console
$ dotnet new install DotProlog.Templates
$ dotnet new prolog-console -n HelloProlog
$ dotnet run --project HelloProlog
Hello from Prolog on .NET!
```

`prolog-console` builds a Prolog program as a .NET application; `prolog-lib` builds a rule set as a
typed library for C#, F#, and VB; and `prolog-test` creates a Prolog test executable that runs under
`dotnet test`. A project pins the DotProlog SDK on the element itself:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="DotProlog.Sdk" Version="1.0.0" />
```

A `.dplproj` publishes with NativeAOT like any other project:

```console
$ dotnet publish HelloProlog -c Release -r osx-arm64 -p:PublishAot=true
```

**Nothing is published to NuGet yet.** Package identity is settled as `DotProlog.*`, and the commands
above are verified against a local feed built by `dotnet pack`.

## Testing Prolog

Create a test project with `dotnet new prolog-test`. Any zero-arity predicate named `test_*` is a
test — it passes if it can be proved:

```prolog
test_tier_boundaries :-
    tier(1000, gold),
    tier(999, silver).
```
```console
$ dotnet new prolog-test -n PricingTests
$ cd PricingTests
$ dotnet test --project PricingTests.dplproj
Test run summary: Passed!
  total: 2
  failed: 0
  succeeded: 2
```

Each test runs in a fresh engine, so one cannot see clauses another asserted.

The .NET 10 SDK selects Microsoft.Testing.Platform through `global.json`. The standalone
`prolog-test` template includes that setting. A solution containing Prolog tests should keep the
same `test.runner` setting in its solution-root `global.json`; this repository does so for its mixed
xUnit and Prolog test suite.

## Building from source

```console
$ git clone https://github.com/kidoz/dotprolog.git
$ cd dotprolog
$ just check          # format-check, build, and test
```

.NET SDK 10.0 or later is the only requirement; everything else restores from NuGet.

## Releasing

Packages are `DotProlog.*`: `DotProlog.Runtime`, `DotProlog.Syntax`, `DotProlog.Compiler`,
`DotProlog.Testing`, `DotProlog.Tool`, `DotProlog.Sdk`, and `DotProlog.Templates`. Every
assembly-bearing package carries Source Link and a symbol package, so a debugger can step from a
package into the exact commit it was built from.

```bash
just pack        # every package into ./artifacts, with SHA256SUMS
```

`Prolog.NET`, which this project's brief originally proposed, is taken on NuGet by an unrelated
WAM-based .NET Prolog. `DotProlog.*` is what shipped instead.

Releasing is a tag. `.github/workflows/release.yml` runs on `v*`, re-runs every CI gate against the
tagged commit, checks the tag agrees with `VersionPrefix`, publishes native binaries for Linux,
Windows, and macOS, packs with checksums and an SBOM, opens a GitHub release whose notes are the
changelog section for that version, and only then pushes to NuGet — from a separate job in a
`release` environment, so a required reviewer can stand between the tag and the feed.

```bash
# 1. Move the version's changelog heading from "unreleased" to today's date.
# 2. Set VersionPrefix in Directory.Build.props if the version is changing.
# 3. Commit, then:
git tag v0.1.1 && git push origin v0.1.1
```

Nothing is published to NuGet yet, and the publication job fails unless `NUGET_API_KEY` is set.

## Licence

MIT — see [LICENSE](LICENSE).

Author: Aleksandr Pavlov &lt;ckidoz@gmail.com&gt;
