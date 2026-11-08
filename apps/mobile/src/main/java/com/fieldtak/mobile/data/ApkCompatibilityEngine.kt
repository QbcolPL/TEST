package com.fieldtak.mobile.data

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream

object ApkCompatibilityEngine {

    fun checkCompatibility(context: Context, pkg: FtakPackageSummary): List<InstalledApkStatus> {
        val pm = context.packageManager
        val zipFile = File(pkg.localFilePath)
        val zipEntryNames = mutableSetOf<String>()
        if (zipFile.exists()) {
            runCatching {
                ZipInputStream(BufferedInputStream(FileInputStream(zipFile))).use { zis ->
                    var e: ZipEntry?
                    while (zis.nextEntry.also { e = it } != null) {
                        zipEntryNames.add(e!!.name.replace('\\', '/'))
                        zis.closeEntry()
                    }
                }
            }
        }

        return pkg.requiredPackages.map { req ->
            val installedVer = getInstalledVersionName(pm, req.packageName)
            val isInstalled = installedVer != null
            val isUpToDate = isInstalled && compareVersions(installedVer!!, req.requiredVersion) >= 0
            val hasBundled = req.bundledZipPath.isNotBlank() && zipEntryNames.any {
                it.equals(req.bundledZipPath.replace('\\', '/'), ignoreCase = true) ||
                it.substringAfterLast('/').equals(req.bundledZipPath.substringAfterLast('/'), ignoreCase = true)
            }

            InstalledApkStatus(
                item = req,
                installedVersion = installedVer,
                isInstalled = isInstalled,
                isUpToDate = isUpToDate,
                hasBundledApkInFtak = hasBundled
            )
        }
    }

    private fun getInstalledVersionName(pm: PackageManager, packageName: String): String? {
        if (packageName.isBlank()) return null
        return try {
            val info = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                pm.getPackageInfo(packageName, PackageManager.PackageInfoFlags.of(0))
            } else {
                @Suppress("DEPRECATION")
                pm.getPackageInfo(packageName, 0)
            }
            info.versionName ?: "1.0"
        } catch (_: PackageManager.NameNotFoundException) {
            null
        }
    }

    fun compareVersions(installed: String, required: String): Int {
        val cleanInst = installed.substringBefore('-').split('.').map { it.filter { c -> c.isDigit() }.toIntOrNull() ?: 0 }
        val cleanReq = required.substringBefore('-').split('.').map { it.filter { c -> c.isDigit() }.toIntOrNull() ?: 0 }
        val maxLen = maxOf(cleanInst.size, cleanReq.size)
        for (i in 0 until maxLen) {
            val v1 = cleanInst.getOrElse(i) { 0 }
            val v2 = cleanReq.getOrElse(i) { 0 }
            if (v1 != v2) return v1.compareTo(v2)
        }
        return 0
    }

    suspend fun extractAndInstallBundledApk(
        context: Context,
        pkg: FtakPackageSummary,
        req: RequiredApkItem
    ): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val ftakFile = File(pkg.localFilePath)
            require(ftakFile.exists()) { "Plik paczki .ftak nie istnieje: ${pkg.localFilePath}" }

            val outApkFile = File(context.cacheDir, "${req.role}_${req.packageName}.apk")
            var extracted = false

            ZipInputStream(BufferedInputStream(FileInputStream(ftakFile))).use { zis ->
                var entry: ZipEntry?
                while (zis.nextEntry.also { entry = it } != null) {
                    val entryPath = entry!!.name.replace('\\', '/')
                    val matchesExact = req.bundledZipPath.isNotBlank() &&
                        (entryPath.equals(req.bundledZipPath.replace('\\', '/'), ignoreCase = true) ||
                         entryPath.substringAfterLast('/').equals(req.bundledZipPath.substringAfterLast('/'), ignoreCase = true))
                    val matchesFallback = entryPath.endsWith(".apk", ignoreCase = true) &&
                        ((req.role.contains("ATAK", true) && entryPath.contains("atak", true) && !entryPath.contains("plugin", true)) ||
                         (req.role.contains("MESH", true) && entryPath.contains("mesh", true) && !entryPath.contains("plugin", true)) ||
                         (req.role.contains("PLUGIN", true) && entryPath.contains("plugin", true)))

                    if (matchesExact || matchesFallback) {
                        FileOutputStream(outApkFile).use { fos ->
                            zis.copyTo(fos)
                        }
                        extracted = true
                        break
                    }
                    zis.closeEntry()
                }
            }

            if (!extracted) {
                throw IllegalStateException("Brak instalatora ${req.displayName} (.apk) wewnątrz tej paczki .ftak.")
            }

            val apkUri = FileProvider.getUriForFile(
                context,
                "${context.packageName}.fileprovider",
                outApkFile
            )

            val installIntent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(apkUri, "application/vnd.android.package-archive")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(installIntent)
        }
    }
}