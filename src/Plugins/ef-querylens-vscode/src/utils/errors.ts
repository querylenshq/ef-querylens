export type QueryLensErrorCode =
    | 'QL1001_CLIENT_NOT_READY'
    | 'QL1002_INVALID_URI'
    | 'QL1003_HOVER_EMPTY'
    | 'QL1004_HOVER_REQUEST_FAILED'
    | 'QL1005_DAEMON_RESTART_NOT_READY'
    | 'QL1006_DAEMON_RESTART_FAILED'
    | 'QL1007_DAEMON_RESTART_INCOMPLETE'
    | 'QL1008_WARMUP_FAILED';

export function formatUserMessage(code: QueryLensErrorCode, message: string): string {
    return `EF QueryLens (${code}): ${message}`;
}

export function formatLogMessage(code: QueryLensErrorCode, message: string): string {
    return `[EFQueryLens][${code}] ${message}`;
}
