package efquerylens

import com.intellij.openapi.application.ReadAction
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.project.Project
import com.intellij.openapi.roots.ProjectFileIndex
import com.intellij.openapi.startup.ProjectActivity
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspServerManager
import kotlinx.coroutines.delay

class EFQueryLensProjectStartupWarmupActivity : ProjectActivity {
    override suspend fun execute(project: Project) {
        if (project.isDisposed) {
            return
        }

        val warmupTarget = findFirstProjectCSharpFile(project) ?: return

        repeat(20) {
            if (project.isDisposed) {
                return
            }

            val server = LspServerManager.getInstance(project)
                .getServersForProvider(EFQueryLensLspServerSupportProvider::class.java)
                .firstOrNull()

            if (server != null) {
                EFQueryLensLspServerSupportProvider.scheduleStartupPlumbing(server, warmupTarget)
                logInfo("[EFQueryLens] Startup activity queued warmup for '${warmupTarget.path}'")
                return
            }

            delay(1_000)
        }

        logInfo("[EFQueryLens] Startup activity did not find a running LSP server within timeout. Warmup will run when a C# file opens.")
    }

    private fun findFirstProjectCSharpFile(project: Project): VirtualFile? {
        return ReadAction.compute<VirtualFile?, RuntimeException> {
            var first: VirtualFile? = null
            ProjectFileIndex.getInstance(project).iterateContent { file ->
                if (!file.isDirectory && file.extension.equals("cs", ignoreCase = true)) {
                    first = file
                    false
                } else {
                    true
                }
            }
            first
        }
    }

    private fun logInfo(message: String) {
        thisLogger().info(message)
    }
}
