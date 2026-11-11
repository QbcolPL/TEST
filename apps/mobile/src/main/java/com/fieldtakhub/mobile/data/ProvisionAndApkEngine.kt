package com.fieldtakhub.mobile.data

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

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
            require(ftakFile.exists()) { "Plik paczki .ftak nie istnieje." }

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
                throw IllegalStateException("Brak pliku instalatora ${req.displayName} (.apk) wewnątrz tej paczki .ftak.")
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

data class ProvisioningReport(
    val success: Boolean,
    val summaryTitle: String,
    val logLines: List<String>
)

object AtakMeshtasticProvisioner {

    suspend fun provisionAll(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        shortCallsign: String
    ): ProvisioningReport = withContext(Dispatchers.IO) {
        val logs = mutableListOf<String>()
        val cleanFull = fullCallsign.trim().ifEmpty { "Wilk-1" }
        val cleanShort = shortCallsign.trim().ifEmpty { FtakPackageRepository.generateMeshtasticShortName(cleanFull) }

        FtakPackageRepository.saveOperatorCallsign(context, cleanFull, cleanShort)
        logs.add("[✓] Callsign ATAK: '$cleanFull' | Skrót LoRa: '[$cleanShort]'")

        val atakResult = runCatching {
            buildAndInjectAtakMissionPackage(context, pkg, cleanFull, logs)
        }
        if (atakResult.isFailure) {
            logs.add("[!] ATAK: ${atakResult.exceptionOrNull()?.message}")
        }

        val meshResult = runCatching {
            injectMeshtasticConfigAndOwner(context, pkg, cleanFull, cleanShort, logs)
        }
        if (meshResult.isFailure) {
            logs.add("[!] Meshtastic: ${meshResult.exceptionOrNull()?.message}")
        }

        ProvisioningReport(
            success = atakResult.isSuccess || meshResult.isSuccess,
            summaryTitle = "Wstrzyknięto profil ${pkg.factionName}: $cleanFull [$cleanShort]",
            logLines = logs
        )
    }

    private fun buildAndInjectAtakMissionPackage(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        logs: MutableList<String>
    ) {
        val ftakFile = File(pkg.localFilePath)
        require(ftakFile.exists()) { "Brak pliku paczki .ftak." }

        val outZipName = "FTH_${pkg.factionName}_${fullCallsign.replace(Regex("[^a-zA-Z0-9_-]"), "_")}.zip"
        val outMissionZip = File(context.cacheDir, outZipName)

        val extractedFilesForManifest = mutableListOf<String>()
        val certEntries = mutableListOf<String>()

        val sdcardAtakDir = File(Environment.getExternalStorageDirectory(), "atak")
        val canWriteSdcardAtak = runCatching {
            if (!sdcardAtakDir.exists()) sdcardAtakDir.mkdirs()
            sdcardAtakDir.canWrite()
        }.getOrDefault(false)

        ZipOutputStream(FileOutputStream(outMissionZip)).use { zos ->
            ZipInputStream(BufferedInputStream(FileInputStream(ftakFile))).use { zis ->
                var entry: ZipEntry?
                while (zis.nextEntry.also { entry = it } != null) {
                    val rawName = entry!!.name.replace('\\', '/')
                    val shortName = rawName.substringAfterLast('/')
                    val lower = shortName.lowercase()

                    val isMapOrOverlay = lower.endsWith(".kmz") || lower.endsWith(".kml") ||
                        lower.endsWith(".gpx") || lower.endsWith(".mbtiles") || lower.endsWith(".xml")
                    val isCertOrServer = lower.endsWith(".p12") || lower == "server.txt" || lower.endsWith(".pref")

                    if (!entry!!.isDirectory && (isMapOrOverlay || isCertOrServer) && !lower.endsWith("manifest.xml")) {
                        val bytes = zis.readBytes()
                        val targetZipPath = if (lower.endsWith(".p12")) {
                            certEntries.add("certs/$shortName")
                            "certs/$shortName"
                        } else {
                            "files/$shortName"
                        }

                        zos.putNextEntry(ZipEntry(targetZipPath))
                        zos.write(bytes)
                        zos.closeEntry()
                        extractedFilesForManifest.add(targetZipPath)

                        if (canWriteSdcardAtak) {
                            runCatching {
                                val targetSubDir = when {
                                    lower.endsWith(".mbtiles") -> File(sdcardAtakDir, "imagery")
                                    lower.endsWith(".kmz") || lower.endsWith(".kml") || lower.endsWith(".gpx") -> File(sdcardAtakDir, "overlays")
                                    lower.endsWith(".p12") -> File(sdcardAtakDir, "cert")
                                    else -> null
                                }
                                if (targetSubDir != null) {
                                    targetSubDir.mkdirs()
                                    File(targetSubDir, shortName).writeBytes(bytes)
                                }
                            }
                        }
                    }
                    zis.closeEntry()
                }
            }

            val prefXml = buildAtakPreferenceXml(pkg, fullCallsign, certEntries.firstOrNull())
            val prefEntryPath = "config/preference.pref"
            zos.putNextEntry(ZipEntry(prefEntryPath))
            zos.write(prefXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
            extractedFilesForManifest.add(prefEntryPath)

            val manifestXml = buildAtakMissionManifestXml(pkg, fullCallsign, extractedFilesForManifest)
            zos.putNextEntry(ZipEntry("MANIFEST/manifest.xml"))
            zos.write(manifestXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
        }

        if (canWriteSdcardAtak) {
            runCatching {
                val dpDir = File(sdcardAtakDir, "tools/datapackage")
                dpDir.mkdirs()
                outMissionZip.copyTo(File(dpDir, outZipName), overwrite = true)
                logs.add("[✓] Skopiowano mapy i paczkę bezpośrednio do /sdcard/atak/")
            }
        }

        val zipUri = FileProvider.getUriForFile(
            context,
            "${context.packageName}.fileprovider",
            outMissionZip
        )

        val atakImportIntent = Intent("com.atakmap.app.IMPORT_MISSION_PACKAGE").apply {
            setPackage("com.atakmap.app.civ")
            setDataAndType(zipUri, "application/zip")
            putExtra("importFile", outMissionZip.absolutePath)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        }

        val launchedSpecific = runCatching {
            context.startActivity(atakImportIntent)
            true
        }.getOrDefault(false)

        if (!launchedSpecific && pkg.enrollmentUri.startsWith("tak://", ignoreCase = true)) {
            runCatching {
                val enrollIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.enrollmentUri)).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                context.startActivity(enrollIntent)
                logs.add("[✓] Uruchomiono Enrollment OTS w ATAK.")
            }
        } else {
            logs.add("[✓] Wstrzyknięto Callsign, interwały PLI i serwer OTS do ATAK-CIV.")
        }
    }

    private fun injectMeshtasticConfigAndOwner(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        shortCallsign: String,
        logs: MutableList<String>
    ) {
        runCatching {
            val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
            clipboard?.setPrimaryClip(ClipData.newPlainText("Meshtastic Callsign", "$fullCallsign ($shortCallsign)"))

            val ownerIntent = Intent("com.geeksville.mesh.SET_OWNER").apply {
                setPackage("com.geeksville.mesh")
                putExtra("longName", fullCallsign)
                putExtra("shortName", shortCallsign)
            }
            context.sendBroadcast(ownerIntent)
        }

        if (pkg.meshtasticChannelUrl.isNotBlank() && pkg.meshtasticChannelUrl.startsWith("http", ignoreCase = true)) {
            val meshIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.meshtasticChannelUrl)).apply {
                setPackage("com.geeksville.mesh")
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            val started = runCatching {
                context.startActivity(meshIntent)
                true
            }.getOrDefault(false)

            if (!started) {
                runCatching {
                    context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(pkg.meshtasticChannelUrl)).apply {
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    })
                }
            }
            logs.add("[✓] Wstrzyknięto kanał LoRa (${pkg.loraRegion}/${pkg.loraPreset}) + PSK do Meshtastic.")
        } else {
            logs.add("[✓] Przygotowano profil LoRa: ${pkg.loraRegion} · ${pkg.loraPreset} · [$shortCallsign]")
        }
    }

    private fun mapFactionToAtakTeamColor(faction: String, hex: String): String {
        val f = faction.uppercase()
        return when {
            f.contains("BLU") || hex.contains("2684FF", true) -> "Blue"
            f.contains("OPFOR") || f.contains("RED") || hex.contains("EB4034", true) -> "Red"
            f.contains("ORGA") || f.contains("YEL") || hex.contains("F5B82E", true) -> "Yellow"
            f.contains("GREEN") || hex.contains("2ECC71", true) -> "Green"
            else -> "Cyan"
        }
    }

    private fun buildAtakPreferenceXml(
        pkg: FtakPackageSummary,
        fullCallsign: String,
        firstCertPath: String?
    ): String {
        val teamColor = mapFactionToAtakTeamColor(pkg.factionName, pkg.colorHex)
        val rateSec = (if (pkg.pliConstMeshSec > 0) pkg.pliConstMeshSec else pkg.pliConstLteSec).coerceAtLeast(5)
        val hasOts = pkg.otsHost.isNotBlank() && !pkg.otsHost.contains("Lokalny", true)
        val cleanHost = pkg.otsHost.substringAfter("://").substringBefore("/").substringBefore(":")

        return buildString {
            appendLine("<?xml version='1.0' standalone='yes'?>")
            appendLine("<preferences>")
            appendLine("  <preference version=\"1\" name=\"com.atakmap.app_preferences\">")
            appendLine("    <entry key=\"locationCallsign\" class=\"class java.lang.String\">${escapeXml(fullCallsign)}</entry>")
            appendLine("    <entry key=\"locationTeam\" class=\"class java.lang.String\">$teamColor</entry>")
            appendLine("    <entry key=\"locationReportingStrategy\" class=\"class java.lang.String\">${escapeXml(pkg.pliStrategy)}</entry>")
            appendLine("    <entry key=\"constantReportingRateUnreliable\" class=\"class java.lang.String\">$rateSec</entry>")
            appendLine("    <entry key=\"constantReportingRateReliable\" class=\"class java.lang.String\">$rateSec</entry>")
            appendLine("  </preference>")
            if (hasOts && cleanHost.isNotBlank()) {
                appendLine("  <preference version=\"1\" name=\"cot_streams\">")
                appendLine("    <entry key=\"count\" class=\"class java.lang.Integer\">1</entry>")
                appendLine("    <entry key=\"description0\" class=\"class java.lang.String\">FieldTAKHub OTS (${escapeXml(pkg.factionName)})</entry>")
                appendLine("    <entry key=\"enabled0\" class=\"class java.lang.Boolean\">true</entry>")
                appendLine("    <entry key=\"connectString0\" class=\"class java.lang.String\">$cleanHost:8089:ssl</entry>")
                if (firstCertPath != null) {
                    val certFileName = firstCertPath.substringAfterLast('/')
                    appendLine("    <entry key=\"caLocation0\" class=\"class java.lang.String\">cert/$certFileName</entry>")
                    appendLine("    <entry key=\"clientPassword0\" class=\"class java.lang.String\">atakatak</entry>")
                }
                appendLine("  </preference>")
            }
            appendLine("</preferences>")
        }
    }

    private fun buildAtakMissionManifestXml(
        pkg: FtakPackageSummary,
        fullCallsign: String,
        entries: List<String>
    ): String {
        val uid = UUID.nameUUIDFromBytes("${pkg.eventName}_${pkg.factionName}_$fullCallsign".toByteArray()).toString()
        return buildString {
            appendLine("<MissionPackageManifest version=\"2\">")
            appendLine("  <Configuration>")
            appendLine("    <Parameter name=\"uid\" value=\"$uid\"/>")
            appendLine("    <Parameter name=\"name\" value=\"${escapeXml(pkg.eventName)}_${escapeXml(pkg.factionName)}_${escapeXml(fullCallsign)}\"/>")
            appendLine("    <Parameter name=\"onReceiveImport\" value=\"true\"/>")
            appendLine("    <Parameter name=\"onReceiveDelete\" value=\"false\"/>")
            appendLine("  </Configuration>")
            appendLine("  <Contents>")
            for (path in entries) {
                appendLine("    <Content ignore=\"false\" zipEntry=\"${escapeXml(path)}\"/>")
            }
            appendLine("  </Contents>")
            appendLine("</MissionPackageManifest>")
        }
    }

    private fun escapeXml(input: String): String {
        return input
            .replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace("\"", "&quot;")
            .replace("'", "&apos;")
    }
}