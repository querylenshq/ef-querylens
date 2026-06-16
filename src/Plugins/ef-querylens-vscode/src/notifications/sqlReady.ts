import { commands, window } from 'vscode';

import { revealQuerySource } from '../utils/revealQuerySource';
import {
    buildSqlReadyToastMessage,
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
