import { TextEditor } from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { formatLogMessage } from '../utils/errors';

export type WarmupManagerOptions = {
    getClient: () => LanguageClient | undefined;
    getDebugLogsEnabled: () => boolean;
    logOutput: (message: string) => void;
};

export type WarmupManager = {
    scheduleWarmup: (editor: TextEditor | undefined) => boolean;
    scheduleWarmupForUri: (uri: string, version?: number) => boolean;
    requestDaemonRestartOnActivate: () => Promise<void>;
};

export function createWarmupManager(options: WarmupManagerOptions): WarmupManager {
    const { getClient, getDebugLogsEnabled, logOutput } = options;
    const warmedDocumentVersions = new Map<string, number>();
    const warmupInFlightUris = new Set<string>();
    const latestRequestedWarmups = new Map<string, { version: number; line: number; character: number }>();

    const queueWarmupRequest = (
        client: LanguageClient,
        uri: string,
        version: number,
        line: number,
        character: number,
    ): void => {
        latestRequestedWarmups.set(uri, { version, line, character });
        if (warmupInFlightUris.has(uri)) {
            return;
        }

        void runWarmupRequest(client, uri);
    };

    const runWarmupRequest = async (client: LanguageClient, uri: string): Promise<void> => {
        const request = latestRequestedWarmups.get(uri);
        if (!request) {
            return;
        }

        warmupInFlightUris.add(uri);
        await whenClientReady(client);

        try {
            await client.sendRequest('efquerylens/warmup', {
                textDocument: { uri },
                position: { line: request.line, character: request.character },
            });

            warmedDocumentVersions.set(uri, request.version);
            if (getDebugLogsEnabled()) {
                logOutput(
                    `[EFQueryLens] warmup rpc uri=${uri} line=${request.line} character=${request.character} version=${request.version}`
                );
            }
        } catch (error) {
            if (getDebugLogsEnabled()) {
                const message = error instanceof Error ? error.message : String(error);
                logOutput(formatLogMessage('QL1008_WARMUP_FAILED', `warmup skipped uri=${uri} reason=${message}`));
            }
        } finally {
            warmupInFlightUris.delete(uri);
            const latest = latestRequestedWarmups.get(uri);
            if (latest && latest.version > request.version) {
                const refreshedClient = getClient();
                if (refreshedClient) {
                    void runWarmupRequest(refreshedClient, uri);
                }
            }
        }
    };

    const scheduleWarmupForUri = (uri: string, version = 0): boolean => {
        const client = getClient();
        if (!client) {
            return false;
        }

        const normalizedVersion = Math.max(0, Math.floor(version));
        const lastWarmedVersion = warmedDocumentVersions.get(uri);
        if (typeof lastWarmedVersion === 'number' && lastWarmedVersion >= normalizedVersion) {
            return false;
        }

        if (warmupInFlightUris.has(uri)) {
            return false;
        }

        queueWarmupRequest(client, uri, normalizedVersion, 0, 0);
        return true;
    };

    const scheduleWarmup = (editor: TextEditor | undefined): boolean => {
        const client = getClient();
        if (!client) {
            return false;
        }

        const document = editor?.document;
        if (!document) {
            return false;
        }

        if (document.uri.scheme !== 'file' || document.languageId !== 'csharp') {
            return false;
        }

        const uri = document.uri.toString();
        const lastWarmedVersion = warmedDocumentVersions.get(uri);
        if (typeof lastWarmedVersion === 'number' && lastWarmedVersion >= document.version) {
            return false;
        }

        if (warmupInFlightUris.has(uri)) {
            return false;
        }

        const requestedLine = typeof editor?.selection?.active?.line === 'number'
            ? editor.selection.active.line
            : 0;
        const requestedCharacter = typeof editor?.selection?.active?.character === 'number'
            ? editor.selection.active.character
            : 0;
        const line = Math.max(0, Math.floor(requestedLine));
        const character = Math.max(0, Math.floor(requestedCharacter));

        queueWarmupRequest(client, uri, document.version, line, character);
        return true;
    };

    const requestDaemonRestartOnActivate = async (): Promise<void> => {
        const client = getClient();
        if (!client) {
            return;
        }

        await whenClientReady(client);

        try {
            const response = await client.sendRequest('efquerylens/daemon/restart', {});
            const success = !!(response && typeof response === 'object' && (response as { success?: unknown }).success === true);
            const message = response && typeof response === 'object' && typeof (response as { message?: unknown }).message === 'string'
                ? (response as { message: string }).message
                : (success ? 'Daemon restarted.' : 'Daemon restart did not complete.');

            if (success) {
                logOutput(`[EFQueryLens] startup-daemon-restart success=true message=${message}`);
            } else {
                logOutput(formatLogMessage('QL1007_DAEMON_RESTART_INCOMPLETE', `startup daemon restart incomplete message=${message}`));
            }
        } catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            logOutput(formatLogMessage('QL1006_DAEMON_RESTART_FAILED', `startup daemon restart failed reason=${message}`));
        }
    };

    return {
        scheduleWarmup,
        scheduleWarmupForUri,
        requestDaemonRestartOnActivate,
    };
}

function whenClientReady(client: LanguageClient): Promise<void> {
    const onReady = (client as unknown as { onReady?: () => Promise<void> }).onReady;
    return typeof onReady === 'function'
        ? onReady.call(client)
        : Promise.resolve();
}
