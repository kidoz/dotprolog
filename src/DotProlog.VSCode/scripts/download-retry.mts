import { setTimeout } from "node:timers/promises";

function transientDownloadFailure(error: unknown): boolean {
    if (error instanceof AggregateError) {
        return (
            error.errors.length > 0 &&
            error.errors.every(transientDownloadFailure)
        );
    }
    if (!(error instanceof Error)) {
        return false;
    }
    return (
        ("code" in error &&
            [
                "ETIMEDOUT",
                "ECONNRESET",
                "ECONNREFUSED",
                "ENETUNREACH",
                "EHOSTUNREACH",
                "EAI_AGAIN",
                "ENOTFOUND",
            ].includes(String(error.code))) ||
        error.message.startsWith("@vscode/test-electron request timeout")
    );
}

export async function retryEditorDownload<T>(
    operation: () => Promise<T>,
    delay: (milliseconds: number) => Promise<unknown> = setTimeout,
): Promise<T> {
    for (let attempt = 0; ; attempt++) {
        try {
            return await operation();
        } catch (error) {
            if (attempt === 2 || !transientDownloadFailure(error)) {
                throw error;
            }
            const milliseconds = 2000 * (attempt + 1);
            console.warn(
                `VS Code download failed; retrying in ${milliseconds / 1000}s.`,
                error,
            );
            await delay(milliseconds);
        }
    }
}
