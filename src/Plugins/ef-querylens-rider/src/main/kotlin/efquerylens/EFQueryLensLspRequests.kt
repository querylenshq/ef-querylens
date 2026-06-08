package efquerylens

import com.intellij.openapi.project.Project
import com.intellij.platform.lsp.api.LspServerManager

internal data class SetupHostCandidate(
    val projectPath: String,
    val displayName: String,
    val assemblyPath: String?,
)

internal data class SetupDetectResult(
    val requiresHostSelection: Boolean,
    val defaultHostProjectPath: String?,
    val hosts: List<SetupHostCandidate>,
    val message: String?,
)

internal data class SetupApplyResult(
    val success: Boolean,
    val message: String,
    val action: String?,
    val generatedFilePath: String?,
    val requiresReview: Boolean = false,
)

internal object EFQueryLensLspRequests {
    private const val REQUEST_TIMEOUT_MS: Int = 30_000

    internal fun requestSetupDetect(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
    ): SetupDetectResult? {
        val payload =
            mapOf(
                "textDocument" to mapOf("uri" to fileUri),
                "position" to mapOf("line" to line, "character" to character),
            )

        val response =
            sendCustomRequest(project, "efquerylens/setup/detect", payload) ?: return null
        return parseSetupDetectResponse(response)
    }

    internal fun requestSetupApply(
        project: Project,
        fileUri: String,
        hostProjectPath: String?,
        provider: String?,
        force: Boolean = false,
    ): SetupApplyResult? {
        val payload =
            buildMap<String, Any?> {
                put("textDocument", mapOf("uri" to fileUri))
                put("force", force)
                if (!hostProjectPath.isNullOrBlank()) {
                    put("hostProjectPath", hostProjectPath)
                }
                if (!provider.isNullOrBlank()) {
                    put("provider", provider)
                }
            }

        val response =
            sendCustomRequest(project, "efquerylens/setup/apply", payload) ?: return null
        return parseSetupApplyResponse(response)
    }

    private fun sendCustomRequest(
        project: Project,
        method: String,
        payload: Map<String, Any?>,
    ): Map<String, Any?>? {
        val server =
            LspServerManager
                .getInstance(project)
                .getServersForProvider(EFQueryLensLspServerSupportProvider::class.java)
                .firstOrNull() ?: return null

        val response =
            when (method) {
                "efquerylens/setup/detect" ->
                    server.sendRequestSync(REQUEST_TIMEOUT_MS) { languageServer ->
                        (languageServer as EFQueryLensLspServer).setupDetect(payload)
                    }

                "efquerylens/setup/apply" ->
                    server.sendRequestSync(REQUEST_TIMEOUT_MS) { languageServer ->
                        (languageServer as EFQueryLensLspServer).setupApply(payload)
                    }

                else -> return null
            }

        return response as? Map<String, Any?>
    }

    private fun parseSetupDetectResponse(response: Map<String, Any?>): SetupDetectResult {
        val rawHosts = response["hosts"] ?: response["Hosts"]
        val hosts =
            (rawHosts as? List<*>)
                ?.mapNotNull { host ->
                    val hostMap = host as? Map<*, *> ?: return@mapNotNull null
                    val projectPath = readStringField(hostMap, "projectPath") ?: return@mapNotNull null
                    SetupHostCandidate(
                        projectPath = projectPath,
                        displayName = readStringField(hostMap, "displayName") ?: "Host project",
                        assemblyPath = readStringField(hostMap, "assemblyPath"),
                    )
                }.orEmpty()

        return SetupDetectResult(
            requiresHostSelection = readBoolField(response, "requiresHostSelection"),
            defaultHostProjectPath = readStringField(response, "defaultHostProjectPath"),
            hosts = hosts,
            message = readStringField(response, "message"),
        )
    }

    private fun parseSetupApplyResponse(response: Map<String, Any?>): SetupApplyResult {
        val success = readBoolField(response, "success")
        val message =
            readStringField(response, "message")
                ?: if (success) {
                    "QueryLens factory generated."
                } else {
                    "Set up QueryLens did not complete."
                }

        return SetupApplyResult(
            success = success,
            message = message,
            action = readStringField(response, "action"),
            generatedFilePath = readStringField(response, "generatedFilePath"),
            requiresReview = readBoolField(response, "requiresReview"),
        )
    }

    private fun readStringField(
        payload: Map<*, *>,
        camelKey: String,
    ): String? {
        val pascalKey = camelKey.replaceFirstChar { it.uppercaseChar() }
        val value = payload[camelKey] ?: payload[pascalKey]
        return value as? String
    }

    private fun readBoolField(
        payload: Map<*, *>,
        camelKey: String,
    ): Boolean {
        val pascalKey = camelKey.replaceFirstChar { it.uppercaseChar() }
        val value = payload[camelKey] ?: payload[pascalKey]
        return value == true
    }
}
