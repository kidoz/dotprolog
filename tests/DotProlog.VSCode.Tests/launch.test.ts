import assert from "node:assert/strict";
import { test } from "node:test";
import path from "node:path";
import { launch, Settings } from "../../src/DotProlog.VSCode/src/launch";

const settings: Settings = {
    serverPath: "",
    dotnetPath: "dotnet",
    project: "",
    configuration: "Debug",
    mode: "modern",
};
const dll = path.resolve("folder with spaces/dotprolog-lsp.dll");

test("managed loose files use an argument array and the selected mode", () => {
    assert.deepEqual(launch({ ...settings, mode: "strict-iso" }, dll, []), {
        command: "dotnet",
        args: [dll, "--mode", "strict-iso"],
    });
});

test("project arguments omit mode and resolve paths against the workspace", () => {
    const folder = path.resolve("workspace with spaces");
    assert.deepEqual(
        launch(
            { ...settings, project: "App.dplproj", configuration: "Release" },
            dll,
            [folder],
        ),
        {
            command: "dotnet",
            args: [
                dll,
                "--project",
                path.join(folder, "App.dplproj"),
                "--configuration",
                "Release",
            ],
        },
    );
});

test("native overrides launch directly without a stdio switch or shell interpolation", () => {
    const server = path.resolve("native server/dotprolog-lsp");
    const project = path.resolve("literal $(name)/App.dplproj");
    assert.deepEqual(
        launch({ ...settings, serverPath: server, project }, dll, []),
        {
            command: server,
            args: ["--project", project, "--configuration", "Debug"],
        },
    );
});

test("relative projects are rejected when workspace resolution is ambiguous", () => {
    for (const folders of [[], [path.resolve("one"), path.resolve("two")]]) {
        assert.throws(
            () => launch({ ...settings, project: "App.dplproj" }, dll, folders),
            /absolute project path/,
        );
    }
});

test("invalid launch settings produce actionable failures", () => {
    assert.throws(
        () => launch({ ...settings, serverPath: "relative.dll" }, dll, []),
        /absolute path/,
    );
    assert.throws(
        () => launch({ ...settings, mode: "extended" }, dll, []),
        /modern or strict-iso/,
    );
    assert.throws(
        () =>
            launch(
                { ...settings, project: path.resolve("App.csproj") },
                dll,
                [],
            ),
        /dplproj/,
    );
    assert.throws(
        () =>
            launch(
                {
                    ...settings,
                    project: path.resolve("App.dplproj"),
                    configuration: "-target:Build",
                },
                dll,
                [],
            ),
        /configuration/,
    );
});
