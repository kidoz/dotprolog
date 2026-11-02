#!/usr/bin/env python3
"""Verify document outlines against a managed or native server over real stdio."""
import importlib.util
from pathlib import Path
import sys

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


def main(command):
    for hierarchical in (False, True):
        session = smoke.Session(command)
        uri = "untitled:outline"
        try:
            session.initialize(hierarchical=hierarchical)
            source = ":- module(m, []).\r\n'p😀'(a).\r\n'p😀'(b).\r\ng(X), [X] --> [X].\r\nbroken("
            session.open(uri, source)
            session.send({"id": "outline", "method": "textDocument/documentSymbol",
                          "params": {"textDocument": {"uri": uri}}})
            symbols = session.reply("outline")["result"]
            assert [item["name"] for item in symbols] == ["m:p😀/1", "m:g//1"], symbols
            if hierarchical:
                assert len(symbols[0]["children"]) == 2
                assert symbols[0]["selectionRange"] == {"start": {"line": 1, "character": 0}, "end": {"line": 1, "character": 5}}
                assert symbols[1]["detail"] == "DCG"
            else:
                assert symbols[0]["location"]["uri"] == uri
                assert symbols[0]["containerName"] == "m"
            for version in range(2, 12):
                session.send({"method": "textDocument/didChange", "params": {
                    "textDocument": {"uri": uri, "version": version},
                    "contentChanges": [{"text": "latest." if version == 11 else "obsolete."}]}})
            session.send({"id": "latest", "method": "textDocument/documentSymbol",
                          "params": {"textDocument": {"uri": uri}}})
            assert [item["name"] for item in session.reply("latest")["result"]] == ["latest/0"]
            session.close_document(uri)
            session.send({"id": "closed", "method": "textDocument/documentSymbol",
                          "params": {"textDocument": {"uri": uri}}})
            assert session.reply("closed")["result"] == []
            session.finish()
        finally:
            session.close()
    print("PASS: hierarchical/flat outlines, Unicode name ranges, DCGs, recovered syntax and current unsaved snapshots")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-symbols.py <executable> [arguments...]")
    main(sys.argv[1:])
