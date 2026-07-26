% The first DotProlog sample: run with `dotnet prolog run samples/HelloProlog/hello.pl`.

:- initialization(main).

main :-
    greeting(Greeting),
    write(Greeting),
    nl.

greeting('Hello! World!').
