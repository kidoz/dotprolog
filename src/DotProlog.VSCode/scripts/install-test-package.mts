import assert from "node:assert/strict";
import { constants } from "node:fs";
import { access, cp, mkdir, readFile, readdir, rm } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { runVSCodeCommand } from "@vscode/test-electron";
import { root, run } from "./server.mjs";

const target = process.argv[2];
if (target && target !== `${process.platform}-${process.arch}`) {
    throw new Error(`Package target ${target} does not match this host.`);
}
const manifest = JSON.parse(
    await readFile(path.join(root, "package.json"), "utf8"),
);
const requested = process.env.VSCODE_VERSION || "stable";
const version =
    requested === "minimum"
        ? /^\^(\d+\.\d+\.\d+)$/.exec(manifest.engines.vscode)?.[1]
        : requested;
assert.ok(version, "Expected an explicit minimum VS Code version.");
const directory = path.join(root, "bin/installed");
await rm(directory, { recursive: true, force: true });
await mkdir(directory, { recursive: true });
const extensions = path.join(directory, "extensions");
const packagePath = path.join(
    root,
    `bin/${manifest.name}-${manifest.version}${target ? `-${target}` : ""}.vsix`,
);
const result = await runVSCodeCommand(
    [
        "--install-extension",
        packagePath,
        "--extensions-dir",
        extensions,
        "--user-data-dir",
        path.join(directory, "profile"),
        "--force",
        "--no-sandbox",
    ],
    { version, cachePath: path.join(root, "bin/vscode-test") },
);
console.log(result.stdout);
const candidates = (await readdir(extensions)).filter((name) =>
    name.startsWith(`${manifest.publisher}.${manifest.name}-`),
);
assert.equal(
    candidates.length,
    1,
    "Expected one installed DotProlog extension.",
);
const installed = path.join(extensions, candidates[0]);
const installedManifest = JSON.parse(
    await readFile(path.join(installed, "package.json"), "utf8"),
);
assert.equal(installedManifest.version, manifest.version);
// Preserve the modes installed by VS Code; a manual chmod would hide a broken VSIX.
const payload = path.join(directory, "extension");
await cp(installed, payload, { recursive: true });
const server = path.join(payload, "bin/server");
if (target) {
    const executable = path.join(
        server,
        process.platform === "win32" ? "dotprolog-lsp.exe" : "dotprolog-lsp",
    );
    await access(executable, constants.X_OK);
    assert.ok(
        !(await readdir(server)).includes("dotprolog-lsp.dll"),
        "Native package must not fall back to a managed server.",
    );
    const pythonCommand = process.platform === "win32" ? "python" : "python3";
    const python = spawnSync(
        pythonCommand,
        ["-c", "import sys; print(sys.executable)"],
        { encoding: "utf8" },
    );
    assert.equal(python.status, 0, python.stderr);
    const empty = path.join(directory, "no-dotnet");
    await mkdir(empty);
    const env = Object.fromEntries(
        Object.entries(process.env).filter(
            ([key]) =>
                key.toUpperCase() !== "PATH" &&
                !key.toUpperCase().startsWith("DOTNET_ROOT"),
        ),
    );
    run(
        python.stdout.trim(),
        [
            path.resolve(
                root,
                "../../tests/Integration/verify-language-server.py",
            ),
            executable,
        ],
        {
            env: {
                ...env,
                PATH: empty,
                DOTNET_ROOT: empty,
                DOTNET_ROOT_X64: empty,
                DOTNET_ROOT_ARM64: empty,
            },
            timeout: 120000,
        },
    );
    console.log(
        `PASS: installed ${target} native server with an empty PATH and isolated DOTNET_ROOT.`,
    );
} else {
    await access(path.join(server, "dotprolog-lsp.dll"));
}
console.log(`Verified installed package: ${payload}`);
