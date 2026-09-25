% Sample-local terminal presentation. All artwork uses printable ASCII.
:- module(greymere_art, [set_graphics/1, artwork/1, styled/3, health_bar/2]).

:- dynamic graphics_mode/1.

% Explicit opt-in keeps redirected input/output and unsupported terminals plain.
graphics_mode(off).

set_graphics(Mode) :-
    (Mode == on ; Mode == ascii ; Mode == off),
    retractall(graphics_mode(_)),
    assertz(graphics_mode(Mode)).

styled(Style, Format, Args) :-
    (   graphics_mode(on)
    ->  color(Style, Code),
        setup_call_cleanup(sgr(Code), format(Format, Args), sgr(0))
    ;   format(Format, Args)
    ).

sgr(Code) :-
    put_code(27),
    format('[~dm', [Code]).

color(stone, 37).
color(title, 96).
color(danger, 91).
color(ember, 33).
color(magic, 36).
color(life, 32).
color(shadow, 35).

artwork(Scene) :-
    (   \+ graphics_mode(off), scene(Scene, Rows)
    ->  frame,
        draw_rows(Rows),
        frame
    ;   true
    ).

frame :-
    styled(stone, '+----------------------------------------------------------+~n', []).

draw_rows([]).
draw_rows([row(Style, Text)|Rows]) :-
    styled(stone, '| ', []),
    styled(Style, '~w', [Text]),
    atom_length(Text, Length),
    Padding is 56 - Length,
    repeat_char(Padding, ' '),
    styled(stone, ' |~n', []),
    draw_rows(Rows).

repeat_char(Count, Char) :-
    (   Count > 0
    ->  write(Char),
        Next is Count - 1,
        repeat_char(Next, Char)
    ;   true
    ).

health_bar(Health, Maximum) :-
    (   graphics_mode(off)
    ->  true
    ;   health_color(Health, Maximum, Style),
        Empty is Maximum - Health,
        styled(Style, 'HP ', []),
        write('['),
        bar_marks(Style, Health),
        repeat_char(Empty, '-'),
        format('] ~d/~d~n', [Health, Maximum])
    ).

bar_marks(Style, Count) :-
    (   graphics_mode(on)
    ->  color(Style, Code),
        setup_call_cleanup(sgr(Code), repeat_char(Count, '#'), sgr(0))
    ;   repeat_char(Count, '#')
    ).

health_color(Health, Maximum, Style) :-
    (   Health * 4 =< Maximum
    ->  Style = danger
    ;   Health * 2 =< Maximum
    ->  Style = ember
    ;   Style = life
    ).

% Rows are at most 56 columns, inside a 60-column frame. Scenes show architecture
% only: creatures and loose items are drawn from current game state separately.
scene(gloamwatch, [
    row(danger, '                                  *'),
    row(danger, '                              THE RED STAR'),
    row(stone, '           |_|_|                   |_|_|'),
    row(stone, '           |   |    |_|_|_|_|_|     |   |'),
    row(stone, '           | o |____|         |_____| o |'),
    row(stone, '           |   :    :  ___    :     :   |'),
    row(stone, '       ____|___:____:_|   |___:_____:___|____'),
    row(shadow, '   ~~~/              |___|                  /~~~'),
    row(ember, '                 G L O A M W A T C H')
]).

scene(village_square, [
    row(magic, '       /   /        /        /       /    /'),
    row(stone, '          /\\                 /\\'),
    row(stone, '     ____/  \\____       ____/  \\____'),
    row(ember, '     | []    [] |       | []    [] |'),
    row(stone, '     |____[]____|   Y   |____[]____|'),
    row(shadow, '                  _|_'),
    row(stone, '     . . . . . THE DEAD ASH . . . . .'),
    row(stone, '             ____       ____')
]).

scene(old_road, [
    row(danger, '                           *'),
    row(life, '        /\\       /\\                    /\\'),
    row(life, '       /**\\     /**\\      /\\           /**\\'),
    row(life, '      /****\\   /****\\    /**\\         /****\\'),
    row(stone, '        ||       ||     /  /            ||'),
    row(stone, '                       /  /'),
    row(stone, '                 _____/  /'),
    row(stone, '           _____/       /     . . wolf tracks')
]).

scene(keep_gate, [
    row(stone, '      |_|_|_|                       |_|_|_|'),
    row(stone, '      |     |_______________________|     |'),
    row(stone, '      |  o  |      GLOAMWATCH        |  o  |'),
    row(stone, '      |     |     .----------.      |     |'),
    row(life,  '      |     |     | | | | | |      |     |'),
    row(life,  '      |     |     | | | | | |      |     |'),
    row(stone, '   ___|_____|_____|_|_|_|_|_|______|_____|___')
]).

scene(outer_courtyard, [
    row(stone, '       _____________      _____________'),
    row(stone, '      |  []   []   |______|   []   []  |'),
    row(stone, '      |            /      \\           |'),
    row(shadow, '      |   (v)     |        |    (v)    |'),
    row(stone, '      |___/|\\_____|        |____/|\\____|'),
    row(life, '          ,,,     .  .  .     ,,,,'),
    row(stone, '            .  .          .  .'),
    row(life, '      ,,,          ,,,              ,,,')
]).

scene(gatehouse, [
    row(stone, '       __________________________'),
    row(stone, '      /  /  /  /      /  /  /   /'),
    row(stone, '     |  /__/          __/__/   |'),
    row(shadow, '     |   ____          ____   |'),
    row(stone, '     |  |____|        |____|  |'),
    row(stone, '     |  |____|   /    |____|  |'),
    row(ember, '     |__________/_____________|'),
    row(stone, '          splinters and old watchfires')
]).

scene(great_hall, [
    row(stone, '      |\\                           /|'),
    row(shadow, '      | |  /V\\             /V\\   | |'),
    row(shadow, '      | |  | |             | |   | |'),
    row(stone, '      | |__|_|_____________|_|___| |'),
    row(stone, '      |/       ___________        \\|'),
    row(ember, '             /___________/|'),
    row(ember, '             |           |'),
    row(shadow, '                      ____   to the crypt')
]).

scene(ruined_chapel, [
    row(ember, '                     \\ | /'),
    row(ember, '                   -- (o) --'),
    row(ember, '                     / | \\'),
    row(stone, '           /\\                   /\\'),
    row(stone, '          |  |    _________    |  |'),
    row(stone, '          |  |   |_________|   |  |'),
    row(stone, '          |__|     |   |      |__|'),
    row(ember, '              CHAPEL OF THE FIRST SUN')
]).

scene(old_armory, [
    row(stone, '       __________________________________'),
    row(stone, '      |   |   |   |       .--------.     |'),
    row(stone, '      | --+-- + --+--     |        |     |'),
    row(stone, '      |   |   |   |      |        |     |'),
    row(stone, '      |   v   v   v       \\______/      |'),
    row(ember, '      |=================================|'),
    row(shadow, '      |     empty hooks and fallen mail |'),
    row(stone, '      |_________________________________|')
]).

scene(crypt_stair, [
    row(shadow, '      | MORVANE  //////  ////// |'),
    row(stone, '      |________________        |'),
    row(stone, '                       |___    |'),
    row(stone, '                           |___|'),
    row(stone, '                               |___'),
    row(stone, '                                   |___'),
    row(magic, '            .     cold blue light      |___'),
    row(shadow, '                                           ...')
]).

scene(ossuary, [
    row(stone, '      | (oo) (oo) |           | (oo) (oo) |'),
    row(stone, '      | _||_ _||_ |  .-----.  | _||_ _||_ |'),
    row(danger, '      |           |  |  o  |  |           |'),
    row(stone, '      | (oo) (oo) |  |     |  | (oo) (oo) |'),
    row(stone, '      | _||_ _||_ |  |     |  | _||_ _||_ |'),
    row(stone, '      |___________|__|_____|__|___________|'),
    row(shadow, '                   dust upon dust')
]).

scene(flooded_vault, [
    row(stone, '               .----------------.'),
    row(stone, '           .---                  ---.'),
    row(stone, '          |        _________         |'),
    row(ember, '          |       /_______ /|        |'),
    row(ember, '          |       |       |/         |'),
    row(magic, '      ~~~~ ~~~ ~~~ ~~~~~~~ ~~~ ~~~~ ~~~~'),
    row(magic, '        ~~~~~    ~~~~~~     ~~~~~    ~~~'),
    row(magic, '      ~~~    ~~~~~    ~~~~~     ~~~~~')
]).

scene(inner_sanctum, [
    row(ember, '              .        *        .'),
    row(danger, '          *        .       .        *'),
    row(stone, '                   |       |'),
    row(stone, '                   |_______|'),
    row(stone, '                ___|       |___'),
    row(stone, '               |   |_______|   |'),
    row(stone, '               |___|_______|___|'),
    row(ember, '          .  ______|_______|______  .')
]).

scene(mire_goblin, [
    row(life, '                      /\\__/\\'),
    row(life, '                     < o  o >'),
    row(life, '                      ( -- )    /|'),
    row(stone, '                    __/|  |\\___/ |'),
    row(life, '                      /    \\    -'),
    row(danger, '                     MIRE GOBLIN')
]).

scene(oathless_knight, [
    row(stone, '                       .----.'),
    row(danger, '                       | -- |'),
    row(stone, '                    ___|____|___'),
    row(stone, '                   /  /|    |\\  \\'),
    row(stone, '                  +  | |____| |  +'),
    row(shadow, '                    OATHLESS KNIGHT')
]).

scene(bone_warden, [
    row(stone, '                       .----.'),
    row(stone, '                      ( o  o )'),
    row(stone, '                       | == |'),
    row(stone, '                    ---| ++ |---'),
    row(stone, '                      /| ++ |\\'),
    row(shadow, '                      BONE WARDEN')
]).

scene(morvane, [
    row(ember, '                  .      *      .'),
    row(shadow, '                       /^^\\'),
    row(danger, '                      / oo \\'),
    row(shadow, '                  ___/      \\___'),
    row(shadow, '                    /        \\'),
    row(shadow, '                  ~/  ~    ~  \\~'),
    row(danger, '                  MORVANE, CINDER WRAITH')
]).

scene(victory, [
    row(ember, '                    *    *    *'),
    row(ember, '                    |\\  /|\\  /|'),
    row(ember, '                    | \\/ | \\/ |'),
    row(ember, '                    |____*____|'),
    row(life, '                  DAWN RETURNS TO GREYMERE')
]).

scene(death, [
    row(danger, '                           *'),
    row(stone, '                       .-------.'),
    row(stone, '                       |   +   |'),
    row(stone, '                       |       |'),
    row(shadow, '                   ____|_______|____'),
    row(shadow, '                    THE STAR BURNS ON')
]).
