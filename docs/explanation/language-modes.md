# Language modes and text

A language mode groups related defaults and supported features into a profile. DotProlog's
Modern mode provides the ISO core together with documented extensions; StrictIso limits
predefined features to the ISO Parts 1–3 inventory. Selecting strict mode is useful when the
standardized surface is part of a program's requirements. Modern's adopted SWI predicates follow
SWI behavior feature by feature, as recorded in the
[compatibility ledger](../reference/swi-compatibility.md); this is not a general SWI compatibility claim.

## Text representation is a separate choice

Modern starts `double_quotes` at `chars`, so `"abc"` is a list of one-character atoms:
`[a,b,c]`. This lets grammar terminals and ordinary list processing work on the same character
representation. StrictIso starts it at `codes`, producing `[97,98,99]`. The ISO standard leaves
the initial value implementation-defined, so a character-list default alone does not imply a
nonstandard language.

The flag can also select an atom, or a distinct string term outside strict mode. These are
different term kinds, not interchangeable labels for the same value. In particular, string
predicates are not aliases for atom predicates. Strings are interned for the program's lifetime,
so a long-running host that creates unbounded distinct strings must account for their storage.

## Defaults and local directives have different scopes

A host override chooses the starting value for every source file without changing the mode's
supported features. A `set_prolog_flag/2` directive changes the remainder of its load unit; the
initial setting is restored when that unit ends. This allows a code-list program to keep its
text representation while using Modern features.

Generated code records its mode and initial `double_quotes` value. Requiring the same settings
in the destination engine keeps build-time parsing and later runtime consultation consistent.

For accepted values and errors, see the [language reference](../language-guide.md#language-modes).
For commands and project settings, see [configure language modes and flags](../how-to/configure-language.md).
