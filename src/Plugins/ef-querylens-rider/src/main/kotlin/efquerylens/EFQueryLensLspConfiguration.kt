package efquerylens

import com.intellij.openapi.project.Project
import com.intellij.platform.lsp.api.LspServerManager
import org.eclipse.lsp4j.DidChangeConfigurationParams

internal object EFQueryLensLspConfiguration {
    fun buildInitializationOptions(project: Project): Map<String, Any?> {
        val settings = EFQueryLensSettingsService.getInstance(project)
        return mapOf(
            "queryLens" to
                mapOf(
                    "debugEnabled" to true,
                    "enableLspHover" to true,
                    "hoverProgressNotify" to false,
                    "sqlReadyNotify" to settings.notifyWhenSqlReady,
                    "hoverProgressDelayMs" to 350,
                    "hoverCacheTtlMs" to 15_000,
                    "markdownQueueAdaptiveWaitMs" to 200,
                    "structuredQueueAdaptiveWaitMs" to 200,
                    "warmupSuccessTtlMs" to 60_000,
                    "warmupFailureCooldownMs" to 5_000,
                    "hoverWaitWhenWarmMs" to settings.hoverWaitWhenWarmMs,
                ),
        )
    }

    fun pushRuntimeConfiguration(project: Project) {
        val server =
            LspServerManager
                .getInstance(project)
                .getServersForProvider(EFQueryLensLspServerSupportProvider::class.java)
                .firstOrNull() ?: return

        val payload =
            mapOf(
                "settings" to
                    mapOf(
                        "queryLens" to
                            (buildInitializationOptions(project)["queryLens"] as Map<*, *>),
                    ),
            )

        server.sendNotification { languageServer ->
            languageServer.workspaceService.didChangeConfiguration(DidChangeConfigurationParams(payload))
        }
    }
}
