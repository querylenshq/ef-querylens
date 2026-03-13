package efquerylens

import com.intellij.openapi.diagnostic.Logger
import java.nio.file.FileVisitResult
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.SimpleFileVisitor
import java.nio.file.StandardCopyOption
import java.nio.file.attribute.BasicFileAttributes
import java.security.MessageDigest
import java.time.Duration
import java.time.Instant
import java.util.UUID
import kotlin.io.path.absolutePathString
import kotlin.io.path.exists
import kotlin.io.path.isDirectory
import kotlin.io.path.isRegularFile
import kotlin.io.path.name
import kotlin.io.path.pathString

internal object EFQueryLensShadowLspCache {
    private const val DefaultMaxAgeHours = 12
    private const val DefaultSoftLimitMb = 5120
    private const val DefaultTargetMb = 3072
    private val CleanupInterval: Duration = Duration.ofMinutes(15)

    private const val MaxAgeEnvVar = "QUERYLENS_SHADOW_CACHE_MAX_AGE_HOURS"
    private const val SoftLimitEnvVar = "QUERYLENS_SHADOW_CACHE_SOFT_LIMIT_MB"
    private const val TargetLimitEnvVar = "QUERYLENS_SHADOW_CACHE_TARGET_MB"

    private val logger = Logger.getInstance(EFQueryLensShadowLspCache::class.java)

    private val debugEnabled: Boolean by lazy {
        val raw = System.getenv("QUERYLENS_DEBUG")?.trim()?.lowercase()
        raw == "1" || raw == "true" || raw == "yes" || raw == "on"
    }

    private val rootDir: Path by lazy {
        Path.of(System.getProperty("java.io.tmpdir"), "EFQueryLens", "rider-lsp-shadow")
    }

    private val bundlesDir: Path by lazy { rootDir.resolve("bundles") }
    private val stagingDir: Path by lazy { rootDir.resolve("staging") }

    @Volatile
    private var lastCleanupAt: Instant = Instant.EPOCH

    @Synchronized
    fun resolveOrCreate(sourceDllPath: Path): Path {
        val sourceDll = sourceDllPath.toAbsolutePath().normalize()
        if (!sourceDll.isRegularFile()) {
            return sourceDllPath
        }

        val sourceDir = sourceDll.parent ?: return sourceDll

        return try {
            Files.createDirectories(bundlesDir)
            Files.createDirectories(stagingDir)

            cleanupIfDue(force = false)

            val manifest = buildManifest(sourceDir)
            val bundleKey = computeBundleKey(sourceDir, manifest)
            val bundleDir = bundlesDir.resolve(bundleKey)
            val bundleDll = bundleDir.resolve(sourceDll.fileName.name)

            if (bundleDll.isRegularFile()) {
                touchDirectory(bundleDir)
                return bundleDll
            }

            val stagingBundle = stagingDir.resolve("$bundleKey-${UUID.randomUUID().toString().replace("-", "")}")
            Files.createDirectories(stagingBundle)

            try {
                manifest.forEach { entry ->
                    val target = stagingBundle.resolve(entry.relativePath)
                    val parent = target.parent
                    if (parent != null) {
                        Files.createDirectories(parent)
                    }

                    Files.copy(entry.fullPath, target, StandardCopyOption.REPLACE_EXISTING)
                }

                promoteBundle(stagingBundle, bundleDir)
                touchDirectory(bundleDir)
                cleanupIfDue(force = true)
                bundleDll
            } finally {
                deleteDirectoryIfExists(stagingBundle)
            }
        } catch (ex: Throwable) {
            logger.warn("Falling back to source LSP DLL path because shadow copy creation failed for '$sourceDll'.", ex)
            sourceDll
        }
    }

    @Synchronized
    private fun cleanupIfDue(force: Boolean) {
        if (!force && Duration.between(lastCleanupAt, Instant.now()) < CleanupInterval) {
            return
        }

        cleanupCore()
        lastCleanupAt = Instant.now()
    }

    private fun cleanupCore() {
        if (!rootDir.exists()) {
            return
        }

        val maxAgeHours = readIntEnv(MaxAgeEnvVar, DefaultMaxAgeHours, min = 1, max = 720)
        val softLimitMb = readIntEnv(SoftLimitEnvVar, DefaultSoftLimitMb, min = 256, max = 1024 * 1024)
        val targetLimitMb = readIntEnv(TargetLimitEnvVar, DefaultTargetMb, min = 128, max = softLimitMb)
        val softLimitBytes = softLimitMb * 1024L * 1024L
        val targetLimitBytes = targetLimitMb * 1024L * 1024L
        val cutoff = Instant.now().minus(Duration.ofHours(maxAgeHours.toLong()))

        try {
            if (stagingDir.isDirectory()) {
                Files.list(stagingDir).use { paths ->
                    paths.filter { it.isDirectory() }
                        .forEach { dir ->
                            if (directoryLastModified(dir).isBefore(cutoff)) {
                                deleteDirectoryIfExists(dir)
                            }
                        }
                }
            }

            if (bundlesDir.isDirectory()) {
                Files.list(bundlesDir).use { paths ->
                    paths.filter { it.isDirectory() }
                        .forEach { dir ->
                            if (directoryLastModified(dir).isBefore(cutoff)) {
                                deleteDirectoryIfExists(dir)
                            }
                        }
                }
            }

            var currentSize = directorySize(bundlesDir)
            if (currentSize > softLimitBytes && bundlesDir.isDirectory()) {
                val oldestFirst = Files.list(bundlesDir).use { paths ->
                    paths.filter { it.isDirectory() }
                        .map { it to directoryLastModified(it) }
                        .sorted { left, right -> left.second.compareTo(right.second) }
                        .toList()
                }

                oldestFirst.forEach { (dir, _) ->
                    deleteDirectoryIfExists(dir)
                    currentSize = directorySize(bundlesDir)
                    if (currentSize <= targetLimitBytes) {
                        return@forEach
                    }
                }
            }

            if (debugEnabled) {
                logger.info("[EFQueryLens] shadow-lsp-cleanup root=${rootDir.pathString}")
            }
        } catch (_: Throwable) {
            // Best-effort cleanup.
        }
    }

    private fun buildManifest(sourceDir: Path): List<ManifestEntry> {
        return Files.walk(sourceDir).use { paths ->
            paths
                .filter { it.isRegularFile() }
                .map { file ->
                    val relativePath = sourceDir.relativize(file).toString().replace('\\', '/')
                    val length = Files.size(file)
                    ManifestEntry(
                        fullPath = file,
                        relativePath = relativePath,
                        length = length,
                        contentHash = fileContentHash(file)
                    )
                }
                .sorted(compareBy(String.CASE_INSENSITIVE_ORDER) { it.relativePath })
                .toList()
        }
    }

    private fun computeBundleKey(sourceDir: Path, manifest: List<ManifestEntry>): String {
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update(sourceDir.absolutePathString().toByteArray())
        digest.update('|'.code.toByte())

        manifest.forEach { entry ->
            digest.update(entry.relativePath.toByteArray())
            digest.update('|'.code.toByte())
            digest.update(entry.length.toString().toByteArray())
            digest.update('|'.code.toByte())
            digest.update(entry.contentHash.toByteArray())
            digest.update(';'.code.toByte())
        }

        return digest.digest()
            .joinToString(separator = "") { "%02x".format(it) }
            .take(16)
    }

    private fun fileContentHash(path: Path): String {
        val digest = MessageDigest.getInstance("SHA-256")
        Files.newInputStream(path).use { input ->
            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) {
                    break
                }

                if (read > 0) {
                    digest.update(buffer, 0, read)
                }
            }
        }

        return digest.digest().joinToString(separator = "") { "%02x".format(it) }
    }

    private fun promoteBundle(stagingBundle: Path, finalBundle: Path) {
        try {
            Files.move(stagingBundle, finalBundle, StandardCopyOption.ATOMIC_MOVE)
            return
        } catch (_: Throwable) {
            if (finalBundle.exists()) {
                return
            }
        }

        try {
            Files.move(stagingBundle, finalBundle)
        } catch (_: Throwable) {
            if (!finalBundle.exists()) {
                throw IllegalStateException("Could not promote LSP shadow bundle '$stagingBundle' to '$finalBundle'.")
            }
        }
    }

    private fun touchDirectory(path: Path) {
        try {
            Files.setLastModifiedTime(path, java.nio.file.attribute.FileTime.from(Instant.now()))
        } catch (_: Throwable) {
            // Best-effort touch.
        }
    }

    private fun directoryLastModified(path: Path): Instant {
        return try {
            Files.getLastModifiedTime(path).toInstant()
        } catch (_: Throwable) {
            Instant.EPOCH
        }
    }

    private fun directorySize(path: Path): Long {
        if (!path.exists()) {
            return 0L
        }

        var total = 0L
        try {
            Files.walk(path).use { paths ->
                paths.filter { it.isRegularFile() }
                    .forEach { file ->
                        total += try {
                            Files.size(file)
                        } catch (_: Throwable) {
                            0L
                        }
                    }
            }
        } catch (_: Throwable) {
            // Best-effort accounting.
        }

        return total
    }

    private fun deleteDirectoryIfExists(path: Path) {
        if (!path.exists()) {
            return
        }

        try {
            Files.walkFileTree(path, object : SimpleFileVisitor<Path>() {
                override fun visitFile(file: Path, attrs: BasicFileAttributes): FileVisitResult {
                    Files.deleteIfExists(file)
                    return FileVisitResult.CONTINUE
                }

                override fun postVisitDirectory(dir: Path, exc: java.io.IOException?): FileVisitResult {
                    Files.deleteIfExists(dir)
                    return FileVisitResult.CONTINUE
                }
            })
        } catch (_: Throwable) {
            // Ignore locked or transient I/O failures.
        }
    }

    private fun readIntEnv(name: String, fallback: Int, min: Int, max: Int): Int {
        val raw = System.getenv(name) ?: return fallback
        val parsed = raw.toIntOrNull() ?: return fallback
        return parsed.coerceIn(min, max)
    }

    private data class ManifestEntry(
        val fullPath: Path,
        val relativePath: String,
        val length: Long,
        val contentHash: String
    )
}