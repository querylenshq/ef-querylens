package efquerylens

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class EFQueryLensHoverProbeTest {
    @Test
    fun normalizeFileUri_lowercasesWindowsDriveLetter() {
        assertEquals(
            "file:///d:/repo/App.cs",
            EFQueryLensHoverProbe.normalizeFileUri("file:///D:/repo/App.cs"),
        )
        assertEquals(
            "file:///tmp/App.cs",
            EFQueryLensHoverProbe.normalizeFileUri("file:///tmp/App.cs"),
        )
    }

    @Test
    fun classify_readsPascalAndCamelStatusFields() {
        assertEquals(
            HoverProbeOutcome.Queued,
            EFQueryLensHoverProbe.classify(mapOf("Status" to 1)),
        )
        assertEquals(
            HoverProbeOutcome.Ready,
            EFQueryLensHoverProbe.classify(mapOf("status" to 0)),
        )
        assertEquals(
            HoverProbeOutcome.Other,
            EFQueryLensHoverProbe.classify(mapOf("Status" to 3)),
        )
    }

    @Test
    fun isThrottled_tracksProbeWindowByKey() {
        EFQueryLensHoverProbe.resetThrottleForTests()
        val key = EFQueryLensHoverProbe.buildKey("file:///D:/repo/App.cs", 1, 2)

        assertFalse(EFQueryLensHoverProbe.isThrottled(key, nowMs = 1_000))
        assertTrue(EFQueryLensHoverProbe.isThrottled(key, nowMs = 1_100))
        assertFalse(EFQueryLensHoverProbe.isThrottled(key, nowMs = 1_600))
    }

    @Test
    fun buildKey_canRepresentLineLevelThrottle() {
        val lineKey = EFQueryLensHoverProbe.buildKey("file:///D:/repo/App.cs", 10, 0)

        assertEquals("file:///d:/repo/App.cs|10|0", lineKey)
    }

    @Test
    fun isTerminalWithoutSql_detectsNoRegionReadyResult() {
        assertTrue(
            EFQueryLensHoverProbe.isTerminalWithoutSql(
                mapOf("Status" to 0, "Success" to false, "CommandCount" to 0),
            ),
        )
        assertTrue(
            EFQueryLensHoverProbe.isTerminalWithoutSql(
                mapOf("status" to 0, "success" to true, "commandCount" to 0),
            ),
        )
        assertFalse(
            EFQueryLensHoverProbe.isTerminalWithoutSql(
                mapOf("Status" to 0, "Success" to true, "CommandCount" to 1),
            ),
        )
        assertFalse(
            EFQueryLensHoverProbe.isTerminalWithoutSql(
                mapOf("Status" to 1, "Success" to false, "CommandCount" to 0),
            ),
        )
    }

    @Test
    fun terminalCooldown_tracksLineLevelMouseProbeCooldown() {
        EFQueryLensHoverProbe.resetThrottleForTests()
        val key = EFQueryLensHoverProbe.buildLineKey("file:///D:/repo/App.cs", 10)

        assertFalse(EFQueryLensHoverProbe.isTerminalCooldownActive(key, nowMs = 1_000))

        EFQueryLensHoverProbe.rememberTerminalCooldown(key, nowMs = 1_000)

        assertTrue(EFQueryLensHoverProbe.isTerminalCooldownActive(key, nowMs = 1_100))
        assertFalse(EFQueryLensHoverProbe.isTerminalCooldownActive(key, nowMs = 6_001))
    }
}
