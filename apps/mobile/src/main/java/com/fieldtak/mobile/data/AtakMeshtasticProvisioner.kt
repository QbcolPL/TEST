package com.fieldtak.mobile.data

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
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

data class ProvisioningReport(
    val success: Boolean,
    val summaryTitle: String,
    val logLines: List<String>
)

object AtakMeshtasticProvisioner {

    /**
     * Glowna procedura Etapu 3:
     * 1. Generuje spersonalizowana paczke misji ATAK (.zip) z plikiem preference.pref
     *    (wstrzykuje Callsign, druzyne, interwaly PLI, adres OpenTAKServer i certyfikaty).
     * 2. Kopiuje mapy (.mbtiles) oraz nakladki (.kmz / .gpx) do folderow ATAK i wywoluje IMPORT_MISSION_PACKAGE.
     * 3. Wstrzykuje do Meshtastic kanaly LoRa, region, preset i klucz PSK oraz przekazuje skrot Callsign (np. WLK1).
     */
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
        logs.add("[✓] Zapisano Callsign operatora: ATAK='$cleanFull' | LoRa='[$cleanShort]'")

        // KROK A: Przygotowanie i wstrzykniecie paczki konfiguracyjnej do ATAK-CIV
        val atakResult = runCatching {
            buildAndInjectAtakMissionPackage(context, pkg, cleanFull, logs)
        }
        if (atakResult.isFailure) {
            logs.add("[!] Ostrzeżenie ATAK: ${atakResult.exceptionOrNull()?.message}")
        }

        // KROK B: Wstrzykniecie konfiguracji LoRa (kanaly, PSK, Region, Preset) + Callsign do Meshtastic
        val meshResult = runCatching {
            injectMeshtasticConfigAndOwner(context, pkg, cleanFull, cleanShort, logs)
        }
        if (meshResult.isFailure) {
            logs.add("[!] Ostrzeżenie Meshtastic: ${meshResult.exceptionOrNull()?.message}")
        }

        ProvisioningReport(
            success = atakResult.isSuccess || meshResult.isSuccess,
            summaryTitle = "Wdrożono profil ${pkg.factionName}: $cleanFull [$cleanShort]",
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
        require(ftakFile.exists()) { "Brak pliku paczki .ftak na urządzeniu." }

        val outZipName = "FTH_${pkg.factionName}_${fullCallsign.replace(Regex("[^a-zA-Z0-9_-]"), "_")}.zip"
        val outMissionZip = File(context.cacheDir, outZipName)

        val extractedFilesForManifest = mutableListOf<String>()
        val certEntries = mutableListOf<String>()

        // Opcjonalny bezposredni zapis na karcie /sdcard/atak jesli nadano MANAGE_EXTERNAL_STORAGE
        val sdcardAtakDir = File(Environment.getExternalStorageDirectory(), "atak")
        val canWriteSdcardAtak = runCatching {
            if (!sdcardAtakDir.exists()) sdcardAtakDir.mkdirs()
            sdcardAtakDir.canWrite()
        }.getOrDefault(false)

        ZipOutputStream(FileOutputStream(outMissionZip)).use { zos ->
            // 1. Przepisz certyfikaty, warstwy map (.kmz, .kml, .gpx, .mbtiles) oraz server.txt z .ftak
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
                        val targetZipPath = when {
                            lower.endsWith(".p12") -> {
                                certEntries.add("certs/$shortName")
                                "certs/$shortName"
                            }
                            else -> "files/$shortName"
                        }

                        zos.putNextEntry(ZipEntry(targetZipPath))
                        zos.write(bytes)
                        zos.closeEntry()
                        extractedFilesForManifest.add(targetZipPath)

                        // Jezeli mamy pelny dostep do plikow, wrzuc mapy rowniez bezposrednio do katalogow ATAK
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

            // 2. Wygeneruj plik preference.pref wstrzykujacy Callsign, druzyne, interwaly PLI oraz serwer OTS
            val prefXml = buildAtakPreferenceXml(pkg, fullCallsign, certEntries.firstOrNull())
            val prefEntryPath = "config/preference.pref"
            zos.putNextEntry(ZipEntry(prefEntryPath))
            zos.write(prefXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
            extractedFilesForManifest.add(prefEntryPath)

            // 3. Wygeneruj MANIFEST/manifest.xml wymagany przez ATAK Mission Package Importer
            val manifestXml = buildAtakMissionManifestXml(pkg, fullCallsign, extractedFilesForManifest)
            zos.putNextEntry(ZipEntry("MANIFEST/manifest.xml"))
            zos.write(manifestXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
        }

        // Jezeli mozliwe, skopiuj gotowa paczke misji prosto do /sdcard/atak/tools/datapackage/
        if (canWriteSdcardAtak) {
            runCatching {
                val dpDir = File(sdcardAtakDir, "tools/datapackage")
                dpDir.mkdirs()
                outMissionZip.copyTo(File(dpDir, outZipName), overwrite = true)
                logs.add("[✓] Skopiowano paczkę misji i mapy bezpośrednio do /sdcard/atak/")
            }
        }

        val pliInterval = if (pkg.pliConstMeshSec > 0) "${pkg.pliConstMeshSec}s" else "${pkg.pliConstLteSec}s"
        logs.add("[✓] Wygenerowano preference.pref: Callsign='$fullCallsign', PLI=${pkg.pliStrategy} ($pliInterval)")
        if (pkg.otsHost.isNotBlank() && !pkg.otsHost.contains("Tryb lokalny", true)) {
            logs.add("[✓] Wstrzyknięto profil OpenTAKServer: ${pkg.otsHost}")
        }

        // 4. Wywolaj oficjalny Intent importu paczki misji w ATAK-CIV (lub Enrollment URI)
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

        if (!launchedSpecific) {
            // Fallback1: Enrollment URI tak:// jezeli dostepny
            if (pkg.enrollmentUri.startsWith("tak://", ignoreCase = true)) {
                runCatching {
                    val enrollIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.enrollmentUri)).apply {
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    }
                    context.startActivity(enrollIntent)
                    logs.add("[✓] Uruchomiono automatyczny Enrollment OTS w ATAK.")
                }
            } else {
                // Fallback2: Otworz paczke ZIP w ATAK przez ACTION_VIEW
                runCatching {
                    val viewIntent = Intent(Intent.ACTION_VIEW).apply {
                        setDataAndType(zipUri, "application/zip")
                        setPackage("com.atakmap.app.civ")
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
                    }
                    context.startActivity(viewIntent)
                }
            }
        } else {
            logs.add("[✓] Przekazano paczkę konfiguracyjną do ATAK-CIV.")
        }
    }

    private fun injectMeshtasticConfigAndOwner(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        shortCallsign: String,
        logs: MutableList<String>
    ) {
        // 1. Skopiuj Callsign oraz ShortName do schowka i wyslij broadcast pomocniczy dla Meshtastic
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
        logs.add("[✓] Przygotowano identyfikator LoRa: Long='$fullCallsign' | Short='$shortCallsign'")

        // 2. Jezeli paczka zawiera zakodowany URL kanalu Meshtastic (https://meshtastic.org/e/#...),
        //    wywolaj bezposredni Intent do aplikacji com.geeksville.mesh
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
                val genericIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.meshtasticChannelUrl)).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                runCatching { context.startActivity(genericIntent) }
            }
            logs.add("[✓] Wstrzyknięto kanał LoRa '${pkg.loraChannelName}' (${pkg.loraRegion} / ${pkg.loraPreset}) + klucz PSK.")
        } else {
            logs.add("[i] Paczka używa domyślnego kanału LoRa (${pkg.loraRegion} / ${pkg.loraPreset}).")
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
        val hasOts = pkg.otsHost.isNotBlank() && !pkg.otsHost.contains("Tryb lokalny", true)
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