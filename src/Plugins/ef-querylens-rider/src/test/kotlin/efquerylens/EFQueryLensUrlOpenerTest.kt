package efquerylens

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class EFQueryLensUrlOpenerTest {
    @Test
    fun extractStructuredPreview_factoryPromptWithoutSqlReturnsStatusMessage() {
        val opener = EFQueryLensUrlOpener()
        val preview =
            opener.extractStructuredPreview(
                mapOf(
                    "hover" to
                        mapOf(
                            "Status" to 0,
                            "Success" to true,
                            "Mode" to "factory-prompt",
                            "StatusMessage" to "**Rebuild needed**\n\n- Rebuild the project, then try SQL Preview again.",
                            "Statements" to emptyList<Map<String, Any?>>(),
                        ),
                ),
                "file:///repo/Demo.cs",
                41,
            )

        assertNotNull(preview)
        assertEquals("", preview.sqlText)
        assertEquals("", preview.actionSqlText)
        val message = assertNotNull(preview.statusMessage)
        assertTrue(message.contains("Rebuild needed"))
        assertTrue(message.contains("Rebuild the project"))
    }

    @Test
    fun extractStructuredPreview_readyWithoutSqlOrMessageReturnsNull() {
        val opener = EFQueryLensUrlOpener()
        val preview =
            opener.extractStructuredPreview(
                mapOf(
                    "hover" to
                        mapOf(
                            "Status" to 0,
                            "Success" to true,
                            "Statements" to emptyList<Map<String, Any?>>(),
                        ),
                ),
                "file:///repo/Demo.cs",
                41,
            )

        assertNull(preview)
    }
}
