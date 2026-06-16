package efquerylens

import kotlin.test.Test
import kotlin.test.assertEquals

class EFQueryLensSqlReadyHandlerTest {
    @Test
    fun buildDedupeKey_ignoresCharacterAndNormalizesFileUri() {
        val first = EFQueryLensSqlReadyHandler.buildDedupeKey("file:///D:/repo/ApplicationService.Cn.cs", 107)
        val second = EFQueryLensSqlReadyHandler.buildDedupeKey("file:///d:/repo/ApplicationService.Cn.cs", 107)

        assertEquals(first, second)
        assertEquals("file:///d:/repo/ApplicationService.Cn.cs|107", first)
    }
}
