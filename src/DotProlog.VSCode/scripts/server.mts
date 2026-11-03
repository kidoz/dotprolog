import { spawnSync, type SpawnSyncOptions } from "node:child_process";
import { rm, mkdir, readFile, writeFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const root = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    "../..",
);
export function run(
    command: string,
    args: string[],
    options: SpawnSyncOptions = {},
): void {
    const result = spawnSync(command, args, {
        cwd: root,
        stdio: "inherit",
        shell: false,
        ...options,
    });
    if (result.error) {
        throw result.error;
    }
    if (result.status !== 0) {
        throw new Error(`${command} exited with ${result.status}`);
    }
}

interface PackageAssets {
    libraries: Record<string, { type: string; path: string }>;
    packageFolders: Record<string, unknown>;
}

export async function publishServer(target?: string): Promise<void> {
    const rids: Record<string, string> = {
        "darwin-arm64": "osx-arm64",
        "darwin-x64": "osx-x64",
        "linux-x64": "linux-x64",
        "linux-arm64": "linux-arm64",
        "win32-x64": "win-x64",
        "win32-arm64": "win-arm64",
    };
    if (target && !rids[target]) {
        throw new Error(`Unsupported VS Code target: ${target}`);
    }
    const output = path.join(root, "bin/server");
    await rm(output, { recursive: true, force: true });
    await mkdir(output, { recursive: true });
    run("dotnet", [
        "publish",
        path.resolve(root, "../DotProlog.LanguageServer"),
        "-c",
        "Release",
        "-o",
        output,
        "-m:1",
        "-nr:false",
        ...(target
            ? [
                  "-r",
                  rids[target],
                  "-p:PublishAot=true",
                  "-p:IlcTreatWarningsAsErrors=true",
                  "-p:ILLinkTreatWarningsAsErrors=true",
              ]
            : ["--no-self-contained", "-p:UseAppHost=false"]),
    ]);
    const assets: PackageAssets = JSON.parse(
        await readFile(
            path.resolve(
                root,
                "../DotProlog.LanguageServer/obj/project.assets.json",
            ),
            "utf8",
        ),
    );
    const mit = (
        await readFile(path.resolve(root, "../../LICENSE"), "utf8")
    ).split("Permission is hereby granted")[1];
    let notices =
        "Notices for NuGet dependencies used to build the bundled DotProlog server.\n";
    for (const [identity, library] of Object.entries(assets.libraries)) {
        if (library.type !== "package") {
            continue;
        }
        let directory;
        for (const cache of Object.keys(assets.packageFolders)) {
            const candidate = path.join(cache, library.path);
            try {
                await readdir(candidate);
                directory = candidate;
                break;
            } catch {}
        }
        if (!directory) {
            throw new Error(`Missing package license metadata for ${identity}`);
        }
        const files = await readdir(directory);
        const nuspec = files.find((file) => file.endsWith(".nuspec"));
        if (!nuspec) {
            throw new Error(`Missing package license metadata for ${identity}`);
        }
        const metadata = await readFile(path.join(directory, nuspec), "utf8");
        const field = (name: string): string =>
            new RegExp(`<${name}(?: [^>]*)?>([\\s\\S]*?)</${name}>`).exec(
                metadata,
            )?.[1] ?? "";
        notices += `\n${identity}\nAuthors: ${field("authors")}\n${field("copyright")}\nLicense: ${field("license")}\n`;
        if (field("license") === "MIT") {
            notices += `MIT License\nPermission is hereby granted${mit}\n`;
        } else if (/<license type="file">/.test(metadata)) {
            notices += await readFile(
                path.join(directory, field("license")),
                "utf8",
            );
        } else {
            throw new Error(
                `Add license text handling for ${identity}: ${field("license")}`,
            );
        }
        for (const file of files.filter((file) => /notice/i.test(file))) {
            const text = await readFile(
                path.join(directory, file),
                "utf8",
            ).catch(() => "");
            notices += `\n${text}\n`;
        }
    }
    await writeFile(path.join(output, "THIRD_PARTY_NOTICES.txt"), notices);
}

if (
    process.argv[1] &&
    path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
    await publishServer(process.argv[2]);
}
