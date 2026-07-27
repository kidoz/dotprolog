# Compatibility

## The claim

DotProlog **does not claim ISO or SWI-Prolog compatibility**, and will not until published
conformance tests verify it. What follows is what is implemented, what is deliberately absent, and
where behaviour is known to differ. It is a description, not a conformance statement.

## What has been measured

**155 conformance cases encoded from ISO/IEC 13211-1 clause 8, all passing.** They live in
[`tests/conformance/iso_conformance.pl`](tests/conformance/iso_conformance.pl) as ordinary Prolog —
a goal, and what the standard says that goal does — and run as part of the test suite. They cover
unification, type testing, term comparison, term construction and decomposition, arithmetic and its
comparisons, the clause and database predicates, all-solutions predicates, `op/3`, logic and
control, atomic term processing, and `catch/3`, with an emphasis on the error terms, which is where
implementations diverge most.

**These cases are our own encoding of the standard, not third-party verification.** Ulrich
Neumerkel's conformity tables carry no licence, and the Logtalk suite is written against its own
test framework, so neither could be vendored. Reading the standard and encoding it is worth more
than nothing and less than an independent suite, and this file will say so until an independent one
has been run.

Writing them was worth it immediately: the first run found four real defects.

- `assertz(4)` raised an exception `catch/3` could not catch, aborting the run. Several standard
  errors were raw messages rather than Prolog terms.
- `functor(F, foo(a), 1)` gave `type_error(atom, …)` where the standard says `type_error(atomic, …)`.
- `X =.. [_, a]` gave a type error where the standard says `instantiation_error`.
- `call((fail, 4))` failed instead of raising `type_error(callable, (fail,4))`; a meta-called goal
  is now checked whole before any of it runs.

Beyond the conformance cases, the engine is covered by 675 of its own tests, an integration suite
that builds and runs the C#, F#, and Visual Basic samples, and a NativeAOT acceptance test.

## Implemented

| Area | Status |
|---|---|
| Terms, unification, backtracking, cut | Implemented |
| Control constructs, `call/1..8` | Implemented |
| Exceptions, ISO `error/2` terms | Implemented |
| Arithmetic, standard order, `compare/3` | Implemented |
| `findall/3`, `bagof/3`, `setof/3`, `forall/2` | Implemented |
| Database predicates, logical update view | Implemented |
| Text, list, sorting, and higher-order library | Implemented |
| `op/3`, `current_op/3`, operator-aware writing | Implemented |
| DCGs, `phrase/2,3` | Implemented |
| Streams, `read/1`, `read_term/2,3`, character I/O | Implemented |
| Modules, `use_module/1,2`, `meta_predicate/1` | Implemented |

## Absent, deliberately

| Feature | Why |
|---|---|
| A string type, and the SWI string predicates | An atom is the only text term. Aliasing the string predicates to atoms would let portable code compile here and then behave differently. |
| Binary streams, stream repositioning | A Prolog program that needs bytes is better served by its host. |
| `set_prolog_flag/2` acting on anything | Accepted and ignored so that portable files load. |
| Constraint solving, tabling, attributed variables | Out of scope for 0.1.0. |

## Known differences

| Behaviour | DotProlog | Elsewhere |
|---|---|---|
| Cut inside a goal reached through `call/1` | Local to that goal; prunes nothing outside it | ISO: opaque, same result; SWI: same |
| `\+ 4` | `type_error(callable, 4)` — the inner goal | Same |
| `findall(X, (G, !), L)` | Does not stop at the first solution, because the cut is local | Stops at the first |
| A character is a UTF-16 code unit | A character outside the Basic Multilingual Plane is two codes, and `atom_length/2` counts it as two | SWI counts code points |
| `read_term/2` `singletons/1` option | `domain_error`, rather than a wrong answer | Supported |
| Two modules exporting the same name | The first loaded gets the unqualified name | SWI reports a conflict |
| Clause selection | Linear scan; no first-argument indexing | Indexed |

## Platforms

Built and tested on .NET 10. NativeAOT publishing is exercised on the host platform of whatever
machine runs the acceptance test; the packages themselves are platform-neutral.
