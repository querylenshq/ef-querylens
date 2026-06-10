package efquerylens

object EFQueryLensHostStatus {
    @Volatile
    var statusText: String = "QueryLens: Starting…"
        private set

    fun updateFromSnapshot(payload: Any?) {
        val root = payload as? Map<*, *> ?: return
        val state = root["State"]?.toString() ?: root["state"]?.toString() ?: "Starting"
        val message = root["Message"]?.toString() ?: root["message"]?.toString()
        statusText =
            when (state) {
                "Warming" -> "QueryLens: Warming…"
                "Computing" -> "QueryLens: Computing SQL…"
                "Ready" -> "QueryLens: Ready"
                "Unavailable" -> "QueryLens: Unavailable"
                else -> message?.let { "QueryLens: $it" } ?: "QueryLens: Starting…"
            }
    }
}
