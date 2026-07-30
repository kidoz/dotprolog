# ISO processor characteristics

This page records DotProlog’s processor-defined choices for the ISO-oriented core. It is a
characteristics declaration, not a claim that every requirement of ISO/IEC 13211-1 has been
certified.

## Representation limits

| Characteristic | DotProlog choice |
|---|---|
| Integer model | Bounded signed tagged integers |
| `min_integer` | −576460752303423488 |
| `max_integer` | 576460752303423487 |
| `bounded` | `true` |
| Maximum predicate and compound arity | 255 |
| Float model | Finite IEEE 754 binary64 |
| Integer division rounding | Toward zero |

Integer results outside the tagged range raise `evaluation_error(int_overflow)`. Integer source
literals outside the range raise `representation_error(min_integer)` or
`representation_error(max_integer)`. Float arithmetic rejects NaN and infinity with the applicable
ISO evaluation error. A decimal float literal that overflows binary64 is a
`syntax_error(float_overflow)`; underflow rounds to signed zero.

## Bitwise arithmetic

Bitwise functions use two’s-complement signed integer semantics. In particular, DotProlog fixes the
implementation-defined examples as follows:

| Expression | Result |
|---|---:|
| `\ 10` | −11 |
| `-10 \/ 12` | −2 |
| `-10 /\ 12` | 4 |
| `xor(-10, 12)` | −6 |
| `-16 << 2` | −64 |
| `-16 >> 2` | −4 |

Right shift is sign-extending. A left shift whose result cannot be represented as a tagged integer
raises `evaluation_error(int_overflow)`.

## Text and syntax

Atoms and source text use .NET Unicode strings. The reader accepts Unicode source characters and
the ISO numeric character escapes supported by the language guide. Character predicates require a
one-character atom as represented by one .NET UTF-16 code unit.

The initial `double_quotes` flag is `codes`. The initial `char_conversion` flag is `off`.
Character conversion applies to unquoted lexical input while quoted text, escapes, character-code
literal payloads, and primitive character input remain unchanged.

## Procedures, errors, and streams

The initial `unknown` flag is `error`, so calling an undefined procedure raises
`existence_error(procedure, Name/Arity)`. Dynamic predicates use the logical update view.

Text streams use the host .NET text readers and writers; binary streams use raw bytes. File-system
names, invalid paths, permissions, seekability, and durable I/O failures follow the host operating
system, translated to the documented Prolog `source_sink`, permission, and `system_error` terms.
The permanent `user_input`, `user_output`, and `user_error` streams are text streams and are not
repositionable.

## Modules

A module text starts with one `module/2` declaration. Selected imports must name predicates the
source module exports; malformed selections and two modules supplying the same visible predicate
are rejected during source preparation. Local definitions take precedence over imports.

An exported predicate is also published under its plain name when that name is still free. The
first loaded export therefore owns a plain-name alias; later modules remain reachable through
qualification or an unambiguous import. Loading source and its relationship to files are DotProlog
extensions rather than claims about the Part 2 filesystem model.

## Definite clause grammars

Grammar alternative `|` is predefined as `xfy` at priority 1105. DotProlog supports terminal
semicontexts, `Name//Arity` in `dynamic/1`, `multifile/1`, and `discontiguous/1`, and the standard
grammar control constructs. The additional soft-cut grammar forms follow the corresponding
DotProlog control semantics.

If an ordinary `Name/(Arity+2)` clause and `Name//Arity` grammar rule coexist, DotProlog combines
their expanded clauses into one procedure in source order. Grammar-rule heads that are themselves
grammar control constructs or that expand to a predefined procedure are rejected.

`phrase/2` reports `type_error(list, Culprit)` when its sequence cannot be a list. `phrase/3`
selects the specification's implementation-defined unchecked option for its second and third
arguments. A partial terminal list inside the grammar body raises `instantiation_error`; an
improper terminal list raises `type_error(list, Culprit)`.
