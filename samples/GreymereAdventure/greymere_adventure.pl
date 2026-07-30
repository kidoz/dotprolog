% The Ember Crown of Greymere
% An original, old-school fantasy text adventure for DotProlog.

:- module(greymere_adventure, [main/0]).

:- dynamic here/1.
:- dynamic player_hp/1.
:- dynamic holding/1.
:- dynamic at/2.
:- dynamic alive/1.
:- dynamic enemy_hp/2.
:- dynamic flag/1.

:- initialization(main).

% ---------------------------------------------------------------------------
% Starting state
% ---------------------------------------------------------------------------

here(village_square).
player_hp(14).
holding(hunter_knife).

at(sun_medallion, ruined_chapel).
at(healing_draught, flooded_vault).
at(steel_sword, old_armory).
at(iron_shield, old_armory).
at(lantern, gatehouse).

alive(mire_goblin).
alive(oathless_knight).
alive(bone_warden).
alive(morvane).

enemy_hp(mire_goblin, 4).
enemy_hp(oathless_knight, 6).
enemy_hp(bone_warden, 10).
enemy_hp(morvane, 14).

% ---------------------------------------------------------------------------
% Story world
% ---------------------------------------------------------------------------

room_title(village_square, 'Greymere Village Square').
room_title(old_road, 'The Old North Road').
room_title(keep_gate, 'Gate of Gloamwatch Keep').
room_title(outer_courtyard, 'The Outer Courtyard').
room_title(gatehouse, 'The Fallen Gatehouse').
room_title(great_hall, 'The Great Hall').
room_title(ruined_chapel, 'Chapel of the First Sun').
room_title(old_armory, 'The Old Armory').
room_title(crypt_stair, 'The Lightless Stair').
room_title(ossuary, 'The Ossuary').
room_title(flooded_vault, 'The Flooded Vault').
room_title(inner_sanctum, 'The Ember Sanctum').

room_description(village_square,
    'Rain needles the roofs of Greymere. The villagers have barred their doors, and Reeve Elowen waits beneath the dead ash tree.').
room_description(old_road,
    'The north road climbs through black pines. Wolf tracks vanish beneath a fall of soot that no hearth made.').
room_description(keep_gate,
    'Gloamwatch Keep crowns the ridge. A gate of green-black iron stands between you and its abandoned court.').
room_description(outer_courtyard,
    'Broken gargoyles stare into a weed-choked court. The keep doors yawn north; a fallen gatehouse leans to the east.').
room_description(gatehouse,
    'Splintered bunks and rusted spearheads litter the gatehouse. A brass lantern glows beneath a stolen heap of blankets.').
room_description(great_hall,
    'Tattered banners hang above a long stone table. The chapel lies east, the armory west, and cold air rises from stairs below.').
room_description(ruined_chapel,
    'Dawn is painted on the cracked apse, though no sunlight reaches it. A silver medallion rests upon the altar.').
room_description(old_armory,
    'Weapon racks sag beneath centuries of dust. One sword and one shield remain bright, watched by an armored corpse.').
room_description(crypt_stair,
    'Your lantern reveals steps carved through the roots of the hill. Old names cover the walls, each scratched out except one: Morvane.').
room_description(ossuary,
    'Skulls fill alcoves from floor to ceiling. A door of red stone waits to the north, and dark water glimmers to the east.').
room_description(flooded_vault,
    'Knee-deep water hides the vault floor. A sealed crystal flask floats inside an open coffer.').
room_description(inner_sanctum,
    'Embers orbit a basalt throne. Lord Morvane stands before it, neither living nor dead, with Greymere''s stolen crown burning above his hand.').

% Each passage is stated once. exit/3 reasons out the reverse direction.
passage(village_square, north, old_road).
passage(old_road, north, keep_gate).
passage(keep_gate, north, outer_courtyard).
passage(outer_courtyard, north, great_hall).
passage(outer_courtyard, east, gatehouse).
passage(great_hall, east, ruined_chapel).
passage(great_hall, west, old_armory).
passage(great_hall, down, crypt_stair).
passage(crypt_stair, down, ossuary).
passage(ossuary, east, flooded_vault).
passage(ossuary, north, inner_sanctum).

opposite(north, south).
opposite(south, north).
opposite(east, west).
opposite(west, east).
opposite(up, down).
opposite(down, up).

exit(From, Direction, To) :-
    passage(From, Direction, To).
exit(From, Direction, To) :-
    passage(To, Opposite, From),
    opposite(Direction, Opposite).

% ---------------------------------------------------------------------------
% Names, lore, and creatures
% ---------------------------------------------------------------------------

item_name(hunter_knife, 'hunter''s knife').
item_name(keep_key, 'Gloamwatch keep key').
item_name(lantern, 'brass lantern').
item_name(sun_medallion, 'sun medallion').
item_name(steel_sword, 'Gloamwatch steel sword').
item_name(iron_shield, 'iron shield').
item_name(healing_draught, 'healing draught').
item_name(bone_key, 'bone key').
item_name(ember_crown, 'Ember Crown').

item_description(hunter_knife,
    'A practical blade. It has dressed more rabbits than monsters, but the edge is honest.').
item_description(keep_key,
    'A long iron key bearing the worn wolf sigil of Gloamwatch Keep.').
item_description(lantern,
    'Blue witchlight burns inside this old brass lantern without oil or heat.').
item_description(sun_medallion,
    'The medallion is warm. Its rays were engraved to turn restless dead and pierce their shadow-magic.').
item_description(steel_sword,
    'The balanced arming sword still bears the words: Stand between the dark and the door.').
item_description(iron_shield,
    'A heavy round shield. The painted white wolf has nearly faded away.').
item_description(healing_draught,
    'A ruby liquid in a crystal flask. One swallow should restore six health, up to the maximum of fourteen.').
item_description(bone_key,
    'Seven finger bones have been bound into the shape of a key. It is unpleasantly cold.').
item_description(ember_crown,
    'Greymere''s lost crown is black iron set with a living coal. In worthy hands, its fire protects rather than consumes.').

enemy_name(mire_goblin, 'mire goblin').
enemy_name(oathless_knight, 'oathless knight').
enemy_name(bone_warden, 'bone warden').
enemy_name(morvane, 'Lord Morvane, the Cinder Wraith').

enemy_location(mire_goblin, gatehouse).
enemy_location(oathless_knight, old_armory).
enemy_location(bone_warden, ossuary).
enemy_location(morvane, inner_sanctum).

enemy_damage(mire_goblin, 2).
enemy_damage(oathless_knight, 2).
enemy_damage(bone_warden, 3).
enemy_damage(morvane, 4).

undead(oathless_knight).
undead(bone_warden).
undead(morvane).

defeat_words(mire_goblin,
    'The goblin drops its notched cleaver and escapes through a gap too small for you to follow.').
defeat_words(oathless_knight,
    'The empty armor falls apart. A whisper escapes its helm: My watch is ended.').
defeat_words(bone_warden,
    'The warden collapses into lifeless bones. A crooked key clatters among them.').
defeat_words(morvane,
    'Sun-fire tears through Morvane''s shadow. With a cry like a dying furnace, the wraith is gone. The Ember Crown falls upon the throne.').

drop_on_defeat(bone_warden, bone_key).
drop_on_defeat(morvane, ember_crown).

% ---------------------------------------------------------------------------
% Presentation
% ---------------------------------------------------------------------------

banner :-
    nl,
    writeln('============================================================'),
    writeln('              THE EMBER CROWN OF GREYMERE'),
    writeln('============================================================'),
    writeln('An old-school fantasy adventure written entirely in Prolog.'),
    nl,
    writeln('For three nights, a red star has burned above ruined Gloamwatch'),
    writeln('Keep. Now the dead walk and winter grain turns to ash. Reeve'),
    writeln('Elowen believes the traitor-lord Morvane has awakened below'),
    writeln('the keep and stolen the Ember Crown that once guarded Greymere.'),
    nl,
    writeln('Type help. for commands. Every command ends with a period.'),
    nl.

look :-
    here(Room),
    room_title(Room, Title),
    nl,
    format('--- ~w ---~n', [Title]),
    room_description(Room, Description),
    writeln(Description),
    show_enemy(Room),
    show_items(Room),
    show_exits(Room).

show_enemy(Room) :-
    (   enemy_here(Room, Enemy)
    ->  enemy_name(Enemy, Name),
        enemy_hp(Enemy, Health),
        format('DANGER: ~w is here (health ~d).~n', [Name, Health])
    ;   true
    ).

show_items(Room) :-
    forall(
        at(Item, Room),
        (item_name(Item, Name), format('You see the ~w here.~n', [Name]))
    ).

show_exits(Room) :-
    write('Exits:'),
    forall(exit(Room, Direction, _), format(' ~w', [Direction])),
    nl.

help :-
    nl,
    writeln('Commands (remember the final period):'),
    writeln('  look.                 describe your location'),
    writeln('  go(north).            travel north, south, east, west, up, or down'),
    writeln('  talk(reeve).          speak to someone nearby'),
    writeln('  take(lantern).        pick up a visible item'),
    writeln('  drop(lantern).        put down a carried item'),
    writeln('  examine(lantern).     inspect a visible or carried item'),
    writeln('  attack.               fight the creature in your location'),
    writeln('  use(healing_draught). use a carried item'),
    writeln('  inventory.            list your equipment'),
    writeln('  status.               show health and combat strength'),
    writeln('  help.                  show these commands'),
    writeln('  quit.                  leave the story').

inventory :-
    nl,
    (   holding(_)
    ->  writeln('You are carrying:'),
        forall(
            holding(Item),
            (item_name(Item, Name), format('  - ~w~n', [Name]))
        )
    ;   writeln('You are carrying nothing.')
    ).

status :-
    player_hp(Health),
    weapon_damage(BaseDamage),
    shield_reduction(Protection),
    format('Health: ~d/14. Weapon damage: ~d. Shield protection: ~d.~n',
        [Health, BaseDamage, Protection]),
    (   holding(sun_medallion)
    ->  writeln('The sun medallion adds 2 damage against undead creatures.')
    ;   true
    ).

% ---------------------------------------------------------------------------
% Movement and barriers
% ---------------------------------------------------------------------------

go(Direction) :-
    here(From),
    (   exit(From, Direction, To)
    ->  enter(From, To)
    ;   format('There is no path ~w from here.~n', [Direction])
    ).

enter(From, To) :-
    (   can_cross(From, To)
    ->  retract(here(From)),
        assertz(here(To)),
        look
    ;   true
    ).

can_cross(keep_gate, outer_courtyard) :-
    !,
    (   flag(keep_unlocked)
    ->  true
    ;   holding(keep_key)
    ->  assertz(flag(keep_unlocked)),
        writeln('The reeve''s key groans in the lock. Gloamwatch opens for the first time in sixty years.')
    ;   writeln('The iron gate is locked. Someone in Greymere may still have its key.'),
        fail
    ).
can_cross(great_hall, crypt_stair) :-
    !,
    (   holding(lantern)
    ->  writeln('The lantern''s blue flame pushes the tomb-darkness down the stair.')
    ;   writeln('The stair descends into absolute darkness. You will need a light.'),
        fail
    ).
can_cross(ossuary, inner_sanctum) :-
    !,
    (   flag(sanctum_unlocked)
    ->  true
    ;   holding(bone_key)
    ->  assertz(flag(sanctum_unlocked)),
        writeln('The bone key twists like a living hand. The red stone door opens.')
    ;   writeln('The red door has no ordinary lock. The ossuary''s guardian may hold its secret.'),
        fail
    ).
can_cross(_, _).

% ---------------------------------------------------------------------------
% Exploration and conversation
% ---------------------------------------------------------------------------

take(Item) :-
    here(Room),
    (   enemy_here(Room, Enemy)
    ->  enemy_name(Enemy, EnemyName),
        format('The ~w gives you no chance to reach it. You must deal with your foe first.~n',
            [EnemyName])
    ;   at(Item, Room)
    ->  retract(at(Item, Room)),
        assertz(holding(Item)),
        item_name(Item, Name),
        format('You take the ~w.~n', [Name])
    ;   format('You cannot find ~w here.~n', [Item])
    ).

drop(Item) :-
    (   holding(Item),
        Item \== hunter_knife
    ->  retract(holding(Item)),
        here(Room),
        assertz(at(Item, Room)),
        item_name(Item, Name),
        format('You put down the ~w.~n', [Name])
    ;   Item == hunter_knife
    ->  writeln('You keep your last weapon. Greymere is too dangerous for empty hands.')
    ;   format('You are not carrying ~w.~n', [Item])
    ).

examine(Item) :-
    here(Room),
    (   holding(Item)
    ;   at(Item, Room)
    ),
    !,
    item_name(Item, Name),
    item_description(Item, Description),
    format('~w: ~w~n', [Name, Description]).
examine(Item) :-
    format('You see no ~w to examine.~n', [Item]).

talk(reeve) :-
    here(village_square),
    !,
    speak_with_reeve.
talk(Person) :-
    format('There is no ~w here to answer you.~n', [Person]).

speak_with_reeve :-
    holding(ember_crown),
    !,
    ending_victory.
speak_with_reeve :-
    flag(quest_begun),
    !,
    writeln('Elowen says, "The crown lies below Gloamwatch. Take light, steel, and the old sun-blessing with you."').
speak_with_reeve :-
    assertz(flag(quest_begun)),
    assertz(holding(keep_key)),
    writeln('Elowen presses a long iron key into your hand.'),
    writeln('"Morvane betrayed the keep for eternal life. He found only eternal hunger."'),
    writeln('"Bring back the Ember Crown, and dawn may yet return to Greymere."').

% ---------------------------------------------------------------------------
% Combat
% ---------------------------------------------------------------------------

enemy_here(Room, Enemy) :-
    enemy_location(Enemy, Room),
    alive(Enemy).

weapon_damage(4) :-
    holding(steel_sword),
    !.
weapon_damage(2).

shield_reduction(1) :-
    holding(iron_shield),
    !.
shield_reduction(0).

holy_bonus(Enemy, 2) :-
    undead(Enemy),
    holding(sun_medallion),
    !.
holy_bonus(_, 0).

attack :-
    here(Room),
    (   enemy_here(Room, Enemy)
    ->  player_attack(Room, Enemy)
    ;   writeln('There is nothing here that wishes to fight you.')
    ).

player_attack(Room, Enemy) :-
    weapon_damage(WeaponDamage),
    holy_bonus(Enemy, HolyDamage),
    Damage is WeaponDamage + HolyDamage,
    enemy_hp(Enemy, OldHealth),
    NewHealth is OldHealth - Damage,
    enemy_name(Enemy, Name),
    format('You strike the ~w for ~d damage.~n', [Name, Damage]),
    retract(enemy_hp(Enemy, OldHealth)),
    (   NewHealth =< 0
    ->  assertz(enemy_hp(Enemy, 0)),
        defeat_enemy(Room, Enemy)
    ;   assertz(enemy_hp(Enemy, NewHealth)),
        format('The ~w has ~d health remaining.~n', [Name, NewHealth]),
        enemy_turn(Enemy)
    ).

defeat_enemy(Room, Enemy) :-
    retract(alive(Enemy)),
    defeat_words(Enemy, Words),
    writeln(Words),
    forall(
        drop_on_defeat(Enemy, Item),
        (assertz(at(Item, Room)), item_name(Item, Name), format('The ~w lies here.~n', [Name]))
    ).

enemy_turn(Enemy) :-
    enemy_damage(Enemy, RawDamage),
    shield_reduction(Protection),
    ReducedDamage is RawDamage - Protection,
    (   ReducedDamage < 1
    ->  Damage = 1
    ;   Damage = ReducedDamage
    ),
    player_hp(OldHealth),
    NewHealth is OldHealth - Damage,
    enemy_name(Enemy, Name),
    format('The ~w hits you for ~d damage.~n', [Name, Damage]),
    retract(player_hp(OldHealth)),
    (   NewHealth =< 0
    ->  assertz(player_hp(0)),
        assertz(flag(dead))
    ;   assertz(player_hp(NewHealth)),
        format('You have ~d health remaining.~n', [NewHealth])
    ).

% ---------------------------------------------------------------------------
% Useful items and endings
% ---------------------------------------------------------------------------

use(healing_draught) :-
    holding(healing_draught),
    !,
    player_hp(OldHealth),
    RawHealth is OldHealth + 6,
    (   RawHealth > 14
    ->  NewHealth = 14
    ;   NewHealth = RawHealth
    ),
    retract(player_hp(OldHealth)),
    assertz(player_hp(NewHealth)),
    retract(holding(healing_draught)),
    format('Warmth floods your limbs. Your health rises from ~d to ~d.~n',
        [OldHealth, NewHealth]).
use(sun_medallion) :-
    holding(sun_medallion),
    !,
    writeln('The medallion kindles like sunrise. Its power is always active against the undead.').
use(lantern) :-
    holding(lantern),
    !,
    writeln('The lantern''s blue flame reveals old footprints leading toward the crypt stair.').
use(Item) :-
    holding(Item),
    !,
    item_name(Item, Name),
    format('You find no special use for the ~w just now.~n', [Name]).
use(Item) :-
    format('You are not carrying ~w.~n', [Item]).

ending_victory :-
    assertz(flag(victory)),
    nl,
    writeln('Elowen lifts the Ember Crown with both hands. Its coal becomes a'),
    writeln('golden flame, and the red star above Gloamwatch goes dark. Across'),
    writeln('Greymere, hearths awaken and the first clean snow begins to fall.'),
    nl,
    writeln('"You entered the keep as one brave soul," the reeve says.'),
    writeln('"You return as the shield of Greymere."'),
    nl,
    writeln('                    *** YOU ARE VICTORIOUS ***').

ending_death :-
    nl,
    writeln('Your strength fails, and the dark of Gloamwatch closes over you.'),
    writeln('The red star burns on. Greymere must await another hero.'),
    nl,
    writeln('                         *** THE END ***').

farewell :-
    nl,
    writeln('You leave the road to Gloamwatch for another day. Farewell.').

% ---------------------------------------------------------------------------
% Command loop
% ---------------------------------------------------------------------------

do(look) :-
    !,
    look.
do(help) :-
    !,
    help.
do(inventory) :-
    !,
    inventory.
do(status) :-
    !,
    status.
do(go(Direction)) :-
    !,
    go(Direction).
do(take(Item)) :-
    !,
    take(Item).
do(drop(Item)) :-
    !,
    drop(Item).
do(examine(Item)) :-
    !,
    examine(Item).
do(talk(Person)) :-
    !,
    talk(Person).
do(attack) :-
    !,
    attack.
do(use(Item)) :-
    !,
    use(Item).
do(Command) :-
    format('The tale does not understand ~w. Type help. to see the commands.~n',
        [Command]).

loop :-
    nl,
    write('What do you do? > '),
    flush_output,
    read(Command),
    (   Command == quit
    ->  farewell
    ;   Command == end_of_file
    ->  farewell
    ;   do(Command),
        continue_story
    ).

continue_story :-
    (   flag(dead)
    ->  ending_death
    ;   flag(victory)
    ->  true
    ;   loop
    ).

main :-
    banner,
    look,
    loop.
