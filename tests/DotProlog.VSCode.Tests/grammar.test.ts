import assert from "node:assert/strict";
import { test } from "node:test";
import fs from "node:fs/promises";
import path from "node:path";
import { Registry, parseRawGrammar, INITIAL } from "vscode-textmate";
import { loadWASM, OnigScanner, OnigString } from "vscode-oniguruma";

test("TextMate grammar distinguishes comments, escaped quotes, variables, numbers and DCGs", async () => {
    const wasm = await fs.readFile(
        require.resolve("vscode-oniguruma/release/onig.wasm"),
    );
    await loadWASM(
        wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength),
    );
    const registry = new Registry({
        onigLib: Promise.resolve({
            createOnigScanner: (patterns) => new OnigScanner(patterns),
            createOnigString: (value) => new OnigString(value),
        }),
        loadGrammar: async () =>
            parseRawGrammar(
                await fs.readFile(
                    path.resolve("syntaxes/dotprolog.tmLanguage.json"),
                    "utf8",
                ),
                "grammar.json",
            ),
    });
    const grammar = (await registry.loadGrammar("source.dotprolog"))!;
    const line =
        "rule(X, 0xff, 1.2e-3) --> ['it''s', \"text\"], {X = 0'a}. % note";
    const tokens = grammar.tokenizeLine(line, INITIAL).tokens;
    const has = (text: string, scope: string) =>
        assert.ok(
            tokens.some(
                (token) =>
                    line
                        .slice(token.startIndex, token.endIndex)
                        .includes(text) && token.scopes.includes(scope),
            ),
            `${text}: ${scope}`,
        );
    has("X", "variable.other.dotprolog");
    has("0xff", "constant.numeric.dotprolog");
    has("1.2e-3", "constant.numeric.dotprolog");
    has("-->", "keyword.operator.dotprolog");
    has("it", "string.quoted.single.dotprolog");
    has("text", "string.quoted.double.dotprolog");
    has("0'a", "constant.character.escape.dotprolog");
    has("note", "comment.line.percentage.dotprolog");
    const block = grammar.tokenizeLine("/* comment", INITIAL);
    assert.ok(
        grammar
            .tokenizeLine("continued */ p(X).", block.ruleStack)
            .tokens.some((token) =>
                token.scopes.includes("variable.other.dotprolog"),
            ),
    );
    registry.dispose();
});
