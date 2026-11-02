#!/usr/bin/env python3
"""Verify source predicate references through real managed/native LSP sessions."""
import importlib.util
from pathlib import Path
import sys
import tempfile

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)
URI = "untitled:references"


def references(session, identifier, uri, line, character, declarations=False):
    session.send({"id": identifier, "method": "textDocument/references", "params": {
        "textDocument": {"uri": uri}, "position": {"line": line, "character": character},
        "context": {"includeDeclaration": declarations}}})
    return session.reply(identifier)["result"]


def initialize(session):
    session.send({"id": 1, "method": "initialize", "params": {"capabilities": {}}})
    assert session.reply(1)["result"]["capabilities"]["referencesProvider"] is True
    session.send({"method": "initialized", "params": {}})


def change(session, uri, version, text):
    session.send({"method": "textDocument/didChange", "params": {"textDocument": {
        "uri": uri, "version": version}, "contentChanges": [{"text": text}]}})


def main(command):
    session = smoke.Session(command)
    try:
        initialize(session)
        line = "run :- write('😀'), 'has space'(a), call('has space', b)."
        session.open(URI, "'has space'(a). 'has space'(b).\r\n" + line)
        calls = references(session, "calls", URI, 0, 1)
        columns = [len(line[:index].encode("utf-16-le")) // 2 for index in (line.index("'has space'"), line.rindex("'has space'"))]
        assert calls == [{"uri": URI, "range": {"start": {"line": 1, "character": column},
                         "end": {"line": 1, "character": column + 11}}} for column in columns]
        assert len(references(session, "heads", URI, 1, columns[0], True)) == 4
        session.open("untitled:unrelated", "'has space'(x). other :- 'has space'(x).")
        assert references(session, "isolated", URI, 0, 1) == calls
        change(session, URI, 2, "word(X) --> [X].\nstart --> word(a).\nrun :- phrase(word(a), []), call(word(a), [], []), write(word(data)).")
        assert len(references(session, "dcg", URI, 0, 0)) == 3
        assert len(references(session, "dcg-head", URI, 0, 0, True)) == 4
        session.send({"id": "invalid", "method": "textDocument/references", "params": {
            "textDocument": {"uri": URI}, "position": {"line": 0, "character": 0},
            "context": {"includeDeclaration": "false"}}})
        assert session.reply("invalid")["error"]["code"] == -32602
        session.close_document(URI)
        assert references(session, "closed", URI, 0, 0, True) == []
        session.finish()
    finally:
        session.close()

    with tempfile.TemporaryDirectory(prefix="lsp-references-") as folder:
        root = Path(folder)
        client, facade, library = (root / name for name in ("a-client.pl", "b-facade.pl", "z-library space.pl"))
        project = root / "references.dplproj"
        client.write_text(":- module(client, []). :- use_module('b-facade.pl'). run :- item(a), item(b).")
        facade.write_text(":- module(facade, []). :- reexport(library, [item/1]).")
        library.write_text(":- module(library, [item/1]).\nitem(a). item(b).")
        project.write_text('''<Project><PropertyGroup><DotPrologLanguageMode>Modern</DotPrologLanguageMode></PropertyGroup>
<ItemGroup><PrologCompile Include="a-client.pl;b-facade.pl;z-library space.pl" /></ItemGroup></Project>''')
        session = smoke.Session(command + ["--project", str(project)])
        try:
            initialize(session)
            calls = references(session, "import", library.as_uri(), 1, 0)
            assert len(calls) == 2 and all(item["uri"] == client.as_uri() for item in calls)
            assert len(references(session, "all", library.as_uri(), 1, 0, True)) == 4
            prefix = ":- module(client, []). :- use_module('b-facade.pl').\n"
            session.open(client.as_uri(), prefix + "/* 😀 */ run :- item(a).")
            calls = references(session, "overlay", library.as_uri(), 1, 0)
            assert len(calls) == 1 and calls[0]["range"]["start"] == {"line": 1, "character": 16}
            client.write_text(prefix + "run :- true.")
            session.changed(client)
            assert references(session, "overlay-wins", library.as_uri(), 1, 0) == calls
            session.close_document(client.as_uri())
            assert references(session, "disk", library.as_uri(), 1, 0) == []
            client.write_text(prefix + "run :- item(a).")
            session.changed(client)
            assert len(references(session, "disk-new", library.as_uri(), 1, 0)) == 1
            project.write_text('''<Project><PropertyGroup><DotPrologLanguageMode>Modern</DotPrologLanguageMode></PropertyGroup>
<ItemGroup><PrologCompile Include="b-facade.pl;z-library space.pl" /></ItemGroup></Project>''')
            session.changed(project)
            assert references(session, "membership", library.as_uri(), 1, 0) == []
            session.finish()
        finally:
            session.close()
    print("PASS: references, optional heads, UTF-16 locations, DCG/meta calls, loose-file isolation, reexports, overlays, disk updates and membership")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-references.py <executable> [arguments...]")
    main(sys.argv[1:])
