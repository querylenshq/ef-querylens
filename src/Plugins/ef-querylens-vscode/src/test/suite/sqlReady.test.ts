import * as assert from 'assert';
import {
    buildSqlReadyToastMessage,
    resetSqlReadyNotificationDedupeForTests,
    shouldShowSqlReadyNotification,
    SQL_READY_GO_TO_QUERY_ACTION,
    SQL_READY_OPEN_SQL_ACTION,
} from '../../notifications/sqlReadyLogic';
import {
    resetSqlReadyWatchesForTests,
    runSqlReadyWatchForTests,
} from '../../notifications/sqlReadyHoverWatcher';
import { QueryLensSettings } from '../../types';

suite('SQL ready notifications', () => {
    setup(() => {
        resetSqlReadyNotificationDedupeForTests();
        resetSqlReadyWatchesForTests();
    });

    test('buildSqlReadyToastMessage uses 1-based line numbers', () => {
        const message = buildSqlReadyToastMessage({
            fileUri: 'file:///tmp/Orders.cs',
            line: 55,
            character: 12,
            fileName: 'Orders.cs',
        });

        assert.ok(message.includes('Orders.cs:56'));
    });

    test('shouldShowSqlReadyNotification respects enabled flag', () => {
        const payload = {
            fileUri: 'file:///tmp/Orders.cs',
            line: 1,
            character: 2,
            fileName: 'Orders.cs',
            commandCount: 1,
        };

        assert.strictEqual(shouldShowSqlReadyNotification(payload, false), false);
        assert.strictEqual(shouldShowSqlReadyNotification(payload, true), true);
    });

    test('shouldShowSqlReadyNotification ignores zero commandCount', () => {
        const payload = {
            fileUri: 'file:///tmp/Orders.cs',
            line: 1,
            character: 2,
            fileName: 'Orders.cs',
            commandCount: 0,
        };

        assert.strictEqual(shouldShowSqlReadyNotification(payload, true), false);
    });

    test('sql ready toast action titles are stable', () => {
        assert.strictEqual(SQL_READY_GO_TO_QUERY_ACTION, 'Go to Query');
        assert.strictEqual(SQL_READY_OPEN_SQL_ACTION, 'Open SQL');
    });

    test('shouldShowSqlReadyNotification dedupes repeated payloads', () => {
        const payload = {
            fileUri: 'file:///tmp/Orders.cs',
            line: 1,
            character: 2,
            fileName: 'Orders.cs',
            commandCount: 1,
        };
        const now = 1_000_000;

        assert.strictEqual(shouldShowSqlReadyNotification(payload, true, now), true);
        assert.strictEqual(shouldShowSqlReadyNotification(payload, true, now + 1_000), false);
        assert.strictEqual(shouldShowSqlReadyNotification(payload, true, now + 31_000), true);
    });

    test('watcher notifies after queued then ready', async () => {
        const logs: string[] = [];
        const toasts: string[] = [];

        await runSqlReadyWatchForTests(
            [
                { Status: 1 },
                { Status: 0, Success: true, CommandCount: 1 },
            ],
            createSettings,
            logs.push.bind(logs),
            payload => {
                toasts.push(`${payload.fileUri}:${payload.line}:${payload.character}:${payload.commandCount}`);
            },
        );

        assert.strictEqual(toasts.length, 1);
        assert.ok(logs.some(line => line.includes('sql-ready-watch-ready')));
    });

    test('watcher notifies when first poll is already ready', async () => {
        const logs: string[] = [];
        const toasts: string[] = [];

        await runSqlReadyWatchForTests(
            [
                { Status: 0, Success: true, CommandCount: 1 },
            ],
            createSettings,
            logs.push.bind(logs),
            payload => {
                toasts.push(`${payload.fileUri}:${payload.line}:${payload.character}:${payload.commandCount}`);
            },
        );

        assert.strictEqual(toasts.length, 1);
        assert.ok(logs.some(line => line.includes('sql-ready-watch-ready')));
    });

    test('watcher exits failed ready without notification', async () => {
        const logs: string[] = [];
        const toasts: string[] = [];

        await runSqlReadyWatchForTests(
            [
                { Status: 1 },
                { Status: 0, Success: false, CommandCount: 0 },
            ],
            createSettings,
            logs.push.bind(logs),
            () => {
                toasts.push('unexpected');
            },
        );

        assert.deepStrictEqual(toasts, []);
        assert.ok(logs.some(line => line.includes('terminal-not-ready')));
    });

    test('watcher accepts camelCase status fields', async () => {
        const logs: string[] = [];
        const toasts: string[] = [];

        await runSqlReadyWatchForTests(
            [
                { status: 1 },
                { status: 0, success: true, commandCount: 2 },
            ],
            createSettings,
            logs.push.bind(logs),
            payload => {
                toasts.push(String(payload.commandCount));
            },
        );

        assert.deepStrictEqual(toasts, ['2']);
        assert.ok(logs.some(line => line.includes('commands=2')));
    });

    test('watcher exits on null response', async () => {
        const logs: string[] = [];
        const toasts: string[] = [];

        await runSqlReadyWatchForTests(
            [null],
            createSettings,
            logs.push.bind(logs),
            () => {
                toasts.push('unexpected');
            },
        );

        assert.deepStrictEqual(toasts, []);
        assert.ok(logs.some(line => line.includes('null-response')));
    });
});

function createSettings(): QueryLensSettings {
    return {
        maxCodeLensPerDocument: 50,
        codeLensDebounceMs: 250,
        codeLensUseModelFilter: false,
        formatSqlOnShow: true,
        sqlDialect: 'auto',
        debugLogsEnabled: false,
        showStatusBar: true,
        hoverWaitWhenWarmMs: 0,
        notifyWhenSqlReady: true,
    };
}
