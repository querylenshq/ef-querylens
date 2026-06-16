package efquerylens

import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class EFQueryLensHostStatusTest {
    @Test
    fun mapSnapshot_requiresWarmedForReadyTextButKeepsFixedDisplayWidth() {
        val warming =
            EFQueryLensHostStatus.mapSnapshot(
                mapOf(
                    "State" to "Ready",
                    "Message" to "Ready",
                    "Warmed" to false,
                ),
            )

        assertContains(warming.text, "Warming")
        assertEquals("[W] QueryLens", warming.displayText)
        assertEquals(EFQueryLensHostStatus.StateKind.Warming, warming.stateKind)

        val ready =
            EFQueryLensHostStatus.mapSnapshot(
                mapOf(
                    "State" to "Ready",
                    "Message" to "Ready",
                    "Warmed" to true,
                ),
            )

        assertEquals("QueryLens: Ready", ready.text)
        assertEquals("[R] QueryLens", ready.displayText)
        assertEquals(EFQueryLensHostStatus.StateKind.Ready, ready.stateKind)
        assertEquals(warming.displayText.length, ready.displayText.length)
    }

    @Test
    fun mapSnapshot_tooltipIncludesExplicitStateAndDetails() {
        val computing =
            EFQueryLensHostStatus.mapSnapshot(
                mapOf(
                    "State" to "Computing",
                    "Message" to "Computing SQL...",
                    "Warmed" to true,
                    "InflightCount" to 2,
                    "AssemblyPath" to "C:/repo/App.dll",
                ),
            )

        assertEquals("[C] QueryLens", computing.displayText)
        assertContains(computing.tooltip, "State: QueryLens: Computing SQL")
        assertContains(computing.tooltip, "Computing SQL")
        assertContains(computing.tooltip, "In flight: 2")
        assertContains(computing.tooltip, "Assembly: C:/repo/App.dll")
    }

    @Test
    fun updateFromSnapshot_notifiesOnlyWhenMappedStatusChanges() {
        var notifications = 0
        val listener = Runnable { notifications++ }
        EFQueryLensHostStatus.addListener(listener)
        try {
            val ready =
                mapOf(
                    "State" to "Ready",
                    "Message" to "Ready",
                    "Warmed" to true,
                )
            EFQueryLensHostStatus.updateFromSnapshot(ready)
            EFQueryLensHostStatus.updateFromSnapshot(ready)

            assertEquals(1, notifications)
            assertEquals("[R] QueryLens", EFQueryLensHostStatus.displayText)
            assertTrue(EFQueryLensHostStatus.tooltipText.contains("State: QueryLens: Ready"))
        } finally {
            EFQueryLensHostStatus.removeListener(listener)
        }
    }
}
