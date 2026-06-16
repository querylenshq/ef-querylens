package efquerylens

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.project.Project
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.util.concurrent.ConcurrentHashMap

internal object EFQueryLensSqlReadyHoverWatcher {
    private const val POLL_INTERVAL_MS = 200L
    private const val DEFAULT_NOTIFICATION_WAIT_MS = 120_000
    private const val MAX_NOTIFICATION_WAIT_MS = 120_000L
    private const val STATUS_READY = 0
    private const val STATUS_IN_QUEUE = 1
    private const val STATUS_DAEMON_UNAVAILABLE = 3

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val activeWatches = ConcurrentHashMap.newKeySet<String>()
    private val watchJobs = ConcurrentHashMap<String, Job>()

    fun cancelWatch(
        fileUri: String,
        line: Int,
        character: Int,
    ) {
        val key = buildKey(fileUri, line, character)
        watchJobs.remove(key)?.cancel()
        activeWatches.remove(key)
        thisLogger().info("[EFQueryLens] sql-ready-watch-cancelled key=$key")
    }

    fun watchIfQueued(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
        status: Int,
    ) {
        if (status != STATUS_IN_QUEUE) {
            return
        }

        if (!EFQueryLensSettingsService.getInstance(project).notifyWhenSqlReady) {
            return
        }

        val key = buildKey(fileUri, line, character)
        if (!activeWatches.add(key)) {
            thisLogger().info("[EFQueryLens] sql-ready-watch-coalesced key=$key")
            return
        }

        watchJobs.remove(key)?.cancel()
        thisLogger().info("[EFQueryLens] sql-ready-watch-started key=$key")

        val waitBudgetMs =
            computeNotificationWaitMs(
                EFQueryLensSettingsService.getInstance(project).hoverWaitWhenWarmMs,
            )

        val job =
            scope.launch {
                try {
                    runWatch(project, fileUri, line, character, waitBudgetMs)
                } finally {
                    activeWatches.remove(key)
                    watchJobs.remove(key)
                }
            }
        watchJobs[key] = job
    }

    private suspend fun runWatch(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
        waitBudgetMs: Long,
    ) {
        val key = buildKey(fileUri, line, character)
        val deadline = System.currentTimeMillis() + waitBudgetMs
        var sawInQueue = true

        while (scope.isActive) {
            val hover =
                EFQueryLensLspRequests.requestStructuredHover(
                    project,
                    fileUri,
                    line,
                    character,
                    startSqlReadyWatch = false,
                )
            if (hover == null) {
                thisLogger().info("[EFQueryLens] sql-ready-watch-exit key=$key reason=null-response")
                return
            }

            val status = (hover["Status"] as? Number)?.toInt() ?: (hover["status"] as? Number)?.toInt() ?: -1
            val success = hover["Success"] as? Boolean ?: hover["success"] as? Boolean ?: false
            val commandCount =
                (hover["CommandCount"] as? Number)?.toInt()
                    ?: (hover["commandCount"] as? Number)?.toInt()
                    ?: 0

            when {
                status == STATUS_IN_QUEUE -> sawInQueue = true
                status == STATUS_READY || status == STATUS_DAEMON_UNAVAILABLE -> {
                    if (sawInQueue && status == STATUS_READY && success && commandCount > 0) {
                        thisLogger().info("[EFQueryLens] sql-ready-watch-ready key=$key commands=$commandCount")
                        val fileName =
                            (hover["FileName"] as? String)
                                ?: (hover["fileName"] as? String)
                                ?: fileUri.substringAfterLast('/')
                        val notificationLine =
                            ((hover["SourceLine"] as? Number)?.toInt()
                                ?: (hover["sourceLine"] as? Number)?.toInt())
                                ?.minus(1)
                                ?.coerceAtLeast(0)
                                ?: line
                        val payload =
                            mapOf(
                                "fileUri" to fileUri,
                                "line" to notificationLine,
                                "character" to character,
                                "fileName" to fileName,
                                "commandCount" to commandCount,
                            )
                        ApplicationManager.getApplication().invokeLater {
                            EFQueryLensSqlReadyHandler.handle(project, payload)
                        }
                    }
                    return
                }
                else -> return
            }

            if (System.currentTimeMillis() >= deadline) {
                thisLogger().info("[EFQueryLens] sql-ready-watch-timeout key=$key budgetMs=$waitBudgetMs status=$status")
                return
            }

            delay(POLL_INTERVAL_MS)
        }
    }

    private fun computeNotificationWaitMs(hoverWaitWhenWarmMs: Int): Long {
        val budget = maxOf(hoverWaitWhenWarmMs, DEFAULT_NOTIFICATION_WAIT_MS)
        return minOf(maxOf(budget.toLong(), 500L), MAX_NOTIFICATION_WAIT_MS)
    }

    private fun buildKey(
        fileUri: String,
        line: Int,
        character: Int,
    ): String = "$fileUri|$line|$character"
}
