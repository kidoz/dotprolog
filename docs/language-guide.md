# Language guide

DotProlog's `StrictIso` mode implements ISO/IEC 13211-1:1995 with Technical Corrigenda 1–3,
ISO/IEC 13211-2:2000, and ISO/IEC TS 13211-3:2025. The declaration is backed by the repository's
[Part 1 traceability ledger](reference/iso-part1-conformance.md), its 608 standard-derived cases,
and a 763-case independent corpus executed through every engine path, plus the
[Parts 2 and 3 traceability ledger](reference/iso-parts2-3-conformance.md) and focused cross-path
tests. It does not claim SWI-Prolog compatibility.

## Language modes

The default `Extended` mode accepts the ISO Parts 1–3 surface plus documented DotProlog
extensions such as soft cut, `format/1,2,3`, `member/2`, and higher-order list predicates.

The opt-in `StrictIso` mode restricts predefined language features to the explicit ISO Parts 1–3
inventory. A source call to a known predefined extension is rejected with diagnostic `DPL1018`.
Runtime-constructed meta-goals and host bindings reject the same extensions with a catchable
`permission_error(access, implementation_specific_feature, Name/Arity)`. Arbitrarily named
predicates defined by the program remain valid, including a user definition whose name matches an
extended library predicate.

Strict mode starts from the standardized operator table rather than the additional predefined
operators in Extended and Modern modes. A program may still define any operator permitted by
`op/3`.

The opt-in `Modern` mode accepts the same surface as `Extended`, but starts the `double_quotes`
flag at `chars` rather than DotProlog's documented default `codes` — ISO/IEC 13211-1 leaves the
initial value implementation defined, so both are conforming defaults. A double-quoted token
therefore reads as a list of one-character atoms:

```prolog
?- "abc" = [L|Ls].
   L = a, Ls = [b,c].
```

This is the default the newer Prolog systems settled on, and it is what makes text convenient to
work with in DCGs. Nothing else about the mode differs from `Extended` today, and any mode may
still move the flag with `:- set_prolog_flag(double_quotes, codes).` Outside strict ISO mode the
flag also accepts `string`, reading `"..."` as a distinct string term with its own `string_*`
library; no mode defaults to it.

`Modern` is also the dialect whose extension direction is SWI-Prolog: when a predicate exists in
SWI and is adopted here, its behavior and error terms follow SWI's, and the coverage is recorded
feature by feature in the [SWI compatibility ledger](reference/swi-compatibility.md). The aligned
predicates land in the surface `Extended` and `Modern` share, so selecting `Modern` is about the
`chars` default rather than extra predicates.

A mode is a curated dialect, not a flag matrix. A program that wants a combination no mode names —
`double_quotes` starting at `atom`, say — sets the flag itself, or asks the host to seed it: the
`DotPrologFlags` project property, the `--flag` option, and the engine constructor's flag
overrides layer an initial value for a curated flag over the mode without leaving the profile.

Select a mode with the `PrologEngine` constructor, `dotnet prolog run --mode <name>`, or the
`DotPrologLanguageMode` property in a `.dplproj`.

## Terms and clauses

The reader supports variables, atoms, unbounded integers, rationals, finite floats, lists, structures, and
double-quoted code lists. Programs contain facts, rules, and directives:

```prolog
parent(ada, byron).
parent(byron, anne).

ancestor(X, Y) :-
    parent(X, Y).
ancestor(X, Y) :-
    parent(X, Z),
    ancestor(Z, Y).
```

The maximum compound arity is 255. Atom text is the language's text term; DotProlog deliberately
does not add the SWI-Prolog string type or alias string predicates to atoms.

## Control and errors

The extended control surface includes conjunction, disjunction, if-then-else, soft cut, negation
as failure, cut, `call/1..8`, `once/1`, `repeat/0`, and `ignore/1`. Strict mode excludes soft cut
and `ignore/1`.

Engine errors are catchable `error(Formal, Context)` terms:

```prolog
safe_divide(X, Y, Result) :-
    catch(
        Result is X / Y,
        error(evaluation_error(zero_divisor), _),
        Result = undefined
    ).
```

Ordinary Prolog failure and backtracking do not use CLR exceptions.

## Solutions and the database

`findall/3`, `bagof/3`, `setof/3`, `forall/2`, and `aggregate_all/3` collect or aggregate solutions.
Dynamic predicates support `asserta/1`, `assertz/1`, `retract/1`, `retractall/1`, `clause/2`, and
`abolish/1`.

`findall(Template, Goal, Answers)` returns one list and uses `[]` when `Goal` has no solutions.
`bagof(Template, Goal, Answers)` instead groups answers by each free variable in `Goal` and fails
when a group has no solutions. For example, with `kind(apple, fruit)`, `kind(pear, fruit)`, and
`kind(carrot, vegetable)`, backtracking over `bagof(Item, kind(Item, Kind), Items)` produces:

```text
Kind = fruit,     Items = [apple,pear]
Kind = vegetable, Items = [carrot]
```

Quantify a variable with `^` when it should not create groups:
`bagof(Item, Kind^kind(Item, Kind), Items)`. `setof/3` uses the same grouping rules, then sorts each
group in standard order and removes duplicates.

```prolog
:- dynamic item/1.

item(first).
item(second).
```

Dynamic predicates use the logical update view: a running goal sees the clauses that existed when
that goal began, even if it changes the predicate while enumerating.

## Modules, grammars, and operators

ISO modules separate their interface from one or more bodies:

```prolog
:- module(values).
:- export(value/1).
:- end_module(values).

:- body(values).
value(ok).
:- end_body(values).
```

Interfaces use `export/1`, `reexport/1,2`, and `metapredicate/1`; bodies use `import/1,2`.
`Module:Goal` selects a module explicitly. Operators, character conversions, flags, term I/O,
database operations, and meta-arguments all observe the calling module. Conflicting visibility,
missing interfaces, invalid exports, and implicit modification of imports are rejected.

Extended and Modern modes retain `module/2`, `use_module/1,2`, and `meta_predicate/1` as
compatibility extensions. StrictIso requires the standard Part 2 forms.

Definite clause grammars use `-->/2` and run through `phrase/2,3`:

```prolog
digits([D | Ds]) --> [D], { D >= 0'0, D =< 0'9 }, digits(Ds).
digits([]) --> [].
```

Part 3 terminal pushback (semicontexts) is supported:

```prolog
look_ahead(X), [X] --> [X].
```

`dynamic/1`, `multifile/1`, and `discontiguous/1` accept `Name//Arity` indicators. Grammar rules
cannot define grammar control constructs or expand over predefined procedures. `phrase/2` checks
that its input can be a list; `phrase/3` deliberately leaves its sequence arguments unchecked, the
implementation-defined lower-overhead choice permitted by the grammar specification.

In extended mode, soft cut is also recognized as a grammar control extension. In strict mode it is
an ordinary nonterminal, as required for additional grammar controls by Part 3.

`op/3` changes the program-owned operator table used by both reading and writing terms.

## Streams and text

DotProlog provides text and binary streams, stream aliases and properties, repositioning for
supported streams, configurable end-of-file actions, term I/O, character and character-code I/O,
and byte I/O.

The standard library also includes list processing, sorting, higher-order predicates, atom and
number conversion, term inspection, arithmetic, formatting, standard-order comparison, ordered
sets, AVL association lists, and the `library(error)` validation predicates around `must_be/2`.

## Known limits

- Clause selection uses first-argument indexing in the bytecode VM: a call with a bound first
  argument skips clauses whose first argument could never unify, and creates no choice point when
  only one clause can match. Build-time generated C# still tries clauses in order.
- Runtime-loaded source compiles to DotProlog bytecode, not new CLR IL.
- Build-time source compiles to direct-threaded generated C# blocks sharing the same explicit
  machine state as runtime bytecode.
- Constraint solving, tabling, and attributed variables are outside the current scope.

For the detailed and continually updated compatibility record, see
[COMPATIBILITY.md on GitHub](https://github.com/kidoz/dotprolog/blob/main/COMPATIBILITY.md).
