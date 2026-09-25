package org.fieldtak.hub.util
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import android.widget.Toast
import androidx.core.content.FileProvider
import androidx.core.content.pm.PackageInfoCompat
import java.io.File
import java.io.FileOutputStream
import java.util.zip.ZipFile
data class DetectedAppEntry(
    val title: String,
    val packageName: String,
    val installedVersion: String?,
    val installedCode: Long?,
    val localApkFile: File?,
    val bundledVersion: String?,
    val bundledCode: Long?,
    val isPlugin: Boolean
) {
    val isInstalled: Boolean
        get() = installedVersion != null
    val hasLocalApk: Boolean
        get() = localApkFile != null && localApkFile.exists()
    val needsInstallOrUpdate: Boolean
        get() = !isInstalled || (bundledCode != null &&
            installedCode != null && bundledCode > installedCode)
}
object ApkInstallerManager {
    private val ATAK_PACKAGES = listOf(
        "com.atakmap.app.civ",
        "com.atakmap.app",
        "com.atakmap.app.mil"
    )
    private const val MESHTASTIC_PACKAGE = "com.geeksville.mesh"
    @JvmStatic
    fun isAtakInstalled(context: Context): Boolean {
        return ATAK_PACKAGES.any {
            getInstalledPkgInfo(context, it) != null
        }
    }
    @JvmStatic
    fun isMeshtasticInstalled(context: Context): Boolean {
        return getInstalledPkgInfo(
            context, MESHTASTIC_PACKAGE
        ) != null
    }
    @JvmStatic
    fun getInstalledPkgInfo(
        context: Context,
        pkg: String
    ): PackageInfo? {
        val pm = context.packageManager
        return try {
            if (Build.VERSION.SDK_INT >= 33) {
                pm.getPackageInfo(
                    pkg,
                    PackageManager.PackageInfoFlags.of(0L)
                )
            } else {
                @Suppress("DEPRECATION")
                pm.getPackageInfo(pkg, 0)
            }
        } catch (_: Exception) {
            try {
                @Suppress("DEPRECATION")
                pm.getInstalledPackages(0).firstOrNull {
                    it.packageName.equals(pkg, ignoreCase = true)
                }
            } catch (_: Exception) { null }
        }
    }
    @JvmStatic
    fun extractAllApksFromFtakArchives(
        context: Context
    ): List<File> {
        val outDir = File(
            context.getExternalFilesDir(null) ?: context.filesDir,
            "extracted_apks"
        )
        outDir.mkdirs()
        val archives = mutableListOf<File>()
        collectFiles(context.filesDir, archives)
        collectFiles(context.cacheDir, archives)
        collectFiles(context.getExternalFilesDir(null), archives)
        val ftaks = archives.filter {
            (it.name.endsWith(".ftak", true) ||
                it.name.endsWith(".zip", true)) &&
                !it.name.contains("ATAK_Config", true)
        }
        for (zFile in ftaks) {
            try {
                ZipFile(zFile).use { zip ->
                    val entries = zip.entries()
                    while (entries.hasMoreElements()) {
                        val e = entries.nextElement()
                        if (!e.isDirectory &&
                            e.name.endsWith(".apk", true)) {
                            val baseName = File(e.name).name
                            val dest = File(outDir, baseName)
                            if (!dest.exists() ||
                                dest.length() != e.size) {
                                zip.getInputStream(e).use { inp ->
                                    FileOutputStream(dest).use { o ->
                                        inp.copyTo(o)
                                    }
                                }
                            }
                        }
                    }
                }
            } catch (_: Exception) {}
        }
        val allApks = mutableListOf<File>()
        collectFiles(outDir, allApks)
        collectFiles(context.filesDir, allApks)
        collectFiles(context.getExternalFilesDir(null), allApks)
        try {
            @Suppress("DEPRECATION")
            val dl = Environment.getExternalStoragePublicDirectory(
                Environment.DIRECTORY_DOWNLOADS
            )
            dl?.listFiles()?.filter {
                it.isFile && it.name.endsWith(".apk", true)
            }?.let { allApks.addAll(it) }
        } catch (_: Exception) {}
        return allApks.filter {
            it.name.endsWith(".apk", true) && it.length() > 1024
        }.distinctBy { it.name.lowercase() }
    }
    @JvmStatic
    fun scanAllAppsAndPlugins(
        context: Context
    ): List<DetectedAppEntry> {
        val pm = context.packageManager
        val apks = extractAllApksFromFtakArchives(context)
        val parsedApks = apks.mapNotNull { file ->
            val info = getArchiveInfo(pm, file)
                ?: return@mapNotNull null
            Triple(file, info, info.packageName ?: "")
        }
        val results = mutableListOf<DetectedAppEntry>()
        val atakInstalled = ATAK_PACKAGES.firstNotNullOfOrNull {
            getInstalledPkgInfo(context, it)
        }
        val atakApk = parsedApks.firstOrNull { (_, _, pkg) ->
            ATAK_PACKAGES.any { it.equals(pkg, true) } ||
                (pkg.contains("atakmap.app", true) &&
                    !pkg.contains("plugin", true))
        }
        results.add(
            DetectedAppEntry(
                title = "ATAK-CIV",
                packageName = atakInstalled?.packageName
                    ?: atakApk?.third ?: "com.atakmap.app.civ",
                installedVersion = atakInstalled?.versionName,
                installedCode = atakInstalled?.let {
                    PackageInfoCompat.getLongVersionCode(it)
                },
                localApkFile = atakApk?.first,
                bundledVersion = atakApk?.second?.versionName,
                bundledCode = atakApk?.second?.let {
                    PackageInfoCompat.getLongVersionCode(it)
                },
                isPlugin = false
            )
        )
        val meshInstalled = getInstalledPkgInfo(
            context, MESHTASTIC_PACKAGE
        )
        val meshApk = parsedApks.firstOrNull { (_, _, pkg) ->
            pkg.equals(MESHTASTIC_PACKAGE, true) ||
                pkg.contains("geeksville.mesh", true)
        }
        results.add(
            DetectedAppEntry(
                title = "Meshtastic Android",
                packageName = MESHTASTIC_PACKAGE,
                installedVersion = meshInstalled?.versionName,
                installedCode = meshInstalled?.let {
                    PackageInfoCompat.getLongVersionCode(it)
                },
                localApkFile = meshApk?.first,
                bundledVersion = meshApk?.second?.versionName,
                bundledCode = meshApk?.second?.let {
                    PackageInfoCompat.getLongVersionCode(it)
                },
                isPlugin = false
            )
        )
        val pluginPkgs = mutableSetOf<String>()
        parsedApks.forEach { (_, _, pkg) ->
            if (pkg.isNotBlank() &&
                !ATAK_PACKAGES.contains(pkg) &&
                pkg != MESHTASTIC_PACKAGE) {
                pluginPkgs.add(pkg)
            }
        }
        try {
            @Suppress("DEPRECATION")
            pm.getInstalledPackages(0).forEach { pi ->
                val p = pi.packageName ?: ""
                if ((p.contains("atakmap", true) &&
                    p.contains("plugin", true)) ||
                    p.contains("meshtastic.plugin", true)) {
                    pluginPkgs.add(p)
                }
            }
        } catch (_: Exception) {}
        for (pPkg in pluginPkgs) {
            val inst = getInstalledPkgInfo(context, pPkg)
            val bnd = parsedApks.firstOrNull {
                it.third.equals(pPkg, true)
            }
            val label = bnd?.first?.name
                ?: pPkg.substringAfterLast(".")
            results.add(
                DetectedAppEntry(
                    title = "Plugin: $label",
                    packageName = pPkg,
                    installedVersion = inst?.versionName,
                    installedCode = inst?.let {
                        PackageInfoCompat.getLongVersionCode(it)
                    },
                    localApkFile = bnd?.first,
                    bundledVersion = bnd?.second?.versionName,
                    bundledCode = bnd?.second?.let {
                        PackageInfoCompat.getLongVersionCode(it)
                    },
                    isPlugin = true
                )
            )
        }
        return results
    }
    @JvmStatic
    fun launchApkInstall(context: Context, apkFile: File): String {
        if (!apkFile.exists()) {
            return "Plik APK nie istnieje: ${apkFile.name}"
        }
        if (Build.VERSION.SDK_INT >= 26 &&
            !context.packageManager.canRequestPackageInstalls()) {
            try {
                val i = Intent(
                    Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:${context.packageName}")
                ).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                context.startActivity(i)
            } catch (_: Exception) {}
            Toast.makeText(
                context,
                "W\u0142\u0105cz zgod\u0119 na instalacj\u0119 APK i pon\u00F3w!",
                Toast.LENGTH_LONG
            ).show()
            return "Wymagana zgoda na instalowanie APK."
        }
        val stagingDir = File(context.cacheDir, "apk_staging")
        stagingDir.mkdirs()
        val stagedFile = if (apkFile.parentFile == stagingDir) {
            apkFile
        } else {
            val target = File(stagingDir, apkFile.name)
            apkFile.copyTo(target, overwrite = true)
            target
        }
        val authorities = listOf(
            "${context.packageName}.fileprovider",
            "${context.packageName}.provider"
        )
        var uri: Uri? = null
        for (auth in authorities) {
            try {
                uri = FileProvider.getUriForFile(
                    context, auth, stagedFile
                )
                if (uri != null) break
            } catch (_: Exception) {}
        }
        if (uri == null) {
            return "B\u0142\u0105d FileProvider dla ${stagedFile.name}"
        }
        return try {
            @Suppress("DEPRECATION")
            val intent = Intent(Intent.ACTION_INSTALL_PACKAGE).apply {
                data = uri
                setDataAndType(
                    uri,
                    "application/vnd.android.package-archive"
                )
                putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true)
                putExtra(Intent.EXTRA_RETURN_RESULT, true)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(intent)
            "Uruchomiono instalator: ${stagedFile.name}"
        } catch (_: Exception) {
            try {
                val fb = Intent(Intent.ACTION_VIEW).apply {
                    setDataAndType(
                        uri,
                        "application/vnd.android.package-archive"
                    )
                    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                context.startActivity(fb)
                "Uruchomiono instalator APK: ${stagedFile.name}"
            } catch (e2: Exception) {
                "B\u0142\u0105d instalatora: ${e2.message}"
            }
        }
    }
    @JvmStatic
    fun launchStoreOrGithubFallback(
        context: Context,
        packageName: String
    ) {
        try {
            val i = Intent(
                Intent.ACTION_VIEW,
                Uri.parse("market://details?id=$packageName")
            ).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) }
            context.startActivity(i)
        } catch (_: Exception) {
            val web = Intent(
                Intent.ACTION_VIEW,
                Uri.parse(
                    "https://play.google.com/store/apps/details?id=$packageName"
                )
            ).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) }
            context.startActivity(web)
        }
    }
    private fun getArchiveInfo(
        pm: PackageManager,
        f: File
    ): PackageInfo? {
        return try {
            if (Build.VERSION.SDK_INT >= 33) {
                pm.getPackageArchiveInfo(
                    f.absolutePath,
                    PackageManager.PackageInfoFlags.of(0L)
                )
            } else {
                @Suppress("DEPRECATION")
                pm.getPackageArchiveInfo(f.absolutePath, 0)
            }
        } catch (_: Exception) { null }
    }
    private fun collectFiles(d: File?, out: MutableList<File>) {
        val list = d?.listFiles() ?: return
        for (c in list) {
            if (c.isDirectory) collectFiles(c, out) else out.add(c)
        }
    }
}