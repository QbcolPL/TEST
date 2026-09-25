package org.fieldtak.hub.diagnostics
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import org.fieldtak.hub.util.ApkInstallerManager
import org.fieldtak.hub.util.FirstRunPermissionHelper
import org.fieldtak.hub.util.FtakPackageLibraryManager
import java.io.BufferedReader
import java.io.File
import java.io.InputStreamReader
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
data class GeneratedLogReport(
    val fileName: String,
    val savedLocationDescription: String,
    val localFile: File,
    val fullText: String,
    val issuesCount: Int
)
object ExtendedDiagnosticsManager {
    @JvmStatic
    fun generateAndSaveLog(context: Context): GeneratedLogReport {
        val now = Date()
        val st = SimpleDateFormat(
            "yyyyMMdd_HHmmss", Locale.US
        ).format(now)
        val dt = SimpleDateFormat(
            "yyyy-MM-dd HH:mm:ss", Locale.getDefault()
        ).format(now)
        val fName = "FieldTAKHub_Diagnostyka_$st.log"
        val issues = mutableListOf<String>()
        val sb = StringBuilder()
        val camOk = FirstRunPermissionHelper.isCameraReady(context)
        val locOk = FirstRunPermissionHelper.isLocationReady(context)
        val bleOk =
            FirstRunPermissionHelper.isNearbyBluetoothReady(context)
        val stgOk =
            FirstRunPermissionHelper.isStorageAccessReady(context)
        val insOk =
            FirstRunPermissionHelper.canInstallUnknownApps(context)
        if (!camOk) issues.add("[UPRAWNIENIA] Brak Aparatu (QR).")
        if (!locOk) issues.add("[UPRAWNIENIA] Brak Lokalizacji.")
        if (!bleOk) issues.add("[UPRAWNIENIA] Brak Bluetooth BLE.")
        if (!stgOk) issues.add("[UPRAWNIENIA] Brak dost\u0119pu do plik\u00F3w.")
        if (!insOk) issues.add("[UPRAWNIENIA] Brak instalacji APK.")
        val scanned = ApkInstallerManager.scanAllAppsAndPlugins(context)
        scanned.forEach { entry ->
            if (!entry.isInstalled) {
                issues.add(
                    "[APLIKACJE] ${entry.title} (${entry.packageName}) " +
                    "NIE JEST ZAINSTALOWANY."
                )
            } else if (entry.needsInstallOrUpdate) {
                issues.add(
                    "[AKTUALIZACJA] ${entry.title}: " +
                    "w telefonie=${entry.installedVersion}, " +
                    "w paczce=${entry.bundledVersion}."
                )
            }
        }
        val pkgs = FtakPackageLibraryManager.listAllPackages(context)
        sb.appendLine("=== FIELD TAK HUB 2.4 - DIAGNOSTYKA ===")
        sb.appendLine("Data: $dt")
        sb.appendLine("Model: ${Build.MANUFACTURER} ${Build.MODEL}")
        sb.appendLine("")
        sb.appendLine("--- 1. DLACZEGO CO\u015A NIE DZIA\u0141A ---")
        if (issues.isEmpty()) {
            sb.appendLine("[OK] Brak wykrytych blokad.")
        } else {
            issues.forEachIndexed { i, s ->
                sb.appendLine("${i + 1}. $s")
            }
        }
        sb.appendLine("")
        sb.appendLine("--- 2. BIBLIOTEKA PAKIET\u00D3W .FTAK (${pkgs.size}) ---")
        pkgs.forEach { p ->
            sb.appendLine("- ${p.fileName} (${p.sizeKb} KB, projekt=${p.projectTitle} v${p.projectVersion}, APK=${p.apkCount})")
        }
        sb.appendLine("")
        sb.appendLine("--- 3. WYKRYTE APLIKACJE I PLUGINY ---")
        scanned.forEach { e ->
            sb.appendLine(
                "- ${e.title} (${e.packageName}): " +
                "zainstalowana=${e.installedVersion ?: "BRAK"}, " +
                "plik APK=${e.localApkFile?.name ?: "brak"}"
            )
        }
        sb.appendLine("")
        sb.appendLine("--- 4. LOGCAT ---")
        sb.appendLine(readLogcat())
        val txt = sb.toString()
        val dir = File(
            context.getExternalFilesDir(null) ?: context.filesDir,
            "logs"
        )
        dir.mkdirs()
        val lf = File(dir, fName)
        lf.writeText(txt, Charsets.UTF_8)
        val desc = saveDownloads(context, fName, txt, lf)
        return GeneratedLogReport(
            fName, desc, lf, txt, issues.size
        )
    }
    @JvmStatic
    fun shareLogReport(
        context: Context,
        rep: GeneratedLogReport
    ) {
        val i = Intent(Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(Intent.EXTRA_SUBJECT, rep.fileName)
            putExtra(Intent.EXTRA_TEXT, rep.fullText)
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        val ch = Intent.createChooser(i, "Udost\u0119pnij .LOG")
        ch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(ch)
    }
    private fun saveDownloads(
        context: Context,
        fName: String,
        txt: String,
        fb: File
    ): String {
        try {
            if (Build.VERSION.SDK_INT >= 29) {
                val cv = ContentValues().apply {
                    put(MediaStore.Downloads.DISPLAY_NAME, fName)
                    put(MediaStore.Downloads.MIME_TYPE, "text/plain")
                    put(
                        MediaStore.Downloads.RELATIVE_PATH,
                        Environment.DIRECTORY_DOWNLOADS +
                            "/FieldTAKHub_Logs"
                    )
                }
                val cr = context.contentResolver
                val u = cr.insert(
                    MediaStore.Downloads.EXTERNAL_CONTENT_URI, cv
                )
                if (u != null) {
                    cr.openOutputStream(u)?.use {
                        it.write(txt.toByteArray(Charsets.UTF_8))
                    }
                    return "Pobrane/FieldTAKHub_Logs/$fName"
                }
            }
        } catch (_: Exception) {}
        return fb.absolutePath
    }
    private fun readLogcat(): String {
        return try {
            val p = Runtime.getRuntime().exec(
                arrayOf("logcat", "-d", "-t", "100", "-v", "time")
            )
            val r = BufferedReader(InputStreamReader(p.inputStream))
            r.readLines().takeLast(80).joinToString("\n")
        } catch (e: Exception) { "B\u0142\u0105d logcat: ${e.message}" }
    }
}