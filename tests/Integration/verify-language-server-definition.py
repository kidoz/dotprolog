#!/usr/bin/env python3
"""Verify definition navigation over managed/native LSP stdio, including project buffers."""
import importlib.util
from pathlib import Path
import sys
import tempfile

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)
URI = "untitled:definition"


def definition(session, identifier, uri, line, character):
    session.send({"id": identifier, "method": "textDocument/definition", "params": {
        "textDocument": {"uri": uri}, "position": {"line": line, "character": character}}})
    return session.reply(identifier)["result"]


def initialize(session, links):
    session.send({"id": 1, "method": "initialize", "params": {"capabilities": {
        "textDocument": {"definition": {"linkSupport": links}}}}})
    assert session.reply(1)["result"]["capabilities"]["definitionProvider"] is True
    session.send({"method": "initialized", "params": {}})


def change(session, uri, version, text):
    session.send({"method": "textDocument/didChange", "params": {"textDocument": {
        "uri": uri, "version": version}, "contentChanges": [{"text": text}]}})


def main(command):
    for links in (False, True):
        session = smoke.Session(command)
        try:
            initialize(session, links)
            line = "run :- write('😀'), 'has space'(a)."
            session.open(URI, "'has space'(a). 'has space'(b).\r\n" + line)
            column = len(line[:line.index("'has space'")].encode("utf-16-le")) // 2
            result = definition(session, "quoted", URI, 1, column + 1)
            assert len(result) == 2
            for index, target in enumerate(result):
                assert target["targetUri" if links else "uri"] == URI
                assert target["targetSelectionRange" if links else "range"] == {
                    "start": {"line": 0, "character": index * 16}, "end": {"line": 0, "character": index * 16 + 11}}
                if links:
                    assert target["originSelectionRange"] == {
                        "start": {"line": 1, "character": column}, "end": {"line": 1, "character": column + 11}}
                    assert target["targetRange"]["end"]["character"] > index * 16 + 11
            change(session, URI, 2, "word(X) --> [X].\nrun :- phrase(word(a), []).")
            result = definition(session, "grammar", URI, 1, 14)
            assert len(result) == 1
            assert result[0]["targetSelectionRange" if links else "range"]["start"] == {"line": 0, "character": 0}
            change(session, URI, 3, "word(_). run :- write(word(data)).")
            assert definition(session, "data", URI, 0, 22) == []
            assert definition(session, "builtin", URI, 0, 16) == []
            session.close_document(URI)
            assert definition(session, "closed", URI, 0, 0) == []
            session.finish()
        finally:
            session.close()

    with tempfile.TemporaryDirectory(prefix="lsp-definition-") as folder:
        root = Path(folder)
        main_file = root / "a-client.pl"
        facade = root / "b-facade.pl"
        library = root / "z-library space.pl"
        project = root / "navigation.dplproj"
        main_file.write_text(":- module(client, []). :- use_module('b-facade.pl'). run :- item(X).")
        facade.write_text(":- module(facade, []). :- reexport(library, [item/1]).")
        library.write_text(":- module(library, [item/1]). item(one). item(two).")
        project.write_text('''<Project><PropertyGroup><DotPrologLanguageMode>Modern</DotPrologLanguageMode></PropertyGroup>
<ItemGroup><PrologCompile Include="a-client.pl;b-facade.pl;z-library space.pl" /></ItemGroup></Project>''')
        session = smoke.Session(command + ["--project", str(project)])
        try:
            initialize(session, True)
            # Client appears first in evaluated source order; reexports must still resolve.
            column = main_file.read_text().index("item(X)")
            result = definition(session, "import", main_file.as_uri(), 0, column)
            assert len(result) == 2
            assert all(target["targetUri"] == library.as_uri() for target in result)
            session.open(library.as_uri(), ":- module(library, [item/1]).\n/* 😀 */ item(edited).")
            target = definition(session, "overlay", main_file.as_uri(), 0, column)[0]
            assert target["targetSelectionRange"]["start"] == {"line": 1, "character": 9}
            session.close_document(library.as_uri())
            assert len(definition(session, "disk", main_file.as_uri(), 0, column)) == 2
            library.write_text(":- module(library, [item/1]).")
            session.changed(library, 2)
            assert definition(session, "removed", main_file.as_uri(), 0, column) == []
            session.finish()
        finally:
            session.close()
    print("PASS: definition links/locations, UTF-16 ranges, multiple clauses, DCGs, reexports, target overlays, disk changes and close cleanup")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-definition.py <executable> [arguments...]")
    main(sys.argv[1:])
