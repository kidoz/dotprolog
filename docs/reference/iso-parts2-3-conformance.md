# ISO Parts 2 and 3 conformance

This ledger maps the normative areas of ISO/IEC 13211-2:2000 and ISO/IEC TS 13211-3:2025 to
executable DotProlog evidence. It records clause identifiers and implementation behavior without
reproducing the licensed publications.

Evidence codes are:

- **M** — focused managed tests in `ModuleTests`
- **G** — focused grammar tests in `GrammarTests`
- **C** — generated-C# installation and execution
- **A** — published NativeAOT execution
- **P** — processor choice recorded in `iso-processor-characteristics.md`

## Part 2 modules

| Area | Implemented behavior | Evidence |
|---|---|---|
| 4.4, 6.2.1 module `user` | Unbracketed text is prepared as `user`; an explicit interface may precede its bodies | M/P |
| 5.1, 6.2.3–6.2.5 module text | Interfaces and multiple bodies are bracketed, paired, ordered per module, non-contiguous, and nestable only through `user` | M/C/A |
| 5.2.1 module syntax | `:/2` is predefined at priority 600 and module-local operator tables govern reading and writing | M/P |
| 6.2.4 interface directives | `export/1`, `reexport/1,2`, `metapredicate/1`, `op/3`, `char_conversion/2`, and `set_prolog_flag/2` prepare interface state | M/C |
| 6.2.5 body directives | `import/1,2` implement whole and selective import; body reader state accumulates across bodies | M/C |
| 6.2.6 clauses | Heads belong to their body module; imported definitions and predefined heads are rejected | M |
| 6.3 visible database | Local definitions, imports, and transitive re-exports resolve without ambiguous visible predicates | M/C |
| 6.4 calling context | Static and runtime qualification propagates through meta-arguments and control constructs | M/C/A |
| 6.4.2 context-sensitive built-ins | Operators, conversion, flags, term input/output, reflection, and database operations use module state | M/C/A |
| 6.5 term/clause conversion | Static and dynamic module clauses retain inspectable terms with shared variables and source-level bodies | M/C |
| 6.6–6.7 execution | Qualified calls select the qualifying module; missing modules and procedures carry qualified ISO errors | M/A |
| 6.8 predicate properties | Static/dynamic, public/private, built-in, multifile, exported, metapredicate, imported-from, and defined-in properties are accepted | M/C |
| 6.9.1 flag | `colon_sets_calling_context` is fixed to `true` | M/P |
| 6.10 module errors | Module existence, implicit access/modification, property domain, and qualification type errors are catchable `error/2` terms | M |
| 7.2 module predicates | `current_module/1` and `predicate_property/2` enumerate the prepared module database | M/C |
| 7.3 retrieval | Module-aware `clause/2` and `current_predicate/1` use the visible database and preserve re-execution | M/C |
| 7.4 modification | `asserta/1`, `assertz/1`, `retract/1`, and `abolish/1` target the defining module and reject implicit imported modification | M/C/A |

Part 2 permits optional inaccessible-procedure and dynamic-module extensions. DotProlog implements
neither: every ISO-module procedure is reachable by explicit qualification, and modules are created
by preparing interfaces rather than by a runtime module-creation predicate. Filesystem loading and
the plain-name alias assigned to the first free export are documented compatibility extensions.

## Part 3 definite clause grammars

| Area | Implemented behavior | Evidence |
|---|---|---|
| 7.4.2, 7.13.3 indicators | `dynamic/1`, `multifile/1`, and `discontiguous/1` accept `Name//Arity` | G/C |
| 7.4.4 restrictions | Grammar heads cannot be grammar controls or expand over predefined procedures | G |
| 7.5.1 coexistence choice | Ordinary `Name/(Arity+2)` clauses and `Name//Arity` rules combine in source order | G/P |
| 7.13.1–7.13.2 rules and semicontexts | Ordinary rules, terminal sequences, empty bodies, pushback, and look-ahead translate at preparation time | G/C |
| 7.13.4 missing nonterminal | The processor uses the Part 1 `existence_error(procedure, Name/Arity)` representation | G/P |
| 7.13.5 logical expansion | Static, asserted, and runtime-built grammar bodies share the prescribed expansion semantics | G/C/A |
| 7.14.1–7.14.10 required controls | Empty and non-empty terminals, conjunction, alternatives, if-then-else, braces, `call//1`, `phrase//1`, and cut are implemented | G/C/A |
| 7.14.11–7.14.12 optional controls | `\+//1` and standalone `->//2` are present with the specified behavior | G/P |
| 8.18.1 `phrase/2,3` | Parsing/generation, re-execution, runtime expansion, error priority, and third-argument steadfastness are covered | G/C/A |
| 8.18.1.3 errors | Variables, non-callable bodies, partial/improper terminals, and selected sequence-list errors produce the specified terms | G |

DotProlog selects both implementation-defined sequence checks for `phrase/2`, producing
`type_error(list, Sequence)` when no list instance exists. `phrase/3` selects the permitted
unchecked option for its second and third arguments, while remaining steadfast in the third.
An invalid semicontext is rejected during source preparation. Extended mode additionally recognizes
soft cut as a grammar control extension; StrictIso treats it as an ordinary nonterminal.

## Continuing gate

A release must keep the focused module and grammar suites green in both Extended and StrictIso,
preserve module metadata in generated C#, and execute the strict generated/consulted scenario in a
warning-free NativeAOT publication. The independent Logtalk corpus remains Part 1 evidence and is
not represented here as independent Part 2 or Part 3 certification.
