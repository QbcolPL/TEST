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
data class DetectedAppEntry(val title: String, val packageName: String, val installedVersion: String?, val installedCode: Long?, val localApkFile: File?, val bundledVersion: String?, val bundledCode: Long?, val isPlugin: Boolean) {
    val isInstalled: Boolean get() = installedVersion != null
    val hasLocalApk: Boolean get() = localApkFile != null && localApkFile.exists()
    val needsInstallOrUpdate: Boolean get() = if (!isInstalled) true else (bundledCode != null && installedCode != null && bundledCode > installedCode)
}
object ApkInstallerManager {
    private val ATAK_PACKAGES = listOf("com.atakmap.app.civ", "com.atakmap.app", "com.atakmap.app.mil")
    private const val MESHTASTIC_PACKAGE = "com.geeksville.mesh"
    @JvmStatic fun isAtakInstalled(context: Context): Boolean = ATAK_PACKAGES.any { getInstalledPkgInfo(context, it) != null }
    @JvmStatic fun isMeshtasticInstalled(context: Context): Boolean = getInstalledPkgInfo(context, MESHTASTIC_PACKAGE) != null
    @JvmStatic fun getInstalledPkgInfo(context: Context, pkg: String): PackageInfo? {
        val pm = context.packageManager
        return try {
            if (Build.VERSION.SDK_INT >= 33) pm.getPackageInfo(pkg, PackageManager.PackageInfoFlags.of(0L)) else @Suppress("DEPRECATION") pm.getPackageInfo(pkg, 0)
        } catch (_: Exception) {
            try { @Suppress("DEPRECATION") pm.getInstalledPackages(0).firstOrNull { it.packageName.equals(pkg, true) } } catch (_: Exception) { null }
        }
    }
    @JvmStatic fun extractAllApksFromFtakArchives(context: Context): List<File> {
        val outDir = File(context.getExternalFilesDir(null) ?: context.filesDir, "extracted_apks").apply { mkdirs() }
        val archives = mutableListOf<File>()
        collectFiles(context.filesDir, archives); collectFiles(context.cacheDir, archives); collectFiles(context.getExternalFilesDir(null), archives)
        archives.filter {
            val isArch = if (it.name.endsWith(".ftak", true)) true else it.name.endsWith(".zip", true)
            isArch && !it.name.contains("ATAK_Config", true)
        }.forEach { zFile ->
            try {
                ZipFile(zFile).use { zip ->
                    val entries = zip.entries()
                    while (entries.hasMoreElements()) {
                        val e = entries.nextElement()
                        if (!e.isDirectory && e.name.endsWith(".apk", true)) {
                            val dest = File(outDir, File(e.name).name)
                            val sameSize = dest.exists() && dest.length() == e.size
                            if (!sameSize) zip.getInputStream(e).use { i -> FileOutputStream(dest).use { o -> i.copyTo(o) } }
                        }
                    }
                }
            } catch (_: Exception) {}
        }
        val allApks = mutableListOf<File>()
        collectFiles(outDir, allApks); collectFiles(context.filesDir, allApks); collectFiles(context.getExternalFilesDir(null), allApks)
        try { @Suppress("DEPRECATION") Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)?.listFiles()?.filter { it.isFile && it.name.endsWith(".apk", true) }?.let { allApks.addAll(it) } } catch (_: Exception) {}
        return allApks.filter { it.name.endsWith(".apk", true) && it.length() > 1024 }.distinctBy { it.name.lowercase() }
    }
    @JvmStatic fun scanAllAppsAndPlugins(context: Context): List<DetectedAppEntry> {
        val pm = context.packageManager
        val parsedApks = extractAllApksFromFtakArchives(context).mapNotNull { file -> val info = getArchiveInfo(pm, file) ?: return@mapNotNull null; Triple(file, info, info.packageName ?: "") }
        val results = mutableListOf<DetectedAppEntry>()
        val atakInst = ATAK_PACKAGES.firstNotNullOfOrNull { getInstalledPkgInfo(context, it) }
        val atakApk = parsedApks.firstOrNull { (_, _, pkg) ->
            val isAtak = ATAK_PACKAGES.any { it.equals(pkg, true) }
            val isCore = pkg.contains("atakmap.app", true) && !pkg.contains("plugin", true)
            if (isAtak) true else isCore
        }
        results.add(DetectedAppEntry("ATAK-CIV", atakInst?.packageName ?: atakApk?.third ?: "com.atakmap.app.civ", atakInst?.versionName, atakInst?.let { PackageInfoCompat.getLongVersionCode(it) }, atakApk?.first, atakApk?.second?.versionName, atakApk?.second?.let { PackageInfoCompat.getLongVersionCode(it) }, false))
        val meshInst = getInstalledPkgInfo(context, MESHTASTIC_PACKAGE)
        val meshApk = parsedApks.firstOrNull { (_, _, pkg) -> if (pkg.equals(MESHTASTIC_PACKAGE, true)) true else pkg.contains("geeksville.mesh", true) }
        results.add(DetectedAppEntry("Meshtastic Android", MESHTASTIC_PACKAGE, meshInst?.versionName, meshInst?.let { PackageInfoCompat.getLongVersionCode(it) }, meshApk?.first, meshApk?.second?.versionName, meshApk?.second?.let { PackageInfoCompat.getLongVersionCode(it) }, false))
        val pluginPkgs = mutableSetOf<String>()
        parsedApks.forEach { (_, _, pkg) -> if (pkg.isNotBlank() && !ATAK_PACKAGES.contains(pkg) && pkg != MESHTASTIC_PACKAGE) pluginPkgs.add(pkg) }
        try {
            @Suppress("DEPRECATION")
            pm.getInstalledPackages(0).forEach { pi ->
                val p = pi.packageName ?: ""
                val isAtakPlg = p.contains("atakmap", true) && p.contains("plugin", true)
                if (isAtakPlg) pluginPkgs.add(p)
                else if (p.contains("meshtastic.plugin", true)) pluginPkgs.add(p)
            }
        } catch (_: Exception) {}
        pluginPkgs.forEach { pPkg ->
            val inst = getInstalledPkgInfo(context, pPkg); val bnd = parsedApks.firstOrNull { it.third.equals(pPkg, true) }
            results.add(DetectedAppEntry("Plugin: ${bnd?.first?.name ?: pPkg.substringAfterLast(".")}", pPkg, inst?.versionName, inst?.let { PackageInfoCompat.getLongVersionCode(it) }, bnd?.first, bnd?.second?.versionName, bnd?.second?.let { PackageInfoCompat.getLongVersionCode(it) }, true))
        }
        return results
    }
    @JvmStatic fun launchApkInstall(context: Context, apkFile: File): String {
        if (!apkFile.exists()) return "Plik APK nie istnieje: ${apkFile.name}"
        if (Build.VERSION.SDK_INT >= 26 && !context.packageManager.canRequestPackageInstalls()) {
            try { context.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}")).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) }) } catch (_: Exception) {}
            Toast.makeText(context, "Wlacz zgode na instalacje APK!", Toast.LENGTH_LONG).show()
            return "Wymagana zgoda na instalowanie APK."
        }
        val stagingDir = File(context.cacheDir, "apk_staging").apply { mkdirs() }
        val stagedFile = if (apkFile.parentFile == stagingDir) apkFile else File(stagingDir, apkFile.name).also { apkFile.copyTo(it, true) }
        val uri = listOf("${context.packageName}.fileprovider", "${context.packageName}.provider").firstNotNullOfOrNull { auth -> try { FileProvider.getUriForFile(context, auth, stagedFile) } catch (_: Exception) { null } } ?: return "Blad FileProvider"
        return try {
            @Suppress("DEPRECATION")
            context.startActivity(Intent(Intent.ACTION_INSTALL_PACKAGE).apply {
                data = uri
                setDataAndType(uri, "application/vnd.android.package-archive")
                putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            })
            "Uruchomiono instalator: ${stagedFile.name}"
        } catch (e: Exception) { "Blad instalatora: ${e.message}" }
    }
    @JvmStatic fun launchStoreOrGithubFallback(context: Context, packageName: String) {
        try { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=$packageName")).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) }) }
        catch (_: Exception) { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=$packageName")).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) }) }
    }
    private fun getArchiveInfo(pm: PackageManager, f: File): PackageInfo? = try { if (Build.VERSION.SDK_INT >= 33) pm.getPackageArchiveInfo(f.absolutePath, PackageManager.PackageInfoFlags.of(0L)) else @Suppress("DEPRECATION") pm.getPackageArchiveInfo(f.absolutePath, 0) } catch (_: Exception) { null }
    private fun collectFiles(d: File?, out: MutableList<File>) { d?.listFiles()?.forEach { if (it.isDirectory) collectFiles(it, out) else out.add(it) } }
}