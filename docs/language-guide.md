# Language guide

DotProlog implements an ISO-oriented Prolog core. It does **not** claim full ISO or SWI-Prolog
compatibility; the repository's conformance cases are useful evidence, but not independent
verification.

## Terms and clauses

The reader supports variables, atoms, bounded integers, finite floats, lists, structures, and
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

The supported control surface includes conjunction, disjunction, if-then-else, soft cut, negation
as failure, cut, `call/1..8`, `once/1`, `repeat/0`, and `ignore/1`.

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

Modules declare exports with `module/2`, import with `use_module/1,2`, and qualify calls with
`Module:Goal`. `meta_predicate/1` declares goal arguments. A module declaration must be the first
term in its source, selected imports must be exported predicate indicators, and conflicting
imports are rejected.

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

`op/3` changes the program-owned operator table used by both reading and writing terms.

## Streams and text

DotProlog provides text and binary streams, stream aliases and properties, repositioning for
supported streams, configurable end-of-file actions, term I/O, character and character-code I/O,
and byte I/O.

The standard library also includes list processing, sorting, higher-order predicates, atom and
number conversion, term inspection, arithmetic, formatting, and standard-order comparison.

## Known limits

- Clause selection is currently a linear scan; first-argument indexing is not implemented.
- Runtime-loaded source compiles to DotProlog bytecode, not new CLR IL.
- Build-time source compiles to direct-threaded generated C# blocks sharing the same explicit
  machine state as runtime bytecode.
- Constraint solving, tabling, and attributed variables are outside the current scope.

For the detailed and continually updated compatibility record, see
[COMPATIBILITY.md on GitHub](https://github.com/kidoz/dotprolog/blob/main/COMPATIBILITY.md).
