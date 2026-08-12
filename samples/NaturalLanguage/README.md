# Natural language processing in Modern mode

A complete natural-language pipeline written in DotProlog: raw text in, answers out.

```text
text --tokenizer--> words --grammar--> parse tree + logical form --model--> answer
```

The sample demonstrates:

- a tokenizer written as a DCG over the text itself — Modern mode starts the
  `double_quotes` flag at `chars`, so `"who chases a mouse"` is already the character
  list the grammar walks;
- a second DCG over the word list that builds a parse tree and a Montague-style
  logical form by unification alone;
- number agreement threaded through the grammar as a shared variable, so
  `every cat chase a mouse` is rejected with no parse;
- quantifier semantics: `every cat chases a mouse` becomes
  `all(X, cat(X), exists(Y, and(mouse(Y), chases(X, Y))))`;
- a model evaluator that decides whether a statement is true in a small world of facts;
- question answering: a `who`-question leaves the questioned position as a free
  variable, and answering it is a `findall/3` over the logical form.

## Run

From the repository root:

```console
dotnet run --project samples/NaturalLanguage/NaturalLanguage.dplproj
```

Statements are parsed, translated, and judged against the world:

```text
statement: every cat chases a mouse
  words:   [every,cat,chases,a,mouse]
  tree:    s(np(det(every),n(cat)),vp(v(chases),np(det(a),n(mouse))))
  meaning: all(x,cat(x),exists(y,and(mouse(y),chases(x,y))))
  holds:   yes
```

Questions are answered by enumerating the bindings that satisfy their logical form:

```text
question:  who chases a mouse?
  words:   [who,chases,a,mouse]
  tree:    q(who,vp(v(chases),np(det(a),n(mouse))))
  meaning: exists(y,and(mouse(y),chases(x,y)))
  answers: x = [tom,whiskers]
```

And agreement violations fail in the grammar, before any tree is built:

```text
statement: every cat chase a mouse
  words:   [every,cat,chase,a,mouse]
  no parse: subject and verb disagree in number
```

## Extend it

The grammar and the world are both ordinary Prolog facts. Add a word to the lexicon
(`noun_word/3`, `verb_word/3`, `det_word/6`), a creature to the model
(`individual/1`, `world_fact/1`), or a new sentence to `main/0`, and rerun.
