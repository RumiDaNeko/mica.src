import java.util.Properties
plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Shared with desktop, copied in at build time rather than duplicated: the blocker's rule lists, the
// built-in game pages and the Vietnamese strings (src\Strings.vi.tsv, English TAB Vietnamese).
val repo = rootProject.projectDir.parentFile
val generatedAssets = layout.buildDirectory.dir("generated/micaAssets")
val generatedRes = layout.buildDirectory.dir("generated/micaRes")

val gamePages = listOf("GameHub", "Tetris", "Snake", "Game2048", "Minesweeper", "Breakout", "Sudoku", "Memory", "Flappy", "Pong", "Invaders", "ConnectFour")

// Only the lists default.json turns on are shipped; v1 has no list picker.
val defaultLists = Regex("\"lists\"\\s*:\\s*\\[([^\\]]*)]").find(File(repo, "extensions/mica-blocker/rules/default.json").readText())
    ?.groupValues?.get(1)?.split(',')?.map { it.trim().trim('"') }?.filter { it.isNotEmpty() }.orEmpty()

val copyMicaAssets by tasks.registering(Sync::class) {
    into(generatedAssets)
    from(File(repo, "extensions/mica-blocker/rules")) { include(listOf("default.json") + defaultLists.map { "$it.json" }); into("blocker") }
    from(File(repo, "src")) { include(gamePages.map { "$it.html" }); into("games") }
    from(File(repo, "src")) { include("Reader.js"); into("scripts") }
}

// values-vi/strings.xml: every English string in values/strings.xml looked up in Strings.vi.tsv. Strings
// the TSV doesn't have are listed and left to res/values-vi/strings_android.xml (or English).
val generateViStrings by tasks.registering {
    val tsv = File(repo, "src/Strings.vi.tsv")
    val english = file("src/main/res/values/strings.xml")
    val handWritten = file("src/main/res/values-vi/strings_android.xml")
    val out = generatedRes.map { it.file("values-vi/strings.xml") }
    inputs.files(tsv, english, handWritten)
    outputs.file(out)
    doLast {
        val map = HashMap<String, String>()
        tsv.readLines(Charsets.UTF_8).forEach { line ->
            if (line.startsWith("#") || !line.contains('\t')) return@forEach
            val (en, vi) = line.split('\t', limit = 2)
            if (en.startsWith("re:")) {
                // A count pattern like "re:(\d+) ads blocked" becomes "%1$d ads blocked".
                if (en.contains("(\\d+)")) map[en.removePrefix("re:").replace("(\\d+)", "%1\$d")] = vi.replace("\$1", "%1\$d")
            } else map[en] = vi
        }
        val entry = Regex("<string name=\"([^\"]+)\"[^>]*>(.*?)</string>", RegexOption.DOT_MATCHES_ALL)
        val manual = if (handWritten.exists()) entry.findAll(handWritten.readText()).map { it.groupValues[1] }.toSet() else emptySet()
        fun unescape(s: String) = s.replace("\\'", "'").replace("\\\"", "\"").replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">")
        fun escape(s: String) = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("'", "\\'").replace("\"", "\\\"")
        val sb = StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- Generated from src/Strings.vi.tsv. Do not edit. -->\n<resources>\n")
        val missing = ArrayList<String>()
        entry.findAll(english.readText()).forEach { m ->
            val name = m.groupValues[1]
            if (name in manual || m.value.contains("translatable=\"false\"")) return@forEach
            val vi = map[unescape(m.groupValues[2])]
            if (vi == null) missing += name else sb.append("    <string name=\"").append(name).append("\">").append(escape(vi)).append("</string>\n")
        }
        sb.append("</resources>\n")
        val file = out.get().asFile
        file.parentFile.mkdirs(); file.writeText(sb.toString(), Charsets.UTF_8)
        if (missing.isNotEmpty()) logger.warn("Mica: no Vietnamese text in Strings.vi.tsv or strings_android.xml for: " + missing.joinToString())
    }
}

android {
    namespace = "app.mica"
    compileSdk = 36
    buildToolsVersion = "36.0.0"

    defaultConfig {
        applicationId = "online.lessnoise.mica"
        minSdk = 26
        targetSdk = 36
        versionCode = 4
        versionName = "0.1.3"
    }

    // The Play upload key lives outside the repo (D:\creds by default, or -Pmica.upload=<file>); without it,
    // release builds are left unsigned.
    val uploadProps = File((project.findProperty("mica.upload") as String?) ?: "D:/creds/mica-upload.properties")
    signingConfigs {
        if (uploadProps.exists()) create("upload") {
            val p = Properties().apply { uploadProps.inputStream().use { load(it) } }
            storeFile = File(p.getProperty("storeFile"))
            storePassword = p.getProperty("storePassword")
            keyAlias = p.getProperty("keyAlias")
            keyPassword = p.getProperty("keyPassword")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfigs.findByName("upload")?.let { signingConfig = it }
        }
        // The release build (R8 and all) under its own id and the debug key, to try on a device beside the debug app.
        create("r8test") {
            initWith(getByName("release"))
            applicationIdSuffix = ".r8test"
            signingConfig = signingConfigs.getByName("debug")
            matchingFallbacks += "release"
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    androidResources {
        // Rule lists are read once at start-up; keep them uncompressed so they're read straight from the APK.
        noCompress += "json"
        localeFilters += listOf("en", "vi")
    }
    sourceSets["main"].assets.srcDir(generatedAssets)
    sourceSets["main"].res.srcDir(generatedRes)
    testOptions {
        unitTests.isReturnDefaultValues = true
        unitTests.all { t -> System.getProperty("mica.vaultOut")?.let { t.systemProperty("mica.vaultOut", it) } }
    }
    buildFeatures { buildConfig = true }
}

kotlin { jvmToolchain(17) }

tasks.named("preBuild") { dependsOn(copyMicaAssets, generateViStrings) }

dependencies {
    implementation("androidx.appcompat:appcompat:1.7.1")
    implementation("androidx.webkit:webkit:1.14.0")
    implementation("androidx.recyclerview:recyclerview:1.4.0")
    implementation("androidx.drawerlayout:drawerlayout:1.2.0")
    testImplementation("junit:junit:4.13.2")
}
