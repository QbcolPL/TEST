package org.fieldtak.hub.util
import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import org.fieldtak.hub.provision.ResumablePackageDownloader
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.zip.ZipFile
data class FtakLibraryEntry(
    val file: File,
    val fileName: String,
    val projectTitle: String,
    val projectVersion: String,
    val sizeKb: Long,
    val modifiedDate: String,
    val apkCount: Int,
    val mapCount: Int,
    val overlayCount: Int,
    val isPartBuffer: Boolean,
    val isActive: Boolean
)
object FtakPackageLibraryManager {
    @Volatile
    var latestScannerCallback: ((String) -> Unit)? = null
    private const val PREFS_NAME = "fth_package_library_prefs"
    private const val KEY_ACTIVE_PATH = "active_ftak_path"
    @JvmStatic
    fun getPackagesDirectory(context: Context): File {
        val dir = File(context.filesDir, "packages")
        dir.mkdirs()
        return dir
    }
    @JvmStatic
    fun getActivePackagePath(context: Context): String? {
        return context.getSharedPreferences(PREFS_NAME, 0)
            .getString(KEY_ACTIVE_PATH, null)
    }
    @JvmStatic
    fun setActivePackage(context: Context, file: File): String {
        context.getSharedPreferences(PREFS_NAME, 0)
            .edit()
            .putString(KEY_ACTIVE_PATH, file.absolutePath)
            .apply()
        ApkInstallerManager.extractAllApksFromFtakArchives(context)
        notifyHostViewModelRefresh(context)
        return "Ustawiono aktywny pakiet: ${file.name}"
    }
    @JvmStatic
    fun listAllPackages(context: Context): List<FtakLibraryEntry> {
        val rawFiles = mutableListOf<File>()
        collectFiles(context.filesDir, rawFiles)
        collectFiles(context.getExternalFilesDir(null), rawFiles)
        collectFiles(context.cacheDir, rawFiles)
        val activePath = getActivePackagePath(context)
        val df = SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.getDefault())
        val candidates = rawFiles.filter { f ->
            val n = f.name.lowercase()
            val isPkg = n.endsWith(".ftak") ||
                n.endsWith(".ftak.part") ||
                (n.endsWith(".zip") && !n.contains("atak_config") &&
                    !n.contains("picked_install"))
            isPkg && f.length() > 0L
        }.distinctBy { "${it.name.lowercase()}_${it.length()}" }
            .sortedByDescending { it.lastModified() }
        return candidates.mapIndexed { idx, f ->
            val isPart = f.name.endsWith(".part", true)
            var title = f.nameWithoutExtension
            var ver = "2.4"
            var apks = 0
            var maps = 0
            var overlays = 0
            if (!isPart) {
                try {
                    ZipFile(f).use { zip ->
                        val entries = zip.entries()
                        while (entries.hasMoreElements()) {
                            val e = entries.nextElement()
                            if (!e.isDirectory) {
                                val en = e.name.lowercase()
                                if (en.endsWith(".apk")) apks++
                                else if (en.endsWith(".kml") ||
                                    en.endsWith(".kmz") ||
                                    en.endsWith(".gpx")) overlays++
                                else if (en.endsWith(".sqlitedb") ||
                                    en.endsWith(".mbtiles") ||
                                    en.contains("maps/")) maps++
                            }
                        }
                        val mfEntry = zip.getEntry("manifest.json")
                            ?: zip.getEntry("META-INF/fieldtak.json")
                        if (mfEntry != null) {
                            val txt = zip.getInputStream(mfEntry)
                                .bufferedReader().readText()
                            val json = JSONObject(txt)
                            val proj = json.optJSONObject("project")
                            if (proj != null) {
                                title = proj.optString("name", title)
                                    .ifBlank { title }
                                ver = proj.optString("version", ver)
                                    .ifBlank { ver }
                            } else {
                                title = json.optString("name", title)
                                    .ifBlank { title }
                                ver = json.optString("version", ver)
                                    .ifBlank { ver }
                            }
                        }
                    }
                } catch (_: Exception) {}
            }
            val isAct = if (activePath != null) {
                f.absolutePath == activePath
            } else idx == 0 && !isPart
            FtakLibraryEntry(
                file = f,
                fileName = f.name,
                projectTitle = title,
                projectVersion = ver,
                sizeKb = maxOf(1L, f.length() / 1024L),
                modifiedDate = df.format(Date(f.lastModified())),
                apkCount = apks,
                mapCount = maps,
                overlayCount = overlays,
                isPartBuffer = isPart,
                isActive = isAct
            )
        }
    }
    @JvmStatic
    fun deletePackage(context: Context, entry: FtakLibraryEntry): String {
        val baseName = entry.file.nameWithoutExtension
        val targetName = entry.file.name
        val allFiles = mutableListOf<File>()
        collectFiles(context.filesDir, allFiles)
        collectFiles(context.getExternalFilesDir(null), allFiles)
        collectFiles(context.cacheDir, allFiles)
        var deletedCount = 0
        allFiles.forEach { f ->
            if (f.absolutePath == entry.file.absolutePath ||
                f.name.equals(targetName, true) ||
                f.name.equals("$targetName.part", true) ||
                f.name.equals("$baseName.json", true)) {
                if (f.delete()) deletedCount++
            }
        }
        deleteMatchingDirs(context.filesDir, baseName)
        deleteMatchingDirs(context.getExternalFilesDir(null), baseName)
        val extractedDir = File(
            context.getExternalFilesDir(null) ?: context.filesDir,
            "extracted_apks"
        )
        deleteRecursively(extractedDir)
        val active = getActivePackagePath(context)
        if (active == entry.file.absolutePath) {
            context.getSharedPreferences(PREFS_NAME, 0)
                .edit().remove(KEY_ACTIVE_PATH).apply()
        }
        notifyHostViewModelRefresh(context)
        return "Usuni\u0119to pakiet ${entry.fileName} (plik\u00F3w: $deletedCount)."
    }
    @JvmStatic
    fun deleteAllPackages(context: Context): String {
        val list = listAllPackages(context)
        var count = 0
        list.forEach { e ->
            deletePackage(context, e)
            count++
        }
        notifyHostViewModelRefresh(context)
        return "Wyczyszczono bibliotek\u0119 pakiet\u00F3w (usuni\u0119to: $count)."
    }
    @JvmStatic
    fun importFtakFromUri(context: Context, uri: Uri): String {
        return try {
            val stamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US)
                .format(Date())
            var displayName = "Pakiet_$stamp.ftak"
            context.contentResolver.query(
                uri, null, null, null, null
            )?.use { c ->
                val idx = c.getColumnIndex("_display_name")
                if (idx >= 0 && c.moveToFirst()) {
                    val n = c.getString(idx)
                    if (!n.isNullOrBlank()) displayName = n
                }
            }
            if (!displayName.endsWith(".ftak", true) &&
                !displayName.endsWith(".zip", true)) {
                displayName = "$displayName.ftak"
            }
            val dest = File(getPackagesDirectory(context), displayName)
            context.contentResolver.openInputStream(uri)?.use { inp ->
                FileOutputStream(dest).use { out -> inp.copyTo(out) }
            }
            setActivePackage(context, dest)
            forwardUriOrQrToHostViewModel(context, uri, null)
            "Zaimportowano pakiet do biblioteki: ${dest.name}"
        } catch (e: Exception) {
            "B\u0142\u0105d importu pakietu: ${e.message}"
        }
    }
    @JvmStatic
    fun processDecodedQrPayload(
        context: Context,
        rawQr: String,
        onProgress: ((String) -> Unit)? = null
    ): String {
        val trimmed = rawQr.trim()
        try {
            val cb = context.getSystemService(
                Context.CLIPBOARD_SERVICE
            ) as? ClipboardManager
            cb?.setPrimaryClip(
                ClipData.newPlainText("FieldTAK_QR", trimmed)
            )
        } catch (_: Exception) {}
        latestScannerCallback?.let { cb ->
            try { cb.invoke(trimmed) } catch (_: Exception) {}
        }
        forwardUriOrQrToHostViewModel(context, null, trimmed)
        var downloadUrl: String? = null
        var expectedSha: String? = null
        var pkgNameHint: String? = null
        if (trimmed.startsWith("http://", true) ||
            trimmed.startsWith("https://", true)) {
            downloadUrl = trimmed
        } else if (trimmed.startsWith("{")) {
            try {
                val j = JSONObject(trimmed)
                downloadUrl = j.optString("url", "")
                    .ifBlank { j.optString("packageUrl", "") }
                    .ifBlank { j.optString("downloadUrl", "") }
                    .ifBlank { null }
                expectedSha = j.optString("sha256", "")
                    .ifBlank { null }
                pkgNameHint = j.optString("name", "")
                    .ifBlank { j.optString("projectName", "") }
                    .ifBlank { null }
            } catch (_: Exception) {}
        }
        if (!downloadUrl.isNullOrBlank()) {
            return try {
                val cleanName = (pkgNameHint ?: downloadUrl.substringAfterLast("/").substringBefore("?"))
                    .replace(Regex("[^a-zA-Z0-9._-]"), "_")
                    .let { if (it.isBlank()) "pakiet_qr.ftak" else it }
                    .let {
                        if (it.endsWith(".ftak", true) || it.endsWith(".zip", true)) it
                        else "$it.ftak"
                    }
                val target = File(getPackagesDirectory(context), cleanName)
                onProgress?.invoke("Pobieranie $cleanName...")
                ResumablePackageDownloader.downloadToFileWithResume(
                    url = downloadUrl,
                    targetFile = target,
                    expectedSha256 = expectedSha
                ) { cur, tot ->
                    if (tot > 0) {
                        val pct = ((cur * 100L) / tot).toInt()
                        onProgress?.invoke("Pobieranie: $pct% (${cur / 1024} KB)")
                    }
                }
                setActivePackage(context, target)
                "Pobrano z kodu QR i dodano do biblioteki: ${target.name}"
            } catch (e: Exception) {
                "Odczytano kod QR ($downloadUrl), uwaga przy pobieraniu: ${e.message}"
            }
        }
        return "Odczytano kod QR i przekazano do aplikacji."
    }
    private fun forwardUriOrQrToHostViewModel(
        context: Context,
        uri: Uri?,
        qrText: String?
    ) {
        try {
            val act = context as? Activity ?: return
            act.runOnUiThread {
                try {
                    for (f in act.javaClass.declaredFields) {
                        f.isAccessible = true
                        val obj = f.get(act) ?: continue
                        if (obj.javaClass.name.contains("ViewModel", true)) {
                            for (m in obj.javaClass.methods) {
                                val n = m.name.lowercase()
                                if (qrText != null &&
                                    m.parameterTypes.size == 1 &&
                                    m.parameterTypes[0] == String::class.java &&
                                    (n.contains("qr") || n.contains("scan") || n.contains("provision"))) {
                                    try { m.invoke(obj, qrText); break } catch (_: Exception) {}
                                }
                                if (uri != null &&
                                    m.parameterTypes.size == 1 &&
                                    m.parameterTypes[0] == Uri::class.java &&
                                    n.contains("import")) {
                                    try { m.invoke(obj, uri); break } catch (_: Exception) {}
                                }
                            }
                        }
                    }
                } catch (_: Exception) {}
            }
        } catch (_: Exception) {}
    }
    private fun notifyHostViewModelRefresh(context: Context) {
        try {
            val act = context as? Activity ?: return
            act.runOnUiThread {
                try {
                    for (f in act.javaClass.declaredFields) {
                        f.isAccessible = true
                        val obj = f.get(act) ?: continue
                        if (obj.javaClass.name.contains("ViewModel", true)) {
                            for (m in obj.javaClass.methods) {
                                val n = m.name.lowercase()
                                if (m.parameterTypes.isEmpty() &&
                                    (n.contains("refresh") || n.contains("load") || n.contains("reload"))) {
                                    try { m.invoke(obj) } catch (_: Exception) {}
                                }
                            }
                        }
                    }
                } catch (_: Exception) {}
            }
        } catch (_: Exception) {}
    }
    private fun collectFiles(d: File?, out: MutableList<File>) {
        val list = d?.listFiles() ?: return
        for (c in list) {
            if (c.isDirectory) collectFiles(c, out) else out.add(c)
        }
    }
    private fun deleteMatchingDirs(root: File?, baseName: String) {
        val list = root?.listFiles() ?: return
        for (c in list) {
            if (c.isDirectory) {
                if (c.name.equals(baseName, true)) {
                    deleteRecursively(c)
                } else {
                    deleteMatchingDirs(c, baseName)
                }
            }
        }
    }
    private fun deleteRecursively(f: File?) {
        if (f == null || !f.exists()) return
        if (f.isDirectory) {
            f.listFiles()?.forEach { deleteRecursively(it) }
        }
        try { f.delete() } catch (_: Exception) {}
    }
}