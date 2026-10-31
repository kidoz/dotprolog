:- use_module(library(portable_greeting)).
:- initialization(main).

main :- greet(Word), writeln(Word).
