:- module(portable_greeting, [greet/1]).
:- use_module(library(portable_words)).

greet(Word) :- word(Word).
