import * as assert from 'assert';
import {
    buildSqlReadyToastMessage,
    resetSqlReadyNotificationDedupeForTests,
    shouldShowSqlReadyNotification,
    SQL_READY_GO_TO_QUERY_ACTION,
    SQL_READY_OPEN_SQL_ACTION,
} from '../../notifications/sqlReadyLogic';

suite('SQL ready notifications', () => {
    setup(() => {
        resetSqlReadyNotificationDedupeForTests();
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
});
