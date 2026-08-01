% Parsing text with DCGs in the Modern language mode.
%
% This project sets <DotPrologLanguageMode>modern</DotPrologLanguageMode>, so the double_quotes
% flag starts at chars and a double-quoted token reads as a list of one-character atoms. A grammar
% over text then looks like the text it parses: "0123456789" is a list of digit characters, and
% " " is a single space character.
%
% Under the default extended mode the same literals would be lists of character codes, and every
% rule below would have to be written against integers such as 0'0 and 0' instead.

:- initialization(main).

main :-
    demo_decomposition,
    demo_arithmetic,
    demo_words.

% A string is an ordinary list, so unification takes it apart.

demo_decomposition :-
    "abc" = [First|Rest],
    write('"abc" = [First|Rest]  gives  First = '),
    write(First),
    write(', Rest = '),
    write(Rest),
    nl.

% A grammar over characters: sum a run of decimal numbers separated by '+'.

demo_arithmetic :-
    Text = "12+34+6",
    phrase(expr(Total), Text),
    write('sum of '),
    write_text(Text),
    write(' is '),
    write(Total),
    nl.

expr(N) --> number_(A), expr_rest(A, N).

expr_rest(A, N) --> "+", number_(B), { S is A + B }, expr_rest(S, N).
expr_rest(A, A) --> [].

number_(N) --> digits(Ds), { number_chars(N, Ds) }.

digits([D|Ds]) --> digit(D), digits(Ds).
digits([D]) --> digit(D).

digit(D) --> [D], { memberchk(D, "0123456789") }.

% The use case the mode exists for: splitting text into words.

demo_words :-
    Text = "the quick brown fox",
    phrase(words(Words), Text),
    write('words of '),
    write_text(Text),
    write(': '),
    write(Words),
    nl.

words([W|Ws]) --> word(Cs), { atom_chars(W, Cs) }, more_words(Ws).

more_words(Ws) --> " ", words(Ws).
more_words([]) --> [].

word([C|Cs]) --> letter(C), word_rest(Cs).

word_rest([C|Cs]) --> letter(C), word_rest(Cs).
word_rest([]) --> [].

letter(C) --> [C], { C \== ' ' }.

write_text(Text) :-
    atom_chars(Atom, Text),
    write(Atom).
