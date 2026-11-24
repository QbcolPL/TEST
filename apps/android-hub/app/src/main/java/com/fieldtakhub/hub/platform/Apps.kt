package com.fieldtakhub.hub.platform

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import androidx.core.content.IntentCompat
import androidx.core.content.pm.PackageInfoCompat
import com.fieldtakhub.hub.core.Atak
import com.fieldtakhub.hub.data.ApkInfo
import kotlinx.coroutines.channels.Channel
import java.io.File
import java.io.InputStream

data class InstalledApp(val packageName: String, val versionName: String, val versionCode: Long)

object AppInspector {
    @Suppress("DEPRECATION")
    private fun info(context: Context, packageName: String): PackageInfo? = try {
        context.packageManager.getPackageInfo(packageName, 0)
    } catch (e: PackageManager.NameNotFoundException) {
        null
    }

    fun installed(context: Context, packageName: String?): InstalledApp? {
        if (packageName.isNullOrBlank()) return null
        val i = info(context, packageName) ?: return null
        return InstalledApp(packageName, i.versionName.orEmpty(), PackageInfoCompat.getLongVersionCode(i))
    }

    fun findAtak(context: Context): InstalledApp? = Atak.PACKAGE_CANDIDATES.firstNotNullOfOrNull { installed(context, it) }

    /** Package name and version read from an APK file. */
    @Suppress("DEPRECATION")
    fun archiveInfo(context: Context, apk: File, entry: String): ApkInfo? {
        val pm = context.packageManager
        val i = pm.getPackageArchiveInfo(apk.absolutePath, 0) ?: return null
        val label = i.applicationInfo?.let { ai ->
            ai.sourceDir = apk.absolutePath
            ai.publicSourceDir = apk.absolutePath
            runCatching { ai.loadLabel(pm).toString() }.getOrNull()
        } ?: i.packageName
        return ApkInfo(entry, i.packageName, i.versionName.orEmpty(), PackageInfoCompat.getLongVersionCode(i), label)
    }

    fun launchIntent(context: Context, packageName: String): Intent? =
        context.packageManager.getLaunchIntentForPackage(packageName)

    fun storeIntent(packageName: String): Intent =
        Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=$packageName"))

    fun storeWebIntent(packageName: String): Intent =
        Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=$packageName"))

    fun canInstallPackages(context: Context): Boolean = context.packageManager.canRequestPackageInstalls()

    fun installPermissionIntent(context: Context): Intent =
        Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
}

sealed class InstallEvent {
    data class NeedsUser(val intent: Intent) : InstallEvent()
    data class Finished(val packageName: String?, val success: Boolean, val message: String) : InstallEvent()
}

/** Installs APKs through PackageInstaller sessions. The system always asks the user to confirm. */
object ApkInstaller {
    const val ACTION = "com.fieldtakhub.hub.INSTALL_RESULT"
    const val EXTRA_EXPECTED = "expected_package"

    val events = Channel<InstallEvent>(Channel.BUFFERED)

    fun install(context: Context, input: InputStream, sizeBytes: Long, expectedPackage: String?) {
        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL)
        if (!expectedPackage.isNullOrBlank()) params.setAppPackageName(expectedPackage)
        if (sizeBytes > 0) params.setSize(sizeBytes)
        if (Build.VERSION.SDK_INT >= 31) {
            params.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
        }
        val sessionId = installer.createSession(params)
        installer.openSession(sessionId).use { session ->
            input.use { src ->
                session.openWrite("base.apk", 0, if (sizeBytes > 0) sizeBytes else -1).use { out ->
                    src.copyTo(out, 256 * 1024)
                    session.fsync(out)
                }
            }
            val intent = Intent(context, InstallResultReceiver::class.java)
                .setAction(ACTION)
                .putExtra(EXTRA_EXPECTED, expectedPackage)
            val flags = PendingIntent.FLAG_UPDATE_CURRENT or (if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0)
            val sender = PendingIntent.getBroadcast(context, sessionId, intent, flags)
            session.commit(sender.intentSender)
        }
    }
}

class InstallResultReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val expected = intent.getStringExtra(ApkInstaller.EXTRA_EXPECTED)
        val pkg = intent.getStringExtra(PackageInstaller.EXTRA_PACKAGE_NAME) ?: expected
        when (val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                val confirm = IntentCompat.getParcelableExtra(intent, Intent.EXTRA_INTENT, Intent::class.java)
                if (confirm != null) ApkInstaller.events.trySend(InstallEvent.NeedsUser(confirm))
            }
            PackageInstaller.STATUS_SUCCESS ->
                ApkInstaller.events.trySend(InstallEvent.Finished(pkg, true, "Zainstalowano ${pkg.orEmpty()}"))
            else -> {
                val raw = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE).orEmpty()
                val message = when (status) {
                    PackageInstaller.STATUS_FAILURE_ABORTED -> "Instalację anulowano."
                    PackageInstaller.STATUS_FAILURE_CONFLICT ->
                        "Na telefonie jest wersja podpisana innym kluczem. Odinstaluj ją i spróbuj ponownie (ustawienia tej aplikacji zostaną utracone)."
                    PackageInstaller.STATUS_FAILURE_INCOMPATIBLE -> "Ta aplikacja nie jest zgodna z tym telefonem."
                    PackageInstaller.STATUS_FAILURE_STORAGE -> "Za mało miejsca na instalację."
                    PackageInstaller.STATUS_FAILURE_INVALID -> "Plik APK w paczce jest uszkodzony."
                    PackageInstaller.STATUS_FAILURE_BLOCKED -> "Instalację zablokował system lub administrator telefonu."
                    else -> "Instalacja nie powiodła się. $raw".trim()
                }
                ApkInstaller.events.trySend(InstallEvent.Finished(pkg, false, message))
            }
        }
    }
}
