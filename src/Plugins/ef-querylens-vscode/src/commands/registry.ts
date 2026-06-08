import {
    commands,
    Disposable,
    OutputChannel,
    ProgressLocation,
    Uri,
    window,
    workspace,
} from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

import { SqlActionHandlers } from './sqlActions';
import { QueryLensSettings } from '../types';
import { formatLogMessage, formatUserMessage } from '../utils/errors';

export type QueryLensCommandRegistryOptions = {
    getSettings: () => QueryLensSettings;
    sqlActions: SqlActionHandlers;
    getClient: () => LanguageClient | undefined;
    outputChannel: OutputChannel | undefined;
    logOutput: (message: string) => void;
};

let setupInProgress = false;

export function registerQueryLensCommands(options: QueryLensCommandRegistryOptions): Disposable[] {
    const {
        getSettings,
        sqlActions,
        getClient,
        outputChannel,
        logOutput,
    } = options;

    const showSqlCommand = commands.registerCommand(
        'efquerylens.showSql',
        async (uriInput: unknown, lineInput: unknown, characterInput: unknown) => {
            const settings = getSettings();
            if (settings.debugLogsEnabled) {
                logCommandInvocation(logOutput, 'showSql', uriInput, lineInput, characterInput);
            }
            await sqlActions.showSqlPopupFromLens(uriInput, lineInput, characterInput);
        }
    );

    const recalculateCommand = commands.registerCommand(
        'efquerylens.recalculate',
        async (uriInput: unknown, lineInput: unknown, characterInput: unknown) => {
            const settings = getSettings();
            if (settings.debugLogsEnabled) {
                logCommandInvocation(logOutput, 'recalculate', uriInput, lineInput, characterInput);
            }

            await sqlActions.recalculatePreviewFromLens(uriInput, lineInput, characterInput);
        }
    );

    const copySqlCommand = commands.registerCommand(
        'efquerylens.copySql',
        async (uriInput: unknown, lineInput: unknown, characterInput: unknown) => {
            const settings = getSettings();
            if (settings.debugLogsEnabled) {
                logCommandInvocation(logOutput, 'copySql', uriInput, lineInput, characterInput);
            }
            await sqlActions.copySqlFromLens(
                uriInput,
                lineInput,
                characterInput,
                settings.formatSqlOnShow,
                settings.sqlDialect
            );
        }
    );

    const openSqlEditorCommand = commands.registerCommand(
        'efquerylens.openSqlEditor',
        async (uriInput: unknown, lineInput: unknown, characterInput: unknown) => {
            const settings = getSettings();
            if (settings.debugLogsEnabled) {
                logCommandInvocation(logOutput, 'openSqlEditor', uriInput, lineInput, characterInput);
            }
            await sqlActions.openSqlEditorFromLens(
                uriInput,
                lineInput,
                characterInput,
                settings.formatSqlOnShow,
                settings.sqlDialect
            );
        }
    );

    const openOutputCommand = commands.registerCommand(
        'efquerylens.openOutput',
        async () => {
            outputChannel?.show(true);
        }
    );

    const setupCommand = commands.registerCommand(
        'efquerylens.setup',
        async () => {
            const client = getClient();
            if (!client) {
                window.showWarningMessage(formatUserMessage('QL1005_DAEMON_RESTART_NOT_READY', 'Language client is not initialized yet.'));
                return;
            }

            const editor = window.activeTextEditor;
            if (!editor) {
                window.showWarningMessage('EF QueryLens: open the C# file with the EF query, then run Set up QueryLens.');
                return;
            }

            const textDocumentUri = editor.document.uri.toString();
            const position = {
                line: editor.selection.active.line,
                character: editor.selection.active.character,
            };

            if (setupInProgress) {
                window.showInformationMessage('EF QueryLens: setup is already running.');
                return;
            }

            setupInProgress = true;
            try {
                await window.withProgress(
                    {
                        location: ProgressLocation.Notification,
                        title: 'EF QueryLens: setting up offline factory…',
                        cancellable: false,
                    },
                    async () => {
                        const detect = await client.sendRequest('efquerylens/setup/detect', {
                            textDocument: { uri: textDocumentUri },
                            position,
                        });

                        const detectResult = parseSetupDetectResponse(detect);
                        if (detectResult.message && detectResult.hosts.length === 0) {
                            window.showWarningMessage(`EF QueryLens: ${detectResult.message}`);
                            return;
                        }

                        let hostProjectPath = detectResult.defaultHostProjectPath;
                        if (detectResult.requiresHostSelection) {
                            const picked = await window.showQuickPick(
                                detectResult.hosts.map(host => ({
                                    label: host.displayName,
                                    description: host.projectPath,
                                    detail: host.assemblyPath ?? 'Build required',
                                    hostProjectPath: host.projectPath,
                                })),
                                {
                                    placeHolder: 'Select the executable host project for the QueryLens factory',
                                }
                            );

                            if (!picked) {
                                return;
                            }

                            hostProjectPath = picked.hostProjectPath;
                        }

                        await runSetupApply(client, {
                            textDocumentUri,
                            hostProjectPath,
                            logOutput,
                        });
                    }
                );
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                logOutput(`[EFQueryLens] setup failed reason=${message}`);
                window.showErrorMessage(`EF QueryLens: setup failed. ${message}`);
            } finally {
                setupInProgress = false;
            }
        }
    );

    const restartCommand = commands.registerCommand(
        'efquerylens.restart',
        async () => {
            const client = getClient();
            if (!client) {
                window.showWarningMessage(formatUserMessage('QL1005_DAEMON_RESTART_NOT_READY', 'Language client is not initialized yet.'));
                return;
            }

            try {
                const response = await client.sendRequest('efquerylens/daemon/restart', {});
                const { success, message } = parseDaemonRestartResponse(response);

                if (success) {
                    window.showInformationMessage(`EF QueryLens: ${message}`);
                } else {
                    logOutput(formatLogMessage('QL1007_DAEMON_RESTART_INCOMPLETE', `daemon restart incomplete message=${message}`));
                    window.showWarningMessage(formatUserMessage('QL1007_DAEMON_RESTART_INCOMPLETE', message));
                }
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                logOutput(formatLogMessage('QL1006_DAEMON_RESTART_FAILED', `daemon restart failed reason=${message}`));
                window.showErrorMessage(formatUserMessage('QL1006_DAEMON_RESTART_FAILED', `Daemon restart failed. ${message}`));
            }
        }
    );

    return [
        showSqlCommand,
        recalculateCommand,
        copySqlCommand,
        openSqlEditorCommand,
        openOutputCommand,
        restartCommand,
        setupCommand,
    ];
}

type SetupHostCandidate = {
    projectPath: string;
    displayName: string;
    assemblyPath?: string;
};

type SetupDetectResult = {
    requiresHostSelection: boolean;
    defaultHostProjectPath?: string;
    hosts: SetupHostCandidate[];
    message?: string;
};

type SetupApplyOptions = {
    textDocumentUri: string;
    hostProjectPath?: string;
    provider?: string;
    force?: boolean;
    logOutput: (message: string) => void;
};

async function runSetupApply(
    client: LanguageClient,
    options: SetupApplyOptions
): Promise<void> {
    const response = await client.sendRequest('efquerylens/setup/apply', {
        textDocument: { uri: options.textDocumentUri },
        hostProjectPath: options.hostProjectPath,
        provider: options.provider,
        force: options.force ?? false,
    });

    const parsed = parseSetupResponse(response);
    if (parsed.action === 'NeedProvider') {
        const provider = await window.showQuickPick(
            [
                { label: 'SQL Server', provider: 'SqlServer' },
                { label: 'PostgreSQL (Npgsql)', provider: 'Npgsql' },
                { label: 'MySQL (Pomelo)', provider: 'MySql' },
                { label: 'SQLite', provider: 'Sqlite' },
            ],
            { placeHolder: 'Select the EF Core provider for the generated factory' }
        );

        if (!provider) {
            return;
        }

        await runSetupApply(client, {
            ...options,
            provider: provider.provider,
        });
        return;
    }

    if (parsed.success) {
        const opened = await tryOpenGeneratedFactory(parsed.generatedFilePath);
        const notification = opened
            ? buildFactoryOpenedMessage(parsed.requiresReview)
            : `EF QueryLens: ${parsed.message}`;

        if (parsed.requiresReview) {
            window.showWarningMessage(notification);
        } else {
            window.showInformationMessage(notification);
        }
        return;
    }

    options.logOutput(`[EFQueryLens] setup not completed message=${parsed.message}`);
    window.showWarningMessage(`EF QueryLens: ${parsed.message}`);
}

function readStringField(payload: Record<string, unknown>, camelKey: string): string | undefined {
    const value = payload[camelKey] ?? payload[camelKey.charAt(0).toUpperCase() + camelKey.slice(1)];
    return typeof value === 'string' ? value : undefined;
}

function readBoolField(payload: Record<string, unknown>, camelKey: string): boolean {
    const value = payload[camelKey] ?? payload[camelKey.charAt(0).toUpperCase() + camelKey.slice(1)];
    return value === true;
}

function parseSetupDetectResponse(response: unknown): SetupDetectResult {
    if (!response || typeof response !== 'object') {
        return { requiresHostSelection: false, hosts: [] };
    }

    const payload = response as Record<string, unknown>;
    const rawHosts = payload.hosts ?? payload.Hosts;
    const hosts = Array.isArray(rawHosts)
        ? rawHosts
            .filter((host): host is Record<string, unknown> => !!host && typeof host === 'object')
            .map(host => ({
                projectPath: readStringField(host, 'projectPath') ?? '',
                displayName: readStringField(host, 'displayName') ?? 'Host project',
                assemblyPath: readStringField(host, 'assemblyPath'),
            }))
            .filter(host => host.projectPath.length > 0)
        : [];

    return {
        requiresHostSelection: readBoolField(payload, 'requiresHostSelection'),
        defaultHostProjectPath: readStringField(payload, 'defaultHostProjectPath'),
        hosts,
        message: readStringField(payload, 'message'),
    };
}

function parseSetupResponse(response: unknown): {
    success: boolean;
    message: string;
    action?: string;
    requiresReview?: boolean;
    generatedFilePath?: string;
} {
    if (!response || typeof response !== 'object') {
        return { success: false, message: 'Set up QueryLens did not complete.' };
    }

    const payload = response as Record<string, unknown>;
    const success = readBoolField(payload, 'success');
    const message = readStringField(payload, 'message')
        ?? (success ? 'QueryLens factory generated.' : 'Set up QueryLens did not complete.');
    const action = readStringField(payload, 'action');
    const requiresReview = readBoolField(payload, 'requiresReview');
    const generatedFilePath = readStringField(payload, 'generatedFilePath');

    return { success, message, action, requiresReview, generatedFilePath };
}

const factoryOpenedMessage =
    'EF QueryLens: Factory opened — rebuild the project, then confirm each CreateOfflineContext().';

function buildFactoryOpenedMessage(requiresReview?: boolean): string {
    if (!requiresReview) {
        return factoryOpenedMessage;
    }

    return `${factoryOpenedMessage} Review best-effort defaults if any DbContext did not match AddDbContext.`;
}

async function tryOpenGeneratedFactory(generatedFilePath?: string): Promise<boolean> {
    if (!generatedFilePath) {
        return false;
    }

    try {
        const document = await workspace.openTextDocument(Uri.file(generatedFilePath));
        await window.showTextDocument(document, { preview: false });
        return true;
    } catch {
        return false;
    }
}

function logCommandInvocation(
    logOutput: (message: string) => void,
    commandName: string,
    uriInput: unknown,
    lineInput: unknown,
    characterInput: unknown
): void {
    logOutput(
        `[EFQueryLens] command ${commandName} uriType=${typeof uriInput} lineType=${typeof lineInput} charType=${typeof characterInput} uri=${String(uriInput)} line=${String(lineInput)} char=${String(characterInput)}`
    );
}

function parseDaemonRestartResponse(response: unknown): { success: boolean; message: string } {
    const success = !!(response && typeof response === 'object' && (response as { success?: unknown }).success === true);
    const message = response && typeof response === 'object' && typeof (response as { message?: unknown }).message === 'string'
        ? (response as { message: string }).message
        : (success ? 'Daemon restarted.' : 'Daemon restart did not complete.');

    return { success, message };
}
