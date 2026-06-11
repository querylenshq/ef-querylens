import * as assert from 'assert';
import {
    extractCompileMsFromHover,
    formatHoverQueuedMessage,
    formatHoverReadyMessage,
    isLikelyCachedHover,
} from '../../hover/logging';

suite('hover logging', () => {
    test('formatHoverReadyMessage distinguishes cached from fresh compile', () => {
        const hoverText = '**EF QueryLens** · 3 statements\n\n```sql\nSELECT 1\n```\n\n*SQL generation time 3488 ms*';

        assert.strictEqual(
            formatHoverReadyMessage('PrApplicationApiService.cs', 1208, 26, 3, hoverText),
            '[EFQueryLens] hover-ready file=PrApplicationApiService.cs line=1208 char=26 roundTripMs=3 cached=true compileMs=3488',
        );

        assert.strictEqual(
            formatHoverReadyMessage('PrApplicationApiService.cs', 1205, 21, 3504, hoverText),
            '[EFQueryLens] hover-ready file=PrApplicationApiService.cs line=1205 char=21 roundTripMs=3504 compileMs=3488',
        );
    });

    test('isLikelyCachedHover uses round-trip threshold', () => {
        assert.strictEqual(isLikelyCachedHover(3, 3488), true);
        assert.strictEqual(isLikelyCachedHover(3504, 3488), false);
    });

    test('extractCompileMsFromHover returns undefined when footer missing', () => {
        assert.strictEqual(extractCompileMsFromHover('**EF QueryLens** - in queue'), undefined);
    });

    test('formatHoverQueuedMessage includes round trip', () => {
        assert.strictEqual(
            formatHoverQueuedMessage('PrApplicationApiService.cs', 1202, 24, 42),
            '[EFQueryLens] hover-queued file=PrApplicationApiService.cs line=1202 char=24 roundTripMs=42',
        );
    });
});
