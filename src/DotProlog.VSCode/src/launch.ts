import * as path from "node:path";

export interface Settings {
    serverPath: string;
    dotnetPath: string;
    project: string;
    configuration: string;
    mode: string;
}

// This mirrors the existing CLI contract; never construct a shell command from settings.
export function launch(
    settings: Settings,
    bundledServer: string,
    folders: readonly string[],
) {
    const server = settings.serverPath || bundledServer;
    if (!path.isAbsolute(server)) {
        throw new Error("dotprolog.serverPath must be an absolute path.");
    }
    if (!["modern", "strict-iso"].includes(settings.mode)) {
        throw new Error("dotprolog.mode must be modern or strict-iso.");
    }
    let args: string[];
    if (settings.project) {
        if (!/^[A-Za-z0-9_.-]+$/.test(settings.configuration)) {
            throw new Error("Invalid DotProlog build configuration.");
        }
        if (!path.isAbsolute(settings.project) && folders.length !== 1) {
            throw new Error(
                "Use an absolute project path when there is not exactly one workspace folder.",
            );
        }
        const project = path.resolve(folders[0] ?? ".", settings.project);
        if (!project.toLowerCase().endsWith(".dplproj")) {
            throw new Error("dotprolog.project must name a .dplproj file.");
        }
        args = [
            "--project",
            project,
            "--configuration",
            settings.configuration,
        ];
    } else {
        args = ["--mode", settings.mode];
    }
    return server.toLowerCase().endsWith(".dll")
        ? { command: settings.dotnetPath || "dotnet", args: [server, ...args] }
        : { command: server, args };
}
