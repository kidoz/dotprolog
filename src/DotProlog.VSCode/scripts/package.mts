import { createVSIX } from "@vscode/vsce";
import path from "node:path";
import { readFile } from "node:fs/promises";
import { publishServer, root } from "./server.mjs";

const target = process.argv[2];
await publishServer(target);
const manifest: { name: string; version: string } = JSON.parse(
    await readFile(path.join(root, "package.json"), "utf8"),
);
const packagePath = path.join(
    root,
    `bin/${manifest.name}-${manifest.version}${target ? `-${target}` : ""}.vsix`,
);
await createVSIX({ cwd: root, packagePath, target, dependencies: false });
console.log(`Install with: code --install-extension "${packagePath}"`);
