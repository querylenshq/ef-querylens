import { commands, window } from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

import { revealQuerySource } from '../utils/revealQuerySource';
import {
    buildSqlReadyToastMessage,
    shouldShowSqlReadyNotification,
    SQL_READY_GO_TO_QUERY_ACTION,
    SQL_READY_OPEN_SQL_ACTION,
    SqlReadyNotificationPayload,
} from './sqlReadyLogic';

export type { SqlReadyNotificationPayload } from './sqlReadyLogic';

export async function showSqlReadyToast(payload: SqlReadyNotificationPayload): Promise<void> {
    const choice = await window.showInformationMessage(
        buildSqlReadyToastMessage(payload),
        { title: SQL_READY_GO_TO_QUERY_ACTION },
        { title: SQL_READY_OPEN_SQL_ACTION },
    );

    if (choice?.title === SQL_READY_GO_TO_QUERY_ACTION) {
        await revealQuerySource(payload.fileUri, payload.line, payload.character);
        return;
    }

    if (choice?.title === SQL_READY_OPEN_SQL_ACTION) {
        await commands.executeCommand(
            'efquerylens.openSqlEditor',
            payload.fileUri,
            payload.line,
            payload.character,
        );
    }
}

export function attachSqlReadyNotifications(
    client: LanguageClient,
    isEnabled: () => boolean,
): void {
    client.onNotification('efquerylens/sqlReady', (payload: SqlReadyNotificationPayload) => {
        if (!shouldShowSqlReadyNotification(payload, isEnabled())) {
            return;
        }

        void showSqlReadyToast(payload);
    });
}
