import * as assert from 'assert';
import { mapStatusSnapshot } from '../../status/statusBar';

suite('QueryLens status bar mapping', () => {
    test('maps ready state to stable text', () => {
        const mapped = mapStatusSnapshot({ State: 'Ready', Message: 'Ready', Warmed: true });
        assert.strictEqual(mapped.text, '$(check) QueryLens');
        assert.ok(mapped.tooltip.includes('State: QueryLens: Ready'));
    });

    test('maps computing state to stable label with details in tooltip', () => {
        const mapped = mapStatusSnapshot({ State: 'Computing', Message: 'Translating LINQ to SQL…', InflightCount: 2 });
        assert.strictEqual(mapped.text, '$(sync~spin) QueryLens');
        assert.ok(mapped.tooltip.includes('State: QueryLens: Computing SQL'));
        assert.ok(mapped.tooltip.includes('Translating LINQ to SQL'));
        assert.ok(mapped.tooltip.includes('In flight: 2'));
    });

    test('maps unavailable state', () => {
        const mapped = mapStatusSnapshot({ State: 'Unavailable', Message: 'QueryLens engine is unavailable.' });
        assert.strictEqual(mapped.text, '$(error) QueryLens');
        assert.ok(mapped.tooltip.includes('State: QueryLens: Unavailable'));
        assert.ok(mapped.backgroundColor);
    });
});
