import { build } from "esbuild";
import { mkdir, readFile, writeFile, copyFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tests = path.resolve(root, "../../tests/DotProlog.VSCode.Tests");
const common = {
    bundle: true,
    platform: "node",
    target: "node20",
    format: "cjs",
    nodePaths: [path.join(root, "node_modules")],
    external: ["vscode", "vscode-oniguruma/release/onig.wasm"],
    legalComments: "inline",
};
await mkdir(path.join(root, "bin"), { recursive: true });
await build({
    ...common,
    entryPoints: [path.join(root, "src/extension.ts")],
    outfile: path.join(root, "bin/extension.js"),
});
await build({
    ...common,
    entryPoints: [
        path.join(tests, "launch.test.ts"),
        path.join(tests, "grammar.test.ts"),
    ],
    outdir: path.join(root, "bin/unit"),
});
await build({
    ...common,
    entryPoints: [
        path.join(tests, "editor.test.ts"),
        path.join(tests, "restricted.test.ts"),
    ],
    outdir: path.join(root, "bin/integration"),
});
await copyFile(path.resolve(root, "../../LICENSE"), path.join(root, "LICENSE"));
// Retain the full license texts for the npm runtime dependencies included by the bundle.
const visited = new Set();
let notices = "";
async function license(name, parent = root) {
    const require = createRequire(path.join(parent, "package.json"));
    let directory = path.dirname(require.resolve(name));
    while (true) {
        try {
            const candidate = JSON.parse(
                await readFile(path.join(directory, "package.json"), "utf8"),
            );
            if (candidate.name === name) {
                break;
            }
        } catch {}
        const next = path.dirname(directory);
        if (next === directory) {
            throw new Error(`Cannot locate package ${name}`);
        }
        directory = next;
    }
    const manifest = JSON.parse(
        await readFile(path.join(directory, "package.json"), "utf8"),
    );
    const identity = `${name}@${manifest.version}`;
    if (visited.has(identity)) {
        return;
    }
    visited.add(identity);
    let text;
    for (const file of [
        "License.txt",
        "LICENSE",
        "LICENSE.txt",
        "LICENSE.md",
    ]) {
        try {
            text = await readFile(path.join(directory, file), "utf8");
            break;
        } catch {}
    }
    if (!text) {
        throw new Error(`Missing license text for ${name}`);
    }
    notices += `\n${name} ${manifest.version}\n${text}\n`;
    for (const dependency of Object.keys(manifest.dependencies ?? {})) {
        await license(dependency, directory);
    }
}
await license("vscode-languageclient");
await writeFile(path.join(root, "bin/THIRD_PARTY_NOTICES.txt"), notices);
