import * as assert from 'assert';
import { mapStatusSnapshot } from '../../status/statusBar';

suite('QueryLens status bar mapping', () => {
    test('maps ready state', () => {
        const mapped = mapStatusSnapshot({ State: 'Ready', Message: 'Ready', Warmed: true });
        assert.ok(mapped.text.includes('Ready'));
    });

    test('maps computing state', () => {
        const mapped = mapStatusSnapshot({ State: 'Computing', Message: 'Translating LINQ to SQL…', InflightCount: 2 });
        assert.ok(mapped.text.includes('Computing SQL'));
        assert.ok(mapped.tooltip.includes('In flight: 2'));
    });

    test('maps unavailable state', () => {
        const mapped = mapStatusSnapshot({ State: 'Unavailable', Message: 'QueryLens engine is unavailable.' });
        assert.ok(mapped.text.includes('Unavailable'));
        assert.ok(mapped.backgroundColor);
    });
});
