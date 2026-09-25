# The Ember Crown of Greymere

An original, old-school fantasy text adventure written entirely in DotProlog.

The red star above ruined Gloamwatch Keep has awakened Lord Morvane, an oathbreaker who is neither
living nor dead. He has stolen the Ember Crown, and the farms around Greymere are turning to ash.
Explore the keep, gather the tools needed to open its sealed crypt, survive its guardians, recover
the crown, and return it to Reeve Elowen.

The sample demonstrates:

- a world represented by Prolog facts and rules;
- reverse-path reasoning from one set of `passage/3` facts;
- mutable game state with dynamic predicates;
- an interactive `read/1` command loop;
- inventory, keyed barriers, equipment, healing, and deterministic combat;
- conditional narration and complete victory and death endings.
- optional ANSI illustrations, enemy portraits, and health bars, written in Prolog.

## Play

From the repository root:

```console
dotnet run --project samples/GreymereAdventure/GreymereAdventure.dplproj
```

Commands are Prolog terms and must end with a period. Start with:

```text
talk(reeve).
go(north).
look.
help.
```

## Illustrations

Enable the illustrated mode at the command prompt:

```prolog
graphics(on).
```

This displays Gloamwatch Keep, redraws your current room, and enables colored room art,
enemy portraits, health bars, and victory/death illustrations. Rooms are illustrated again
on entry or `look.`; defeated enemies no longer appear. Health bars update after enemy
attacks and healing, and with `status.`. Commands still end with a period.

Three modes are available:

| Command | Presentation |
| --- | --- |
| `graphics(on).` | ASCII artwork with ANSI colors |
| `graphics(ascii).` | The same artwork without terminal escape sequences |
| `graphics(off).` | Plain narrative text (the default) |

Artwork fits a 60-column terminal and uses ordinary ASCII characters, so it does not
require Unicode block glyphs or a special font. Narration wraps normally. Output scrolls
without clearing the screen, moving the cursor, or replacing the terminal's scrollback.
Color is decorative: danger labels and numerical health remain visible in every mode.

Graphics are explicitly opt-in. The sample does not detect terminal capabilities, redirected
streams, or `NO_COLOR`. Use `graphics(ascii).` if your terminal does not render ANSI colors
correctly, and keep the default mode for plain transcripts. Some Windows console hosts
require virtual-terminal processing to be enabled; see
[Microsoft's terminal documentation](https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences).

### Why this approach

The artwork and renderer live in `greymere_art.pl`; the game supplies the current room,
live enemy, and health. This keeps the sample entirely Prolog and adds no package dependency
or runtime feature. The renderer resets color after each styled write.

[Spectre.Console Canvas](https://spectreconsole.net/console/widgets/canvas/) is a useful
alternative for pixel-style drawing, but would need a C# presentation layer here. Unicode
block art would increase detail while introducing glyph/font requirements. A persistent
full-screen UI would also need resize and input handling. For this small command-driven
adventure, optional ANSI character art is the simpler fit.

## Verify the winning path

`winning_path.txt` is a full playthrough and doubles as a repeatable smoke test:

```console
dotnet run --project samples/GreymereAdventure/GreymereAdventure.dplproj \
  < samples/GreymereAdventure/winning_path.txt
```

The final line of output should be:

```text
                    *** YOU ARE VICTORIOUS ***
```

To smoke-test the colored version in a POSIX shell:

```sh
{ printf 'graphics(on).\n'; cat samples/GreymereAdventure/winning_path.txt; } |
  dotnet run --project samples/GreymereAdventure/GreymereAdventure.dplproj
```

Replace `on` with `ascii` for an illustrated transcript without colors. Compiler tests also
exercise all three modes, mode switching, invalid settings, defeated enemies, and death.
