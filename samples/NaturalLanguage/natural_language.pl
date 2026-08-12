% Natural language processing in the Modern language mode.
%
% The program is the classic Prolog treatment of natural language, end to end:
%
%   text --tokenizer--> words --grammar--> parse tree + logical form --model--> answer
%
% Modern mode carries the first stage: double_quotes starts at chars, so
% "every cat chases a mouse" is already the list of characters a tokenizing DCG walks.
% The second stage is a DCG over the resulting word list. Unification threads number
% agreement through it ("every cat chase a mouse" has no parse) and assembles a
% Montague-style logical form without a single explicit assignment. The final stage
% evaluates that logical form against a small world model, so the program does not
% just parse sentences — it decides whether they are true and answers questions.

:- initialization(main).

main :-
    demo_statement("every cat chases a mouse"),
    demo_statement("a dog sleeps"),
    demo_statement("every mouse fears a dog"),
    demo_statement("tom chases jerry"),
    demo_question("who chases a mouse?"),
    demo_question("who sleeps?"),
    demo_ungrammatical("every cat chase a mouse").

% A declarative sentence: parse it, show the tree and the logical form, then
% evaluate the logical form against the world model.

demo_statement(Text) :-
    tokenize(Text, Words),
    show_text('statement:', Text),
    format('  words:   ~w~n', [Words]),
    (   phrase(sentence(Tree, Meaning), Words)
    ->  format('  tree:    ~w~n', [Tree]),
        show_meaning(Meaning),
        (   holds(Meaning)
        ->  format('  holds:   yes~n')
        ;   format('  holds:   no~n')
        )
    ;   format('  no parse~n')
    ),
    nl.

% A who-question: the grammar leaves the questioned position as a free variable,
% so answering is findall over the logical form.

demo_question(Text) :-
    tokenize(Text, Words),
    show_text('question: ', Text),
    format('  words:   ~w~n', [Words]),
    (   phrase(question(Who, Meaning, Tree), Words)
    ->  format('  tree:    ~w~n', [Tree]),
        copy_term(Who-Meaning, ShownWho-ShownMeaning),
        name_variables(ShownWho-ShownMeaning, [x, y, z, u, v, w], _),
        format('  meaning: ~w~n', [ShownMeaning]),
        findall(Who, holds(Meaning), Found),
        sort(Found, Answers),
        format('  answers: ~w = ~w~n', [ShownWho, Answers])
    ;   format('  no parse~n')
    ),
    nl.

% A sentence the grammar rejects: the subject is singular, the verb form plural,
% and unification of the Number argument fails before any tree is built.

demo_ungrammatical(Text) :-
    tokenize(Text, Words),
    show_text('statement:', Text),
    format('  words:   ~w~n', [Words]),
    (   phrase(sentence(_, _), Words)
    ->  format('  parsed, unexpectedly~n')
    ;   format('  no parse: subject and verb disagree in number~n')
    ),
    nl.

% ---------------------------------------------------------------------------
% Stage one: characters to words. This DCG runs over the text itself, which is
% what Modern mode buys — " " and "?" below are character lists, not code lists.

tokenize(Text, Words) :-
    phrase(word_list(Words), Text),
    !.

word_list([]) --> [].
word_list(Words) --> " ", word_list(Words).
word_list(Words) --> "?", word_list(Words).
word_list([Word|Words]) --> letters(Chars), { atom_chars(Word, Chars) }, word_list(Words).

letters([Char|Chars]) --> letter(Char), letters(Chars).
letters([Char]) --> letter(Char).

letter(Char) --> [Char], { Char @>= a, Char @=< z }.

% ---------------------------------------------------------------------------
% Stage two: words to a parse tree and a logical form. Each noun phrase is given
% the scope its verb phrase builds and wraps it in its own quantifier, so
% "every cat chases a mouse" becomes all(X, cat(X), exists(Y, and(mouse(Y), chases(X, Y)))).
% The Number argument is the agreement feature: subject and verb must unify on it.

sentence(s(SubjectTree, VerbTree), Meaning) -->
    noun_phrase(Number, X, Scope, Meaning, SubjectTree),
    verb_phrase(Number, X, Scope, VerbTree).

question(Who, Meaning, q(who, VerbTree)) -->
    [who],
    verb_phrase(sg, Who, Meaning, VerbTree).

noun_phrase(Number, X, Scope, Meaning, np(det(Word), n(NounWord))) -->
    [Word],
    { det_word(Word, Number, X, Restriction, Scope, Meaning) },
    noun(Number, X, Restriction, NounWord).
noun_phrase(sg, X, Scope, Scope, np(name(X))) -->
    [X],
    { individual(X) }.

noun(Number, X, Restriction, Word) -->
    [Word],
    { noun_word(Word, Number, Predicate), Restriction =.. [Predicate, X] }.

verb_phrase(Number, X, Meaning, vp(v(Word), ObjectTree)) -->
    [Word],
    { verb_word(Word, Number, transitive(Predicate)), Core =.. [Predicate, X, Y] },
    noun_phrase(_, Y, Core, Meaning, ObjectTree).
verb_phrase(Number, X, Meaning, vp(v(Word))) -->
    [Word],
    { verb_word(Word, Number, intransitive(Predicate)), Meaning =.. [Predicate, X] }.

% The lexicon. A determiner is a fact from surface word to the quantifier it
% builds; nouns and verbs map each surface form to its number and predicate.

det_word(a, sg, X, Restriction, Scope, exists(X, and(Restriction, Scope))).
det_word(some, sg, X, Restriction, Scope, exists(X, and(Restriction, Scope))).
det_word(every, sg, X, Restriction, Scope, all(X, Restriction, Scope)).
det_word(some, pl, X, Restriction, Scope, exists(X, and(Restriction, Scope))).
det_word(all, pl, X, Restriction, Scope, all(X, Restriction, Scope)).

noun_word(cat, sg, cat).
noun_word(cats, pl, cat).
noun_word(mouse, sg, mouse).
noun_word(mice, pl, mouse).
noun_word(dog, sg, dog).
noun_word(dogs, pl, dog).

verb_word(chases, sg, transitive(chases)).
verb_word(chase, pl, transitive(chases)).
verb_word(fears, sg, transitive(fears)).
verb_word(fear, pl, transitive(fears)).
verb_word(sleeps, sg, intransitive(sleeps)).
verb_word(sleep, pl, intransitive(sleeps)).

% ---------------------------------------------------------------------------
% Stage three: the world model and the evaluator that decides a logical form.

individual(tom).
individual(whiskers).
individual(jerry).
individual(rex).

world_fact(cat(tom)).
world_fact(cat(whiskers)).
world_fact(mouse(jerry)).
world_fact(dog(rex)).
world_fact(chases(tom, jerry)).
world_fact(chases(whiskers, jerry)).
world_fact(fears(jerry, tom)).
world_fact(fears(jerry, whiskers)).
world_fact(sleeps(rex)).

holds(and(A, B)) :-
    holds(A),
    holds(B).
holds(exists(_, Body)) :-
    holds(Body).
holds(all(_, Restriction, Body)) :-
    forall(holds(Restriction), holds(Body)).
holds(Fact) :-
    world_fact(Fact).

% ---------------------------------------------------------------------------
% Display helpers.

show_text(Label, Text) :-
    atom_chars(Atom, Text),
    format('~w ~w~n', [Label, Atom]).

% A logical form is full of unbound variables, which would print as generated
% names. Bind the variables of a copy to x, y, z, ... so it reads as intended.

show_meaning(Meaning) :-
    copy_term(Meaning, Shown),
    name_variables(Shown, [x, y, z, u, v, w], _),
    format('  meaning: ~w~n', [Shown]).

name_variables(Term, Names0, Names) :-
    (   var(Term)
    ->  Names0 = [Term|Names]
    ;   compound(Term)
    ->  Term =.. [_|Arguments],
        name_argument_variables(Arguments, Names0, Names)
    ;   Names = Names0
    ).

name_argument_variables([], Names, Names).
name_argument_variables([Argument|Arguments], Names0, Names) :-
    name_variables(Argument, Names0, Names1),
    name_argument_variables(Arguments, Names1, Names).
