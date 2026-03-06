import * as path from 'path';
import { workspace, ExtensionContext } from 'vscode';

import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: ExtensionContext) {
    // Let's resolve the path to the compiled DLL of the LSP server
    const serverPath = context.asAbsolutePath(
        path.join('..', '..', 'QueryLens.Lsp', 'bin', 'Debug', 'net10.0', 'QueryLens.Lsp.dll')
    );

    const serverOptions: ServerOptions = {
        command: 'dotnet',
        args: [serverPath],
        options: { cwd: path.join(context.extensionPath, '..', '..') }
    };

    const clientOptions: LanguageClientOptions = {
        // Register the server for plain C# documents
        documentSelector: [{ scheme: 'file', language: 'csharp' }],
        synchronize: {
            fileEvents: workspace.createFileSystemWatcher('**/*.cs')
        }
    };

    client = new LanguageClient(
        'querylens-lsp',
        'QueryLens Language Server',
        serverOptions,
        clientOptions
    );

    client.start();
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
