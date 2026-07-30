# Глава 11 — Настоящий проект: текстовый квест

Десять глав вы собирали инструменты: факты и правила, рекурсию, списки, арифметику, умение
принимать решения, базу данных, которую можно менять прямо во время работы программы, текст,
входящий в терминал и выходящий из него. Каждый инструмент приходил в маленькой программе,
написанной ради него одного. Эта глава — другая. Вы будете строить одну программу, этап за
этапом, пока она не станет игрой, в которую действительно можно играть.

Игра эта — *текстовый квест*, старейший жанр компьютерных игр. До графики игры состояли из
предложений. Компьютер описывал, где вы находитесь; вы печатали, что хотите сделать; компьютер
сообщал, что из этого вышло. Пролог подходит для такого как нельзя лучше: мир — это набор
фактов, а всё, что вы можете сделать, — правила.

Наша игра называется *«Серебряная корона»*. В ней четыре комнаты, три предмета, одна запертая
дверь и одна цель: найти корону. К концу главы всё это уместится примерно в сотню строк — и
каждая из этих строк использует то, что вы уже знаете.

Работайте в одном файле, `adventure.pl`, и наращивайте его по ходу главы. Каждый этап
запускается, так что после каждого раздела можно проверять свои успехи привычной командой:

```console
dotnet run --project src/DotProlog.Tool -- run adventure.pl
```

## Мир как факты

Мир — это набор истинных утверждений, а истинные утверждения — это факты; так было в главе 2.
Начнём с комнат. Каждая комната — атом, и у каждой есть описание, тоже атом: кусочек текста в
кавычках, как в [главе 10](10-words-and-text.md).

```prolog
description(hall,    'You are in a dusty hall. A grandfather clock ticks somewhere.').
description(kitchen, 'You are in the kitchen. It smells faintly of bread.').
description(garden,  'You are in a walled garden. Bees drift between the roses.').
description(cellar,  'You are in the cellar. Cobwebs everywhere - and a glint of silver.').
```

Комнатам нужны проходы. Дверной проём соединяет две комнаты в каком-то направлении:

```prolog
door(hall, kitchen, north).
door(hall, garden, south).
door(hall, cellar, east).
```

Первый факт читается так: *из холла есть дверь в кухню, с северной стороны*. Но дверь работает
в обе стороны: если кухня к северу от холла, то холл — к югу от кухни. Можно было бы записать
каждый проход дважды — а можно записать его один раз и поручить рассуждение правилу; именно
для этого правила и существуют (глава 3):

```prolog
opposite(north, south).
opposite(south, north).
opposite(east, west).
opposite(west, east).

exit(From, Direction, To) :- door(From, To, Direction).
exit(From, Direction, To) :- door(To, From, Opposite), opposite(Direction, Opposite).
```

Первое предложение `exit` говорит: из `From` можно выйти в направлении `Direction`, если в ту
сторону есть дверь. Второе: можно выйти и через дверь, записанную *в обратную* сторону, — двигаясь
в противоположном направлении. Три факта `door` теперь описывают шесть проходов.

Наконец, разложим по комнатам предметы:

```prolog
at(loaf, kitchen).
at(key, garden).
at(crown, cellar).
```

Вот и весь мир. Чтобы его проверить, добавьте временный `main`, который спрашивает о выходах, —
`forall` вы знаете по [главе 9](09-collecting-answers.md):

```prolog
:- initialization(main).

main :-
    forall(exit(hall, Direction, Room),
           format('From the hall you can go ~w to the ~w.~n', [Direction, Room])),
    forall(exit(garden, Direction, Room),
           format('From the garden you can go ~w to the ~w.~n', [Direction, Room])).
```

```text
From the hall you can go north to the kitchen.
From the hall you can go south to the garden.
From the hall you can go east to the cellar.
From the garden you can go north to the hall.
```

У холла три выхода — из трёх фактов `door`; у сада один, выведенный вторым предложением
`exit`. Мир уже рассуждает о самом себе, а мы ещё не написали ни строчки собственно игры.

## Состояние игрока

Мир стоит на месте, игрок — нет. Во время игры меняются три вещи: где вы находитесь, что лежит
на полу в каждой комнате и что вы несёте с собой. Меняющиеся факты — это динамическая база из
[главы 9](09-collecting-answers.md): объявите их `dynamic` и перемещайте с помощью `assertz` и
`retract`:

```prolog
:- dynamic here/1.
here(hall).

:- dynamic at/2.
at(loaf, kitchen).
at(key, garden).
at(crown, cellar).

:- dynamic holding/1.
```

Обратите внимание на схему: объявление `dynamic`, а за ним обычные факты. Факты в файле — это
*начальное* состояние: игра начинается с того, что вы в холле, ключ в саду, а руки пусты. У
`holding/1` фактов нет вовсе, и это нормально: динамическому предикату разрешено начинать с
пустоты. (`at/2` в прошлом разделе был статическим; теперь, когда вещи можно поднимать, он
переезжает в динамическую колонку.)

Когда состояние на месте, можно написать самый важный предикат игры: `look`, который описывает
то место, где вы сейчас находитесь.

```prolog
look :-
    here(Here),
    description(Here, Text),
    writeln(Text),
    write('Exits:'),
    forall(exit(Here, Direction, _), format(' ~w', [Direction])),
    nl,
    forall(at(Item, Here), format('There is a ~w here.~n', [Item])).
```

Прочитайте его вслух: выясни, где это «здесь», напечатай описание, напечатай каждый выход,
напечатай каждый предмет, что лежит вокруг. Перечислением занимаются два вызова `forall` — *для
каждого выхода напечатай его; для каждого предмета здесь напечатай его*.

Чтобы увидеть, как состояние действительно меняется, попробуйте `main`, который осматривается,
вручную телепортирует игрока и осматривается снова:

```prolog
main :-
    look,
    retract(here(hall)),
    assertz(here(garden)),
    look.
```

```text
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
You are in a walled garden. Bees drift between the roses.
Exits: north
There is a key here.
```

Пара `retract`–`assertz` — именно так игрок и будет перемещаться до конца игры: забудь старое
местоположение, запомни новое.

## Команды

Игрок не обязан телепортироваться, редактируя программу. Пора завести глаголы. Каждая
команда — предикат, и каждая подчиняется одному правилу дизайна, которое стоит произнести
вслух: **команда всегда успешна и всегда что-нибудь говорит** — даже когда то, о чём попросил
игрок, сделать нельзя. Игровой цикл, который мы построим дальше, на это опирается.

`go/1` — это перемещение через `retract`–`assertz`, обёрнутое в if-then-else из
[главы 8](08-making-decisions.md). Если выход в ту сторону есть — переместись и осмотрись; если
нет — так и скажи:

```prolog
go(Direction) :-
    here(Here),
    (   exit(Here, Direction, There)
    ->  retract(here(Here)),
        assertz(here(There)),
        look
    ;   writeln('You cannot go that way.')
    ).
```

`take/1` и `drop/1` перекладывают предмет между полом и вашими руками — между `at/2` и
`holding/1`:

```prolog
take(Item) :-
    here(Here),
    (   at(Item, Here)
    ->  retract(at(Item, Here)),
        assertz(holding(Item)),
        format('You take the ~w.~n', [Item])
    ;   format('There is no ~w here.~n', [Item])
    ).

drop(Item) :-
    (   holding(Item)
    ->  retract(holding(Item)),
        here(Here),
        assertz(at(Item, Here)),
        format('You drop the ~w.~n', [Item])
    ;   format('You are not holding a ~w.~n', [Item])
    ).
```

А `inventory` перечисляет, что вы несёте, — с сообщением подобрее для пустых рук:

```prolog
inventory :-
    (   holding(_)
    ->  writeln('You are carrying:'),
        forall(holding(Item), format('  a ~w~n', [Item]))
    ;   writeln('You are carrying nothing.')
    ).
```

Сценарный `main` прогоняет всё разом — включая вежливые отказы:

```prolog
main :-
    look,
    go(south),
    take(key),
    inventory,
    go(west),
    take(loaf).
```

```text
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
You are in a walled garden. Bees drift between the roses.
Exits: north
There is a key here.
You take the key.
You are carrying:
  a key
You cannot go that way.
There is no loaf here.
```

И попытка пройти сквозь стену, и попытка схватить буханку, которой здесь нет, рождают
предложения, а не провалы. Это наше правило дизайна делает свою работу.

## Игровой цикл

Теперь место сценария занимает игрок. Игровой цикл делает три вещи — и делает их вечно:
показывает приглашение, читает команду, выполняет её. *Читает команду* — это `read/1` из
[главы 10](10-words-and-text.md): игрок печатает прологовский терм с точкой на конце, например
`go(north).` А *вечно* — не ключевое слово цикла, потому что циклов у Пролога нет. Это
рекурсия, прямиком из [главы 5](05-recursion.md): цикл выполняет одну команду, а затем вызывает
сам себя.

Выполнение — это маленький предикат-диспетчер `do/1`, по одному предложению на команду:

```prolog
do(look)       :- look.
do(go(D))      :- go(D).
do(take(X))    :- take(X).
do(drop(X))    :- drop(X).
do(inventory)  :- inventory.
do(Command)    :- format('I do not know how to ~w.~n', [Command]).
```

Пролог пробует предложения сверху вниз (глава 4), и терм, который напечатал игрок,
унифицируется с подходящей головой — `take(loaf)` находит предложение `take(X)` при
`X = loaf`. Последнее предложение сопоставляется с чем угодно, поэтому оно и должно стоять
последним: это запасной вариант для команд, которых игра не знает. И поскольку каждая команда
держит наше обещание «всегда успешна», первое подошедшее предложение оказывается единственным,
которое выполняется.

Сам цикл, с `quit`, обрабатываемым до диспетчеризации:

```prolog
loop :-
    write('> '),
    read(Command),
    (   Command = quit
    ->  writeln('Thanks for playing. Goodbye!')
    ;   do(Command),
        loop
    ).
```

Замените сценарный `main` настоящим:

```prolog
main :-
    look,
    loop.
```

Запустите и играйте. Вот один сеанс — строки после каждого `>` набраны игроком, вместе с
точками:

```text
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> go(north).
You are in the kitchen. It smells faintly of bread.
Exits: south
There is a loaf here.
> take(loaf).
You take the loaf.
> sing.
I do not know how to sing.
> go(south).
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> quit.
Thanks for playing. Goodbye!
```

!!! note "Как тестировать игру, не играя в неё"
    Этот трюк вы встречали в [главе 10](10-words-and-text.md): вместо того чтобы набирать
    сценарий сеанса вручную, подайте его в программу через канал. С целой игрой он работает
    ничуть не хуже. В POSIX-оболочке:

    ```console
    printf 'go(north).\ntake(loaf).\nsing.\ngo(south).\nquit.\n' | dotnet run --project src/DotProlog.Tool -- run adventure.pl
    ```

    В PowerShell:

    ```powershell
    @("go(north).", "take(loaf).", "sing.", "go(south).", "quit.") |
        dotnet run --project src/DotProlog.Tool -- run adventure.pl
    ```

    Ввод из канала не отображается на экране, поэтому транскрипт выглядит скупее
    интерактивного — ответы идут сразу после каждого `>`, — но игра та же самая. Держите под
    рукой выигрышный сценарий, и после каждого изменения игру можно будет перепроверить одной
    командой.

## Запираем погреб — и побеждаем

Игре нужно, чтобы в ней было чего хотеть. Корона в погребе; давайте запрём погреб, а ключ
спрячем в саду. Заперта дверь или нет — это состояние: оно меняется один раз, когда вы её
отпираете, — а значит, это ещё один динамический предикат:

```prolog
:- dynamic locked/1.
locked(cellar).
```

Вход в комнату теперь требует некоторого рассуждения, поэтому мы выносим его из `go/1`.
Цепочка условий — это многоступенчатый if-then-else из [главы 8](08-making-decisions.md), а
`\+` — отрицание из той же главы: *заперто, и ключа у вас в руках нет*:

```prolog
go(Direction) :-
    here(Here),
    (   exit(Here, Direction, There)
    ->  enter(There)
    ;   writeln('You cannot go that way.')
    ).

enter(There) :-
    (   locked(There), \+ holding(key)
    ->  writeln('The door is locked. Perhaps a key would help.')
    ;   locked(There)
    ->  retract(locked(There)),
        writeln('You unlock the door with the key.'),
        move_to(There)
    ;   move_to(There)
    ).

move_to(There) :-
    retract(here(_)),
    assertz(here(There)),
    look.
```

Три случая, сверху вниз: заперто и ключа нет — вас разворачивают; заперто, но ключ у вас —
отпираете (изымая факт `locked`, так что дверь остаётся открытой) и входите; не заперто —
просто входите. Само перемещение теперь живёт в `move_to/1` и записано один раз.

Победа — самый простой предикат в игре:

```prolog
won :- holding(crown).
```

Цикл проверяет её после каждой команды и завершает рекурсию фанфарами вместо очередного
приглашения:

```prolog
loop :-
    write('> '),
    read(Command),
    (   Command = quit
    ->  writeln('Thanks for playing. Goodbye!')
    ;   do(Command),
        (   won
        ->  nl,
            writeln('The silver crown is yours. You win!')
        ;   loop
        )
    ).
```

Наконец, достойное вступление. `main` объявляет игру, учит командам и передаёт управление:

```prolog
main :-
    writeln('THE SILVER CROWN'),
    writeln('Somewhere in this house is a silver crown. Find it.'),
    writeln('Commands: look. go(north). take(key). drop(key). inventory. quit.'),
    nl,
    look,
    loop.
```

## Игра целиком

Вот программа полностью, ровно в том виде, в каком она запускается, — те же предложения, что
вы построили, собранные в одном месте:

```prolog
% «Серебряная корона» — маленький текстовый квест.
:- initialization(main).

% ----- Мир -----

description(hall,    'You are in a dusty hall. A grandfather clock ticks somewhere.').
description(kitchen, 'You are in the kitchen. It smells faintly of bread.').
description(garden,  'You are in a walled garden. Bees drift between the roses.').
description(cellar,  'You are in the cellar. Cobwebs everywhere - and a glint of silver.').

door(hall, kitchen, north).
door(hall, garden, south).
door(hall, cellar, east).

opposite(north, south).
opposite(south, north).
opposite(east, west).
opposite(west, east).

exit(From, Direction, To) :- door(From, To, Direction).
exit(From, Direction, To) :- door(To, From, Opposite), opposite(Direction, Opposite).

% ----- Состояние игрока -----

:- dynamic here/1.
here(hall).

:- dynamic at/2.
at(loaf, kitchen).
at(key, garden).
at(crown, cellar).

:- dynamic holding/1.

:- dynamic locked/1.
locked(cellar).

% ----- Команды -----

look :-
    here(Here),
    description(Here, Text),
    writeln(Text),
    write('Exits:'),
    forall(exit(Here, Direction, _), format(' ~w', [Direction])),
    nl,
    forall(at(Item, Here), format('There is a ~w here.~n', [Item])).

go(Direction) :-
    here(Here),
    (   exit(Here, Direction, There)
    ->  enter(There)
    ;   writeln('You cannot go that way.')
    ).

enter(There) :-
    (   locked(There), \+ holding(key)
    ->  writeln('The door is locked. Perhaps a key would help.')
    ;   locked(There)
    ->  retract(locked(There)),
        writeln('You unlock the door with the key.'),
        move_to(There)
    ;   move_to(There)
    ).

move_to(There) :-
    retract(here(_)),
    assertz(here(There)),
    look.

take(Item) :-
    here(Here),
    (   at(Item, Here)
    ->  retract(at(Item, Here)),
        assertz(holding(Item)),
        format('You take the ~w.~n', [Item])
    ;   format('There is no ~w here.~n', [Item])
    ).

drop(Item) :-
    (   holding(Item)
    ->  retract(holding(Item)),
        here(Here),
        assertz(at(Item, Here)),
        format('You drop the ~w.~n', [Item])
    ;   format('You are not holding a ~w.~n', [Item])
    ).

inventory :-
    (   holding(_)
    ->  writeln('You are carrying:'),
        forall(holding(Item), format('  a ~w~n', [Item]))
    ;   writeln('You are carrying nothing.')
    ).

% ----- Диспетчер: по предложению на команду и запасной вариант для остальных -----

do(look)       :- look.
do(go(D))      :- go(D).
do(take(X))    :- take(X).
do(drop(X))    :- drop(X).
do(inventory)  :- inventory.
do(Command)    :- format('I do not know how to ~w.~n', [Command]).

% ----- Победа -----

won :- holding(crown).

% ----- Игровой цикл -----

loop :-
    write('> '),
    read(Command),
    (   Command = quit
    ->  writeln('Thanks for playing. Goodbye!')
    ;   do(Command),
        (   won
        ->  nl,
            writeln('The silver crown is yours. You win!')
        ;   loop
        )
    ).

% ----- Запуск -----

main :-
    writeln('THE SILVER CROWN'),
    writeln('Somewhere in this house is a silver crown. Find it.'),
    writeln('Commands: look. go(north). take(key). drop(key). inventory. quit.'),
    nl,
    look,
    loop.
```

А вот полное победное прохождение — и снова строки после каждого `>` принадлежат игроку:

```text
THE SILVER CROWN
Somewhere in this house is a silver crown. Find it.
Commands: look. go(north). take(key). drop(key). inventory. quit.

You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> look.
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> go(east).
The door is locked. Perhaps a key would help.
> go(south).
You are in a walled garden. Bees drift between the roses.
Exits: north
There is a key here.
> take(key).
You take the key.
> inventory.
You are carrying:
  a key
> go(north).
You are in a dusty hall. A grandfather clock ticks somewhere.
Exits: north south east
> go(east).
You unlock the door with the key.
You are in the cellar. Cobwebs everywhere - and a glint of silver.
Exits: west
There is a crown here.
> take(crown).
You take the crown.
The silver crown is yours. You win!
```

Отступите на шаг и посмотрите, из чего сделана эта программа. Мир — это факты (глава 2), над
которыми рассуждают правила (глава 3). Команды находятся унификацией с головами предложений,
перебираемых по порядку (глава 4). Цикл — это рекурсия (глава 5). Ошибки и выбор — if-then-else
и отрицание (глава 8). Меняющийся мир — динамическая база, а перечисления — `forall`
(глава 9). Слова на входе и выходе — атомы, `format` и `read` (глава 10). Ничего нового не
понадобилось: настоящая программа — это всё те же маленькие идеи, аккуратно прибранные и сложенные друг на
друга.

## Упражнения

Каждое из них расширяет игру. После каждого изменения прогоняйте свой выигрышный сценарий —
тот самый канал с `printf`, — чтобы убедиться, что игра по-прежнему работает.

1. **Дом побольше.** Добавьте кабинет к западу от холла — с описанием и чем-нибудь стоящим на
   столе, что можно взять. Один факт `door`, один факт `description`, один факт `at` —
   остальная игра подстроится сама. Затем добавьте лестничную площадку и лестницу: нигде не
   сказано, что направления обязаны быть сторонами света.
2. **Рассматриваем предметы.** Добавьте факты `item_description/2` — например, что ключ
   маленький и латунный — и команду `look(Item)`, которая печатает описание, если предмет
   здесь или у вас в руках, и что-нибудь вежливое в противном случае. Понадобится новое
   предложение `do/1`; подумайте, почему `do(look)` и `do(look(X))` не сталкиваются.
3. **Тёмный погреб.** Положите на кухню лампу и сделайте погреб тёмным: если вы входите без
   лампы, `look` печатает только `'It is pitch dark.'` — ни описания, ни выходов, ни блеска
   серебра. В главе 8 есть всё, что нужно.
4. **Заняты руки.** Дайте игроку всего две руки: `take/1` должен отказывать, когда вы уже
   несёте два предмета. Считайте с помощью `aggregate_all(count, holding(_), N)` из главы 9.
5. **Настоящее ограбление.** Взять корону — мало: её нужно вынести обратно в сад. Измените
   `won` (и победное сообщение) так, чтобы игра считалась выигранной, только когда вы держите
   корону *и* стоите в саду. Заметьте: больше ничего в программе менять не нужно.
6. **Сохранённая партия.** `assertz` и `retract` знают всю историю прохождения. Добавьте
   команду `score`, которая сообщает, сколько предметов вы несёте и сколько ещё лежит по
   дому, — два вызова `aggregate_all` и один `format`.

---

Дальше: [Глава 12 — Пролог и .NET](12-prolog-meets-dotnet.md), где выяснится, что игра,
которую вы только что написали, живёт в куда большем мире — мире, где ваш Пролог можно
вызывать из других языков, тестировать, как любой профессиональный код, и поставлять как
настоящее приложение.
