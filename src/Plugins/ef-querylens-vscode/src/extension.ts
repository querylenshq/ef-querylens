import * as fs from 'fs';
import * as path from 'path';
import {
    commands,
    ExtensionContext,
    Hover,
    LogOutputChannel,
    window,
    workspace,
} from 'vscode';

import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
} from 'vscode-languageclient/node';

import { readSettings } from './config/settings';
import {
    enableTrustedHoverCommands,
} from './hover/markdown';
import {
    formatHoverQueuedMessage,
    formatHoverReadyMessage,
} from './hover/logging';
import { registerQueryLensCommands } from './commands/registry';
import { createSqlActionHandlers } from './commands/sqlActions';
import { createServerLogChannel } from './logging/serverLogChannel';
import { cancelSqlReadyWatch, watchSqlReadyIfQueued } from './notifications/sqlReadyHoverWatcher';
import { createQueryLensStatusBar, runStartupWarmup } from './status/statusBar';
import { QueryLensSettings } from './types';

let client: LanguageClient | undefined;
let queryLensOutputChannel: LogOutputChannel | undefined;

export function activate(context: ExtensionContext) {
    queryLensOutputChannel = window.createOutputChannel('EF QueryLens', { log: true });
    context.subscriptions.push(queryLensOutputChannel);

    const packagedLspDir = context.asAbsolutePath('server');
    const packagedDaemonDir = context.asAbsolutePath('daemon');

    const hasPackagedRuntime =
        fs.existsSync(path.join(packagedLspDir, 'EFQueryLens.Lsp.dll'))
        && fs.existsSync(path.join(packagedDaemonDir, 'EFQueryLens.Daemon.dll'));

    if (!hasPackagedRuntime) {
        const missingMessage =
            `EF QueryLens runtime is missing from extension package. ` +
            `Expected '${packagedLspDir}' and '${packagedDaemonDir}'.`;
        logOutput(`[EFQueryLens] ${missingMessage}`);
        void window.showErrorMessage(missingMessage);
        return;
    }

    const serverPath = path.join(packagedLspDir, 'EFQueryLens.Lsp.dll');
    const fallbackRepoRoot = path.resolve(context.extensionPath, '..', '..', '..');
    const workspaceRoot = workspace.workspaceFolders?.[0]?.uri.fsPath ?? fallbackRepoRoot;
    const daemonDllPath = path.join(packagedDaemonDir, 'EFQueryLens.Daemon.dll');
    const daemonExecutablePath = [
        path.join(packagedDaemonDir, 'EFQueryLens.Daemon.exe'),
        path.join(packagedDaemonDir, 'EFQueryLens.Daemon'),
    ].find(candidate => fs.existsSync(candidate));

    let currentSettings = readSettings();
    logOutput(`activate workspace=${workspaceRoot}`);
    logOutput(`[EFQueryLens] runtime source=packaged lsp=${packagedLspDir} daemon=${packagedDaemonDir}`);

    const serverEnv: NodeJS.ProcessEnv = {
        ...process.env,
        QUERYLENS_CLIENT: 'vscode',
        QUERYLENS_WORKSPACE: workspaceRoot,
        QUERYLENS_DAEMON_WORKSPACE: workspaceRoot,
        QUERYLENS_DAEMON_START_TIMEOUT_MS: '30000',
        QUERYLENS_DAEMON_CONNECT_TIMEOUT_MS: '10000',
        QUERYLENS_DAEMON_SHUTDOWN_ON_DISPOSE: '1',
        // Keep rolling-window latency at 20 samples by default, but honor explicit env overrides.
        QUERYLENS_AVG_WINDOW_SAMPLES: process.env.QUERYLENS_AVG_WINDOW_SAMPLES ?? '20',
        // VS Code hides inline SQL Preview badges; hover/command actions remain available.
        QUERYLENS_MAX_CODELENS_PER_DOCUMENT: '0',
        // InlayHint SQL Preview labels are used by Rider; disable them for VS Code UX.
        QUERYLENS_MAX_INLAY_HINTS_PER_DOCUMENT: '0',
        QUERYLENS_CODELENS_DEBOUNCE_MS: String(currentSettings.codeLensDebounceMs),
        QUERYLENS_CODELENS_USE_MODEL_FILTER: currentSettings.codeLensUseModelFilter ? '1' : '0',
    };

    if (currentSettings.debugLogsEnabled) {
        serverEnv.QUERYLENS_DEBUG = '1';
    }

    if (daemonExecutablePath) {
        serverEnv.QUERYLENS_DAEMON_EXE = daemonExecutablePath;
    } else if (currentSettings.debugLogsEnabled) {
        logOutput(`[EFQueryLens] daemon executable not found in ${packagedDaemonDir}`);
    }

    if (fs.existsSync(daemonDllPath)) {
        serverEnv.QUERYLENS_DAEMON_DLL = daemonDllPath;
    } else if (currentSettings.debugLogsEnabled) {
        logOutput(`[EFQueryLens] daemon dll not found at ${daemonDllPath}`);
    }

    const serverOptions: ServerOptions = {
        command: 'dotnet',
        args: [serverPath],
        options: {
            cwd: workspaceRoot,
            env: serverEnv,
        }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'csharp' }],
        initializationOptions: buildLspInitializationOptions(currentSettings),
        // Route language-server stderr into EF QueryLens with info/warn levels (not all [error]).
        outputChannel: createServerLogChannel(queryLensOutputChannel),
        middleware: {
            provideCodeLenses: async (_document, _token, _next) => {
                // VS Code UX choice: use hover + explicit commands, no inline SQL Preview code lenses.
                return [];
            },
            provideHover: async (document, position, token, next) => {
                try {
                    const startedAt = performance.now();
                    const hover = await next(document, position, token);
                    logHoverResult(
                        document.uri.fsPath,
                        position.line,
                        position.character,
                        hover as Hover | null,
                        performance.now() - startedAt,
                        () => currentSettings,
                    );
                    return enableTrustedHoverCommands(
                        hover as Hover | null,
                        ['efquerylens.copySql', 'efquerylens.showSql', 'efquerylens.openSqlEditor', 'efquerylens.recalculate', 'efquerylens.setup'],
                    );
                } catch (error) {
                    logOutput(`[EFQueryLens] hover-middleware-error ${String(error)}`);
                    throw error;
                }
            }
        },
        synchronize: {
            fileEvents: workspace.createFileSystemWatcher('**/*.cs')
        }
    };

    client = new LanguageClient(
        'efquerylens-lsp',
        'EF QueryLens Language Server',
        serverOptions,
        clientOptions
    );
    const sqlActions = createSqlActionHandlers(() => client);
    const commandDisposables = registerQueryLensCommands({
        getSettings: () => currentSettings,
        sqlActions,
        getClient: () => client,
        outputChannel: queryLensOutputChannel,
        logOutput,
    });
    context.subscriptions.push(...commandDisposables);

    let statusBar = createQueryLensStatusBar(
        context,
        () => client,
        {
            enabled: currentSettings.showStatusBar,
            outputChannel: queryLensOutputChannel!,
        },
    );
    context.subscriptions.push({ dispose: () => statusBar.dispose() });

    context.subscriptions.push(
        workspace.onDidChangeConfiguration(async event => {
            if (!event.affectsConfiguration('efquerylens')) {
                return;
            }

            const previousSettings = currentSettings;
            currentSettings = readSettings();
            logOutput(
                `[EFQueryLens] settings-updated formatOnShow=${currentSettings.formatSqlOnShow} dialect=${currentSettings.sqlDialect} debug=${currentSettings.debugLogsEnabled}`
            );

            await pushLspRuntimeConfiguration(client, currentSettings);

            if (currentSettings.debugLogsEnabled !== previousSettings.debugLogsEnabled) {
                logOutput(
                    `[EFQueryLens] verbose server logs ${currentSettings.debugLogsEnabled ? 'enabled' : 'disabled'} — restart language server to apply`
                );
            }

            statusBar.dispose();
            statusBar = createQueryLensStatusBar(
                context,
                () => client,
                {
                    enabled: currentSettings.showStatusBar,
                    outputChannel: queryLensOutputChannel!,
                },
            );
            statusBar.attachClientNotifications();
            await statusBar.refresh();

            if (!requiresLanguageServerRestart(previousSettings, currentSettings)) {
                return;
            }

            const selection = await window.showInformationMessage(
                'EF QueryLens: some setting changes require a language server restart to fully apply.',
                'Restart Now'
            );
            if (selection === 'Restart Now') {
                await commands.executeCommand('efquerylens.restart');
            }
        })
    );

    void client.start().then(async () => {
        logOutput('language-client-ready');
        statusBar.attachClientNotifications();
        await statusBar.refresh();
        await runStartupWarmup(client, logOutput);
    });
    logOutput('language-client-started');
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}

function logOutput(message: string): void {
    queryLensOutputChannel?.info(message);
}

function logHoverResult(
    filePath: string,
    line: number,
    character: number,
    hover: Hover | null | undefined,
    roundTripMs: number,
    getSettings: () => QueryLensSettings,
): void {
    if (!hover) {
        return;
    }

    const text = extractHoverMarkdown(hover);
    if (!text.includes('EF QueryLens')) {
        return;
    }

    const fileName = path.basename(filePath);
    const fileUri = pathToFileUri(filePath);
    if (text.includes('translating query') || text.includes('in queue')) {
        if (client?.isRunning()) {
            watchSqlReadyIfQueued(
                client,
                fileUri,
                filePath,
                line,
                character,
                1,
                getSettings,
                logOutput,
            );
        }
        logOutput(formatHoverQueuedMessage(fileName, line, character, roundTripMs));
        return;
    }

    if (text.match(/SQL generation time\s+(\d+)\s*ms/i)) {
        cancelSqlReadyWatch(fileUri, line, character);
        logOutput(formatHoverReadyMessage(fileName, line, character, roundTripMs, text));
        return;
    }

    if (text.includes('QueryLens Error')) {
        cancelSqlReadyWatch(fileUri, line, character);
        logOutput(`[EFQueryLens] hover-error file=${fileName} line=${line} char=${character} roundTripMs=${Math.round(roundTripMs)}`);
        return;
    }

    logOutput(`[EFQueryLens] hover file=${fileName} line=${line} char=${character} roundTripMs=${Math.round(roundTripMs)}`);
}

function pathToFileUri(filePath: string): string {
    const normalized = filePath.replace(/\\/g, '/');
    if (/^[a-zA-Z]:\//.test(normalized)) {
        return `file:///${normalized}`;
    }

    return `file://${normalized.startsWith('/') ? '' : '/'}${normalized}`;
}

function extractHoverMarkdown(hover: Hover): string {
    const contents = Array.isArray(hover.contents) ? hover.contents : [hover.contents];
    return contents
        .map(item => {
            if (typeof item === 'string') {
                return item;
            }

            if (item && typeof item === 'object' && 'value' in item) {
                const value = (item as { value?: unknown }).value;
                return typeof value === 'string' ? value : '';
            }

            return '';
        })
        .join('\n');
}

function requiresLanguageServerRestart(previous: QueryLensSettings, next: QueryLensSettings): boolean {
    return previous.codeLensDebounceMs !== next.codeLensDebounceMs
        || previous.codeLensUseModelFilter !== next.codeLensUseModelFilter;
}

function buildLspInitializationOptions(settings: QueryLensSettings): unknown {
    return {
        queryLens: buildLspRuntimeConfiguration(settings)
    };
}

function buildLspRuntimeConfiguration(settings: QueryLensSettings): Record<string, unknown> {
    return {
        debugEnabled: settings.debugLogsEnabled,
        enableLspHover: true,
        hoverProgressNotify: settings.hoverProgressNotify,
        sqlReadyNotify: settings.notifyWhenSqlReady,
        hoverProgressDelayMs: 350,
        hoverCacheTtlMs: 15_000,
        hoverCancelGraceMs: 1_500,
        markdownQueueAdaptiveWaitMs: 200,
        structuredQueueAdaptiveWaitMs: 200,
        warmupSuccessTtlMs: 60_000,
        warmupFailureCooldownMs: 5_000,
        hoverWaitWhenWarmMs: settings.hoverWaitWhenWarmMs,
        hoverForegroundResolveBudgetMs: 75,
        hoverFastProbeEnabled: true,
    };
}

async function pushLspRuntimeConfiguration(
    languageClient: LanguageClient | undefined,
    settings: QueryLensSettings,
): Promise<void> {
    if (!languageClient || !languageClient.isRunning()) {
        return;
    }

    await languageClient.sendNotification('workspace/didChangeConfiguration', {
        settings: {
            queryLens: buildLspRuntimeConfiguration(settings)
        }
    });
}

