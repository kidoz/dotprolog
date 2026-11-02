#!/usr/bin/env python3
"""Verify builtin hover over real managed/native stdio, including unsaved updates."""
import importlib.util
from pathlib import Path
import sys

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


def hover(session, identifier, character, line=0):
    session.send({"id": identifier, "method": "textDocument/hover", "params": {
        "textDocument": {"uri": "untitled:hover"}, "position": {"line": line, "character": character}}})
    return session.reply(identifier)["result"]


def main(command):
    for kind in ("plaintext", "markdown"):
        session = smoke.Session(command)
        try:
            session.send({"id": 1, "method": "initialize", "params": {"capabilities": {
                "textDocument": {"hover": {"contentFormat": [kind]}}}}})
            assert session.reply(1)["result"]["capabilities"]["hoverProvider"] is True
            session.send({"method": "initialized", "params": {}})
            line = "run :- write('😀'), 'member'(a, [])."
            session.open("untitled:hover", "% comment\r\n" + line)
            # Compute protocol columns independently in UTF-16 code units.
            column = len(line[:line.index("'member'")].encode("utf-16-le")) // 2
            result = hover(session, "builtin", column + 1, line=1)
            assert result["contents"]["kind"] == kind
            assert "member/2" in result["contents"]["value"]
            assert "list member" in result["contents"]["value"]
            assert result["range"] == {"start": {"line": 1, "character": column}, "end": {"line": 1, "character": column + 8}}
            session.send({"method": "textDocument/didChange", "params": {"textDocument": {
                "uri": "untitled:hover", "version": 2}, "contentChanges": [{"text": "member(_, _). run :- member(a, [])."}]}})
            assert hover(session, "shadowed", 20) is None
            session.send({"method": "textDocument/didChange", "params": {"textDocument": {
                "uri": "untitled:hover", "version": 3}, "contentChanges": [{"text": "run :- write(member(a, []))."}]}})
            assert hover(session, "data", 13) is None
            assert "write/1" in hover(session, "writer", 7)["contents"]["value"]
            session.close_document("untitled:hover")
            assert hover(session, "closed", 7) is None
            session.finish()
        finally:
            session.close()
    session = smoke.Session(command + ["--mode", "strict-iso"])
    try:
        session.initialize()
        session.open("untitled:hover", "run :- member(a, []).")
        assert "unavailable" in hover(session, "strict", 7)["contents"]["value"]
        session.finish()
    finally:
        session.close()
    print("PASS: builtin hover, negotiated markup, UTF-16 ranges, shadowing, data exclusion, current edits and StrictIso notes")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-hover.py <executable> [arguments...]")
    main(sys.argv[1:])
