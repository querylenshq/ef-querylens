package efquerylens

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspServer
import com.intellij.platform.lsp.api.LspServerManager
import com.intellij.platform.lsp.api.LspServerSupportProvider
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor
import org.eclipse.lsp4j.ExecuteCommandParams
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap
import kotlin.io.path.absolutePathString
import kotlin.io.path.exists
import kotlin.io.path.isDirectory
import kotlin.io.path.isRegularFile
import kotlin.io.path.name
import kotlin.io.path.pathString

class EFQueryLensLspServerSupportProvider : LspServerSupportProvider {
    private val warmedDocumentUrls = ConcurrentHashMap.newKeySet<String>()
    @Volatile
    private var startupRestartRequested = false
    private val startupRestartLock = Any()

    override fun fileOpened(project: Project, file: VirtualFile, serverStarter: LspServerSupportProvider.LspServerStarter) {
        logInfo(project, "[EFQueryLens] fileOpened path='${file.path}' extension='${file.extension}'")
        if (!isSupported(file)) {
            logInfo(project, "[EFQueryLens] fileOpened skipped unsupported file '${file.path}'")
            return
        }

        logInfo(project, "[EFQueryLens] Ensuring LSP server is started for '${file.path}'")
        serverStarter.ensureServerStarted(EFQueryLensServerDescriptor(project))
        
        val server = LspServerManager.getInstance(project).getServersForProvider(EFQueryLensLspServerSupportProvider::class.java).firstOrNull()
        if (server != null) {
            scheduleStartupPlumbing(server, file)
        }
    }

    private fun scheduleStartupPlumbing(server: LspServer, file: VirtualFile) {
        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                // Wait a bit for the server to be fully ready if it just started
                Thread.sleep(1000)
                requestDaemonRestartOnActivate(server)
                requestWarmup(server, file)
            } catch (e: Exception) {
                logWarn(server.project, "[EFQueryLens] Startup plumbing failed", e)
            }
        }
    }

    private fun requestDaemonRestartOnActivate(server: LspServer) {
        if (startupRestartRequested) {
            return
        }

        synchronized(startupRestartLock) {
            if (startupRestartRequested) {
                return
            }

            startupRestartRequested = true
        }

        try {
            val response = server.sendRequestSync(10000) {
                it.workspaceService.executeCommand(
                    ExecuteCommandParams("efquerylens.daemon.restart", emptyList())
                )
            }

            logInfo(server.project, "[EFQueryLens] Startup daemon restart response='$response'")
        } catch (e: Exception) {
            logWarn(server.project, "[EFQueryLens] Startup daemon restart failed", e)
        }
    }

    private fun requestWarmup(server: LspServer, file: VirtualFile) {
        if (!warmedDocumentUrls.add(file.url)) {
            return
        }

        try {
            val warmupPayload = mapOf(
                "textDocument" to mapOf("uri" to file.url),
                "position" to mapOf("line" to 0, "character" to 0)
            )

            val response = server.sendRequestSync(5000) {
                it.workspaceService.executeCommand(
                    ExecuteCommandParams("efquerylens.warmup", listOf(warmupPayload))
                )
            }

            logInfo(server.project, "[EFQueryLens] Warmup command completed for '${file.path}' response='$response'")
        } catch (e: Exception) {
            warmedDocumentUrls.remove(file.url)
            logWarn(server.project, "[EFQueryLens] Warmup command failed for '${file.path}'", e)
        }
    }

    private fun isSupported(file: VirtualFile): Boolean {
        return file.extension.equals("cs", ignoreCase = true)
    }

    private fun logInfo(project: Project, message: String) {
        thisLogger().info(message)
    }

    private fun logWarn(project: Project, message: String, error: Throwable? = null) {
        if (error == null) {
            thisLogger().warn(message)
            return
        }

        thisLogger().warn(message, error)
    }
}

private class EFQueryLensServerDescriptor(
    private val hostProject: Project
) : ProjectWideLspServerDescriptor(hostProject, "EF QueryLens") {
    private companion object {
        private const val LspDllOverrideEnvVar = "QUERYLENS_LSP_DLL"
        private const val RepositoryRootOverrideEnvVar = "QUERYLENS_REPOSITORY_ROOT"
    }

    override fun isSupportedFile(file: VirtualFile): Boolean {
        return file.extension.equals("cs", ignoreCase = true)
    }

    override fun createCommandLine(): GeneralCommandLine {
        val projectBasePath = hostProject.basePath
            ?: error("Cannot start EF QueryLens language server: project has no base path.")

        val repositoryRoot = resolveRepositoryRoot(projectBasePath)
        val lspLogFilePath = buildLspLogFilePath(projectBasePath, repositoryRoot)

        val lspDllOverride = resolveLspDllOverride()
        if (lspDllOverride != null) {
            logInfo("[EFQueryLens] Starting EF QueryLens LSP from override '${lspDllOverride.pathString}'")
            // Use shadow cache to avoid locking the live bin output during builds
            val shadowLspDll = EFQueryLensShadowLspCache.resolveOrCreate(lspDllOverride)

            val workDir = repositoryRoot ?: (shadowLspDll.parent ?: shadowLspDll)
            return GeneralCommandLine("dotnet", shadowLspDll.pathString)
                .withWorkDirectory(workDir.toFile())
                .applyQueryLensEnvironment(repositoryRoot, lspLogFilePath)
        }

        val resolvedRepositoryRoot = repositoryRoot
            ?: error(
                "Cannot locate QueryLens repository root from '$projectBasePath'. " +
                    "Set $RepositoryRootOverrideEnvVar or $LspDllOverrideEnvVar to override.")

        val lspDll = resolveLspDll(resolvedRepositoryRoot)
            ?: error(
                "Cannot locate EFQueryLens.Lsp build output. Expected under src/EFQueryLens.Lsp/bin. " +
                    "Set $LspDllOverrideEnvVar to override.")

        // Use shadow cache to avoid locking the live bin output during builds
        val shadowLspDll = EFQueryLensShadowLspCache.resolveOrCreate(lspDll)
        logInfo("[EFQueryLens] Starting EF QueryLens LSP from shadow cache '${shadowLspDll.pathString}'")

        return GeneralCommandLine("dotnet", shadowLspDll.pathString)
            .withWorkDirectory(resolvedRepositoryRoot.toFile())
            .applyQueryLensEnvironment(resolvedRepositoryRoot, lspLogFilePath)
    }

    private fun resolveLspDllOverride(): Path? {
        val raw = System.getenv(LspDllOverrideEnvVar)
        if (raw.isNullOrBlank()) {
            return null
        }

        val candidate = Path.of(raw).toAbsolutePath().normalize()
        if (candidate.isRegularFile()) {
            return candidate
        }

        logWarn("Ignoring $LspDllOverrideEnvVar='$candidate' because the file does not exist.")
        return null
    }

    private fun resolveRepositoryRoot(basePath: String): Path? {
        val raw = System.getenv(RepositoryRootOverrideEnvVar)
        if (!raw.isNullOrBlank()) {
            val candidate = Path.of(raw).toAbsolutePath().normalize()
            val hasLspProject = candidate
                .resolve("src")
                .resolve("EFQueryLens.Lsp")
                .resolve("EFQueryLens.Lsp.csproj")
                .exists()

            if (hasLspProject) {
                return candidate
            }

            logWarn("Ignoring $RepositoryRootOverrideEnvVar='$candidate' because EFQueryLens.Lsp.csproj was not found.")
        }

        return findRepositoryRoot(Path.of(basePath))
    }

    private fun findRepositoryRoot(startPath: Path): Path? {
        var current: Path? = startPath
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

        return null
    }

    private fun resolveLspDll(repositoryRoot: Path): Path? {
        val lspOutputRoot = repositoryRoot
            .resolve("src")
            .resolve("EFQueryLens.Lsp")
            .resolve("bin")

        if (!lspOutputRoot.isDirectory()) {
            return null
        }

        // Prefer Release output to avoid stale/debug-lock scenarios during Rider runs.
        val preferredCandidates = listOf(
            lspOutputRoot.resolve("Release").resolve("net10.0").resolve("EFQueryLens.Lsp.dll"),
            lspOutputRoot.resolve("Debug").resolve("net10.0").resolve("EFQueryLens.Lsp.dll")
        )

        preferredCandidates.firstOrNull { it.exists() }?.let { return it }

        return Files.walk(lspOutputRoot).use { paths ->
            paths
                .filter { path ->
                    path.name == "EFQueryLens.Lsp.dll" &&
                        path.parent?.name == "net10.0" &&
                        (path.parent?.parent?.name == "Debug" || path.parent?.parent?.name == "Release")
                }
                .findFirst()
                .orElse(null)
        }
    }

    private fun buildLspLogFilePath(projectBasePath: String, repositoryRoot: Path?): Path {
        val workspacePath = repositoryRoot
            ?.toAbsolutePath()
            ?.normalize()
            ?: Path.of(projectBasePath).toAbsolutePath().normalize()
        val hash = hashWorkspacePath(workspacePath.absolutePathString())
        return Path.of(System.getProperty("java.io.tmpdir"), "EFQueryLens", "rider-logs", "lsp-$hash.log")
    }

    private fun hashWorkspacePath(path: String): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(path.toByteArray(StandardCharsets.UTF_8))
        return digest.joinToString("") { "%02x".format(it) }.take(16)
    }

    private fun GeneralCommandLine.applyQueryLensEnvironment(repositoryRoot: Path?, lspLogFilePath: Path): GeneralCommandLine {
        withEnvironment("QUERYLENS_CLIENT", "rider")
        // Keep Rider diagnostics on by default so LSP/daemon logs are available in all runs.
        withEnvironment("QUERYLENS_DEBUG", "1")
        // Rider can cancel/re-issue hover requests aggressively; allow a short grace
        // window so canceled requests can still reuse an in-flight hover computation.
        withEnvironment("QUERYLENS_HOVER_CANCEL_GRACE_MS", "1200")
        // Show a lightweight progress indicator if hover translation is slow.
        withEnvironment("QUERYLENS_HOVER_PROGRESS_NOTIFY", "1")
        withEnvironment("QUERYLENS_HOVER_PROGRESS_DELAY_MS", "350")
        // Rider cold starts can take longer to bring daemon online than VS Code.
        withEnvironment("QUERYLENS_DAEMON_START_TIMEOUT_MS", "30000")
        withEnvironment("QUERYLENS_DAEMON_CONNECT_TIMEOUT_MS", "10000")
        withEnvironment("QUERYLENS_DAEMON_SHUTDOWN_ON_DISPOSE", "1")
        withEnvironment("QUERYLENS_LSP_LOG_FILE", lspLogFilePath.absolutePathString())

        if (repositoryRoot == null) {
            return this
        }

        val workspacePath = repositoryRoot.absolutePathString()
        withEnvironment("QUERYLENS_WORKSPACE", workspacePath)
        withEnvironment("QUERYLENS_DAEMON_WORKSPACE", workspacePath)

        resolveDaemonExecutable(repositoryRoot)?.let {
            withEnvironment("QUERYLENS_DAEMON_EXE", it.absolutePathString())
        }

        resolveDaemonAssembly(repositoryRoot)?.let {
            withEnvironment("QUERYLENS_DAEMON_DLL", it.absolutePathString())
        }

        return this
    }

    private fun resolveDaemonExecutable(repositoryRoot: Path): Path? {
        val candidates = listOf(
            // Prefer Debug for local plugin runs to stay in lockstep with recent code changes.
            repositoryRoot.resolve("src").resolve("EFQueryLens.Daemon").resolve("bin").resolve("Debug").resolve("net10.0").resolve("EFQueryLens.Daemon.exe"),
            repositoryRoot.resolve("src").resolve("EFQueryLens.Daemon").resolve("bin").resolve("Release").resolve("net10.0").resolve("EFQueryLens.Daemon.exe")
        )

        return candidates.firstOrNull { it.exists() && it.isRegularFile() }
    }

    private fun resolveDaemonAssembly(repositoryRoot: Path): Path? {
        val candidates = listOf(
            repositoryRoot.resolve("src").resolve("EFQueryLens.Daemon").resolve("bin").resolve("Debug").resolve("net10.0").resolve("EFQueryLens.Daemon.dll"),
            repositoryRoot.resolve("src").resolve("EFQueryLens.Daemon").resolve("bin").resolve("Release").resolve("net10.0").resolve("EFQueryLens.Daemon.dll")
        )

        return candidates.firstOrNull { it.exists() && it.isRegularFile() }
    }

    private fun logInfo(message: String) {
        thisLogger().info(message)
    }

    private fun logWarn(message: String, error: Throwable? = null) {
        if (error == null) {
            thisLogger().warn(message)
            return
        }

        thisLogger().warn(message, error)
    }
}
