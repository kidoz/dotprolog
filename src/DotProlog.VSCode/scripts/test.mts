import { runTests, downloadAndUnzipVSCode } from "@vscode/test-electron";
import { spawn, spawnSync } from "node:child_process";
import {
    mkdtemp,
    mkdir,
    readFile,
    realpath,
    writeFile,
    rm,
    cp,
} from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { root } from "./server.mjs";

const extensionPath = process.env.DOTPROLOG_EXTENSION_PATH || root;
const requestedVersion = process.env.VSCODE_VERSION || "stable";
let version = requestedVersion;
if (requestedVersion === "minimum") {
    const manifest = JSON.parse(
        await readFile(path.join(extensionPath, "package.json"), "utf8"),
    );
    const minimum = /^\^(\d+\.\d+\.\d+)$/.exec(manifest.engines.vscode);
    if (!minimum) {
        throw new Error(
            "Minimum-version tests require an explicit ^major.minor.patch VS Code engine range.",
        );
    }
    version = minimum[1];
}
if (version !== "stable" && !/^\d+\.\d+\.\d+$/.test(version)) {
    throw new Error(
        "VSCODE_VERSION must be minimum, stable or an exact major.minor.patch version.",
    );
}
console.log(
    `Testing VS Code ${version} (${requestedVersion}) with ${extensionPath}`,
);
const dotnetLookup = spawnSync(
    process.platform === "win32" ? "where" : "which",
    ["dotnet"],
    { encoding: "utf8" },
);
if (dotnetLookup.status !== 0) {
    throw new Error("Editor tests require dotnet on PATH.");
}
const dotnetPath = dotnetLookup.stdout.split(/\r?\n/, 1)[0].trim();
const sdkList = spawnSync(dotnetPath, ["--list-sdks"], { encoding: "utf8" });
if (sdkList.status !== 0) {
    throw new Error(`Could not enumerate .NET SDKs: ${sdkList.stderr}`);
}
const net11Sdk = sdkList.stdout
    .split(/\r?\n/)
    .map((line) => /^((?:1[1-9]|[2-9]\d)\.\d+\.\d+)\s/.exec(line)?.[1])
    .filter((version) => version !== undefined)
    .at(-1);

// Windows TEMP can use an 8.3 alias (RUNNER~1). .NET expands that alias when
// publishing unopened-file diagnostics, so give both processes the same physical path.
const temp = await realpath(
    await mkdtemp(path.join(os.tmpdir(), "dotprolog-vscode-")),
);
const workspace = path.join(temp, "workspace with spaces");
const restricted = process.argv.includes("--restricted");
await mkdir(workspace);
await mkdir(path.join(workspace, ".vscode"));
await writeFile(
    path.join(workspace, ".vscode/settings.json"),
    JSON.stringify({
        "files.autoSave": "off",
        "editor.wordBasedSuggestions": "off",
    }),
);
await mkdir(path.join(temp, "profile/User"), { recursive: true });
await writeFile(
    path.join(temp, "profile/User/settings.json"),
    JSON.stringify({ "security.workspace.trust.startupPrompt": "never" }),
);
// Use a real Extension Host with a fresh profile, not the developer's settings or extensions.
try {
    const options = {
        extensionDevelopmentPath: extensionPath,
        extensionTestsPath: path.join(root, "bin/test-runner.cjs"),
        vscodeExecutablePath: process.env.VSCODE_EXECUTABLE_PATH,
        version,
        cachePath: path.join(root, "bin/vscode-test"),
        launchArgs: [
            workspace,
            "--disable-extensions",
            ...(restricted ? [] : ["--disable-workspace-trust"]),
            "--skip-welcome",
            "--skip-release-notes",
            "--extensions-dir",
            path.join(temp, "extensions"),
            "--user-data-dir",
            path.join(temp, "profile"),
        ],
        extensionTestsEnv: {
            DOTPROLOG_TEST_WORKSPACE: workspace,
            DOTPROLOG_TEST_RESTRICTED: restricted ? "1" : "",
            DOTPROLOG_TEST_VSCODE_VERSION: version,
            DOTPROLOG_TEST_DOTNET_PATH: dotnetPath,
            DOTPROLOG_TEST_NET11_SDK: net11Sdk ?? "",
        },
    };
    if (restricted) {
        // runTests always appends --disable-workspace-trust. Launch the same Extension Host
        // directly for this check so Restricted Mode is actually exercised.
        const executable =
            options.vscodeExecutablePath ||
            (await downloadAndUnzipVSCode({
                cachePath: options.cachePath,
                version,
            }));
        const child = spawn(
            executable,
            [
                ...options.launchArgs,
                "--no-sandbox",
                "--disable-gpu-sandbox",
                "--disable-updates",
                `--extensionDevelopmentPath=${options.extensionDevelopmentPath}`,
                `--extensionTestsPath=${options.extensionTestsPath}`,
            ],
            {
                stdio: "inherit",
                env: { ...process.env, ...options.extensionTestsEnv },
                shell: false,
            },
        );
        await new Promise<void>((resolve, reject) => {
            child.on("error", reject);
            child.on("exit", (code) =>
                code === 0
                    ? resolve()
                    : reject(
                          new Error(
                              `Restricted Mode tests exited with ${code}`,
                          ),
                      ),
            );
        });
    } else {
        await runTests(options);
    }
} catch (error) {
    const logs = path.join(root, "bin/last-editor-run");
    await rm(logs, { recursive: true, force: true });
    await cp(temp, logs, { recursive: true });
    throw error;
} finally {
    await rm(temp, { recursive: true, force: true });
}
