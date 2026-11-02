#!/usr/bin/env python3
"""Verify predicate/variable completion through managed or native LSP stdio."""
import importlib.util
from pathlib import Path
import sys

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)
URI = "untitled:completion"


def completion(session, identifier, character, line=0):
    session.send({"id": identifier, "method": "textDocument/completion", "params": {
        "textDocument": {"uri": URI}, "position": {"line": line, "character": character}}})
    return session.reply(identifier)["result"]


def change(session, version, text):
    session.send({"method": "textDocument/didChange", "params": {"textDocument": {
        "uri": URI, "version": version}, "contentChanges": [{"text": text}]}})


def main(command):
    session = smoke.Session(command)
    try:
        session.send({"id": 1, "method": "initialize", "params": {"capabilities": {}}})
        provider = session.reply(1)["result"]["capabilities"]["completionProvider"]
        assert provider == {"resolveProvider": False, "triggerCharacters": [":"]}
        session.send({"method": "initialized", "params": {}})
        line = "run :- write('😀'), memOther"
        session.open(URI, "% comment\r\n" + line)
        column = len(line[:line.index("memOther")].encode("utf-16-le")) // 2
        # Request before diagnostics to verify that completion waits for the accepted snapshot.
        items = completion(session, "builtin", column + 3, line=1)
        member = next(item for item in items if item["label"] == "member/2")
        assert member["kind"] == 3 and "list member" in member["documentation"]
        assert member["textEdit"] == {"newText": "member", "range": {
            "start": {"line": 1, "character": column}, "end": {"line": 1, "character": column + 8}}}
        text = "previous(Other). run(Value) :- pair(Va"
        change(session, 2, text)
        items = completion(session, "variable", len(text))
        assert [item["label"] for item in items] == ["Value"]
        assert items[0]["kind"] == 6
        text = ":- module(a, []). local. :- module(b, []). other. run :- lo"
        change(session, 3, text)
        assert not any(item["label"] == "local/0" for item in completion(session, "hidden", len(text)))
        text += "cal, a:lo"
        change(session, 4, text)
        assert any(item["label"] == "local/0" for item in completion(session, "qualified", len(text)))
        text = "member(_, _). run :- mem"
        change(session, 5, text)
        member = next(item for item in completion(session, "shadow", len(text)) if item["label"] == "member/2")
        assert member["detail"] == "Predicate in user" and "documentation" not in member
        text = "run :- /* mem"
        change(session, 6, text)
        assert completion(session, "comment", len(text)) == []
        session.close_document(URI)
        assert completion(session, "closed", 0) == []
        session.finish()
    finally:
        session.close()
    session = smoke.Session(command + ["--mode", "strict-iso"])
    try:
        session.initialize()
        session.open(URI, "run :- mem")
        assert completion(session, "strict", 10) == []
        session.finish()
    finally:
        session.close()
    print("PASS: completion capabilities, UTF-16 edits, incomplete buffers, variables, module visibility, shadowing and StrictIso")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-completion.py <executable> [arguments...]")
    main(sys.argv[1:])
