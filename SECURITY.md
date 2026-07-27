# Security policy

## Supported versions

DotProlog has not had a release yet. Once 0.1.0 ships, the latest released minor version is the
one that receives security fixes; older ones do not.

| Version | Supported |
|---|---|
| 0.1.x | Once released |

## Reporting a vulnerability

Report privately through [GitHub Security Advisories](https://github.com/kidoz/dotprolog/security/advisories/new),
or by email to <ckidoz@gmail.com>. Please do not open a public issue for a vulnerability.

Include what you have: the version, a description, and the smallest program that shows the problem.
You should get an acknowledgement within seven days, and an assessment within thirty.

## What counts

DotProlog runs Prolog programs. The engine's job is to run what it is given, so a Prolog program
doing something destructive is not itself a vulnerability — it is the program doing what it says.
What matters is when the engine does something the program did not ask for:

- Memory corruption, or reading or writing outside the heap, trail, or stacks. The engine is
  written in safe C#, so this should be impossible; a way to do it is a serious bug.
- A crash of the host process that a Prolog program can cause without calling `halt/0` — anything
  the host cannot catch as a `PrologException`.
- Escaping the streams a host granted: reading or writing a file a program was not given.
- A generated facade or entry point that emits C# doing something the contract did not describe.
- Reading a term, or consulting a file, that executes code as a side effect of parsing it.

## What does not count

- A Prolog program that loops forever, exhausts memory, or grows the heap without bound. The engine
  has no execution limits, and adding them is a feature request rather than a fix.
- `consult/1` running the directives of the file it loads. That is what consulting means; do not
  consult files you do not trust.
- A host that hands `user_input` or a writable stream to an untrusted program and is surprised by
  what the program does with it.

DotProlog is not a sandbox. A Prolog program has whatever access the process running it has.
