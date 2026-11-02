#!/usr/bin/env python3
"""Evaluate a real imported project and verify managed/native LSP project diagnostics."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
from xml.sax.saxutils import escape

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("lsp_smoke", Path(__file__).with_name("verify-language-server.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


def main(command):
    if not command:
        raise SystemExit("Usage: verify-language-server-project.py <executable> [arguments...]")
    repository = Path(__file__).resolve().parents[2]
    sdk = repository / "src/DotProlog.Sdk/Sdk"
    with tempfile.TemporaryDirectory(prefix="DotProlog project # ") as directory:
        folder = Path(directory)
        project = folder / "test.dplproj"
        main_source = folder / "main.pl"
        main_source.write_text('run :- helper, atom_codes(_, "abc").', encoding="utf-8")
        (folder / "helper.pl").write_text("helper.", encoding="utf-8")
        (folder / "ignored.pl").write_text("broken(", encoding="utf-8")
        (folder / "main.dpli").write_text("contract.", encoding="utf-8")
        (folder / "settings.props").write_text('''<Project><PropertyGroup Condition="'$(Configuration)' == 'Release'">
<DotPrologLanguageMode>strict-iso</DotPrologLanguageMode><DotPrologFlags>double_quotes=codes</DotPrologFlags>
</PropertyGroup><PropertyGroup Condition="'$(Configuration)' != 'Release'">
<DotPrologFlags>double_quotes=chars</DotPrologFlags></PropertyGroup></Project>''', encoding="utf-8")
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk" InitialTargets="DoNotRun">
<Import Project="{escape(str(sdk / 'Sdk.props'))}" />
<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
<Import Project="settings.props" />
<Import Project="{escape(str(sdk / 'Sdk.targets'))}" />
<ItemGroup><PrologCompile Remove="ignored.pl" /></ItemGroup>
<Target Name="DoNotRun"><Error Text="Evaluation must never execute targets" /></Target>
</Project>''', encoding="utf-8")
        def run(configuration, text=None):
            session = smoke.Session(command + ["--project", str(project), "--configuration", configuration])
            try:
                session.initialize()
                diagnostics = [session.diagnostic(main_source.as_uri(), None)]
                if text is not None:
                    session.open(main_source.as_uri(), text)
                    diagnostics.append(session.diagnostic(main_source.as_uri(), 1))
                session.finish()
                assert all(item.get("method") != "textDocument/publishDiagnostics" or
                           item["params"]["uri"] != (folder / "ignored.pl").as_uri() for item in session.history)
                return diagnostics
            finally:
                session.close()
        debug = run("Debug")
        assert any(item["code"] == "DPL3011" for item in debug[0]["diagnostics"])
        release = run("Release")
        assert not any(item["code"] in ("DPL3011", "DPL3029") for item in release[0]["diagnostics"])
        strict = run("Release", "value(`text`).")
        assert strict[-1]["version"] == 1
        assert any(item["severity"] == 1 for item in strict[-1]["diagnostics"])
        assert not (folder / "obj").exists(), "Evaluation must not restore or generate build artifacts"
        (folder / "settings.props").write_text('<Project><PropertyGroup><DotPrologFlags>bad=true</DotPrologFlags></PropertyGroup></Project>', encoding="utf-8")
        failed = subprocess.run(command + ["--project", str(project)], input=b"", capture_output=True, timeout=45, check=False)
        assert failed.returncode == 65, failed.stderr.decode()
        assert failed.stdout == b""
        assert b"overridable flag" in failed.stderr
    print("PASS: real MSBuild imports, configurations, source removal, cross-file diagnostics, flags, strict mode, and evaluation-only startup")


if __name__ == "__main__":
    main(sys.argv[1:])
