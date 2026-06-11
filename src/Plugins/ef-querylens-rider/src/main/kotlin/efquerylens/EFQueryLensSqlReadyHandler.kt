package efquerylens

import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.LocalFileSystem
import java.util.concurrent.ConcurrentHashMap

internal object EFQueryLensSqlReadyHandler {
    private const val DEDUPE_WINDOW_MS: Long = 30_000
    private const val ACTION_GO_TO_QUERY = "Go to Query"
    private const val ACTION_OPEN_SQL = "Open SQL"

    private val recentNotifications = ConcurrentHashMap<String, Long>()

    fun handle(
        project: Project,
        payload: Map<*, *>,
    ) {
        val fileUri = payload["fileUri"]?.toString() ?: payload["FileUri"]?.toString()
        if (fileUri.isNullOrBlank()) {
            thisLogger().warn("[EFQueryLens] sqlReady ignored: missing fileUri")
            return
        }

        val line = (payload["line"] as? Number)?.toInt() ?: (payload["Line"] as? Number)?.toInt() ?: 0
        val character = (payload["character"] as? Number)?.toInt() ?: (payload["Character"] as? Number)?.toInt() ?: 0
        val fileName = payload["fileName"]?.toString() ?: payload["FileName"]?.toString() ?: "query"

        if (!EFQueryLensSettingsService.getInstance(project).notifyWhenSqlReady) {
            thisLogger().info("[EFQueryLens] sqlReady suppressed by setting file=$fileName")
            return
        }

        val commandCount =
            (payload["commandCount"] as? Number)?.toInt()
                ?: (payload["CommandCount"] as? Number)?.toInt()
                ?: 0
        if (commandCount <= 0) {
            thisLogger().info("[EFQueryLens] sqlReady ignored: commandCount=$commandCount file=$fileName")
            return
        }

        val key = "$fileUri|$line|$character"
        val now = System.currentTimeMillis()
        val lastShown = recentNotifications[key]
        if (lastShown != null && now - lastShown < DEDUPE_WINDOW_MS) {
            thisLogger().info("[EFQueryLens] sqlReady deduped file=$fileName line=${line + 1}")
            return
        }
        recentNotifications[key] = now

        val lineNumber = line + 1
        val title = "SQL ready"
        val message = "$fileName:$lineNumber"
        thisLogger().info("[EFQueryLens] sqlReady showing notification file=$fileName line=$lineNumber commands=$commandCount")

        ApplicationManager.getApplication().invokeLater {
            if (project.isDisposed) {
                return@invokeLater
            }

            showSqlReadyNotification(project, title, message, fileUri, line, character)
        }
    }

    private fun showSqlReadyNotification(
        project: Project,
        title: String,
        message: String,
        fileUri: String,
        line: Int,
        character: Int,
    ) {
        NotificationGroupManager
            .getInstance()
            .getNotificationGroup("EF QueryLens")
            .createNotification(title, message, NotificationType.INFORMATION)
            .setImportant(true)
            .addAction(
                NotificationAction.create(ACTION_GO_TO_QUERY) { _, _ ->
                    revealQuerySource(project, fileUri, line, character)
                },
            ).addAction(
                NotificationAction.create(ACTION_OPEN_SQL) { _, _ ->
                    openSqlAtQuery(project, fileUri, line, character)
                },
            ).notify(project)
    }

    private fun revealQuerySource(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
    ) {
        val path = runCatching { java.net.URI(fileUri).path }.getOrNull() ?: return
        val virtualFile = LocalFileSystem.getInstance().findFileByPath(path) ?: return
        val opener = EFQueryLensUrlOpener()
        opener.openFileAtPosition(project, virtualFile, line, character)
    }

    private fun openSqlAtQuery(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
    ) {
        val opener = EFQueryLensUrlOpener()
        val preview = opener.requestStructuredHoverPreview(project, fileUri, line, character)
        if (preview != null) {
            opener.openSqlInEditor(project, preview)
            return
        }

        revealQuerySource(project, fileUri, line, character)
    }
}
