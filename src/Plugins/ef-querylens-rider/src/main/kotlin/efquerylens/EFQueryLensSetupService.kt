package efquerylens

import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.LocalFileSystem
import java.io.File
import com.intellij.openapi.ui.popup.JBPopupFactory
import com.intellij.openapi.ui.popup.JBPopupListener
import com.intellij.openapi.ui.popup.LightweightWindowEvent
import com.intellij.openapi.ui.popup.PopupStep
import com.intellij.openapi.ui.popup.util.BaseListPopupStep
import java.util.concurrent.CountDownLatch
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

internal object EFQueryLensSetupService {
    private val setupInProgress = AtomicBoolean(false)

    private data class HostPopupItem(
        val host: SetupHostCandidate,
    ) {
        val label: String = host.displayName
        val description: String = host.projectPath
    }

    private data class ProviderPopupItem(
        val label: String,
        val provider: String,
    )

    private val providerOptions =
        listOf(
            ProviderPopupItem("SQL Server", "SqlServer"),
            ProviderPopupItem("PostgreSQL (Npgsql)", "Npgsql"),
            ProviderPopupItem("MySQL (Pomelo)", "MySql"),
            ProviderPopupItem("SQLite", "Sqlite"),
        )

    fun run(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
    ) {
        if (!setupInProgress.compareAndSet(false, true)) {
            showNotification(project, "EF QueryLens: setup is already running.", NotificationType.INFORMATION)
            return
        }

        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                runCore(project, fileUri, line, character)
            } catch (e: Exception) {
                thisLogger().warn("[EFQueryLens] setup failed", e)
                showNotification(
                    project,
                    "EF QueryLens: setup failed. ${e.message ?: "Unknown error"}",
                    NotificationType.ERROR,
                )
            } finally {
                setupInProgress.set(false)
            }
        }
    }

    private fun runCore(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
    ) {
        val detect = EFQueryLensLspRequests.requestSetupDetect(project, fileUri, line, character)
        if (detect == null) {
            showNotification(
                project,
                "EF QueryLens: language server is not ready yet.",
                NotificationType.WARNING,
            )
            return
        }

        if (!detect.message.isNullOrBlank() && detect.hosts.isEmpty()) {
            showNotification(project, "EF QueryLens: ${detect.message}", NotificationType.WARNING)
            return
        }

        var hostProjectPath = detect.defaultHostProjectPath
        if (detect.requiresHostSelection) {
            val picked = pickHostOnEdt(project, detect.hosts) ?: return
            hostProjectPath = picked
        }

        runApply(project, fileUri, hostProjectPath, provider = null)
    }

    private fun runApply(
        project: Project,
        fileUri: String,
        hostProjectPath: String?,
        provider: String?,
    ) {
        val apply =
            EFQueryLensLspRequests.requestSetupApply(
                project,
                fileUri,
                hostProjectPath,
                provider,
            ) ?: run {
                showNotification(
                    project,
                    "EF QueryLens: setup apply did not complete.",
                    NotificationType.WARNING,
                )
                return
            }

        if (apply.action.equals("NeedProvider", ignoreCase = true)) {
            val selectedProvider = pickProviderOnEdt(project) ?: return
            runApply(project, fileUri, hostProjectPath, selectedProvider)
            return
        }

        if (!apply.success) {
            showNotification(project, "EF QueryLens: ${apply.message}", NotificationType.WARNING)
            return
        }

        val openedFactory = openGeneratedFactory(project, apply.generatedFilePath)
        val message =
            if (openedFactory) {
                buildFactoryOpenedMessage(apply.requiresReview)
            } else {
                "EF QueryLens: ${apply.message}"
            }
        val type =
            if (apply.requiresReview) {
                NotificationType.WARNING
            } else {
                NotificationType.INFORMATION
            }
        showNotification(project, message, type)
    }

    private fun openGeneratedFactory(
        project: Project,
        generatedFilePath: String?,
    ): Boolean {
        if (generatedFilePath.isNullOrBlank()) {
            return false
        }

        val file = File(generatedFilePath)
        if (!file.isFile) {
            return false
        }

        val latch = CountDownLatch(1)
        val opened = AtomicBoolean(false)

        ApplicationManager.getApplication().invokeLater {
            try {
                val virtualFile =
                    LocalFileSystem.getInstance().refreshAndFindFileByIoFile(file)
                        ?: run {
                            thisLogger().warn("[EFQueryLens] Could not resolve virtual file for $generatedFilePath")
                            return@invokeLater
                        }
                FileEditorManager.getInstance(project).openFile(virtualFile, true)
                opened.set(true)
            } catch (e: Exception) {
                thisLogger().warn("[EFQueryLens] openGeneratedFactory failed", e)
            } finally {
                latch.countDown()
            }
        }

        try {
            latch.await()
        } catch (_: InterruptedException) {
            return false
        }

        return opened.get()
    }

    private fun buildFactoryOpenedMessage(requiresReview: Boolean): String {
        val message =
            "EF QueryLens: Factory opened — rebuild the project, then confirm each CreateOfflineContext()."
        return if (requiresReview) {
            "$message Review best-effort defaults if any DbContext did not match AddDbContext."
        } else {
            message
        }
    }

    private fun pickHostOnEdt(
        project: Project,
        hosts: List<SetupHostCandidate>,
    ): String? {
        if (hosts.isEmpty()) {
            return null
        }

        if (hosts.size == 1) {
            return hosts.first().projectPath
        }

        val selection = AtomicReference<String?>(null)
        val latch = CountDownLatch(1)

        ApplicationManager.getApplication().invokeAndWait {
            val step =
                object : BaseListPopupStep<HostPopupItem>(
                    "Select the executable host project for the QueryLens factory",
                    hosts.map { HostPopupItem(it) },
                ) {
                    override fun getTextFor(value: HostPopupItem): String = value.label

                    override fun onChosen(
                        selectedValue: HostPopupItem,
                        finalChoice: Boolean,
                    ): PopupStep<*>? {
                        selection.set(selectedValue.host.projectPath)
                        return FINAL_CHOICE
                    }
                }

            val popup =
                JBPopupFactory
                    .getInstance()
                    .createListPopup(step)
            popup.addListener(
                object : JBPopupListener {
                    override fun onClosed(event: LightweightWindowEvent) {
                        latch.countDown()
                    }
                },
            )
            popup.showCenteredInCurrentWindow(project)
        }

        try {
            latch.await()
        } catch (_: InterruptedException) {
            return null
        }

        return selection.get()
    }

    private fun pickProviderOnEdt(project: Project): String? {
        val selection = AtomicReference<String?>(null)
        val latch = CountDownLatch(1)

        ApplicationManager.getApplication().invokeAndWait {
            val step =
                object : BaseListPopupStep<ProviderPopupItem>(
                    "Select the EF Core provider for the generated factory",
                    providerOptions,
                ) {
                    override fun getTextFor(value: ProviderPopupItem): String = value.label

                    override fun onChosen(
                        selectedValue: ProviderPopupItem,
                        finalChoice: Boolean,
                    ): PopupStep<*>? {
                        selection.set(selectedValue.provider)
                        return FINAL_CHOICE
                    }
                }

            val popup =
                JBPopupFactory
                    .getInstance()
                    .createListPopup(step)
            popup.addListener(
                object : JBPopupListener {
                    override fun onClosed(event: LightweightWindowEvent) {
                        latch.countDown()
                    }
                },
            )
            popup.showCenteredInCurrentWindow(project)
        }

        try {
            latch.await()
        } catch (_: InterruptedException) {
            return null
        }

        return selection.get()
    }

    private fun showNotification(
        project: Project,
        message: String,
        type: NotificationType,
    ) {
        ApplicationManager.getApplication().invokeLater {
            NotificationGroupManager
                .getInstance()
                .getNotificationGroup("EF QueryLens")
                .createNotification(message, type)
                .notify(project)
        }
    }
}
