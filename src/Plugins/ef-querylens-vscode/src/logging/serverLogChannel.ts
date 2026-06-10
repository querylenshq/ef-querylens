import { LogOutputChannel } from 'vscode';

/**
 * Language-server stderr is written as plain lines; route them to the correct
 * LogOutputChannel level instead of always appearing as [error].
 */
export function createServerLogChannel(target: LogOutputChannel): LogOutputChannel {
    const write = (line: string) => {
        const normalized = line.trim();
        if (!normalized) {
            return;
        }

        if (/\[error\]|exception|failed|unavailable/i.test(normalized)) {
            target.error(normalized);
            return;
        }

        if (/\[warn\]|warning/i.test(normalized)) {
            target.warn(normalized);
            return;
        }

        if (/\[QL-(Ops|Hover|LSP|Warmup)\]/i.test(normalized)) {
            target.info(normalized);
            return;
        }

        target.debug(normalized);
    };

    return {
        name: target.name,
        logLevel: target.logLevel,
        onDidChangeLogLevel: target.onDidChangeLogLevel,
        trace: (message) => target.trace(message),
        debug: (message) => write(String(message)),
        info: (message) => write(String(message)),
        warn: (message) => target.warn(String(message)),
        error: (message) => target.error(String(message)),
        append: (value) => write(value),
        appendLine: (value) => write(value),
        replace: () => {
            // Language client does not use replace for stderr forwarding.
        },
        clear: () => target.clear(),
        show: (...args) => target.show(...(args as Parameters<LogOutputChannel['show']>)),
        hide: () => target.hide(),
        dispose: () => {
            // Owned by extension context.
        },
    };
}
