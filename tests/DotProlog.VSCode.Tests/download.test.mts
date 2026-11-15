import assert from "node:assert/strict";
import { test } from "node:test";
import { retryEditorDownload } from "../../src/DotProlog.VSCode/scripts/download-retry.mjs";

test("retries the aggregate network failure seen while downloading VS Code", async () => {
    let attempts = 0;
    const delays: number[] = [];
    const result = await retryEditorDownload(
        async () => {
            if (++attempts < 3) {
                throw new AggregateError([
                    Object.assign(new Error("connect timeout"), {
                        code: "ETIMEDOUT",
                    }),
                    Object.assign(new Error("IPv6 unavailable"), {
                        code: "ENETUNREACH",
                    }),
                ]);
            }
            return "downloaded";
        },
        async (milliseconds) => {
            delays.push(milliseconds);
        },
    );
    assert.equal(result, "downloaded");
    assert.equal(attempts, 3);
    assert.deepEqual(delays, [2000, 4000]);
});

test("stops after three transient failures", async () => {
    let attempts = 0;
    const failure = Object.assign(new Error("timeout"), { code: "ETIMEDOUT" });
    await assert.rejects(
        retryEditorDownload(
            async () => {
                attempts++;
                throw failure;
            },
            async () => {},
        ),
        (error) => error === failure,
    );
    assert.equal(attempts, 3);
});

test("does not retry an installation or editor test failure", async () => {
    let attempts = 0;
    const failure = new Error("extension installation failed");
    await assert.rejects(
        retryEditorDownload(
            async () => {
                attempts++;
                throw failure;
            },
            async () => {},
        ),
        (error) => error === failure,
    );
    assert.equal(attempts, 1);
});
