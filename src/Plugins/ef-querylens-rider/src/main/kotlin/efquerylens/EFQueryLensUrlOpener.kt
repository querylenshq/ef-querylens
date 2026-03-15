package efquerylens

import com.intellij.ide.browsers.UrlOpener
import com.intellij.ide.browsers.WebBrowser
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.ide.CopyPasteManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.project.ProjectManager
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.platform.lsp.api.LspServerManager
import org.eclipse.lsp4j.HoverParams
import org.eclipse.lsp4j.Position
import org.eclipse.lsp4j.TextDocumentIdentifier
import java.awt.datatransfer.StringSelection
import java.io.File
import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.Base64

class EFQueryLensUrlOpener : UrlOpener() {

    override fun openUrl(browser: WebBrowser, url: String, project: Project?): Boolean {
        if (!url.startsWith("efquerylens://", ignoreCase = true)) {
            return false
        }

        val uri = runCatching { URI(url) }.getOrNull() ?: return true
        val host = uri.host?.lowercase() ?: return true
        if (host != "copysql" && host != "opensqleditor" && host != "recalculate") return true

        val params = parseQueryParams(uri.rawQuery ?: "")
        val fileUri = params["uri"] ?: return true
        val line = params["line"]?.toIntOrNull() ?: 0
        val character = params["character"]?.toIntOrNull() ?: 0

        val effectiveProject = project ?: ProjectManager.getInstance().openProjects.firstOrNull() ?: return true

        if (host == "recalculate") {
            requestPreviewRecalculate(effectiveProject, fileUri, line, character)
            return true
        }

        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                val content = buildEnrichedContent(effectiveProject, fileUri, line, character)
                    ?: return@executeOnPooledThread
                when (host) {
                    "copysql" -> CopyPasteManager.getInstance().setContents(StringSelection(content))
                    "opensqleditor" -> openInEditor(effectiveProject, content)
                }
            } catch (e: Exception) {
                thisLogger().warn("[EFQueryLens] URL opener failed for host=$host", e)
            }
        }

        return true
    }

    private fun requestPreviewRecalculate(project: Project, fileUri: String, line: Int, character: Int) {
        val server = LspServerManager.getInstance(project)
            .getServersForProvider(EFQueryLensLspServerSupportProvider::class.java)
            .firstOrNull() ?: return

        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                val payload = mapOf(
                    "textDocument" to mapOf("uri" to fileUri),
                    "position" to mapOf("line" to line, "character" to character)
                )

                val response = server.sendRequestSync(10_000) {
                    it.workspaceService.executeCommand(
                        org.eclipse.lsp4j.ExecuteCommandParams(
                            "efquerylens.preview.recalculate",
                            listOf(payload)
                        )
                    )
                }

                thisLogger().info("[EFQueryLens] Recalculate response='$response'")
            } catch (e: Exception) {
                thisLogger().warn("[EFQueryLens] Recalculate request failed", e)
            }
        }
    }

    private fun buildEnrichedContent(project: Project, fileUri: String, line: Int, character: Int): String? {
        val server = LspServerManager.getInstance(project)
            .getServersForProvider(EFQueryLensLspServerSupportProvider::class.java)
            .firstOrNull() ?: return null

        val hoverResult = runCatching {
            server.sendRequestSync(10_000) { serverApi ->
                serverApi.textDocumentService.hover(
                    HoverParams(TextDocumentIdentifier(fileUri), Position(line, character))
                )
            }
        }.getOrNull() ?: return null

        val markdown = when (val contents = hoverResult.contents) {
            is org.eclipse.lsp4j.MarkupContent -> contents.value
            else -> return null
        }

        val rawSql = extractSqlBlocks(markdown) ?: return null
        val metadata = extractMetadata(markdown)
        // Use server-built enriched SQL if available (avoids client-side rebuild).
        if (!metadata?.enrichedSql.isNullOrBlank()) {
            return metadata!!.enrichedSql
        }
        return buildEnrichedSqlContent(rawSql, metadata, fileUri)
    }

    private fun extractSqlBlocks(markdown: String): String? {
        val regex = Regex("```sql\\s*([\\s\\S]*?)```", RegexOption.IGNORE_CASE)
        val blocks = regex.findAll(markdown)
            .map { it.groupValues[1].trim() }
            .filter { it.isNotEmpty() }
            .toList()
        return if (blocks.isEmpty()) null else blocks.joinToString("\n\n-- next query --\n\n")
    }

    private fun extractMetadata(markdown: String): QueryLensMetadata? {
        val match = Regex("""<!--\s*QUERYLENS_META:([A-Za-z0-9+/=]+)\s*-->""", RegexOption.IGNORE_CASE)
            .find(markdown) ?: return null
        return runCatching {
            val json = String(Base64.getDecoder().decode(match.groupValues[1]), StandardCharsets.UTF_8)
            parseMetadataJson(json)
        }.getOrNull()
    }

    /** Lightweight JSON field extraction — avoids a JSON library dependency. */
    private fun parseMetadataJson(json: String): QueryLensMetadata {
        fun strField(name: String): String {
            val regex = Regex(""""$name"\s*:\s*"((?:[^"\\]|\\.)*)"""")
            return regex.find(json)?.groupValues?.get(1)
                ?.replace("\\n", "\n")?.replace("\\r", "")?.replace("\\\"", "\"") ?: ""
        }
        fun intField(name: String): Int {
            val regex = Regex(""""$name"\s*:\s*(\d+)""")
            return regex.find(json)?.groupValues?.get(1)?.toIntOrNull() ?: 0
        }
        return QueryLensMetadata(
            sourceExpression = strField("SourceExpression"),
            executedExpression = strField("ExecutedExpression"),
            sourceFile = strField("SourceFile"),
            sourceLine = intField("SourceLine"),
            dbContextType = strField("DbContextType"),
            providerName = strField("ProviderName"),
            creationStrategy = strField("CreationStrategy"),
            enrichedSql = strField("EnrichedSql"),
        )
    }

    private fun buildEnrichedSqlContent(sql: String, meta: QueryLensMetadata?, fileUri: String): String {
        val sb = StringBuilder()
        sb.appendLine("-- EF QueryLens")

        val sourceFile = meta?.sourceFile?.takeIf { it.isNotBlank() }
            ?: runCatching { File(URI(fileUri)).absolutePath }.getOrNull()
        if (sourceFile != null) {
            val lineDisplay = if ((meta?.sourceLine ?: 0) > 0) ", line ${meta!!.sourceLine}" else ""
            sb.appendLine("-- Source:    $sourceFile$lineDisplay")
        }

        appendCommentedExpression(sb, "LINQ", meta?.sourceExpression)

        if (!meta?.executedExpression.isNullOrBlank() && meta?.executedExpression != meta?.sourceExpression) {
            appendCommentedExpression(sb, "Executed LINQ (differs from source)", meta?.executedExpression)
        }

        if (!meta?.dbContextType.isNullOrBlank()) sb.appendLine("-- DbContext: ${meta!!.dbContextType}")
        if (!meta?.providerName.isNullOrBlank()) sb.appendLine("-- Provider:  ${meta!!.providerName}")
        if (!meta?.creationStrategy.isNullOrBlank()) sb.appendLine("-- Strategy:  ${meta!!.creationStrategy}")

        sb.appendLine()
        sb.append(sql)
        return sb.toString()
    }

    private fun appendCommentedExpression(sb: StringBuilder, label: String, expression: String?) {
        if (expression.isNullOrBlank()) return
        sb.appendLine("-- $label:")
        expression.lines().forEach { exprLine ->
            sb.appendLine(if (exprLine.isEmpty()) "--" else "--   ${exprLine.trimEnd()}")
        }
    }

    private fun openInEditor(project: Project, content: String) {
        val tempDir = File(System.getProperty("java.io.tmpdir"), "EFQueryLens")
        tempDir.mkdirs()
        val stamp = LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMdd_HHmmss"))
        val tempFile = File(tempDir, "preview_$stamp.sql")
        tempFile.writeText(content, StandardCharsets.UTF_8)

        ApplicationManager.getApplication().invokeLater {
            val vFile = LocalFileSystem.getInstance().refreshAndFindFileByIoFile(tempFile)
                ?: return@invokeLater
            FileEditorManager.getInstance(project).openFile(vFile, true)
        }
    }

    private fun parseQueryParams(query: String): Map<String, String> {
        if (query.isBlank()) return emptyMap()
        return query.split("&").mapNotNull { pair ->
            val idx = pair.indexOf('=')
            if (idx < 0) null
            else URLDecoder.decode(pair.substring(0, idx), "UTF-8") to
                    URLDecoder.decode(pair.substring(idx + 1), "UTF-8")
        }.toMap()
    }
}

private data class QueryLensMetadata(
    val sourceExpression: String,
    val executedExpression: String,
    val sourceFile: String,
    val sourceLine: Int,
    val dbContextType: String,
    val providerName: String,
    val creationStrategy: String,
    val enrichedSql: String = "",
)
