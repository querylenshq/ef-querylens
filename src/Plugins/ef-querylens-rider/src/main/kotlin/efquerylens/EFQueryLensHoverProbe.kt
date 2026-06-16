package efquerylens

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.editor.LogicalPosition
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import java.util.concurrent.ConcurrentHashMap

internal enum class HoverProbeOutcome {
    Queued,
    Ready,
    Other,
}

internal object EFQueryLensHoverProbe {
    private const val STATUS_READY = 0
    private const val STATUS_IN_QUEUE = 1
    private const val PROBE_THROTTLE_MS = 500L
    private const val TERMINAL_MOUSE_COOLDOWN_MS = 5_000L

    private val lastProbeAt = ConcurrentHashMap<String, Long>()
    private val terminalMouseCooldownUntil = ConcurrentHashMap<String, Long>()

    internal fun fileUri(file: VirtualFile): String =
        normalizeFileUri(
            runCatching { file.toNioPath().toUri().toString() }
                .getOrElse { file.url },
        )

    internal fun normalizeFileUri(fileUri: String): String {
        val normalizedSlashes = fileUri.replace('\\', '/')
        return if (
            normalizedSlashes.length >= "file:///C:".length &&
            normalizedSlashes.startsWith("file:///", ignoreCase = true) &&
            normalizedSlashes[8].isUpperCase() &&
            normalizedSlashes.getOrNull(9) == ':'
        ) {
            normalizedSlashes.replaceRange(8, 9, normalizedSlashes[8].lowercase())
        } else {
            normalizedSlashes
        }
    }

    internal fun probeAtCaret(
        project: Project,
        editor: Editor,
        force: Boolean = false,
    ) {
        val caret = editor.caretModel.currentCaret
        probeAt(project, editor, caret.logicalPosition, force)
    }

    internal fun probeAt(
        project: Project,
        editor: Editor,
        position: LogicalPosition,
        force: Boolean = false,
    ) {
        val file = editor.virtualFile ?: return
        if (!file.extension.equals("cs", ignoreCase = true)) {
            return
        }

        probe(project, fileUri(file), position.line, position.column, force)
    }

    internal fun probeAtLine(
        project: Project,
        editor: Editor,
        position: LogicalPosition,
        force: Boolean = false,
    ) {
        val file = editor.virtualFile ?: return
        if (!file.extension.equals("cs", ignoreCase = true)) {
            return
        }

        probe(
            project,
            fileUri(file),
            position.line,
            position.column,
            force,
            throttleCharacter = 0,
            useTerminalCooldown = true,
        )
    }

    internal fun probe(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
        force: Boolean = false,
        throttleCharacter: Int = character,
        useTerminalCooldown: Boolean = false,
    ) {
        if (project.isDisposed || !EFQueryLensSettingsService.getInstance(project).notifyWhenSqlReady) {
            return
        }

        val normalizedFileUri = normalizeFileUri(fileUri)
        val safeLine = line.coerceAtLeast(0)
        val safeCharacter = character.coerceAtLeast(0)
        val throttleKey = buildKey(normalizedFileUri, safeLine, throttleCharacter.coerceAtLeast(0))
        val cooldownKey = buildLineKey(normalizedFileUri, safeLine)
        if (useTerminalCooldown && !force && isTerminalCooldownActive(cooldownKey, System.currentTimeMillis())) {
            thisLogger().info("[EFQueryLens] hover-probe skipped key=$cooldownKey reason=terminal-cooldown")
            return
        }

        if (!force && isThrottled(throttleKey, System.currentTimeMillis())) {
            return
        }

        ApplicationManager.getApplication().executeOnPooledThread {
            val hover =
                EFQueryLensLspRequests.requestStructuredHover(
                    project,
                    normalizedFileUri,
                    safeLine,
                    safeCharacter,
                    startSqlReadyWatch = true,
                ) ?: return@executeOnPooledThread
            if (useTerminalCooldown && isTerminalWithoutSql(hover)) {
                rememberTerminalCooldown(cooldownKey, System.currentTimeMillis())
            }
            thisLogger().info("[EFQueryLens] hover-probe status=${readStatus(hover)} key=$throttleKey")
        }
    }

    internal fun handleStructuredHover(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
        hover: Map<String, Any?>?,
    ) {
        val normalizedFileUri = normalizeFileUri(fileUri)
        val safeLine = line.coerceAtLeast(0)
        val safeCharacter = character.coerceAtLeast(0)
        when (classify(hover)) {
            HoverProbeOutcome.Queued ->
                EFQueryLensSqlReadyHoverWatcher.watchIfQueued(
                    project,
                    normalizedFileUri,
                    safeLine,
                    safeCharacter,
                    STATUS_IN_QUEUE,
                )
            HoverProbeOutcome.Ready ->
                EFQueryLensSqlReadyHoverWatcher.cancelWatch(normalizedFileUri, safeLine, safeCharacter)
            HoverProbeOutcome.Other -> Unit
        }
    }

    internal fun classify(hover: Map<String, Any?>?): HoverProbeOutcome =
        when (readStatus(hover)) {
            STATUS_IN_QUEUE -> HoverProbeOutcome.Queued
            STATUS_READY -> HoverProbeOutcome.Ready
            else -> HoverProbeOutcome.Other
        }

    internal fun readStatus(hover: Map<String, Any?>?): Int =
        (hover?.get("Status") as? Number)?.toInt()
            ?: (hover?.get("status") as? Number)?.toInt()
            ?: -1

    internal fun isTerminalWithoutSql(hover: Map<String, Any?>?): Boolean {
        if (readStatus(hover) != STATUS_READY) {
            return false
        }

        val success = hover?.get("Success") as? Boolean ?: hover?.get("success") as? Boolean ?: false
        val commandCount =
            (hover?.get("CommandCount") as? Number)?.toInt()
                ?: (hover?.get("commandCount") as? Number)?.toInt()
                ?: 0
        return !success || commandCount <= 0
    }

    internal fun isThrottled(
        key: String,
        nowMs: Long,
    ): Boolean {
        val previous = lastProbeAt[key]
        if (previous != null && nowMs - previous < PROBE_THROTTLE_MS) {
            return true
        }

        lastProbeAt[key] = nowMs
        return false
    }

    internal fun resetThrottleForTests() {
        lastProbeAt.clear()
        terminalMouseCooldownUntil.clear()
    }

    internal fun buildKey(
        fileUri: String,
        line: Int,
        character: Int,
    ): String = "${normalizeFileUri(fileUri)}|${line.coerceAtLeast(0)}|${character.coerceAtLeast(0)}"

    internal fun buildLineKey(
        fileUri: String,
        line: Int,
    ): String = "${normalizeFileUri(fileUri)}|${line.coerceAtLeast(0)}"

    internal fun rememberTerminalCooldown(
        key: String,
        nowMs: Long,
    ) {
        terminalMouseCooldownUntil[key] = nowMs + TERMINAL_MOUSE_COOLDOWN_MS
    }

    internal fun isTerminalCooldownActive(
        key: String,
        nowMs: Long,
    ): Boolean {
        val expiresAt = terminalMouseCooldownUntil[key] ?: return false
        if (nowMs > expiresAt) {
            terminalMouseCooldownUntil.remove(key)
            return false
        }

        return true
    }
}
