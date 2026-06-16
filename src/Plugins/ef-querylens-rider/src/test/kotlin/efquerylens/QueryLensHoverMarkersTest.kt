package efquerylens

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class QueryLensHoverMarkersTest {
    @Test
    fun processing_requiresQueryLensMarker() {
        assertTrue(
            QueryLensHoverMarkers.isProcessing(
                "**EF QueryLens** - translating query... hover again shortly.",
            ),
        )
        assertTrue(QueryLensHoverMarkers.isProcessing("EF QueryLens - in queue"))
        assertFalse(QueryLensHoverMarkers.isProcessing("Some other tool is in queue"))
    }

    @Test
    fun ready_detectsSqlGenerationTime() {
        assertTrue(QueryLensHoverMarkers.isReady("EF QueryLens\n\nSQL generation time 12 ms"))
        assertFalse(QueryLensHoverMarkers.isReady("EF QueryLens - translating query..."))
        assertFalse(QueryLensHoverMarkers.isReady("SQL generation time 12 ms"))
    }
}
