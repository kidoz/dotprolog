% A whole program written in Prolog, built as a .dplproj application.

:- initialization(main).

main :-
    forall(member_of([world, prolog, dotnet]), true),
    greet_all([world, prolog, dotnet]).

greet_all([]).
greet_all([Name|Rest]) :-
    write('Hello, '), write(Name), write('!'), nl,
    greet_all(Rest).

member_of([_|_]).
