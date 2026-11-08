package com.fieldtak.mobile.data

import android.content.Context
import android.net.Uri
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

object FtakPackageRepository {
    private const val PREFS_NAME = "fth_mobile_prefs"
    private const val KEY_ACTIVE_FILE = "active_ftak_filename"
    private const val KEY_CALLSIGN_FULL = "operator_callsign_full"
    private const val KEY_CALLSIGN_SHORT = "operator_callsign_short"

    fun getPackagesDir(context: Context): File {
        val dir = File(context.filesDir, "ftak_packages")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    fun getActivePackageFileName(context: Context): String? {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(KEY_ACTIVE_FILE, null)
    }

    fun setActivePackageFileName(context: Context, fileName: String) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_ACTIVE_FILE, fileName)
            .apply()
    }

    fun saveOperatorCallsign(context: Context, fullCallsign: String, shortCallsign: String) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_CALLSIGN_FULL, fullCallsign)
            .putString(KEY_CALLSIGN_SHORT, shortCallsign)
            .apply()
    }

    fun loadOperatorCallsign(context: Context): Pair<String, String> {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val full = prefs.getString(KEY_CALLSIGN_FULL, "Wilk-1") ?: "Wilk-1"
        val shortName = prefs.getString(KEY_CALLSIGN_SHORT, generateMeshtasticShortName(full)) ?: "WLK1"
        return Pair(full, shortName)
    }

    /**
     * Deterministyczny generator 4-znakowego skrotu Meshtastic (np. "Wilk-1" -> "WLK1")
     */
    fun generateMeshtasticShortName(fullCallsign: String): String {
        val normalized = normalizePolishChars(fullCallsign.trim()).uppercase()
        if (normalized.isEmpty()) return "OPR1"

        val digits = normalized.filter { it.isDigit() }
        val letters = normalized.filter { it.isLetter() }

        if (letters.isEmpty()) {
            return normalized.filter { it.isLetterOrDigit() }.take(4).padEnd(4, '0')
        }

        val maxLetters = (4 - digits.take(2).length).coerceAtLeast(1)
        val vowels = setOf('A', 'E', 'I', 'O', 'U', 'Y')
        val firstLetter = letters.first()
        val remainingConsonants = letters.drop(1).filter { it !in vowels }
        val remainingAll = letters.drop(1)

        val letterPart = buildString {
            append(firstLetter)
            for (c in remainingConsonants) {
                if (length >= maxLetters) break
                append(c)
            }
            for (c in remainingAll) {
                if (length >= maxLetters) break
                if (!contains(c)) append(c)
            }
        }.take(maxLetters)

        val combined = (letterPart + digits.take(4 - letterPart.length)).take(4)
        return combined.ifEmpty { "OPR1" }
    }

    private fun normalizePolishChars(input: String): String {
        val map = mapOf(
            'Ą' to 'A', 'Ć' to 'C', 'Ę' to 'E', 'Ł' to 'L', 'Ń' to 'N',
            'Ó' to 'O', 'Ś' to 'S', 'Ź' to 'Z', 'Ż' to 'Z',
            'ą' to 'a', 'ć' to 'c', 'ę' to 'e', 'ł' to 'l', 'ń' to 'n',
            'ó' to 'o', 'ś' to 's', 'ź' to 'z', 'ż' to 'z'
        )
        return buildString(input.length) {
            for (ch in input) append(map[ch] ?: ch)
        }
    }

    fun listInstalledPackages(context: Context): List<FtakPackageSummary> {
        val dir = getPackagesDir(context)
        val files = dir.listFiles { f ->
            f.isFile && (f.name.endsWith(".ftak", ignoreCase = true) || f.name.endsWith(".zip", ignoreCase = true))
        }?.sortedByDescending { it.lastModified() } ?: emptyList()

        return files.mapNotNull { file ->
            runCatching { parseFtakArchive(file) }.getOrNull()
        }
    }

    fun deletePackage(context: Context, fileName: String) {
        val file = File(getPackagesDir(context), fileName)
        if (file.exists()) file.delete()
        val currentActive = getActivePackageFileName(context)
        if (currentActive == fileName) {
            val remaining = listInstalledPackages(context)
            setActivePackageFileName(context, remaining.firstOrNull()?.fileName ?: "")
        }
    }

    suspend fun importFromUri(context: Context, uri: Uri, suggestedName: String? = null): FtakPackageSummary? =
        withContext(Dispatchers.IO) {
            val fileName = (suggestedName ?: uri.lastPathSegment?.substringAfterLast('/') ?: "operacja_${System.currentTimeMillis()}.ftak")
                .let { if (it.endsWith(".ftak", true) || it.endsWith(".zip", true)) it else "$it.ftak" }
            val destFile = File(getPackagesDir(context), fileName)
            context.contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(destFile).use { output ->
                    input.copyTo(output)
                }
            }
            val parsed = parseFtakArchive(destFile)
            setActivePackageFileName(context, destFile.name)
            parsed
        }

    suspend fun handleScannedQrPayload(
        context: Context,
        rawQr: String,
        onProgress: (Int, String) -> Unit
    ): FtakPackageSummary = withContext(Dispatchers.IO) {
        val trimmed = rawQr.trim()

        // 1. Obsluga deep-linku fieldtak://provision?packageUrl=...
        val resolvedUrl = if (trimmed.startsWith("fieldtak://", ignoreCase = true)) {
            val uri = Uri.parse(trimmed)
            uri.getQueryParameter("packageUrl")
                ?: uri.getQueryParameter("url")
                ?: throw IllegalArgumentException("Brak parametru packageUrl w linku fieldtak://")
        } else if (trimmed.startsWith("http://", ignoreCase = true) || trimmed.startsWith("https://", ignoreCase = true)) {
            trimmed
        } else {
            null
        }

        // 2. Jezeli QR zawiera URL (LAN Wi-Fi :8766 lub chmura HTTPS) -> pobierz plik .ftak
        if (resolvedUrl != null) {
            onProgress(5, "Łączenie z serwerem dystrybucji: $resolvedUrl")
            val urlObj = URL(resolvedUrl)
            val conn = (urlObj.openConnection() as HttpURLConnection).apply {
                connectTimeout = 12000
                readTimeout = 20000
                requestMethod = "GET"
                connect()
            }
            if (conn.responseCode !in 200..299) {
                throw IllegalStateException("Błąd HTTP ${conn.responseCode} podczas pobierania paczki .ftak")
            }

            val totalBytes = conn.contentLengthLong.coerceAtLeast(1L)
            val rawName = Uri.parse(resolvedUrl).lastPathSegment?.substringAfterLast('/')
                ?.takeIf { it.isNotBlank() } ?: "paczka_${System.currentTimeMillis()}.ftak"
            val safeName = if (rawName.endsWith(".ftak", true) || rawName.endsWith(".zip", true)) rawName else "$rawName.ftak"
            val targetFile = File(getPackagesDir(context), safeName)

            conn.inputStream.use { input ->
                FileOutputStream(targetFile).use { output ->
                    val buffer = ByteArray(16 * 1024)
                    var downloaded = 0L
                    var read: Int
                    while (input.read(buffer).also { read = it } != -1) {
                        output.write(buffer, 0, read)
                        downloaded += read
                        val pct = ((downloaded * 100L) / totalBytes).toInt().coerceIn(5, 98)
                        onProgress(pct, "Pobrano ${downloaded / 1024} KB...")
                    }
                }
            }
            onProgress(100, "Zapisano paczkę: $safeName")
            val summary = parseFtakArchive(targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        // 3. Jezeli QR jest bezposrednim ladunkiem Offline JSON z Buildera -> utworz lokalna paczke .ftak
        if (trimmed.startsWith("{") && trimmed.endsWith("}")) {
            onProgress(50, "Przetwarzanie kodu QR Offline JSON...")
            val json = JSONObject(trimmed)
            val faction = json.optString("FactionName", json.optString("faction", "BLUFOR"))
            val eventName = json.optString("EventName", json.optString("operation", "OPERACJA_OFFLINE"))
            val fileName = "${eventName}_${faction}_OFFLINE.ftak".replace(Regex("[^a-zA-Z0-9._-]"), "_")
            val targetFile = File(getPackagesDir(context), fileName)

            ZipOutputStream(FileOutputStream(targetFile)).use { zos ->
                zos.putNextEntry(ZipEntry("config.json"))
                zos.write(json.toString(2).toByteArray(Charsets.UTF_8))
                zos.closeEntry()
            }
            onProgress(100, "Utworzono paczkę Offline: $fileName")
            val summary = parseFtakArchive(targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        throw IllegalArgumentException("Nieznany format kodu QR. Zeskanuj kod QR paczki .ftak (HTTP/LAN, fieldtak:// lub Offline JSON).")
    }

    fun parseFtakArchive(file: File): FtakPackageSummary {
        var configJsonStr: String? = null
        var manifestJsonStr: String? = null
        var opordJsonStr: String? = null
        var hasOpordHtml = false
        val zipEntries = mutableSetOf<String>()

        ZipInputStream(BufferedInputStream(FileInputStream(file))).use { zis ->
            var entry: ZipEntry?
            while (zis.nextEntry.also { entry = it } != null) {
                val entryName = entry!!.name.replace('\\', '/')
                zipEntries.add(entryName)
                val shortName = entryName.substringAfterLast('/')
                when {
                    shortName.equals("config.json", ignoreCase = true) && configJsonStr == null -> {
                        configJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    shortName.equals("manifest.json", ignoreCase = true) && manifestJsonStr == null -> {
                        manifestJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    shortName.equals("opord.json", ignoreCase = true) && opordJsonStr == null -> {
                        opordJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    shortName.equals("opord.html", ignoreCase = true) -> {
                        hasOpordHtml = true
                    }
                }
                zis.closeEntry()
            }
        }

        val root = when {
            !configJsonStr.isNullOrBlank() -> JSONObject(configJsonStr!!)
            !manifestJsonStr.isNullOrBlank() -> JSONObject(manifestJsonStr!!)
            else -> JSONObject()
        }

        val eventName = root.optString("EventName", root.optString("eventName", file.nameWithoutExtension))
        val factionName = root.optString("FactionName", root.optString("factionName", "BLUFOR"))
        val colorHex = root.optString("ColorHex", root.optString("colorHex", "#1FE0C5"))
        val otsHost = root.optString("OtsHost", root.optString("otsHost", "Tryb lokalny / Off-Grid"))
        val enrollmentUri = root.optString("EnrollmentUri", root.optString("enrollmentUri", ""))
        val channelUrl = root.optString("ChannelUrl", root.optString("channelUrl", ""))
        val loraRegion = root.optString("LoraRegion", root.optString("region", "EU_868"))
        val loraPreset = root.optString("LoraPreset", root.optString("preset", "SHORT_FAST"))
        val loraChannelName = root.optString("LoraChannelName", "$factionName-MESH")
        val emergencyPhone = root.optString("EmergencyPhone", root.optString("emergencyPhone", "112"))
        val pliStrategy = root.optString("PliStrategy", "Constant")
        val pliConstLteSec = root.optInt("PliConstLteSec", 0)
        val pliConstMeshSec = root.optInt("PliConstMeshSec", 15)
        val expirationHours = root.optInt("ExpirationHours", 0)

        val requiredList = mutableListOf<RequiredApkItem>()
        val reqArr: JSONArray? = root.optJSONArray("RequiredPackages") ?: root.optJSONArray("requiredPackages")
        if (reqArr != null) {
            for (i in 0 until reqArr.length()) {
                val obj = reqArr.optJSONObject(i) ?: continue
                requiredList.add(
                    RequiredApkItem(
                        role = obj.optString("role", "APK"),
                        displayName = obj.optString("displayName", obj.optString("role", "Pakiet APK")),
                        packageName = obj.optString("packageName", ""),
                        requiredVersion = obj.optString("requiredVersion", "1.0"),
                        bundledZipPath = obj.optString("bundledZipPath", "")
                    )
                )
            }
        }

        if (requiredList.none { it.packageName == "com.atakmap.app.civ" }) {
            val bundledAtak = zipEntries.firstOrNull { it.contains("atak", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(0, RequiredApkItem("ATAK-CIV", "ATAK-CIV (Taktyczna Mapa)", "com.atakmap.app.civ", "5.0.0", bundledAtak))
        }
        if (requiredList.none { it.packageName == "com.geeksville.mesh" }) {
            val bundledMesh = zipEntries.firstOrNull { it.contains("mesh", true) && !it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(1, RequiredApkItem("MESHTASTIC", "Meshtastic (Komunikator LoRa)", "com.geeksville.mesh", "2.3.0", bundledMesh))
        }
        if (requiredList.none { it.packageName == "com.atakmap.android.meshtastic.plugin" }) {
            val bundledPlugin = zipEntries.firstOrNull { it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(RequiredApkItem("PLUGIN", "Wtyczka ATAK Meshtastic", "com.atakmap.android.meshtastic.plugin", "1.0.0", bundledPlugin))
        }

        val voiceList = mutableListOf<VoiceChannelItem>()
        val voiceArr = root.optJSONArray("VoiceChannels") ?: root.optJSONArray("voiceChannels")
        if (voiceArr != null) {
            for (i in 0 until voiceArr.length()) {
                val v = voiceArr.optJSONObject(i) ?: continue
                voiceList.add(
                    VoiceChannelItem(
                        role = v.optString("Role", v.optString("role", "Kanał ${i + 1}")),
                        band = v.optString("Band", v.optString("band", "PMR")),
                        freqOrChannel = v.optString("FreqOrChannel", v.optString("freq", "CH 1")),
                        ctcssOrTg = v.optString("CtcssOrTg", v.optString("ctcss", "Brak tonu"))
                    )
                )
            }
        }
        if (voiceList.isEmpty()) {
            voiceList.add(VoiceChannelItem("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"))
            voiceList.add(VoiceChannelItem("Taktyczny Alfa", "VHF/UHF", "145.500 MHz", "CTCSS 88.5 Hz"))
        }

        val callsigns = mutableListOf<String>()
        if (!opordJsonStr.isNullOrBlank()) {
            runCatching {
                val opObj = JSONObject(opordJsonStr!!)
                val roster = opObj.optJSONArray("SquadRoster")
                if (roster != null) {
                    for (i in 0 until roster.length()) {
                        val r = roster.optJSONObject(i)
                        val cs = r?.optString("OperatorOrTeam", "")?.trim() ?: ""
                        if (cs.isNotEmpty()) callsigns.add(cs)
                    }
                }
            }
        }

        return FtakPackageSummary(
            id = file.name,
            fileName = file.name,
            localFilePath = file.absolutePath,
            eventName = eventName,
            factionName = factionName,
            colorHex = colorHex,
            otsHost = otsHost,
            enrollmentUri = enrollmentUri,
            meshtasticChannelUrl = channelUrl,
            loraRegion = loraRegion,
            loraPreset = loraPreset,
            loraChannelName = loraChannelName,
            emergencyPhone = emergencyPhone,
            pliStrategy = pliStrategy,
            pliConstLteSec = pliConstLteSec,
            pliConstMeshSec = pliConstMeshSec,
            expirationHours = expirationHours,
            sizeBytes = file.length(),
            requiredPackages = requiredList,
            voiceChannels = voiceList,
            squadCallsigns = callsigns.distinct(),
            hasEmbeddedOpordHtml = hasOpordHtml
        )
    }
}