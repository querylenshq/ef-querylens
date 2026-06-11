import {
    commands,
    Position,
    Range,
    Selection,
    TextEditorRevealType,
    window,
    workspace,
} from 'vscode';

import { formatUserMessage } from './errors';
import { clamp, coerceNonNegativeInt, parseUri } from './parsing';

export async function revealQuerySource(
    fileUri: string,
    line: unknown,
    character: unknown,
    options?: { showHover?: boolean },
): Promise<boolean> {
    const uri = parseUri(fileUri);
    if (!uri) {
        void window.showWarningMessage(
            formatUserMessage('QL1002_INVALID_URI', 'Unable to resolve document URI for SQL preview.'),
        );
        return false;
    }

    const document = await workspace.openTextDocument(uri);
    const editor = await window.showTextDocument(document, {
        preview: false,
        preserveFocus: false,
    });

    const requestedLine = coerceNonNegativeInt(line, 0);
    const requestedCharacter = coerceNonNegativeInt(character, 0);
    const clampedLine = clamp(requestedLine, 0, Math.max(document.lineCount - 1, 0));
    const lineText = document.lineAt(clampedLine).text;
    const clampedCharacter = clamp(requestedCharacter, 0, lineText.length);
    const position = new Position(clampedLine, clampedCharacter);

    editor.selection = new Selection(position, position);
    editor.revealRange(new Range(position, position), TextEditorRevealType.InCenterIfOutsideViewport);

    if (options?.showHover) {
        await commands.executeCommand('editor.action.showHover');
    }

    return true;
}
