package efquerylens

import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals

class EFQueryLensHostStatusTest {
    @Test
    fun mapSnapshot_requiresWarmedForReadyText() {
        val warming =
            EFQueryLensHostStatus.mapSnapshot(
                mapOf(
                    "State" to "Ready",
                    "Message" to "Ready",
                    "Warmed" to false,
                ),
            )

        assertContains(warming.text, "Warming")

        val ready =
            EFQueryLensHostStatus.mapSnapshot(
                mapOf(
                    "State" to "Ready",
                    "Message" to "Ready",
                    "Warmed" to true,
                ),
            )

        assertEquals("QueryLens: Ready", ready.text)
    }
}
