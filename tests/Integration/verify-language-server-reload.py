#!/usr/bin/env python3
"""Exercise live project reload with real MSBuild and a managed or native server."""
import importlib.util
from pathlib import Path
import sys
import tempfile
from xml.sax.saxutils import escape

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


def main(command):
    sdk = Path(__file__).resolve().parents[2] / "src/DotProlog.Sdk/Sdk"
    with tempfile.TemporaryDirectory(prefix="DotProlog reload # ") as directory:
        folder = Path(directory)
        project = folder / "app.dplproj"
        settings = folder / "settings.custom"
        source = folder / "main.pl"
        source.write_text('run :- atom_codes(_, "abc").', encoding="utf-8")
        xml = f'''<Project Sdk="Microsoft.NET.Sdk" InitialTargets="NoBuild">
<Import Project="{escape(str(sdk / 'Sdk.props'))}" />
<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
<Import Project="{escape(str(sdk / 'Sdk.targets'))}" />
<Import Project="settings.custom" />
<Target Name="NoBuild"><Error Text="Must not build" /></Target></Project>'''
        project.write_text(xml, encoding="utf-8")
        def configure(mode, flags):
            settings.write_text(f'<Project><PropertyGroup><DotPrologLanguageMode>{mode}</DotPrologLanguageMode><DotPrologFlags>{flags}</DotPrologFlags></PropertyGroup></Project>', encoding="utf-8")
        configure("modern", "double_quotes=codes")
        session = smoke.Session(command + ["--project", str(project)])
        try:
            session.initialize()
            def diagnostics(path):
                return session.diagnostic(path.as_uri())
            def changed(path, kind=2):
                session.changed(path, kind)
                return diagnostics(path if path.suffix == ".pl" else source)
            assert not any(d["code"] == "DPL3011" for d in diagnostics(source)["diagnostics"])
            configure("modern", "double_quotes=chars")
            assert any(d["code"] == "DPL3011" for d in changed(settings)["diagnostics"])
            added = folder / "new.pl"
            added.write_text("new(X).", encoding="utf-8")
            assert any(d["code"] == "DPL3001" for d in changed(added, 1)["diagnostics"])
            added.unlink()
            assert changed(added, 3)["diagnostics"] == []
            configure("modern", "invalid=true")
            session.changed(settings)
            session.wait(lambda item: item.get("method") == "window/logMessage" and item["params"]["type"] == 1)
            # Duplicate failures are also covered deterministically by the scheduler tests.
            session.changed(settings)
            session.send({"method": "textDocument/didOpen", "params": {"textDocument": {
                "uri": source.as_uri(), "languageId": "prolog", "version": 4, "text": "value(`unsaved`)."}}})
            assert not any(d["severity"] == 1 for d in diagnostics(source)["diagnostics"])
            configure("strict-iso", "")
            session.changed(settings)
            session.wait(lambda item: item.get("method") == "window/logMessage" and item["params"]["type"] == 3)
            updated = diagnostics(source)
            assert updated["version"] == 4
            assert any(d["severity"] == 1 for d in updated["diagnostics"]), "Unsaved text must survive reload"
            project.write_text(xml.replace('<Target Name="NoBuild">', '<ItemGroup><PrologCompile Remove="main.pl" /></ItemGroup><Target Name="NoBuild">'), encoding="utf-8")
            removed = changed(project)
            assert removed["version"] == 4
            assert not any(d["severity"] == 1 for d in removed["diagnostics"]), "Removed open file should use loose-file mode"
            project.write_text(xml, encoding="utf-8")
            assert any(d["severity"] == 1 for d in changed(project)["diagnostics"])
            session.finish()
            assert not (folder / "obj").exists()
        finally:
            session.close()
    print("PASS: live imports, flags, glob membership, failure retention, recovery, unsaved buffers, and membership removal")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-language-server-reload.py <executable> [arguments...]")
    main(sys.argv[1:])
