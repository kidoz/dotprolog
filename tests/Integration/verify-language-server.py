#!/usr/bin/env python3
"""Smoke-test a managed or native language server with asynchronous stdio diagnostics."""
import json
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import time


def frame(message):
    body = json.dumps(message, ensure_ascii=False).encode("utf-8")
    return f"Content-Length: {len(body)}\r\n\r\n".encode("ascii") + body


class Session:
    def __init__(self, command, env=None):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
        self.incoming = queue.Queue()
        self.pending = []
        self.history = []
        self.thread = threading.Thread(target=self.read, daemon=True)
        self.thread.start()

    def read(self):
        try:
            while header := self.process.stdout.readline():
                headers = {}
                while header != b"\r\n":
                    name, value = header.split(b":", 1)
                    headers[name.lower()] = value.strip()
                    header = self.process.stdout.readline()
                    assert header, "Truncated header"
                length = int(headers[b"content-length"])
                body = self.process.stdout.read(length)
                assert len(body) == length, "Truncated frame"
                self.incoming.put(json.loads(body))
            self.incoming.put(EOFError("Server stdout closed"))
        except Exception as error:
            self.incoming.put(error)

    def send(self, message):
        self.process.stdin.write(frame({"jsonrpc": "2.0", **message}))
        self.process.stdin.flush()

    def wait(self, predicate, timeout=40):
        for index, item in enumerate(self.pending):
            if predicate(item):
                return self.pending.pop(index)
        deadline = time.monotonic() + timeout
        while True:
            item = self.incoming.get(timeout=max(0, deadline - time.monotonic()))
            if isinstance(item, Exception):
                raise item
            self.history.append(item)
            if predicate(item):
                return item
            self.pending.append(item)

    def reply(self, identifier, timeout=40):
        return self.wait(lambda item: item.get("id") == identifier and "method" not in item, timeout)

    def diagnostic(self, uri, version="any"):
        return self.wait(lambda item: item.get("method") == "textDocument/publishDiagnostics"
                         and item["params"]["uri"] == uri
                         and (version == "any" or item["params"].get("version") == version))["params"]

    def changed(self, path, kind=2):
        self.send({"method": "workspace/didChangeWatchedFiles", "params": {"changes": [{"uri": path.as_uri(), "type": kind}]}})

    def initialize(self, watch=False, hierarchical=False):
        self.send({"id": 1, "method": "initialize", "params": {"capabilities": {
            "workspace": {"didChangeWatchedFiles": {"dynamicRegistration": watch}},
            "textDocument": {"documentSymbol": {"hierarchicalDocumentSymbolSupport": hierarchical}}}}})
        reply = self.reply(1)
        assert reply["result"]["capabilities"]["textDocumentSync"]["change"] == 1
        self.send({"method": "initialized", "params": {}})
        if watch:
            registration = self.wait(lambda item: item.get("method") == "client/registerCapability")
            self.send({"id": registration["id"], "result": None})

    def open(self, uri, text, version=1):
        self.send({"method": "textDocument/didOpen", "params": {"textDocument": {
            "uri": uri, "version": version, "languageId": "prolog", "text": text}}})

    def close_document(self, uri):
        self.send({"method": "textDocument/didClose", "params": {"textDocument": {"uri": uri}}})

    def finish(self):
        self.send({"id": "shutdown", "method": "shutdown"})
        assert self.reply("shutdown", timeout=5)["result"] is None
        self.send({"method": "exit"})
        assert self.process.wait(timeout=10) == 0
        assert not self.process.stderr.read()

    def close(self):
        if self.process.poll() is None:
            self.process.kill()
        self.process.wait(timeout=10)
        self.thread.join(timeout=10)
        self.process.stdin.close()
        self.process.stdout.close()
        self.process.stderr.close()


def workspace_smoke(command):
    with tempfile.TemporaryDirectory(prefix="DotProlog LSP # ") as directory:
        root = Path(directory) / "main.pl"
        child = Path(directory) / "operators.inc"
        operator_text = ":- op(500, xfy, likes)."
        child.write_text(operator_text, encoding="utf-8")
        session = Session(command)
        def errors():
            return session.diagnostic(root.as_uri())["diagnostics"]
        try:
            session.initialize(watch=True)
            session.open(root.as_uri(), ":- include('operators.inc').\nalice likes bob.")
            assert not any(d["severity"] == 1 for d in errors())
            session.diagnostic(child.as_uri(), None)
            session.open(child.as_uri(), "")
            assert any(d["severity"] == 1 for d in errors())
            session.diagnostic(child.as_uri(), 1)
            session.close_document(child.as_uri())
            assert not any(d["severity"] == 1 for d in errors())
            session.diagnostic(child.as_uri(), None)
            child.write_text("", encoding="utf-8")
            session.changed(child)
            assert any(d["severity"] == 1 for d in errors())
            child.write_text(operator_text, encoding="utf-8")
            session.changed(child)
            assert not any(d["severity"] == 1 for d in errors())
            child.unlink()
            session.changed(child, 3)
            assert any(d["code"] == "DPL3032" for d in errors())
            child.write_text(operator_text, encoding="utf-8")
            session.changed(child, 1)
            assert not any(d["severity"] == 1 for d in errors())
            session.open(child.as_uri(), operator_text)
            session.diagnostic(child.as_uri(), 1)
            child.unlink()
            session.changed(child, 3)
            # A new version proves a generation including the deletion completed with the overlay intact.
            session.send({"method": "textDocument/didChange", "params": {"textDocument": {
                "uri": child.as_uri(), "version": 2}, "contentChanges": [{"text": operator_text}]}})
            assert session.diagnostic(child.as_uri(), 2)["diagnostics"] == []
            session.close_document(child.as_uri())
            assert any(d["code"] == "DPL3032" for d in errors())
            session.close_document(root.as_uri())
            assert session.diagnostic(root.as_uri(), None)["diagnostics"] == []
            session.finish()
        finally:
            session.close()
    print("PASS: include overlays, watch registration, disk create/change/delete, close recovery and cleanup")


def main(command):
    if not command:
        raise SystemExit("Usage: verify-language-server.py <executable> [arguments...]")
    session = Session(command)
    uri = "untitled:smoke.pl"
    try:
        session.initialize()
        session.open(uri, ":- initialization(halt(42)).\r\nvalue('😀', X).")
        diagnostic = next(d for d in session.diagnostic(uri, 1)["diagnostics"] if d["code"] == "DPL3001")
        assert diagnostic["range"] == {"start": {"line": 1, "character": 12}, "end": {"line": 1, "character": 13}}
        for version in range(2, 22):
            session.send({"method": "textDocument/didChange", "params": {
                "textDocument": {"uri": uri, "version": version},
                "contentChanges": [{"text": "value(ok)." if version == 21 else "value(X)."}]}})
        assert session.diagnostic(uri, 21)["diagnostics"] == []
        versions = [item["params"].get("version") for item in session.history
                    if item.get("method") == "textDocument/publishDiagnostics"]
        assert versions == [1, 21], versions
        session.close_document(uri)
        assert session.diagnostic(uri, None)["diagnostics"] == []
        session.finish()
    finally:
        session.close()
    early_exit = subprocess.run(command, input=frame({"jsonrpc": "2.0", "method": "exit"}), capture_output=True, timeout=20, check=False)
    assert early_exit.returncode == 1 and not early_exit.stdout
    print("PASS: initialize, Unicode diagnostics, debounced edit burst, close, shutdown and early exit")
    workspace_smoke(command)


if __name__ == "__main__":
    main(sys.argv[1:])
