package org.fieldtak.hub.util
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
data class FtakLibraryEntry(val file: File, val fileName: String, val projectTitle: String, val projectVersion: String, val sizeKb: Long, val modifiedDate: String, val apkCount: Int, val mapCount: Int, val overlayCount: Int, val isPartBuffer: Boolean, val isActive: Boolean)
object FtakPackageLibraryManager {
    @Volatile var latestScannerCallback: ((String) -> Unit)? = null
    private const val PREFS_NAME = "fth_package_library_prefs"
    private const val KEY_ACTIVE_PATH = "active_ftak_path"
    @JvmStatic fun getPackagesDirectory(context: Context): File = File(context.filesDir, "packages").apply { mkdirs() }
    @JvmStatic fun setActivePackage(context: Context, file: File): String {
        context.getSharedPreferences(PREFS_NAME, 0).edit().putString(KEY_ACTIVE_PATH, file.absolutePath).apply()
        ApkInstallerManager.extractAllApksFromFtakArchives(context)
        return "Aktywny pakiet: ${file.name}"
    }
    @JvmStatic fun listAllPackages(context: Context): List<FtakLibraryEntry> {
        val raw = mutableListOf<File>()
        collectFiles(context.filesDir, raw); collectFiles(context.getExternalFilesDir(null), raw); collectFiles(context.cacheDir, raw)
        val active = context.getSharedPreferences(PREFS_NAME, 0).getString(KEY_ACTIVE_PATH, null)
        val df = SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.getDefault())
        val list = raw.filter { f ->
            val n = f.name.lowercase()
            val isFtak = if (n.endsWith(".ftak")) true else n.endsWith(".ftak.part")
            val isZip = n.endsWith(".zip") && !n.contains("atak_config") && !n.contains("picked_install")
            val validExt = if (isFtak) true else isZip
            validExt && f.length() > 0L
        }.distinctBy { "${it.name.lowercase()}_${it.length()}" }.sortedByDescending { it.lastModified() }
        return list.mapIndexed { idx, f ->
            val isPart = f.name.endsWith(".part", true)
            var title = f.nameWithoutExtension; var ver = "2.4"; var apks = 0; var maps = 0; var overlays = 0
            if (!isPart) {
                try {
                    ZipFile(f).use { zip ->
                        val entries = zip.entries()
                        while (entries.hasMoreElements()) {
                            val e = entries.nextElement()
                            if (!e.isDirectory) {
                                val en = e.name.lowercase()
                                if (en.endsWith(".apk")) apks++
                                else if (en.endsWith(".kml")) overlays++
                                else if (en.endsWith(".kmz")) overlays++
                                else if (en.endsWith(".gpx")) overlays++
                                else if (en.endsWith(".sqlitedb")) maps++
                                else if (en.endsWith(".mbtiles")) maps++
                                else if (en.contains("maps/")) maps++
                            }
                        }
                        val mf = zip.getEntry("manifest.json") ?: zip.getEntry("META-INF/fieldtak.json")
                        if (mf != null) {
                            val j = JSONObject(zip.getInputStream(mf).bufferedReader().readText())
                            val p = j.optJSONObject("project")
                            title = (p?.optString("name", title) ?: j.optString("name", title)).ifBlank { title }
                            ver = (p?.optString("version", ver) ?: j.optString("version", ver)).ifBlank { ver }
                        }
                    }
                } catch (_: Exception) {}
            }
            val isAct = if (active != null) f.absolutePath == active else (idx == 0 && !isPart)
            FtakLibraryEntry(f, f.name, title, ver, maxOf(1L, f.length() / 1024L), df.format(Date(f.lastModified())), apks, maps, overlays, isPart, isAct)
        }
    }
    @JvmStatic fun deletePackage(context: Context, entry: FtakLibraryEntry): String {
        val raw = mutableListOf<File>()
        collectFiles(context.filesDir, raw); collectFiles(context.getExternalFilesDir(null), raw); collectFiles(context.cacheDir, raw)
        var cnt = 0
        raw.forEach { f ->
            var match = (f.absolutePath == entry.file.absolutePath)
            if (!match && f.name.equals(entry.fileName, true)) match = true
            if (!match && f.name.equals("${entry.fileName}.part", true)) match = true
            if (match) {
                if (f.delete()) cnt++
            }
        }
        deleteRec(File(context.getExternalFilesDir(null) ?: context.filesDir, "extracted_apks"))
        return "Usunieto pakiet ${entry.fileName} ($cnt)."
    }
    @JvmStatic fun deleteAllPackages(context: Context): String {
        val l = listAllPackages(context); l.forEach { deletePackage(context, it) }
        return "Usunieto wszystkie pakiety (${l.size})."
    }
    @JvmStatic fun importFtakFromUri(context: Context, uri: Uri): String {
        return try {
            val dest = File(getPackagesDirectory(context), "Pakiet_${SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())}.ftak")
            context.contentResolver.openInputStream(uri)?.use { i -> FileOutputStream(dest).use { o -> i.copyTo(o) } }
            setActivePackage(context, dest)
            "Zaimportowano: ${dest.name}"
        } catch (e: Exception) { "Blad importu: ${e.message}" }
    }
    @JvmStatic fun processDecodedQrPayload(context: Context, rawQr: String, onProgress: ((String) -> Unit)? = null): String {
        val t = rawQr.trim()
        latestScannerCallback?.let { try { it(t) } catch (_: Exception) {} }
        val isHttp = if (t.startsWith("http://", true)) true else t.startsWith("https://", true)
        var dlUrl: String? = if (isHttp) t else null
        var sha: String? = null
        if (t.startsWith("{")) {
            try {
                val j = JSONObject(t)
                dlUrl = j.optString("url", "").ifBlank { j.optString("packageUrl", "") }.ifBlank { j.optString("downloadUrl", "") }.ifBlank { null }
                sha = j.optString("sha256", "").ifBlank { null }
            } catch (_: Exception) {}
        }
        if (!dlUrl.isNullOrBlank()) {
            return try {
                val target = File(getPackagesDirectory(context), "pakiet_qr_${System.currentTimeMillis() % 100000}.ftak")
                ResumablePackageDownloader.downloadToFileWithResume(dlUrl, target, sha) { c, tot -> if (tot > 0) onProgress?.invoke("Pobieranie: ${(c * 100L) / tot}%") }
                setActivePackage(context, target)
                "Pobrano pakiet z QR: ${target.name}"
            } catch (e: Exception) { "Odczytano QR ($dlUrl): ${e.message}" }
        }
        return "Odczytano kod QR."
    }
    private fun collectFiles(d: File?, out: MutableList<File>) { d?.listFiles()?.forEach { if (it.isDirectory) collectFiles(it, out) else out.add(it) } }
    private fun deleteRec(f: File?) {
        if (f == null) return
        if (!f.exists()) return
        if (f.isDirectory) f.listFiles()?.forEach { deleteRec(it) }
        try { f.delete() } catch (_: Exception) {}
    }
}