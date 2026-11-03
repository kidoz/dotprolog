import { createVSIX } from "@vscode/vsce";
import path from "node:path";
import { readFile } from "node:fs/promises";
import { publishServer, root } from "./server.mjs";

const target = process.argv[2];
await publishServer(target);
const manifest: { name: string; version: string; preview?: boolean } =
    JSON.parse(await readFile(path.join(root, "package.json"), "utf8"));
const packagePath = path.join(
    root,
    `bin/${manifest.name}-${manifest.version}${target ? `-${target}` : ""}.vsix`,
);
await createVSIX({
    cwd: root,
    packagePath,
    target,
    dependencies: false,
    preRelease: manifest.preview === true,
    baseContentUrl:
        "https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.VSCode",
    baseImagesUrl:
        "https://raw.githubusercontent.com/kidoz/dotprolog/main/src/DotProlog.VSCode",
});
console.log(`Install with: code --install-extension "${packagePath}"`);
