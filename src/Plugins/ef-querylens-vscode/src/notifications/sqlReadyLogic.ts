export const SQL_READY_GO_TO_QUERY_ACTION = 'Go to Query';
export const SQL_READY_OPEN_SQL_ACTION = 'Open SQL';

export type SqlReadyNotificationPayload = {
    fileUri: string;
    line: number;
    character: number;
    fileName: string;
    commandCount?: number;
};

const DEDUPE_WINDOW_MS = 30_000;
const recentNotifications = new Map<string, number>();

export function shouldShowSqlReadyNotification(
    payload: SqlReadyNotificationPayload,
    enabled: boolean,
    nowMs: number = Date.now(),
): boolean {
    if (!enabled) {
        return false;
    }

    const commandCount = payload.commandCount ?? 0;
    if (commandCount <= 0) {
        return false;
    }

    if (!payload.fileUri?.trim()) {
        return false;
    }

    const key = `${payload.fileUri}|${payload.line}|${payload.character}`;
    const lastShown = recentNotifications.get(key);
    if (lastShown !== undefined && nowMs - lastShown < DEDUPE_WINDOW_MS) {
        return false;
    }

    recentNotifications.set(key, nowMs);
    return true;
}

export function buildSqlReadyToastMessage(payload: SqlReadyNotificationPayload): string {
    const fileName = payload.fileName?.trim() || 'query';
    const lineNumber = Number.isFinite(payload.line) ? payload.line + 1 : 1;
    return `EF QueryLens: SQL ready — ${fileName}:${lineNumber}`;
}

export function resetSqlReadyNotificationDedupeForTests(): void {
    recentNotifications.clear();
}
