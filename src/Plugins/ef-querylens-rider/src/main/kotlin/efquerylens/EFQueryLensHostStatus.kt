package efquerylens

import java.util.concurrent.CopyOnWriteArrayList

object EFQueryLensHostStatus {
    @Volatile
    var statusText: String = "QueryLens: Starting…"
        private set

    @Volatile
    var displayText: String = "[S] QueryLens"
        private set

    @Volatile
    var stateKind: StateKind = StateKind.Starting
        private set

    @Volatile
    var tooltipText: String = "Starting QueryLens…"
        private set

    private val listeners = CopyOnWriteArrayList<Runnable>()

    fun addListener(listener: Runnable) {
        listeners.add(listener)
    }

    fun removeListener(listener: Runnable) {
        listeners.remove(listener)
    }

    fun updateFromSnapshot(payload: Any?) {
        val root = payload as? Map<*, *> ?: return
        val mapped = mapSnapshot(root)
        if (
            statusText == mapped.text &&
            displayText == mapped.displayText &&
            stateKind == mapped.stateKind &&
            tooltipText == mapped.tooltip
        ) {
            return
        }

        statusText = mapped.text
        displayText = mapped.displayText
        stateKind = mapped.stateKind
        tooltipText = mapped.tooltip
        listeners.forEach(Runnable::run)
    }

    internal fun mapSnapshot(root: Map<*, *>): MappedStatus {
        val warmed = readBoolField(root, "warmed")
        val rawState = readStateField(root)
        val state =
            if (warmed) {
                rawState
            } else if (rawState == HostState.Unavailable) {
                HostState.Unavailable
            } else if (rawState == HostState.Computing) {
                HostState.Computing
            } else {
                HostState.Warming
            }

        val message = readStringField(root, "message")?.trim().takeUnless { it.isNullOrBlank() } ?: "Starting QueryLens…"
        val assembly = readStringField(root, "assemblyPath")?.trim()
        val inflight = readIntField(root, "inflightCount") ?: 0

        val text =
            when (state) {
                HostState.Warming -> "QueryLens: Warming…"
                HostState.Computing -> "QueryLens: Computing SQL…"
                HostState.Ready -> "QueryLens: Ready"
                HostState.Unavailable -> "QueryLens: Unavailable"
                else -> "QueryLens: Starting…"
            }

        val displayState = state.toStateKind()
        val displayText = "${displayState.marker} QueryLens"
        val tooltipParts = mutableListOf("State: $text", message)
        if (!assembly.isNullOrBlank()) {
            tooltipParts.add("Assembly: $assembly")
        }
        if (inflight > 0) {
            tooltipParts.add("In flight: $inflight")
        }
        tooltipParts.add("Click to open EF QueryLens output")

        return MappedStatus(
            text = text,
            displayText = displayText,
            stateKind = displayState,
            tooltip = tooltipParts.joinToString("\n"),
        )
    }

    internal data class MappedStatus(
        val text: String,
        val displayText: String,
        val stateKind: StateKind,
        val tooltip: String,
    )

    enum class StateKind(
        val marker: String,
    ) {
        Starting("[S]"),
        Warming("[W]"),
        Ready("[R]"),
        Computing("[C]"),
        Unavailable("[!]"),
    }

    private enum class HostState {
        Starting,
        Warming,
        Ready,
        Computing,
        Unavailable,
    }

    private fun HostState.toStateKind(): StateKind =
        when (this) {
            HostState.Starting -> StateKind.Starting
            HostState.Warming -> StateKind.Warming
            HostState.Ready -> StateKind.Ready
            HostState.Computing -> StateKind.Computing
            HostState.Unavailable -> StateKind.Unavailable
        }

    private fun readStateField(payload: Map<*, *>): HostState {
        val raw = payload["state"] ?: payload["State"] ?: return HostState.Starting
        return when (raw) {
            is Number ->
                when (raw.toInt()) {
                    1 -> HostState.Warming
                    2 -> HostState.Ready
                    3 -> HostState.Computing
                    4 -> HostState.Unavailable
                    else -> HostState.Starting
                }
            else ->
                when (raw.toString()) {
                    "Warming" -> HostState.Warming
                    "Ready" -> HostState.Ready
                    "Computing" -> HostState.Computing
                    "Unavailable" -> HostState.Unavailable
                    else -> HostState.Starting
                }
        }
    }

    private fun readStringField(
        payload: Map<*, *>,
        camelKey: String,
    ): String? {
        val pascalKey = camelKey.replaceFirstChar { it.uppercaseChar() }
        return (payload[camelKey] ?: payload[pascalKey]) as? String
    }

    private fun readBoolField(
        payload: Map<*, *>,
        camelKey: String,
    ): Boolean {
        val pascalKey = camelKey.replaceFirstChar { it.uppercaseChar() }
        return payload[camelKey] == true || payload[pascalKey] == true
    }

    private fun readIntField(
        payload: Map<*, *>,
        camelKey: String,
    ): Int? {
        val pascalKey = camelKey.replaceFirstChar { it.uppercaseChar() }
        val value = payload[camelKey] ?: payload[pascalKey]
        return (value as? Number)?.toInt()
    }
}
