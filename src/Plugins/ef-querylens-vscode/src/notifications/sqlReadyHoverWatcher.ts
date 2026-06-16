import { LanguageClient, State } from 'vscode-languageclient/node';

import { QueryLensSettings, QueryLensStructuredHoverResponse } from '../types';
import { showSqlReadyToast, SqlReadyNotificationPayload } from './sqlReady';
import { shouldShowSqlReadyNotification } from './sqlReadyLogic';

const POLL_INTERVAL_MS = 200;
const DEFAULT_TRANSLATE_TIMEOUT_MS = 15_000;
const MAX_NOTIFICATION_WAIT_MS = 120_000;
const STATUS_READY = 0;
const STATUS_IN_QUEUE = 1;
const STATUS_DAEMON_UNAVAILABLE = 3;
const TERMINAL_SKIP_MS = 5_000;

type WatchKey = string;
type StructuredHoverPoller = (
    fileUri: string,
    line: number,
    character: number,
) => Promise<StructuredHoverWireResponse | null>;
type SqlReadyToastSink = (payload: SqlReadyNotificationPayload) => void | Promise<void>;

const activeWatches = new Set<WatchKey>();
const watchControllers = new Map<WatchKey, AbortController>();
const recentlyTerminalWatches = new Map<WatchKey, number>();

type StructuredHoverWireResponse = Partial<QueryLensStructuredHoverResponse> & {
    status?: number;
    success?: boolean;
    commandCount?: number;
};

function buildWatchKey(fileUri: string, line: number, character: number): WatchKey {
    return `${fileUri}|${line}|${character}`;
}

function computeNotificationWaitMs(hoverWaitWhenWarmMs: number): number {
    const budget = Math.max(hoverWaitWhenWarmMs, DEFAULT_TRANSLATE_TIMEOUT_MS);
    return Math.min(Math.max(budget, 500), MAX_NOTIFICATION_WAIT_MS);
}

function getStatus(response: StructuredHoverWireResponse): number | undefined {
    return response.Status ?? response.status;
}

function getSuccess(response: StructuredHoverWireResponse): boolean {
    return response.Success ?? response.success ?? false;
}

function getCommandCount(response: StructuredHoverWireResponse): number {
    return response.CommandCount ?? response.commandCount ?? 0;
}

function isQueued(status: number | undefined): boolean {
    return status === STATUS_IN_QUEUE;
}

function isTerminal(status: number | undefined): boolean {
    return status === STATUS_READY || status === STATUS_DAEMON_UNAVAILABLE;
}

async function pollStructuredHover(
    client: LanguageClient,
    fileUri: string,
    line: number,
    character: number,
): Promise<StructuredHoverWireResponse | null> {
    if (client.state !== State.Running) {
        return null;
    }

    try {
        const response = await client.sendRequest<StructuredHoverWireResponse | null>(
            'efquerylens/hover',
            {
                textDocument: { uri: fileUri },
                position: { line, character },
            },
        );
        return response ?? null;
    } catch {
        return null;
    }
}

async function runWatch(
    poller: StructuredHoverPoller,
    fileUri: string,
    line: number,
    character: number,
    fileName: string,
    getSettings: () => QueryLensSettings,
    log: (message: string) => void,
    signal: AbortSignal,
    showToast: SqlReadyToastSink = showSqlReadyToast,
    pollIntervalMs = POLL_INTERVAL_MS,
): Promise<void> {
    const key = buildWatchKey(fileUri, line, character);
    const waitBudgetMs = computeNotificationWaitMs(getSettings().hoverWaitWhenWarmMs);
    const deadline = Date.now() + waitBudgetMs;
    let sawInQueue = true;

    try {
        while (!signal.aborted) {
            const response = await poller(fileUri, line, character);
            if (!response) {
                log(`sql-ready-watch-exit key=${key} reason=null-response`);
                rememberTerminalWatch(key);
                return;
            }

            const status = getStatus(response);
            const success = getSuccess(response);
            const commandCount = getCommandCount(response);

            if (isQueued(status)) {
                sawInQueue = true;
            } else if (isTerminal(status)) {
                if (
                    sawInQueue
                    && status === STATUS_READY
                    && success
                    && commandCount > 0
                    && getSettings().notifyWhenSqlReady
                ) {
                    const payload: SqlReadyNotificationPayload = {
                        fileUri,
                        line,
                        character,
                        fileName,
                        commandCount,
                    };

                    if (shouldShowSqlReadyNotification(payload, true)) {
                        log(`sql-ready-watch-ready key=${key} commands=${commandCount}`);
                        void showToast(payload);
                    } else {
                        log(`sql-ready-watch-exit key=${key} reason=deduped commands=${commandCount}`);
                    }
                } else if (status === STATUS_READY && (!success || commandCount <= 0)) {
                    log(
                        `sql-ready-watch-exit key=${key} reason=terminal-not-ready `
                        + `success=${success} commands=${commandCount}`,
                    );
                    rememberTerminalWatch(key);
                } else {
                    log(`sql-ready-watch-exit key=${key} reason=terminal status=${status}`);
                    rememberTerminalWatch(key);
                }

                return;
            } else {
                log(`sql-ready-watch-exit key=${key} reason=unexpected-status status=${String(status)}`);
                rememberTerminalWatch(key);
                return;
            }

            if (Date.now() >= deadline) {
                log(`sql-ready-watch-timeout key=${key} budgetMs=${waitBudgetMs} status=${String(status)}`);
                rememberTerminalWatch(key);
                return;
            }

            await new Promise<void>((resolve, reject) => {
                const timer = setTimeout(resolve, pollIntervalMs);
                signal.addEventListener('abort', () => {
                    clearTimeout(timer);
                    reject(new DOMException('Aborted', 'AbortError'));
                }, { once: true });
            });
        }
    } catch (error) {
        if (signal.aborted) {
            log(`sql-ready-watch-cancelled key=${key}`);
            return;
        }

        log(`sql-ready-watch-failed key=${key} reason=${String(error)}`);
    } finally {
        activeWatches.delete(key);
        watchControllers.delete(key);
    }
}

export function cancelSqlReadyWatch(fileUri: string, line: number, character: number): void {
    const key = buildWatchKey(fileUri, line, character);
    watchControllers.get(key)?.abort();
}

function rememberTerminalWatch(key: WatchKey): void {
    recentlyTerminalWatches.set(key, Date.now() + TERMINAL_SKIP_MS);
}

function isRecentlyTerminal(key: WatchKey): boolean {
    const expiresAt = recentlyTerminalWatches.get(key);
    if (!expiresAt) {
        return false;
    }

    if (Date.now() > expiresAt) {
        recentlyTerminalWatches.delete(key);
        return false;
    }

    return true;
}

export function watchSqlReadyIfQueued(
    client: LanguageClient,
    fileUri: string,
    filePath: string,
    line: number,
    character: number,
    status: number,
    getSettings: () => QueryLensSettings,
    log: (message: string) => void,
): void {
    if (!isQueued(status) || !getSettings().notifyWhenSqlReady) {
        return;
    }

    const key = buildWatchKey(fileUri, line, character);
    if (isRecentlyTerminal(key)) {
        log(`sql-ready-watch-skipped key=${key} reason=recent-terminal`);
        return;
    }

    if (activeWatches.has(key)) {
        log(`sql-ready-watch-coalesced key=${key}`);
        return;
    }

    activeWatches.add(key);
    const controller = new AbortController();
    watchControllers.set(key, controller);
    log(`sql-ready-watch-started key=${key}`);

    const fileName = filePath.split(/[/\\]/).pop() ?? 'query';
    const poller: StructuredHoverPoller = (uri, requestLine, requestCharacter) =>
        pollStructuredHover(client, uri, requestLine, requestCharacter);
    void runWatch(poller, fileUri, line, character, fileName, getSettings, log, controller.signal);
}

export async function runSqlReadyWatchForTests(
    responses: Array<StructuredHoverWireResponse | null>,
    getSettings: () => QueryLensSettings,
    log: (message: string) => void,
    showToast: SqlReadyToastSink,
    pollIntervalMs = 1,
): Promise<void> {
    const queue = [...responses];
    const poller: StructuredHoverPoller = async () => queue.shift() ?? null;
    await runWatch(
        poller,
        'file:///tmp/Orders.cs',
        1,
        2,
        'Orders.cs',
        getSettings,
        log,
        new AbortController().signal,
        showToast,
        pollIntervalMs,
    );
}

export function resetSqlReadyWatchesForTests(): void {
    for (const controller of watchControllers.values()) {
        controller.abort();
    }

    activeWatches.clear();
    watchControllers.clear();
    recentlyTerminalWatches.clear();
}
