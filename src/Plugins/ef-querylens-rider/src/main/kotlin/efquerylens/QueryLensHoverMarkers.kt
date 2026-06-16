package efquerylens

internal object QueryLensHoverMarkers {
    fun isQueryLensHover(text: String): Boolean = text.contains("EF QueryLens", ignoreCase = true)

    fun isProcessing(text: String): Boolean =
        isQueryLensHover(text) &&
            (
                text.contains("translating query", ignoreCase = true) ||
                    text.contains("in queue", ignoreCase = true)
            )

    fun isReady(text: String): Boolean =
        isQueryLensHover(text) &&
            text.contains("SQL generation time", ignoreCase = true)
}
