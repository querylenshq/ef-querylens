plugins {
    kotlin("jvm") version "2.2.0"
    id("org.jetbrains.intellij.platform") version "2.12.0"
}

group = providers.gradleProperty("pluginGroup").get()
version = providers.gradleProperty("pluginVersion").get()

kotlin {
    jvmToolchain(21)
}

repositories {
    mavenCentral()

    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        rider(providers.gradleProperty("platformVersion")) {
            useInstaller = false
        }
    }
    // CommonMark spec-compliant Markdown to HTML (replaces custom regex conversion)
    implementation("org.commonmark:commonmark:0.27.1")
}

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            untilBuild = providers.gradleProperty("pluginUntilBuild")
        }
    }
}

tasks {
    runIde {
        val repoRoot = projectDir.toPath().resolve("../../..").normalize().toAbsolutePath()
        val lspDll = repoRoot.resolve("src/EFQueryLens.Lsp/bin/Debug/net10.0/EFQueryLens.Lsp.dll")
        environment("QUERYLENS_REPOSITORY_ROOT", repoRoot.toString())
        // Force this exact LSP binary (avoids stale shadow cache using an old build)
        if (lspDll.toFile().exists()) {
            environment("QUERYLENS_LSP_DLL", lspDll.toString())
        }
        environment("QUERYLENS_CLIENT", "rider")
        environment("QUERYLENS_STARTUP_BROWSER", "true")
        environment("QUERYLENS_DEBUG", "true")
        environment("QUERYLENS_FORCE_CODELENS", "true")
    }

    wrapper {
        gradleVersion = providers.gradleProperty("gradleVersion").get()
    }
}
