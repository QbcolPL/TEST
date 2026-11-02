package org.fieldtak.hub.util
import android.content.Context
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.pm.PackageInfoCompat
import java.io.File
enum class ApkInstallStatus {
    NOT_INSTALLED,
    UP_TO_DATE,
    UPDATE_AVAILABLE,
    NEWER_ALREADY_INSTALLED,
    INVALID_APK
}
data class ApkVersionCheckResult(
    val packageName: String,
    val appLabel: String,
    val bundledVersionName: String,
    val bundledVersionCode: Long,
    val installedVersionName: String?,
    val installedVersionCode: Long?,
    val status: ApkInstallStatus,
    val userSuggestionPl: String
) {
    val shouldSuggestUpdate: Boolean
        get() = status == ApkInstallStatus.UPDATE_AVAILABLE ||
            status == ApkInstallStatus.NOT_INSTALLED
}
object ApkVersionInspector {
    @JvmStatic
    fun inspectBundledApk(
        context: Context,
        apkFile: File
    ): ApkVersionCheckResult {
        if (!apkFile.exists() || apkFile.length() <= 0L) {
            return ApkVersionCheckResult(
                "", apkFile.name, "", 0L, null, null,
                ApkInstallStatus.INVALID_APK,
                "Nieprawidlowy plik APK"
            )
        }
        val pm = context.packageManager
        val arch: PackageInfo = try {
            if (Build.VERSION.SDK_INT >= 33) {
                pm.getPackageArchiveInfo(
                    apkFile.absolutePath,
                    PackageManager.PackageInfoFlags.of(0L)
                )
            } else {
                @Suppress("DEPRECATION")
                pm.getPackageArchiveInfo(apkFile.absolutePath, 0)
            }
        } catch (_: Exception) { null }
            ?: return ApkVersionCheckResult(
                "", apkFile.name, "", 0L, null, null,
                ApkInstallStatus.INVALID_APK,
                "Blad odczytu APK"
            )
        val pkg = arch.packageName ?: ""
        val bName = arch.versionName ?: "0.0.0"
        val bCode = PackageInfoCompat.getLongVersionCode(arch)
        arch.applicationInfo?.let {
            it.sourceDir = apkFile.absolutePath
            it.publicSourceDir = apkFile.absolutePath
        }
        val label = try {
            arch.applicationInfo?.let {
                pm.getApplicationLabel(it).toString()
            } ?: pkg
        } catch (_: Exception) { pkg }
        val inst: PackageInfo? = try {
            if (Build.VERSION.SDK_INT >= 33) {
                pm.getPackageInfo(
                    pkg,
                    PackageManager.PackageInfoFlags.of(0L)
                )
            } else {
                @Suppress("DEPRECATION")
                pm.getPackageInfo(pkg, 0)
            }
        } catch (_: Exception) { null }
        if (inst == null) {
            return ApkVersionCheckResult(
                pkg, label, bName, bCode, null, null,
                ApkInstallStatus.NOT_INSTALLED,
                "Brak w telefonie. Zalecana instalacja $bName."
            )
        }
        val iName = inst.versionName ?: "0.0.0"
        val iCode = PackageInfoCompat.getLongVersionCode(inst)
        return if (bCode > iCode) {
            ApkVersionCheckResult(
                pkg, label, bName, bCode, iName, iCode,
                ApkInstallStatus.UPDATE_AVAILABLE,
                "Dostepna aktualizacja ($iName -> $bName)!"
            )
        } else {
            ApkVersionCheckResult(
                pkg, label, bName, bCode, iName, iCode,
                ApkInstallStatus.UP_TO_DATE,
                "Wersja aktualna ($iName)."
            )
        }
    }
}