import * as path from 'path';
import {
    ExtensionContext,
    LogOutputChannel,
    StatusBarAlignment,
    StatusBarItem,
    ThemeColor,
    window,
} from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

export type QueryLensHostState =
    | 'Starting'
    | 'Warming'
    | 'Ready'
    | 'Computing'
    | 'Unavailable';

export type QueryLensStatusSnapshot = {
    State: QueryLensHostState;
    Message: string;
    AssemblyPath?: string | null;
    InflightCount?: number;
    Warmed?: boolean;
};

type StatusBarOptions = {
    enabled: boolean;
    outputChannel: LogOutputChannel;
};

export function createQueryLensStatusBar(
    context: ExtensionContext,
    getClient: () => LanguageClient | undefined,
    options: StatusBarOptions,
): {
    dispose: () => void;
    refresh: () => Promise<void>;
    attachClientNotifications: () => void;
} {
    const item = window.createStatusBarItem(StatusBarAlignment.Right, 90);
    item.command = 'efquerylens.openOutput';
    context.subscriptions.push(item);

    let disposed = false;
    let notificationsAttached = false;

    const applySnapshot = (snapshot: QueryLensStatusSnapshot | undefined) => {
        if (!options.enabled || disposed) {
            item.hide();
            return;
        }

        const mapped = mapStatusSnapshot(snapshot);
        item.text = mapped.text;
        item.tooltip = mapped.tooltip;
        item.backgroundColor = mapped.backgroundColor;
        item.show();
    };

    const refresh = async () => {
        const client = getClient();
        if (!client?.isRunning()) {
            applySnapshot({ State: 'Starting', Message: 'Starting QueryLens…' });
            return;
        }

        try {
            const snapshot = await client.sendRequest<QueryLensStatusSnapshot>('efquerylens/status');
            applySnapshot(snapshot);
        } catch (error) {
            options.outputChannel.warn(`[EFQueryLens] status refresh failed: ${String(error)}`);
            applySnapshot({ State: 'Unavailable', Message: 'QueryLens status unavailable.' });
        }
    };

    const attachClientNotifications = () => {
        if (notificationsAttached) {
            return;
        }

        const client = getClient();
        if (!client?.isRunning()) {
            return;
        }

        client.onNotification('efquerylens/statusChanged', (snapshot: QueryLensStatusSnapshot) => {
            applySnapshot(snapshot);
        });
        notificationsAttached = true;
    };

    return {
        dispose: () => {
            disposed = true;
            item.dispose();
        },
        refresh,
        attachClientNotifications,
    };
}

export function mapStatusSnapshot(snapshot: QueryLensStatusSnapshot | undefined): {
    text: string;
    tooltip: string;
    backgroundColor?: StatusBarItem['backgroundColor'];
} {
    const warmed = snapshot?.Warmed === true;
    const state = warmed
        ? normalizeHostState(snapshot?.State)
        : snapshot?.State === 'Unavailable'
            ? 'Unavailable'
            : snapshot?.State === 'Computing'
                ? 'Computing'
                : 'Warming';
    const message = snapshot?.Message?.trim() || 'Starting QueryLens…';
    const assembly = snapshot?.AssemblyPath?.trim();
    const inflight = snapshot?.InflightCount ?? 0;

    const text = (() => {
        switch (state) {
            case 'Warming':
                return '$(sync~spin) QueryLens: Warming…';
            case 'Computing':
                return '$(sync~spin) QueryLens: Computing SQL…';
            case 'Ready':
                return '$(check) QueryLens: Ready';
            case 'Unavailable':
                return '$(error) QueryLens: Unavailable';
            default:
                return '$(loading~spin) QueryLens: Starting…';
        }
    })();

    const tooltipParts = [message];
    if (assembly) {
        tooltipParts.push(`Assembly: ${assembly}`);
    }
    if (inflight > 0) {
        tooltipParts.push(`In flight: ${inflight}`);
    }
    tooltipParts.push('Click to open EF QueryLens output');

    const backgroundColor =
        state === 'Unavailable'
            ? new ThemeColor('statusBarItem.errorBackground')
            : undefined;

    return {
        text,
        tooltip: tooltipParts.join('\n'),
        backgroundColor,
    };
}

function normalizeHostState(raw: QueryLensHostState | number | undefined): QueryLensHostState {
    if (typeof raw === 'string') {
        return raw;
    }

    switch (raw) {
        case 1:
            return 'Warming';
        case 2:
            return 'Ready';
        case 3:
            return 'Computing';
        case 4:
            return 'Unavailable';
        default:
            return 'Starting';
    }
}

type WarmupResponse = {
    Success?: boolean;
    Cached?: boolean;
    AssemblyPath?: string | null;
    Message?: string | null;
};

export async function runStartupWarmup(
    client: LanguageClient | undefined,
    log?: (message: string) => void,
): Promise<void> {
    if (!client?.isRunning()) {
        return;
    }

    const editor = window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'csharp') {
        log?.('[EFQueryLens] warmup-skipped no-active-csharp-editor');
        return;
    }

    try {
        log?.('[EFQueryLens] warmup-start');
        const response = await client.sendRequest<WarmupResponse>('efquerylens/warmup', {
            textDocument: { uri: editor.document.uri.toString() },
            position: editor.selection.active,
        });
        const assembly = response?.AssemblyPath ? path.basename(response.AssemblyPath) : 'unknown';
        log?.(
            `[EFQueryLens] warmup-finished success=${response?.Success ?? false} ` +
            `cached=${response?.Cached ?? false} assembly=${assembly} message=${response?.Message ?? ''}`,
        );
    } catch (error) {
        log?.(`[EFQueryLens] warmup-failed ${String(error)}`);
    }
}
