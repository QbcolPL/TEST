package org.fieldtak.hub.diagnostics
import android.content.Context
import android.content.Intent
import android.os.Build
import org.fieldtak.hub.util.ApkInstallerManager
import org.fieldtak.hub.util.FirstRunPermissionHelper
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
data class GeneratedLogReport(val fileName: String, val savedLocationDescription: String, val localFile: File, val fullText: String, val issuesCount: Int)
object ExtendedDiagnosticsManager {
    @JvmStatic fun generateAndSaveLog(context: Context): GeneratedLogReport {
        val now = Date(); val st = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(now); val fName = "FieldTAKHub_Diagnostyka_$st.log"
        val issues = mutableListOf<String>(); val sb = StringBuilder()
        if (!FirstRunPermissionHelper.isCameraReady(context)) issues.add("[UPRAWNIENIA] Brak Aparatu (QR).")
        if (!FirstRunPermissionHelper.isLocationReady(context)) issues.add("[UPRAWNIENIA] Brak Lokalizacji.")
        if (!FirstRunPermissionHelper.isNearbyBluetoothReady(context)) issues.add("[UPRAWNIENIA] Brak Bluetooth BLE.")
        if (!FirstRunPermissionHelper.isStorageAccessReady(context)) issues.add("[UPRAWNIENIA] Brak dostepu do plikow.")
        if (!FirstRunPermissionHelper.canInstallUnknownApps(context)) issues.add("[UPRAWNIENIA] Brak instalacji APK.")
        val scanned = ApkInstallerManager.scanAllAppsAndPlugins(context)
        scanned.forEach { if (!it.isInstalled) issues.add("[APLIKACJE] ${it.title} (${it.packageName}) NIE JEST ZAINSTALOWANY.") }
        sb.appendLine("=== FIELD TAK HUB 2.4 - DIAGNOSTYKA ===")
        sb.appendLine("Model: ${Build.MANUFACTURER} ${Build.MODEL}")
        if (issues.isEmpty()) sb.appendLine("[OK] Brak wykrytych blokad.") else issues.forEachIndexed { i, s -> sb.appendLine("${i + 1}. $s") }
        val txt = sb.toString()
        val dir = File(context.getExternalFilesDir(null) ?: context.filesDir, "logs").apply { mkdirs() }
        val lf = File(dir, fName).apply { writeText(txt, Charsets.UTF_8) }
        return GeneratedLogReport(fName, lf.absolutePath, lf, txt, issues.size)
    }
    @JvmStatic fun shareLogReport(context: Context, rep: GeneratedLogReport) {
        val i = Intent(Intent.ACTION_SEND).apply { type = "text/plain"; putExtra(Intent.EXTRA_SUBJECT, rep.fileName); putExtra(Intent.EXTRA_TEXT, rep.fullText); addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) }
        context.startActivity(Intent.createChooser(i, "Udostepnij .LOG").apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) })
    }
}