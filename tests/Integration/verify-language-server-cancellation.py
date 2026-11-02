#!/usr/bin/env python3
"""Verify responsive shutdown and evaluator process-tree cancellation on POSIX hosts."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


def alive(pid):
    # A reparented zombie has exited; it may remain briefly until the OS reaps it.
    status = subprocess.run(["ps", "-o", "stat=", "-p", str(pid)], capture_output=True, text=True, check=False)
    return status.returncode == 0 and status.stdout.strip() and not status.stdout.strip().startswith("Z")


def main(command):
    if os.name != "posix":
        raise SystemExit("This process-tree fixture requires POSIX; Windows verification remains separate.")
    executable = shutil.which(command[0])
    assert executable, command[0]
    command = [executable, *command[1:]]
    sdk = Path(__file__).resolve().parents[2] / "src/DotProlog.Sdk/Sdk"
    from xml.sax.saxutils import escape
    with tempfile.TemporaryDirectory(prefix="DotProlog cancel # ") as directory:
        folder = Path(directory)
        project = folder / "app.dplproj"
        (folder / "main.pl").write_text("main.", encoding="utf-8")
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<Import Project="{escape(str(sdk / 'Sdk.props'))}" />
<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
<Import Project="{escape(str(sdk / 'Sdk.targets'))}" /></Project>''', encoding="utf-8")
        marker = folder / "pids.json"
        child = folder / "child.py"
        child.write_text(f'''import json, os, pathlib, time
marker = pathlib.Path({str(marker.with_suffix('.tmp'))!r})
marker.write_text(json.dumps([os.getppid(), os.getpid()]))
marker.rename({str(marker)!r})
time.sleep(60)
''', encoding="utf-8")
        settings = folder / "stall.props"
        settings.write_text("<Project />", encoding="utf-8")
        project.write_text(project.read_text().replace("</Project>", '<Import Project="stall.props" /></Project>'), encoding="utf-8")
        environment = {**os.environ, "MSBUILDENABLEALLPROPERTYFUNCTIONS": "1"}
        session = smoke.Session(command + ["--project", str(project)], env=environment)
        pids = []
        try:
            session.initialize()
            session.diagnostic((folder / "main.pl").as_uri())
            settings.write_text(f'''<Project><PropertyGroup>
<Spawn>$([System.Diagnostics.Process]::Start('{escape(sys.executable)}', '&quot;{escape(str(child))}&quot;'))</Spawn>
<Delay>$([System.Threading.Thread]::Sleep(60000))</Delay>
</PropertyGroup></Project>''', encoding="utf-8")
            session.changed(settings)
            deadline = time.monotonic() + 10
            while not marker.exists() and time.monotonic() < deadline:
                time.sleep(0.01)
            assert marker.exists(), "Reevaluation did not start"
            # A tiny atomic-write window is avoided by publishing the fixture marker via rename.
            pids = json.loads(marker.read_text())
            session.send({"id": "responsive", "method": "unknown"})
            assert session.reply("responsive", timeout=2)["error"]["code"] == -32601
            session.send({"id": "stop", "method": "shutdown"})
            assert session.reply("stop", timeout=2)["result"] is None
            session.send({"method": "exit"})
            assert session.process.wait(timeout=10) == 0
            assert not session.process.stderr.read()
            deadline = time.monotonic() + 3
            while any(alive(pid) for pid in pids) and time.monotonic() < deadline:
                time.sleep(0.05)
            assert not any(alive(pid) for pid in pids), "Evaluator child processes survived shutdown"
        finally:
            session.close()
            for pid in pids:
                if alive(pid):
                    os.kill(pid, signal.SIGKILL)
    print("PASS: requests and shutdown during stalled reevaluation; evaluator and descendant terminated")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-cancellation.py <executable> [arguments...]")
    main(sys.argv[1:])
