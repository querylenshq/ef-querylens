package efquerylens

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.application.PathManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.ide.CopyPasteManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.google.gson.Gson
import com.google.gson.JsonElement
import com.intellij.platform.lsp.api.Lsp4jClient
import com.intellij.platform.lsp.api.LspServerNotificationsHandler
import com.intellij.platform.lsp.api.LspServerSupportProvider
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor
import org.eclipse.lsp4j.jsonrpc.services.JsonNotification
import org.eclipse.lsp4j.services.LanguageServer
import java.awt.datatransfer.StringSelection
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.attribute.PosixFilePermission
import kotlin.io.path.absolutePathString
import kotlin.io.path.exists
import kotlin.io.path.isDirectory
import kotlin.io.path.isRegularFile
import kotlin.io.path.name
import kotlin.io.path.pathString

class EFQueryLensLspServerSupportProvider : LspServerSupportProvider {
    override fun fileOpened(
        project: Project,
        file: VirtualFile,
        serverStarter: LspServerSupportProvider.LspServerStarter,
    ) {
        logInfo(project, "[EFQueryLens] fileOpened path='${file.path}' extension='${file.extension}'")
        if (!isSupported(file)) {
            logInfo(project, "[EFQueryLens] fileOpened skipped unsupported file '${file.path}'")
            return
        }

        logInfo(project, "[EFQueryLens] Ensuring LSP server is started for '${file.path}'")
        EFQueryLensQuickDocSqlReadyHook.register(project)
        EFQueryLensEditorHoverSqlReadyHook.register(project)
        serverStarter.ensureServerStarted(EFQueryLensServerDescriptor(project))
    }

    private fun isSupported(file: VirtualFile): Boolean = file.extension.equals("cs", ignoreCase = true)

    private fun logInfo(
        project: Project,
        message: String,
    ) {
        thisLogger().info(message)
    }

    private fun logWarn(
        project: Project,
        message: String,
        error: Throwable? = null,
    ) {
        if (error == null) {
            thisLogger().warn(message)
            return
        }
        thisLogger().warn(message, error)
    }
}

private class EFQueryLensServerDescriptor(
    private val hostProject: Project,
) : ProjectWideLspServerDescriptor(hostProject, "EF QueryLens") {
    private companion object {
        private const val LSP_DLL_OVERRIDE_ENV_VAR = "QUERYLENS_LSP_DLL"
    }

    override fun isSupportedFile(file: VirtualFile): Boolean = file.extension.equals("cs", ignoreCase = true)

    override val lsp4jServerClass: Class<out LanguageServer>
        get() = EFQueryLensLspServer::class.java

    override fun createLsp4jClient(handler: LspServerNotificationsHandler): Lsp4jClient = EFQueryLensClient(handler, hostProject)

    override fun createInitializationOptions(): JsonElement? =
        Gson().toJsonTree(EFQueryLensLspConfiguration.buildInitializationOptions(hostProject))

    override fun createCommandLine(): GeneralCommandLine {
        val projectBasePath =
            hostProject.basePath
                ?: error("Cannot start EF QueryLens language server: project has no base path.")

        val logIdentity = WorkspaceLogIdentityResolver.fromProjectBasePath(projectBasePath)
        val workspaceRoot = Path.of(projectBasePath).toAbsolutePath().normalize()
        val lspLogFilePath = logIdentity.logFilePath

        logInfo(
            "[EFQueryLens] log identity workspace='${logIdentity.workspacePath.absolutePathString()}' hash='${logIdentity.hash}' file='${logIdentity.logFilePath.absolutePathString()}'",
        )

        val lspDllOverride = resolveLspDllOverride()
        if (lspDllOverride != null) {
            logInfo("[EFQueryLens] Starting EF QueryLens LSP from override '${lspDllOverride.pathString}'")
            return GeneralCommandLine("dotnet", lspDllOverride.pathString)
                .withWorkDirectory(workspaceRoot.toFile())
                .applyQueryLensEnvironment(workspaceRoot, lspLogFilePath)
        }

        val runtimeResolution = resolvePackagedLspRuntime()
        val lspDll = runtimeResolution.lspDll
        if (lspDll == null) {
            error(
                "Cannot locate EFQueryLens packaged runtime (server/EFQueryLens.Lsp.dll). " +
                    "Set $LSP_DLL_OVERRIDE_ENV_VAR to override. ${runtimeResolution.diagnostics}",
            )
        }

        logInfo("[EFQueryLens] Starting EF QueryLens LSP from packaged runtime '${lspDll.pathString}'")

        return GeneralCommandLine("dotnet", lspDll.pathString)
            .withWorkDirectory(workspaceRoot.toFile())
            .applyQueryLensEnvironment(workspaceRoot, lspLogFilePath)
    }

    private fun resolveLspDllOverride(): Path? {
        val raw = System.getenv(LSP_DLL_OVERRIDE_ENV_VAR)
        if (raw.isNullOrBlank()) {
            logInfo("[EFQueryLens] $LSP_DLL_OVERRIDE_ENV_VAR is not set")
            return null
        }

        logInfo("[EFQueryLens] $LSP_DLL_OVERRIDE_ENV_VAR raw='$raw'")
        val candidate = Path.of(raw).toAbsolutePath().normalize()
        return if (candidate.isRegularFile()) {
            logInfo("[EFQueryLens] $LSP_DLL_OVERRIDE_ENV_VAR resolved='$candidate'")
            candidate
        } else {
            logWarn("[EFQueryLens] $LSP_DLL_OVERRIDE_ENV_VAR path does not exist: '$candidate'")
            null
        }
    }

    private data class PackagedRuntimeResolution(
        val lspDll: Path?,
        val diagnostics: String,
    )

    private fun resolvePackagedLspRuntime(): PackagedRuntimeResolution {
        val serverDirs = resolvePackagedRuntimeRoots().map { it.resolve("server") }.distinct()
        val candidates =
            serverDirs.flatMap { serverDir ->
                listOf(
                    serverDir.resolve("EFQueryLens.Lsp.dll"),
                    serverDir.resolve("publish").resolve("EFQueryLens.Lsp.dll"),
                )
            }
        val lspDll = candidates.firstOrNull { it.exists() && it.isRegularFile() }
        return PackagedRuntimeResolution(
            lspDll,
            buildString {
                append("codeSource='")
                append(resolveCodeSourcePath()?.pathString ?: "<unavailable>")
                append("'; pluginRoot='")
                append(resolvePluginRoot()?.pathString ?: "<unavailable>")
                append("'; pluginsDir='")
                append(resolvePluginsDirPath()?.pathString ?: "<unavailable>")
                append("'; runtimeRoots=[")
                append(resolvePackagedRuntimeRoots().joinToString { "'${it.pathString}'" })
                append("]; checked=[")
                append(candidates.joinToString { "'${it.pathString}' exists=${it.exists()} file=${it.isRegularFile()}" })
                append("]")
            },
        )
    }

    private fun resolveCodeSourcePath(): Path? =
        try {
            val location =
                EFQueryLensLspServerSupportProvider::class.java.protectionDomain.codeSource
                    ?.location ?: return null
            Path.of(location.toURI()).toAbsolutePath().normalize()
        } catch (e: Exception) {
            null
        }

    private fun resolvePluginRoot(): Path? =
        try {
            val codeSourcePath = resolveCodeSourcePath() ?: return null
            val start = if (codeSourcePath.isRegularFile()) codeSourcePath.parent else codeSourcePath
            var current: Path? = start
            while (current != null) {
                if (current.resolve("server").isDirectory() || current.resolve("daemon").isDirectory()) {
                    return current
                }
                if (current.name.equals("lib", ignoreCase = true)) {
                    return current.parent
                }
                current = current.parent
            }
            start
        } catch (e: Exception) {
            null
        }

    private fun resolvePluginsDirPath(): Path? =
        try {
            PathManager.getPluginsDir().toAbsolutePath().normalize()
        } catch (e: Exception) {
            null
        }

    private fun resolvePackagedRuntimeRoots(): List<Path> {
        val roots = linkedSetOf<Path>()

        resolvePluginRoot()?.let { pluginRoot ->
            roots.add(pluginRoot)
            pluginRoot.parent?.let { roots.add(it) }

            runCatching {
                Files.list(pluginRoot).use { children ->
                    children
                        .filter { it.isDirectory() }
                        .filter { it.resolve("server").isDirectory() || it.resolve("daemon").isDirectory() }
                        .forEach { roots.add(it) }
                }
            }
        }

        roots.addAll(resolveRuntimeRootsFromPluginsDir())

        return roots.toList()
    }

    private fun resolveRuntimeRootsFromPluginsDir(): List<Path> {
        val pluginsDir = resolvePluginsDirPath() ?: return emptyList()
        if (!pluginsDir.isDirectory()) {
            return emptyList()
        }

        val roots = linkedSetOf<Path>()
        runCatching {
            Files.walk(pluginsDir, 5).use { paths ->
                paths
                    .filter { it.name == "EFQueryLens.Lsp.dll" }
                    .filter { it.parent?.name.equals("server", ignoreCase = true) }
                    .map { it.parent.parent }
                    .filter { it != null }
                    .forEach { roots.add(it) }
            }
        }.onFailure { error ->
            logWarn("[EFQueryLens] Could not scan plugins directory for packaged runtime '$pluginsDir'", error)
        }

        return roots.toList()
    }

    private fun GeneralCommandLine.applyQueryLensEnvironment(
        workspaceRoot: Path,
        lspLogFilePath: Path,
    ): GeneralCommandLine {
        withEnvironment("QUERYLENS_CLIENT", "rider")
        withEnvironment("QUERYLENS_DEBUG", "1")
        withEnvironment("QUERYLENS_DAEMON_START_TIMEOUT_MS", "30000")
        withEnvironment("QUERYLENS_DAEMON_CONNECT_TIMEOUT_MS", "10000")
        withEnvironment("QUERYLENS_DAEMON_SHUTDOWN_ON_DISPOSE", "1")
        withEnvironment("QUERYLENS_AVG_WINDOW_SAMPLES", System.getenv("QUERYLENS_AVG_WINDOW_SAMPLES")?.takeIf { it.isNotBlank() } ?: "20")
        withEnvironment("QUERYLENS_LSP_LOG_FILE", lspLogFilePath.absolutePathString())
        val workspacePath = workspaceRoot.absolutePathString()
        withEnvironment("QUERYLENS_WORKSPACE", workspacePath)
        withEnvironment("QUERYLENS_DAEMON_WORKSPACE", workspacePath)

        resolvePackagedDaemonExecutable()?.let {
            ensureUnixDaemonLauncherExecutable(it)
            withEnvironment("QUERYLENS_DAEMON_EXE", it.absolutePathString())
        }
        resolvePackagedDaemonAssembly()?.let {
            ensureUnixDaemonLauncherExecutable(it)
            withEnvironment("QUERYLENS_DAEMON_DLL", it.absolutePathString())
        }

        return this
    }

    private fun ensureUnixDaemonLauncherExecutable(referencePath: Path) {
        val os = System.getProperty("os.name").lowercase()
        if (!os.contains("linux") && !os.contains("mac")) {
            return
        }

        val launcher = referencePath.parent?.resolve("EFQueryLens.Daemon") ?: return
        if (!Files.isRegularFile(launcher)) {
            return
        }

        runCatching {
            val permissions = Files.getPosixFilePermissions(launcher).toMutableSet()
            permissions.add(PosixFilePermission.OWNER_EXECUTE)
            permissions.add(PosixFilePermission.GROUP_EXECUTE)
            permissions.add(PosixFilePermission.OTHERS_EXECUTE)
            Files.setPosixFilePermissions(launcher, permissions)
        }.onFailure { error ->
            logWarn("[EFQueryLens] Could not mark daemon launcher executable at '$launcher'", error)
        }
    }

    /**
     * Returns the .NET RID string that matches the JVM's current OS and CPU architecture.
     * Used to select the correct per-platform daemon binary from inside the plugin ZIP.
     */
    private fun currentRid(): String {
        val os = System.getProperty("os.name").lowercase()
        val arch = System.getProperty("os.arch").lowercase()
        val isArm = arch == "aarch64"
        return when {
            os.contains("win") -> if (isArm) "win-arm64" else "win-x64"
            os.contains("mac") -> if (isArm) "osx-arm64" else "osx-x64"
            else -> if (isArm) "linux-arm64" else "linux-x64"
        }
    }

    private fun resolvePackagedDaemonExecutable(): Path? {
        val rid = currentRid()
        val isWindows = rid.startsWith("win")
        val exeName = if (isWindows) "EFQueryLens.Daemon.exe" else "EFQueryLens.Daemon"
        val daemonDirs = resolvePackagedRuntimeRoots().map { it.resolve("daemon") }.distinct()
        // Prefer the platform-specific AppHost inside daemon/<rid>/; fall back to root daemon dir.
        val candidates =
            daemonDirs.flatMap { daemonDir ->
                listOf(
                    daemonDir.resolve(rid).resolve(exeName),
                    daemonDir.resolve(exeName),
                )
            }
        return candidates.firstOrNull { it.exists() && it.isRegularFile() }
    }

    private fun resolvePackagedDaemonAssembly(): Path? {
        val rid = currentRid()
        val daemonDirs = resolvePackagedRuntimeRoots().map { it.resolve("daemon") }.distinct()
        // Prefer the RID-specific directory so EngineDiscovery also finds the adjacent AppHost.
        // Fall back to the root daemon dir (framework-dependent DLL without AppHost).
        val candidates =
            daemonDirs.flatMap { daemonDir ->
                listOf(
                    daemonDir.resolve(rid).resolve("EFQueryLens.Daemon.dll"),
                    daemonDir.resolve("EFQueryLens.Daemon.dll"),
                )
            }
        return candidates.firstOrNull { it.exists() && it.isRegularFile() }
    }

    private fun logInfo(message: String) = thisLogger().info(message)

    private fun logWarn(
        message: String,
        error: Throwable? = null,
    ) {
        if (error == null) thisLogger().warn(message) else thisLogger().warn(message, error)
    }
}

private class EFQueryLensClient(
    handler: LspServerNotificationsHandler,
    private val project: Project,
) : Lsp4jClient(handler) {
    private val lifecycleStarted = java.util.concurrent.atomic.AtomicBoolean(false)

    private fun ensureLifecycleStarted() {
        if (!lifecycleStarted.compareAndSet(false, true)) {
            return
        }

        ApplicationManager.getApplication().executeOnPooledThread {
            EFQueryLensLspLifecycle.onServerInitialized(project)
        }
    }

    @JsonNotification("efquerylens/showSqlPreview")
    @Suppress("UNCHECKED_CAST")
    fun showSqlPreview(payload: Any?) {
        val root = payload as? Map<String, Any?> ?: return
        val fallbackFileUri = root["fallbackFileUri"] as? String ?: ""
        val fallbackLine = (root["fallbackLine"] as? Number)?.toInt() ?: 0
        val fallbackCharacter = (root["fallbackCharacter"] as? Number)?.toInt() ?: 0

        val opener = EFQueryLensUrlOpener()
        val hover = root["hover"] as? Map<String, Any?>
        EFQueryLensHoverProbe.handleStructuredHover(project, fallbackFileUri, fallbackLine, fallbackCharacter, hover)
        val preview = opener.extractStructuredPreview(root, fallbackFileUri, fallbackLine)
        if (preview != null) {
            if (preview.statusCode != 0 || preview.actionSqlText.isBlank()) {
                val message = preview.statusMessage ?: opener.fallbackStatusMessage(preview.statusCode)
                opener.showStatusMessage(project, preview.statusCode, message)
                return
            }

            opener.openSqlInEditor(project, preview)
        }
    }

    @JsonNotification("efquerylens/showSqlPopup")
    @Suppress("UNCHECKED_CAST")
    fun showSqlPopup(payload: Any?) {
        val root = payload as? Map<String, Any?> ?: return
        val fallbackFileUri = root["fallbackFileUri"] as? String ?: ""
        val fallbackLine = (root["fallbackLine"] as? Number)?.toInt() ?: 0
        val fallbackCharacter = (root["fallbackCharacter"] as? Number)?.toInt() ?: 0
        val opener = EFQueryLensUrlOpener()
        val hover = root["hover"] as? Map<String, Any?>
        EFQueryLensHoverProbe.handleStructuredHover(project, fallbackFileUri, fallbackLine, fallbackCharacter, hover)
        val preview = opener.extractStructuredPreview(root, fallbackFileUri, fallbackLine) ?: return
        if (preview.statusCode != 0 || preview.sqlText.isBlank()) {
            val message = preview.statusMessage ?: opener.fallbackStatusMessage(preview.statusCode)
            opener.showStatusMessage(project, preview.statusCode, message)
            return
        }

        opener.showSqlPopup(project, preview)
    }

    @JsonNotification("efquerylens/runSetup")
    @Suppress("UNCHECKED_CAST")
    fun runSetup(payload: Any?) {
        val root = payload as? Map<String, Any?> ?: return
        val fileUri = root["fileUri"] as? String ?: return
        val line = (root["line"] as? Number)?.toInt() ?: 0
        val character = (root["character"] as? Number)?.toInt() ?: 0

        EFQueryLensSetupService.run(project, fileUri, line, character)
    }

    @JsonNotification("efquerylens/statusChanged")
    fun statusChanged(payload: Any?) {
        EFQueryLensHostStatus.updateFromSnapshot(payload)
        ensureLifecycleStarted()
    }

    @JsonNotification("efquerylens/copySqlToClipboard")
    @Suppress("UNCHECKED_CAST")
    fun copySqlToClipboard(payload: Any?) {
        val root = payload as? Map<String, Any?> ?: return
        val sql = root["sql"] as? String ?: return
        CopyPasteManager.getInstance().setContents(StringSelection(sql))
        ApplicationManager.getApplication().invokeLater {
            NotificationGroupManager
                .getInstance()
                .getNotificationGroup("EF QueryLens")
                .createNotification("SQL copied to clipboard", NotificationType.INFORMATION)
                .setImportant(true)
                .notify(project)
        }
    }
}
