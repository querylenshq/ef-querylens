package efquerylens

import com.intellij.ide.BrowserUtil
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.openapi.wm.WindowManager
import com.intellij.ui.content.ContentFactory
import com.intellij.util.Alarm
import java.awt.BorderLayout
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.ArrayDeque
import javax.swing.JButton
import javax.swing.JLabel
import javax.swing.JPanel
import javax.swing.JScrollPane
import javax.swing.JTextArea
import kotlin.io.path.absolutePathString
import kotlin.io.path.exists
import kotlin.io.path.isDirectory

class EFQueryLensLogToolWindowFactory : ToolWindowFactory, DumbAware {
    override fun shouldBeAvailable(project: Project): Boolean = true

    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val contentPanel = JPanel(BorderLayout())
        val toolbar = JPanel()
        val statusLabel = JLabel("Initializing EF QueryLens logs...")
        val textArea = JTextArea().apply {
            isEditable = false
            lineWrap = false
            wrapStyleWord = false
        }

        val refreshButton = JButton("Refresh")
        val openFolderButton = JButton("Open Log Folder")
        val openOutputButton = JButton("Open Output")

        toolbar.add(refreshButton)
        toolbar.add(openFolderButton)
        toolbar.add(openOutputButton)

        contentPanel.add(toolbar, BorderLayout.NORTH)
        contentPanel.add(JScrollPane(textArea), BorderLayout.CENTER)
        contentPanel.add(statusLabel, BorderLayout.SOUTH)

        val content = ContentFactory.getInstance().createContent(contentPanel, "", false)
        toolWindow.contentManager.addContent(content)

        val alarm = Alarm(Alarm.ThreadToUse.POOLED_THREAD, content)
        val formatter = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")

        fun refreshNow() {
            val basePath = project.basePath ?: return
            val logFile = resolveWorkspaceLogFile(basePath)
            val logText = readLogTail(logFile, maxLines = 800)
            val status = buildString {
                append("Log: ")
                append(logFile.absolutePathString())
                if (Files.exists(logFile)) {
                    val modified = Files.getLastModifiedTime(logFile).toInstant()
                    append(" | Updated: ")
                    append(formatter.format(modified.atZone(ZoneId.systemDefault())))
                } else {
                    append(" | Waiting for log file...")
                }
            }

            ApplicationManager.getApplication().invokeLater {
                if (project.isDisposed) {
                    return@invokeLater
                }

                textArea.text = logText
                textArea.caretPosition = textArea.document.length
                statusLabel.text = status
            }
        }

        fun schedulePolling() {
            if (project.isDisposed) {
                return
            }

            alarm.addRequest({
                refreshNow()
                schedulePolling()
            }, 1200)
        }

        refreshButton.addActionListener { refreshNow() }
        openFolderButton.addActionListener {
            val folder = logFolderPath()
            BrowserUtil.browse(folder.toUri())
        }
        openOutputButton.addActionListener {
            WindowManager.getInstance().getStatusBar(project)
            toolWindow.activate(null)
        }

        refreshNow()
        schedulePolling()
    }

    private fun resolveWorkspaceLogFile(projectBasePath: String): Path {
        val workspace = resolveWorkspacePath(projectBasePath)
        val hash = hashWorkspacePath(workspace.absolutePathString())
        return logFolderPath().resolve("lsp-$hash.log")
    }

    private fun resolveWorkspacePath(projectBasePath: String): Path {
        val projectPath = Path.of(projectBasePath).toAbsolutePath().normalize()

        val envRepositoryRoot = System.getenv("QUERYLENS_REPOSITORY_ROOT")
        if (!envRepositoryRoot.isNullOrBlank()) {
            val envPath = Path.of(envRepositoryRoot).toAbsolutePath().normalize()
            val hasLspProject = envPath
                .resolve("src")
                .resolve("EFQueryLens.Lsp")
                .resolve("EFQueryLens.Lsp.csproj")
                .exists()
            if (hasLspProject) {
                return envPath
            }
        }

        var current: Path? = projectPath
        while (current != null) {
            val hasSolution = current.resolve("EFQueryLens.slnx").exists()
            val hasLspProject = current
                .resolve("src")
                .resolve("EFQueryLens.Lsp")
                .resolve("EFQueryLens.Lsp.csproj")
                .exists()

            if (hasSolution && hasLspProject) {
                return current
            }

            current = current.parent
        }

        return projectPath
    }

    private fun hashWorkspacePath(path: String): String {
        val bytes = MessageDigest.getInstance("SHA-256").digest(path.toByteArray(StandardCharsets.UTF_8))
        return bytes.joinToString("") { "%02x".format(it) }.take(16)
    }

    private fun logFolderPath(): Path =
        Path.of(System.getProperty("java.io.tmpdir"), "EFQueryLens", "rider-logs")

    private fun readLogTail(logFile: Path, maxLines: Int): String {
        if (!Files.exists(logFile)) {
            return "EF QueryLens log file does not exist yet.\n\nExpected path:\n${logFile.absolutePathString()}"
        }

        val ring = ArrayDeque<String>(maxLines)
        Files.newBufferedReader(logFile, StandardCharsets.UTF_8).use { reader ->
            var line = reader.readLine()
            while (line != null) {
                if (ring.size >= maxLines) {
                    ring.removeFirst()
                }
                ring.addLast(line)
                line = reader.readLine()
            }
        }

        return ring.joinToString("\n")
    }
}