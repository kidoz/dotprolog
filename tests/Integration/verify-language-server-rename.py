#!/usr/bin/env python3
"""Verify conservative predicate rename over real managed/native LSP stdio."""
import importlib.util
from pathlib import Path
import sys
import tempfile

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)
URI = "untitled:rename"


def initialize(session):
    session.send({"id": 1, "method": "initialize", "params": {"capabilities": {
        "workspace": {"workspaceEdit": {"documentChanges": True}},
        "textDocument": {"rename": {"prepareSupport": True}}}}})
    assert session.reply(1)["result"]["capabilities"]["renameProvider"] == {"prepareProvider": True}
    session.send({"method": "initialized", "params": {}})


def request(session, identifier, uri, line=0, character=0, name=None):
    params = {"textDocument": {"uri": uri}, "position": {"line": line, "character": character}}
    if name is not None:
        params["newName"] = name
    session.send({"id": identifier, "method": "textDocument/rename" if name is not None else "textDocument/prepareRename",
                  "params": params})
    return session.reply(identifier)


def change(session, uri, version, text):
    session.send({"method": "textDocument/didChange", "params": {"textDocument": {
        "uri": uri, "version": version}, "contentChanges": [{"text": text}]}})


def apply(text, edits):
    # LSP offsets are UTF-16 code units, including before/after astral characters.
    lines = text.splitlines(keepends=True)
    def offset(position):
        return len("".join(lines[:position["line"]]).encode("utf-16-le")) + position["character"] * 2
    encoded = text.encode("utf-16-le")
    for edit in reversed(edits):
        start, end = offset(edit["range"]["start"]), offset(edit["range"]["end"])
        encoded = encoded[:start] + edit["newText"].encode("utf-16-le") + encoded[end:]
    return encoded.decode("utf-16-le")


def main(command):
    session = smoke.Session(command)
    try:
        initialize(session)
        text = "'item'(a). item(b). item(_, _).\r\nrun :- write('😀'), 'item'(a), call(item, b), write(item(data)), item(a,b)."
        session.open(URI, text, 7)
        prepared = request(session, "prepare", URI)["result"]
        assert prepared == {"range": {"start": {"line": 0, "character": 0}, "end": {"line": 0, "character": 6}}, "placeholder": "item"}
        session.open("untitled:unrelated", "item(x). other :- item(x).")
        result = request(session, "rename", URI, name="entry")["result"]
        edits = result["documentChanges"]
        assert len(edits) == 1 and edits[0]["textDocument"] == {"uri": URI, "version": 7}
        assert len(edits[0]["edits"]) == 4
        renamed = apply(text, edits[0]["edits"])
        assert renamed == "entry(a). entry(b). item(_, _).\r\nrun :- write('😀'), entry(a), call(entry, b), write(item(data)), item(a,b)."
        assert request(session, "still-original", URI)["result"]["placeholder"] == "item"
        assert request(session, "bad-name", URI, name="Invalid")["error"]["code"] == -32602
        change(session, URI, 8, renamed)
        assert request(session, "applied", URI)["result"]["placeholder"] == "entry"
        assert request(session, "no-op", URI, name="entry")["result"] == {"documentChanges": []}
        change(session, URI, 9, "p. occupied. run :- p.")
        assert request(session, "collision", URI, name="occupied")["error"]["code"] == -32803
        for index, suffix in enumerate(("run(G) :- call(G).", ":- dynamic(p/0).", "run :- assertz(p).", "bad(.", "run :- missing."), 10):
            change(session, URI, index, "p. " + suffix)
            assert request(session, f"refuse-{index}", URI)["result"] is None
            assert request(session, f"error-{index}", URI, name="renamed")["error"]["code"] == -32803
        change(session, URI, 15, ":- module(m, [word//1]). word(X) --> [X]. start --> word(a). run :- phrase(word(a), []), call(word(a), [], []).")
        dcg = request(session, "dcg", URI, character=14, name="token")["result"]["documentChanges"]
        assert len(dcg) == 1 and len(dcg[0]["edits"]) == 5
        session.finish()
    finally:
        session.close()

    with tempfile.TemporaryDirectory(prefix="lsp-rename-") as folder:
        root = Path(folder)
        client, facade, library = (root / name for name in ("a-client.pl", "b-facade.pl", "z-library.pl"))
        project = root / "rename.dplproj"
        sources = {
            client: ":- module(client, []). :- use_module('b-facade.pl', [item/1]).\nrun :- item(a), library:item(b).",
            facade: ":- module(facade, []). :- reexport(library, [item/1]).",
            library: ":- module(library, [item/1]).\nitem(a).",
        }
        for path, text in sources.items():
            path.write_text(text, encoding="utf-8")
        project_text = '''<Project><PropertyGroup><DotPrologLanguageMode>Modern</DotPrologLanguageMode></PropertyGroup>
<ItemGroup><PrologCompile Include="a-client.pl;b-facade.pl;z-library.pl" /></ItemGroup></Project>'''
        project.write_text(project_text)
        session = smoke.Session(command + ["--project", str(project)])
        try:
            initialize(session)
            overlay = sources[library] + " item(b)."
            session.open(library.as_uri(), overlay, 3)
            result = request(session, "project", client.as_uri(), character=sources[client].index("item/1"), name="entry")["result"]
            changes = result["documentChanges"]
            assert len(changes) == 3 and sum(len(item["edits"]) for item in changes) == 7
            for path, text in sources.items():
                assert path.read_text(encoding="utf-8") == text
            project.write_text("<Project>broken")
            session.changed(project)
            assert request(session, "stale-prepare", library.as_uri(), line=1)["result"] is None
            assert "reevaluation" in request(session, "stale-rename", library.as_uri(), line=1, name="entry")["error"]["message"]
            project.write_text(project_text)
            session.changed(project)
            assert request(session, "recovered", library.as_uri(), line=1)["result"]["placeholder"] == "item"
            for item in changes:
                uri = item["textDocument"]["uri"]
                path = next(path for path in sources if path.as_uri() == uri)
                version = item["textDocument"]["version"]
                assert version == (3 if path == library else None)
                updated = apply(overlay if path == library else sources[path], item["edits"])
                if path == library:
                    change(session, uri, 4, updated)
                else:
                    session.open(uri, updated, 4)
            assert request(session, "applied-project", library.as_uri(), line=1)["result"]["placeholder"] == "entry"
            session.send({"id": "references", "method": "textDocument/references", "params": {
                "textDocument": {"uri": library.as_uri()}, "position": {"line": 1, "character": 0},
                "context": {"includeDeclaration": True}}})
            assert len(session.reply("references")["result"]) == 4
            session.finish()
        finally:
            session.close()
    print("PASS: versioned predicate rename, UTF-16 edits, declarations, DCGs, reexports, refusal cases and project reload recovery")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-rename.py <executable> [arguments...]")
    main(sys.argv[1:])
