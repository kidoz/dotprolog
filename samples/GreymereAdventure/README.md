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
