# The Ember Crown of Greymere

An original, old-school fantasy text adventure written entirely in DotProlog.

The red star above ruined Gloamwatch Keep has awakened Lord Morvane, an oathbreaker who is neither
living nor dead. He has stolen the Ember Crown, and the farms around Greymere are turning to ash.
Explore seventeen locations, decipher the keep's bell ritual, survive its guardians, recover
the crown, and return it to Reeve Elowen. Along the way, rescue a missing bellkeeper, learn
herbal medicine, and choose whether to fight a restless knight or restore his forgotten oath.

The sample demonstrates:

- a world represented by Prolog facts and rules;
- reverse-path reasoning from one set of `passage/3` facts;
- mutable game state with dynamic predicates;
- an interactive `read/1` command loop;
- inventory, crafting, keyed barriers, equipment, healing, and deterministic combat;
- a recoverable sequence puzzle, optional quests, and an alternative to combat;
- an exploration map, quest journal, and conditional victory and death endings;
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

## Exploration and choices

The village is a safe base: `talk(herbalist).` teaches a recipe, and `rest.` restores
your fourteen health. The old road branches into a moonleaf garden. Inside the keep,
the courtyard leads to a well house, while the great hall leads north to the archive
and its bell tower. `journal.` lists objectives and clues; `map.` lists visited rooms
and their exits, marking unseen destinations as unexplored. Listed paths can still be locked.

| Command | Purpose |
| --- | --- |
| `talk(bellkeeper).` | Speak to Tomas if you can reach his prison below the well |
| `use(rope).` | Secure a permanent route down from the well house |
| `brew(herbal_tonic).` | After learning the recipe, consume moonleaf and spring water to make one tonic |
| `use(herbal_tonic).` | Heal eight health, capped at fourteen |
| `use(watch_oath).` | Release the knight peacefully when standing in the armory |
| `ring(dawn).` | Ring a tower bell; the other names are `noon` and `dusk` |
| `guard.` | Take an enemy turn with four extra protection, then gain two damage on your next attack |

The archive mural explains the bell sequence. A wrong note resets the sequence so you
can retry; completing it permanently breaks the sun seal on the final chamber. You
still need the bone warden's key. The rescue, crafting, and peaceful knight route are
optional, and the ending remembers the rescue and the knight's fate.

Morvane alternates between gathering cinders and striking for seven damage. When he
warns of a charged attack, guard before counterattacking. The bellkeeper's ash ward
reduces Morvane's damage by two while carried; your shield reduces enemy damage by one.
Protection always leaves at least one damage. Counterattack bonuses do not stack and
are lost when you leave the room or resolve the encounter.

Only `attack.` and `guard.` advance an enemy's turn. You can inspect clues, manage
items, heal, or retreat without a timed penalty. Brewing requires a room without a
live enemy. Ingredients and potions are finite; medicine is kept when you are already
at full health, and you can return to the village to recover. Equipment bonuses apply
automatically while you carry the item. Commands must contain concrete names, without
Prolog variables, and each new process begins a fresh game.

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

`winning_path.txt` follows the combat route, solves the bells, and guards against Morvane.
`mercy_path.txt` also explores the garden and prison, crafts a tonic, rescues Tomas, and
releases the knight peacefully. Both are full playthroughs and repeatable smoke tests:

```console
dotnet run --project samples/GreymereAdventure/GreymereAdventure.dplproj \
  < samples/GreymereAdventure/winning_path.txt

dotnet run --project samples/GreymereAdventure/GreymereAdventure.dplproj \
  < samples/GreymereAdventure/mercy_path.txt
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

Replace `on` with `ascii` for an illustrated transcript without colors. Compiler tests exercise
both routes in all three modes, bell recovery and barriers, crafting, rescue rewards,
guarding, rest, the exploration map, invalid commands, defeated enemies, and death:

```console
dotnet test --project tests/DotProlog.Compiler.Tests --filter-class '*GreymereAdventureTests'
```
