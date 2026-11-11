package com.fieldtakhub.mobile.data

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
import java.net.Inet4Address
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.URL
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream
import kotlin.concurrent.thread
import kotlin.math.*

data class RequiredApkItem(
    val role: String,
    val displayName: String,
    val packageName: String,
    val requiredVersion: String,
    val bundledZipPath: String
)

data class InstalledApkStatus(
    val item: RequiredApkItem,
    val installedVersion: String?,
    val isInstalled: Boolean,
    val isUpToDate: Boolean,
    val hasBundledApkInFtak: Boolean
)

data class VoiceChannelItem(
    val role: String,
    val band: String,
    val freqOrChannel: String,
    val ctcssOrTg: String
)

data class OpordDaySheet(
    val dayTitle: String,
    val situation: String,
    val metoc: String,
    val mission5W: String,
    val executionTask: String,
    val sarnegWord: String,
    val challengePassword: String,
    val logistics: String
)

data class FtakPackageSummary(
    val id: String,
    val fileName: String,
    val localFilePath: String,
    val eventName: String,
    val factionName: String,
    val colorHex: String,
    val otsHost: String,
    val enrollmentUri: String,
    val meshtasticChannelUrl: String,
    val loraRegion: String,
    val loraPreset: String,
    val loraChannelName: String,
    val emergencyPhone: String,
    val pliStrategy: String,
    val pliConstLteSec: Int,
    val pliConstMeshSec: Int,
    val expirationHours: Int,
    val sizeBytes: Long,
    val requiredPackages: List<RequiredApkItem>,
    val voiceChannels: List<VoiceChannelItem>,
    val squadCallsigns: List<String>,
    val opordDays: List<OpordDaySheet>,
    val opordHtmlPath: String?,
    val annex3MapImagePath: String?
) {
    val formattedSize: String
        get() {
            val mb = sizeBytes.toDouble() / (1024.0 * 1024.0)
            return if (mb >= 1.0) String.format("%.1f MB", mb)
            else String.format("%.0f KB", sizeBytes.toDouble() / 1024.0)
        }
}

/**
 * Dokladny konwerter WGS84 (Lat/Lon) -> MGRS (np. "34U CA 42757 24185") dzialajacy 100% offline
 */
object LatLonToMgrs {
    fun convert(lat: Double, lon: Double): String {
        if (lat < -80.0 || lat > 84.0) {
            return String.format("%.5f, %.5f", lat, lon)
        }
        val zone = ((lon + 180.0) / 6.0).toInt() + 1
        val bands = "CDEFGHJKLMNPQRSTUVWXX"
        val bandIdx = ((lat + 80.0) / 8.0).toInt().coerceIn(0, bands.length - 1)
        val band = bands[bandIdx]

        val a = 6378137.0
        val f = 1.0 / 298.257223563
        val k0 = 0.9996
        val e2 = 2 * f - f * f
        val ep2 = e2 / (1.0 - e2)

        val latRad = Math.toRadians(lat)
        val lonRad = Math.toRadians(lon)
        val lon0Rad = Math.toRadians(((zone - 1) * 6 - 180 + 3).toDouble())

        val n = a / sqrt(1.0 - e2 * sin(latRad).pow(2))
        val t = tan(latRad).pow(2)
        val c = ep2 * cos(latRad).pow(2)
        val aCoeff = cos(latRad) * (lonRad - lon0Rad)

        val m = a * (
            (1.0 - e2 / 4.0 - 3.0 * e2 * e2 / 64.0 - 5.0 * e2.pow(3) / 256.0) * latRad
                - (3.0 * e2 / 8.0 + 3.0 * e2 * e2 / 32.0 + 45.0 * e2.pow(3) / 1024.0) * sin(2.0 * latRad)
                + (15.0 * e2 * e2 / 256.0 + 45.0 * e2.pow(3) / 1024.0) * sin(4.0 * latRad)
                - (35.0 * e2.pow(3) / 3072.0) * sin(6.0 * latRad)
            )

        val easting = k0 * n * (
            aCoeff + (1.0 - t + c) * aCoeff.pow(3) / 6.0
                + (5.0 - 18.0 * t + t * t + 72.0 * c - 58.0 * ep2) * aCoeff.pow(5) / 120.0
            ) + 500000.0

        var northing = k0 * (
            m + n * tan(latRad) * (
                aCoeff.pow(2) / 2.0
                    + (5.0 - t + 9.0 * c + 4.0 * c * c) * aCoeff.pow(4) / 24.0
                    + (61.0 - 58.0 * t + t * t + 600.0 * c - 330.0 * ep2) * aCoeff.pow(6) / 720.0
                )
            )
        if (lat < 0.0) northing += 10000000.0

        val setNum = (zone - 1) % 6
        val colSets = arrayOf("ABCDEFGH", "JKLMNPQR", "STUVWXYZ", "ABCDEFGH", "JKLMNPQR", "STUVWXYZ")
        val rowSets = arrayOf("ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE")

        val colIdx = ((easting / 100000.0).toInt() - 1).mod(8)
        val colLetter = colSets[setNum][colIdx]

        val rowIdx = ((northing % 2000000.0) / 100000.0).toInt().mod(20)
        val rowLetter = rowSets[setNum % 2][rowIdx]

        val e5 = (easting % 100000.0).toInt().coerceIn(0, 99999)
        val n5 = (northing % 100000.0).toInt().coerceIn(0, 99999)

        return String.format("%02d%c %c%c %05d %05d", zone, band, colLetter, rowLetter, e5, n5)
    }
}

/**
 * Lokalny serwer HTTP Wi-Fi Hotspot (:8766) udostepniajacy paczke .ftak spoznionym graczom w lesie
 */
object LocalFtakHotspotServer {
    private var serverSocket: ServerSocket? = null
    @Volatile
    private var isRunning = false
    private const val PORT = 8766

    fun startSharingPackage(file: File): String {
        stop()
        isRunning = true
        serverSocket = ServerSocket(PORT)
        thread(start = true, isDaemon = true, name = "FthMobileHotspotHttp") {
            while (isRunning) {
                val client = runCatching { serverSocket?.accept() }.getOrNull() ?: break
                thread(start = true, isDaemon = true) {
                    runCatching {
                        client.use { sock ->
                            val out = sock.getOutputStream()
                            val bytes = file.readBytes()
                            val header = buildString {
                                append("HTTP/1.1 200 OK\r\n")
                                append("Content-Type: application/octet-stream\r\n")
                                append("Content-Disposition: attachment; filename=\"${file.name}\"\r\n")
                                append("Content-Length: ${bytes.size}\r\n")
                                append("Connection: close\r\n\r\n")
                            }
                            out.write(header.toByteArray(Charsets.UTF_8))
                            out.write(bytes)
                            out.flush()
                        }
                    }
                }
            }
        }
        val ip = detectLocalLanIp()
        return "http://$ip:$PORT/deploy/${Uri.encode(file.name)}"
    }

    fun stop() {
        isRunning = false
        runCatching { serverSocket?.close() }
        serverSocket = null
    }

    fun detectLocalLanIp(): String {
        runCatching {
            val interfaces = NetworkInterface.getNetworkInterfaces()?.toList() ?: emptyList()
            for (nif in interfaces) {
                if (!nif.isUp || nif.isLoopback) continue
                for (addr in nif.inetAddresses) {
                    if (addr is Inet4Address && !addr.isLoopbackAddress) {
                        val ip = addr.hostAddress ?: continue
                        if (ip.startsWith("192.168.") || ip.startsWith("10.") || ip.startsWith("172.")) {
                            return ip
                        }
                    }
                }
            }
        }
        return "192.168.43.1"
    }
}

object FtakPackageRepository {
    private const val PREFS_NAME = "fth_clean_v34_prefs"
    private const val KEY_ONBOARDING_DONE = "onboarding_completed_v34_clean"
    private const val KEY_ACTIVE_FILE = "active_ftak_filename"
    private const val KEY_CALLSIGN_FULL = "operator_callsign_full"
    private const val KEY_CALLSIGN_SHORT = "operator_callsign_short"

    fun isOnboardingCompleted(context: Context): Boolean {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getBoolean(KEY_ONBOARDING_DONE, false)
    }

    fun setOnboardingCompleted(context: Context, completed: Boolean) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putBoolean(KEY_ONBOARDING_DONE, completed)
            .apply()
    }

    fun getPackagesDir(context: Context): File {
        val dir = File(context.filesDir, "ftak_packages")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    fun getExtractedAssetsDir(context: Context): File {
        val dir = File(context.filesDir, "ftak_extracted")
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

    fun generateMeshtasticShortName(fullCallsign: String): String {
        val normalized = normalizePolishChars(fullCallsign.trim()).uppercase()
        if (normalized.isEmpty()) return "WLK1"

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

        return (letterPart + digits.take(4 - letterPart.length)).take(4).ifEmpty { "WLK1" }
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
            runCatching { parseFtakArchive(context, file) }.getOrNull()
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
            val rawName = suggestedName ?: uri.lastPathSegment?.substringAfterLast('/') ?: "operacja_${System.currentTimeMillis()}.ftak"
            val fileName = if (rawName.endsWith(".ftak", true) || rawName.endsWith(".zip", true)) rawName else "$rawName.ftak"
            val destFile = File(getPackagesDir(context), fileName.replace(Regex("[^a-zA-Z0-9._-]"), "_"))
            context.contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(destFile).use { output ->
                    input.copyTo(output)
                }
            }
            val parsed = parseFtakArchive(context, destFile)
            setActivePackageFileName(context, destFile.name)
            parsed
        }

    suspend fun handleScannedQrPayload(
        context: Context,
        rawQr: String,
        onProgress: (Int, String) -> Unit
    ): FtakPackageSummary = withContext(Dispatchers.IO) {
        val trimmed = rawQr.trim()

        val resolvedUrl = when {
            trimmed.startsWith("fieldtak://", ignoreCase = true) -> {
                val uri = Uri.parse(trimmed)
                uri.getQueryParameter("packageUrl")
                    ?: uri.getQueryParameter("url")
                    ?: throw IllegalArgumentException("Brak parametru packageUrl w linku fieldtak://")
            }
            trimmed.startsWith("http://", ignoreCase = true) || trimmed.startsWith("https://", ignoreCase = true) -> trimmed
            else -> null
        }

        if (resolvedUrl != null) {
            onProgress(10, "Pobieranie paczki z: $resolvedUrl")
            val urlObj = URL(resolvedUrl)
            val conn = (urlObj.openConnection() as HttpURLConnection).apply {
                connectTimeout = 12000
                readTimeout = 25000
                requestMethod = "GET"
                connect()
            }
            if (conn.responseCode !in 200..299) {
                throw IllegalStateException("Serwer zwrócił błąd HTTP ${conn.responseCode}")
            }

            val totalBytes = conn.contentLengthLong.coerceAtLeast(1L)
            val rawName = Uri.parse(resolvedUrl).lastPathSegment?.substringAfterLast('/')
                ?.takeIf { it.isNotBlank() } ?: "paczka_${System.currentTimeMillis()}.ftak"
            val safeName = (if (rawName.endsWith(".ftak", true) || rawName.endsWith(".zip", true)) rawName else "$rawName.ftak")
                .replace(Regex("[^a-zA-Z0-9._-]"), "_")
            val targetFile = File(getPackagesDir(context), safeName)

            conn.inputStream.use { input ->
                FileOutputStream(targetFile).use { output ->
                    val buffer = ByteArray(16 * 1024)
                    var downloaded = 0L
                    var read: Int
                    while (input.read(buffer).also { read = it } != -1) {
                        output.write(buffer, 0, read)
                        downloaded += read
                        val pct = ((downloaded * 100L) / totalBytes).toInt().coerceIn(10, 98)
                        onProgress(pct, "Pobrano ${downloaded / 1024} KB...")
                    }
                }
            }
            onProgress(100, "Pobrano: $safeName")
            val summary = parseFtakArchive(context, targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        if (trimmed.startsWith("{") && trimmed.endsWith("}")) {
            onProgress(50, "Dekodowanie QR Offline JSON...")
            val json = JSONObject(trimmed)
            val faction = json.optString("FactionName", json.optString("faction", "BLUFOR"))
            val eventName = json.optString("EventName", json.optString("operation", "OPERACJA"))
            val fileName = "${eventName}_${faction}_OFFLINE.ftak".replace(Regex("[^a-zA-Z0-9._-]"), "_")
            val targetFile = File(getPackagesDir(context), fileName)

            ZipOutputStream(FileOutputStream(targetFile)).use { zos ->
                zos.putNextEntry(ZipEntry("config.json"))
                zos.write(json.toString(2).toByteArray(Charsets.UTF_8))
                zos.closeEntry()
            }
            onProgress(100, "Zapisano profil Offline: $fileName")
            val summary = parseFtakArchive(context, targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        throw IllegalArgumentException("Nieznany format kodu QR. Zeskanuj kod QR paczki .ftak (Wi-Fi LAN, Chmura lub Offline JSON).")
    }

    fun parseFtakArchive(context: Context, file: File): FtakPackageSummary {
        var configJsonStr: String? = null
        var manifestJsonStr: String? = null
        var opordJsonStr: String? = null
        var extractedOpordHtmlPath: String? = null
        var extractedAnnex3MapPath: String? = null
        val zipEntries = mutableSetOf<String>()

        val pkgAssetsDir = File(getExtractedAssetsDir(context), file.nameWithoutExtension)
        if (!pkgAssetsDir.exists()) pkgAssetsDir.mkdirs()

        ZipInputStream(BufferedInputStream(FileInputStream(file))).use { zis ->
            var entry: ZipEntry?
            while (zis.nextEntry.also { entry = it } != null) {
                val entryName = entry!!.name.replace('\\', '/')
                zipEntries.add(entryName)
                val shortName = entryName.substringAfterLast('/')
                val lower = shortName.lowercase()

                when {
                    lower == "config.json" && configJsonStr == null -> {
                        configJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    lower == "manifest.json" && manifestJsonStr == null -> {
                        manifestJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    lower == "opord.json" && opordJsonStr == null -> {
                        opordJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    lower == "opord.html" -> {
                        val outHtml = File(pkgAssetsDir, "opord.html")
                        outHtml.writeBytes(zis.readBytes())
                        extractedOpordHtmlPath = outHtml.absolutePath
                    }
                    (lower.endsWith(".png") || lower.endsWith(".jpg") || lower.endsWith(".jpeg")) &&
                        (lower.contains("map") || lower.contains("annex") || lower.contains("zalacznik") || extractedAnnex3MapPath == null) -> {
                        val outImg = File(pkgAssetsDir, shortName)
                        outImg.writeBytes(zis.readBytes())
                        extractedAnnex3MapPath = outImg.absolutePath
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
        val otsHost = root.optString("OtsHost", root.optString("otsHost", "Połączono / Lokalny"))
        val enrollmentUri = root.optString("EnrollmentUri", root.optString("enrollmentUri", ""))
        val channelUrl = root.optString("ChannelUrl", root.optString("channelUrl", ""))
        val loraRegion = root.optString("LoraRegion", root.optString("region", "EU_868"))
        val loraPreset = root.optString("LoraPreset", root.optString("preset", "SHORT_FAST"))
        val loraChannelName = root.optString("LoraChannelName", "MESH")
        val emergencyPhone = root.optString("EmergencyPhone", root.optString("emergencyPhone", "+48 600 000 000"))
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
            val bundledAtak = zipEntries.firstOrNull { it.contains("atak", true) && !it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(0, RequiredApkItem("ATAK-CIV", "ATAK-CIV", "com.atakmap.app.civ", "5.0.0", bundledAtak))
        }
        if (requiredList.none { it.packageName == "com.geeksville.mesh" }) {
            val bundledMesh = zipEntries.firstOrNull { it.contains("mesh", true) && !it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(1, RequiredApkItem("Meshtastic", "Meshtastic", "com.geeksville.mesh", "2.3.0", bundledMesh))
        }
        if (requiredList.none { it.packageName == "com.atakmap.android.meshtastic.plugin" }) {
            val bundledPlugin = zipEntries.firstOrNull { it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            if (bundledPlugin.isNotEmpty()) {
                requiredList.add(RequiredApkItem("Plugin LoRa", "Wtyczka ATAK Meshtastic", "com.atakmap.android.meshtastic.plugin", "1.0.0", bundledPlugin))
            }
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
                        freqOrChannel = v.optString("FreqOrChannel", v.optString("freq", "CH 1 (446.00625)")),
                        ctcssOrTg = v.optString("CtcssOrTg", v.optString("ctcss", "CTCSS 67.0"))
                    )
                )
            }
        }
        if (voiceList.isEmpty()) {
            voiceList.add(VoiceChannelItem("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0"))
            voiceList.add(VoiceChannelItem("Taktyczny Alfa", "VHF", "145.500 MHz", "CTCSS 88.5"))
        }

        val callsigns = mutableListOf<String>()
        val opordDays = mutableListOf<OpordDaySheet>()

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

                // Wczytaj zakladki Dzien 1, Dzien 2... z modelu OPORD v3.4
                val daysArr = opObj.optJSONArray("Days") ?: opObj.optJSONArray("DaySheets")
                if (daysArr != null && daysArr.length() > 0) {
                    for (i in 0 until daysArr.length()) {
                        val d = daysArr.optJSONObject(i) ?: continue
                        opordDays.add(
                            OpordDaySheet(
                                dayTitle = d.optString("DayTitle", d.optString("Name", "DZIEŃ ${i + 1}")).uppercase(),
                                situation = d.optString("SituationEnemy", d.optString("Situation", "Siły przeciwnika w rejonie AO prowadzą działania patrolowe.")),
                                metoc = d.optString("MetocSummary", d.optString("Weather", "Brak pobranych danych METOC.")),
                                mission5W = d.optString("Mission5W", d.optString("Mission", "Skryte przemieszczenie czujki z przodu i opanowanie obiektu.")),
                                executionTask = d.optString("ExecutionIntent", d.optString("Priorities", "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.")),
                                sarnegWord = d.optString("SarnegKeyword", d.optString("Sarneg", "BIEGUSZOWICE")),
                                challengePassword = d.optString("ChallengePassword", "GRZMOT / BŁYSK"),
                                logistics = d.optString("Logistics", "FOB / Respawn zgodnie z wytycznymi sztabu.")
                            )
                        )
                    }
                } else {
                    opordDays.add(
                        OpordDaySheet(
                            dayTitle = "DZIEŃ 1",
                            situation = opObj.optString("SituationEnemy", opObj.optString("Situation", "Siły przeciwnika utrzymują punkty kluczowe w sektorze.")),
                            metoc = opObj.optString("MetocSummary", "Warunki terenowe zgodne z prognozą AO."),
                            mission5W = opObj.optString("Mission5W", "Wykonać zadanie główne strony $factionName w wyznaczonym oknie czasowym."),
                            executionTask = opObj.optString("ExecutionIntent", "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu."),
                            sarnegWord = opObj.optString("SarnegKeyword", "BIEGUSZOWICE"),
                            challengePassword = opObj.optString("ChallengePassword", "GRZMOT / BŁYSK"),
                            logistics = opObj.optString("Logistics", "Amunicja i pakiety medyczne w punkcie zbiórki.")
                        )
                    )
                }
            }
        }

        if (opordDays.isEmpty()) {
            val summaryNode = root.optJSONObject("OpordSummary")
            opordDays.add(
                OpordDaySheet(
                    dayTitle = "DZIEŃ 1",
                    situation = summaryNode?.optString("Situation", "Działania rozpoznawczo-bojowe w rejonie operacji $eventName.") ?: "Działania w rejonie $eventName.",
                    metoc = summaryNode?.optString("Metoc", "METOC z rejonu AO.") ?: "METOC z rejonu AO.",
                    mission5W = summaryNode?.optString("Mission5W", "Zabezpieczyć wyznaczone sektory i utrzymać łączność ze sztabem.") ?: "Zabezpieczyć wyznaczone sektory.",
                    executionTask = summaryNode?.optString("Execution", "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.") ?: "Skryte przemieszczenie czujki z przodu.",
                    sarnegWord = summaryNode?.optString("Sarneg", "BIEGUSZOWICE") ?: "BIEGUSZOWICE",
                    challengePassword = summaryNode?.optString("Challenge", "WILK / LAS") ?: "WILK / LAS",
                    logistics = "Punkt medyczny i respawn w bazie strony $factionName."
                )
            )
            opordDays.add(
                OpordDaySheet(
                    dayTitle = "DZIEŃ 2",
                    situation = "Kontynuacja operacji po przegrupowaniu sił strony $factionName.",
                    metoc = "Warunki dzienne / poranne zamglenie.",
                    mission5W = "Utrzymać zajęte rubieże i przeprowadzić kontratak na cel główny.",
                    executionTask = "Rotacja sekcji na punktach obserwacyjnych, cisza radiowa do godziny H.",
                    sarnegWord = "STRUMIEŃ",
                    challengePassword = "ZAMEK / WIEŻA",
                    logistics = "Uzupełnienie baterii LoRa i wody w FOB."
                )
            )
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
            opordDays = opordDays,
            opordHtmlPath = extractedOpordHtmlPath,
            annex3MapImagePath = extractedAnnex3MapPath
        )
    }
}