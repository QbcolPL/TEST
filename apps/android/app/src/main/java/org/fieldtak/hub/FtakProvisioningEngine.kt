package org.fieldtak.hub

import android.annotation.SuppressLint
import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.ContentProvider
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.database.Cursor
import android.database.MatrixCursor
import android.location.Location
import android.location.LocationManager
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
import android.util.Base64
import android.widget.Toast
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.util.Locale
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream
import kotlin.math.*

data class FtakVoiceChannel(
    val roleName: String,
    val bandType: String,
    val freqMhz: String,
    val toneOrCode: String
)

data class FtakFactionProfile(
    val name: String,
    val colorHex: String,
    val channelName: String,
    val pskBase64: String,
    val channelUrl: String,
    val loraRegion: String,
    val loraPreset: String,
    val hopLimit: Int,
    val otsHost: String,
    val enrollmentUri: String,
    val emergencyPhone: String,
    val pliProfile: String,
    val meshProfile: String,
    val voiceChannels: List<FtakVoiceChannel>
)

data class OnDemandMgrsFix(
    val fullMgrs: String,
    val shortGrid100m: String,
    val keypadNum: Int,
    val quarterChar: String,
    val wgs84Text: String,
    val accuracyMeters: Int,
    val hasValidFix: Boolean
)

class FtakShareContentProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun query(
        uri: Uri,
        projection: Array<out String>?,
        selection: String?,
        selectionArgs: Array<out String>?,
        sortOrder: String?
    ): Cursor {
        val file = resolveSharedFile(uri)
        val cols = projection ?: arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE)
        val cursor = MatrixCursor(cols, 1)
        val row = cursor.newRow()
        for (col in cols) {
            when (col) {
                OpenableColumns.DISPLAY_NAME -> row.add(file?.name ?: "deploy.zip")
                OpenableColumns.SIZE -> row.add(file?.length() ?: 0L)
                else -> row.add(null)
            }
        }
        return cursor
    }

    override fun getType(uri: Uri): String {
        val name = uri.lastPathSegment?.lowercase(Locale.ROOT) ?: ""
        return when {
            name.endsWith(".zip") -> "application/zip"
            name.endsWith(".ftak") -> "application/octet-stream"
            name.endsWith(".gpx") -> "application/gpx+xml"
            name.endsWith(".kmz") -> "application/vnd.google-earth.kmz"
            name.endsWith(".mbtiles") || name.endsWith(".sqlite") -> "application/x-sqlite3"
            else -> "application/octet-stream"
        }
    }

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor? {
        val file = resolveSharedFile(uri)
        if (file == null || !file.exists()) return null
        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
    }

    override fun insert(uri: Uri, values: ContentValues?): Uri? = null
    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0
    override fun update(uri: Uri, values: ContentValues?, selection: String?, selectionArgs: Array<out String>?): Int = 0

    private fun resolveSharedFile(uri: Uri): File? {
        val ctx = context ?: return null
        val fileName = uri.lastPathSegment ?: return null
        val safeName = File(fileName).name
        return File(File(ctx.cacheDir, "ftak_shared"), safeName)
    }

    companion object {
        fun buildUriForFile(ctx: Context, sourceFile: File): Uri {
            val sharedDir = File(ctx.cacheDir, "ftak_shared").apply { mkdirs() }
            val targetFile = File(sharedDir, sourceFile.name)
            if (sourceFile.absolutePath != targetFile.absolutePath) {
                sourceFile.copyTo(targetFile, overwrite = true)
            }
            return Uri.parse("content://${ctx.packageName}.ftakprovider/${Uri.encode(targetFile.name)}")
        }
    }
}

object FtakProvisioningEngine {

    private const val PREFS_NAME = "ftak_clean_hub_v24"
    private const val KEY_PERMISSIONS_ACK = "permissions_onboarding_ack"
    private const val KEY_CALLSIGN = "player_callsign"
    private const val KEY_ACTIVE_FACTION = "active_faction_name"
    private const val KEY_EVENT_NAME = "loaded_event_name"
    private const val KEY_RAW_CONFIG_JSON = "loaded_raw_config_json"

    fun isPermissionsOnboardingCompleted(ctx: Context): Boolean =
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getBoolean(KEY_PERMISSIONS_ACK, false)

    fun setPermissionsOnboardingCompleted(ctx: Context, completed: Boolean) {
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit().putBoolean(KEY_PERMISSIONS_ACK, completed).apply()
    }

    fun getCallsign(ctx: Context): String =
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getString(KEY_CALLSIGN, "OPERATOR-1") ?: "OPERATOR-1"

    fun setCallsign(ctx: Context, callsign: String) {
        val clean = callsign.trim().ifEmpty { "OPERATOR-1" }
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit().putString(KEY_CALLSIGN, clean).apply()
    }

    fun hasLoadedProfile(ctx: Context): Boolean {
        val raw = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getString(KEY_RAW_CONFIG_JSON, "") ?: ""
        return raw.isNotBlank() && parseFactionsFromConfigJson(raw).isNotEmpty()
    }

    fun getEventName(ctx: Context): String =
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getString(KEY_EVENT_NAME, "Brak wczytanej operacji") ?: "Brak wczytanej operacji"

    fun getLoadedFactions(ctx: Context): List<FtakFactionProfile> {
        val raw = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getString(KEY_RAW_CONFIG_JSON, "") ?: ""
        if (raw.isBlank()) return emptyList()
        return parseFactionsFromConfigJson(raw)
    }

    fun getActiveFaction(ctx: Context): FtakFactionProfile? {
        val list = getLoadedFactions(ctx)
        if (list.isEmpty()) return null
        val savedName = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getString(KEY_ACTIVE_FACTION, "") ?: ""
        return list.firstOrNull { it.name.equals(savedName, ignoreCase = true) } ?: list.first()
    }

    fun setActiveFaction(ctx: Context, factionName: String) {
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit().putString(KEY_ACTIVE_FACTION, factionName).apply()
    }

    fun getExtractedBundleDir(ctx: Context): File =
        File(ctx.filesDir, "active_ftak_bundle").apply { mkdirs() }

    fun getSavedRawFtakArchiveFile(ctx: Context): File =
        File(ctx.filesDir, "current_operation.ftak")

    fun getRawConfigJsonString(ctx: Context): String =
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getString(KEY_RAW_CONFIG_JSON, "") ?: ""

    fun importFromQrStringOrJson(ctx: Context, rawPayload: String): Boolean {
        val trimmed = rawPayload.trim()
        if (trimmed.isEmpty()) return false

        if (trimmed.startsWith("https://meshtastic.org/e/#", ignoreCase = true) ||
            trimmed.startsWith("meshtastic://", ignoreCase = true)) {
            val synthJson = JSONObject().apply {
                put("eventName", "Profil QR Meshtastic")
                put("factions", JSONArray().put(JSONObject().apply {
                    put("Name", "DOMYŚLNA")
                    put("ColorHex", "#1FE0C5")
                    put("ChannelName", "QR-MESH")
                    put("ChannelUrl", trimmed)
                    put("LoraRegion", "EU_868")
                    put("LoraModemPreset", "SHORT_FAST")
                    put("HopLimit", 3)
                }))
            }
            return saveParsedConfigJson(ctx, synthJson.toString())
        }

        val jsonCandidate = if (trimmed.startsWith("{")) {
            trimmed
        } else {
            try {
                val decoded = String(Base64.decode(trimmed, Base64.DEFAULT), Charsets.UTF_8).trim()
                if (decoded.startsWith("{")) decoded else ""
            } catch (_: Exception) { "" }
        }

        if (jsonCandidate.startsWith("{")) {
            val ok = saveParsedConfigJson(ctx, jsonCandidate)
            if (ok) {
                writeSyntheticFtakZipFromCurrentJson(ctx, jsonCandidate)
            }
            return ok
        }
        return false
    }

    fun importFromFtakStream(ctx: Context, inputStream: InputStream): Boolean {
        return try {
            val rawBytes = inputStream.readBytes()
            if (rawBytes.isEmpty()) return false

            val asText = String(rawBytes, Charsets.UTF_8).trim()
            if (asText.startsWith("{")) {
                return importFromQrStringOrJson(ctx, asText)
            }

            val savedFtak = getSavedRawFtakArchiveFile(ctx)
            savedFtak.writeBytes(rawBytes)

            val bundleDir = getExtractedBundleDir(ctx)
            bundleDir.deleteRecursively()
            bundleDir.mkdirs()

            var foundJsonConfig: String? = null

            ZipInputStream(rawBytes.inputStream()).use { zis ->
                var entry: ZipEntry? = zis.nextEntry
                while (entry != null) {
                    if (!entry.isDirectory) {
                        val relPath = entry.name.replace('\\', '/').trimStart('/')
                        if (!relPath.contains("..")) {
                            val outFile = File(bundleDir, relPath)
                            outFile.parentFile?.mkdirs()
                            val entryBytes = zis.readBytes()
                            outFile.writeBytes(entryBytes)

                            if (relPath.endsWith(".json", ignoreCase = true) ||
                                relPath.endsWith(".fthproj", ignoreCase = true) ||
                                relPath.endsWith("manifest.ftak", ignoreCase = true)) {
                                val candidateTxt = String(entryBytes, Charsets.UTF_8).trim()
                                if (candidateTxt.contains("\"factions\"", ignoreCase = true) ||
                                    candidateTxt.contains("\"Factions\"", ignoreCase = true) ||
                                    candidateTxt.contains("\"ChannelName\"", ignoreCase = true)) {
                                    foundJsonConfig = candidateTxt
                                }
                            }
                        }
                    }
                    zis.closeEntry()
                    entry = zis.nextEntry
                }
            }

            if (foundJsonConfig != null && saveParsedConfigJson(ctx, foundJsonConfig!!)) {
                return true
            }

            val extractedFiles = bundleDir.walkTopDown().filter { it.isFile }.toList()
            if (extractedFiles.isNotEmpty()) {
                val fallbackJson = JSONObject().apply {
                    put("eventName", "Pakiet Map Polowych")
                    put("factions", JSONArray().put(JSONObject().apply {
                        put("Name", "OPERATOR")
                        put("ColorHex", "#1FE0C5")
                        put("ChannelName", "TAK-MESH")
                        put("LoraRegion", "EU_868")
                        put("LoraModemPreset", "SHORT_FAST")
                        put("HopLimit", 3)
                    }))
                }
                return saveParsedConfigJson(ctx, fallbackJson.toString())
            }
            false
        } catch (_: Exception) {
            false
        }
    }

    private fun writeSyntheticFtakZipFromCurrentJson(ctx: Context, jsonText: String) {
        try {
            val outFtak = getSavedRawFtakArchiveFile(ctx)
            ZipOutputStream(BufferedOutputStream(FileOutputStream(outFtak))).use { zos ->
                zos.putNextEntry(ZipEntry("config.json"))
                zos.write(jsonText.toByteArray(Charsets.UTF_8))
                zos.closeEntry()
            }
        } catch (_: Exception) { }
    }

    private fun saveParsedConfigJson(ctx: Context, rawJson: String): Boolean {
        return try {
            val factions = parseFactionsFromConfigJson(rawJson)
            if (factions.isEmpty()) return false
            val root = JSONObject(rawJson)
            val evName = root.optString("eventName", root.optString("name", "OPERACJA TAKTYCZNA")).ifBlank { "OPERACJA TAKTYCZNA" }
            ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
                .putString(KEY_EVENT_NAME, evName)
                .putString(KEY_RAW_CONFIG_JSON, rawJson)
                .putString(KEY_ACTIVE_FACTION, factions.first().name)
                .apply()
            true
        } catch (_: Exception) {
            false
        }
    }

    private fun parseFactionsFromConfigJson(rawJson: String): List<FtakFactionProfile> {
        val list = mutableListOf<FtakFactionProfile>()
        try {
            val root = JSONObject(rawJson)
            val arr = root.optJSONArray("factions") ?: root.optJSONArray("Factions")
            if (arr != null) {
                for (i in 0 until arr.length()) {
                    val o = arr.optJSONObject(i) ?: continue
                    list.add(parseSingleFactionJson(o, i + 1))
                }
            } else if (root.has("ChannelName") || root.has("channelName") || root.has("OtsHost")) {
                list.add(parseSingleFactionJson(root, 1))
            }
        } catch (_: Exception) { }
        return list
    }

    private fun parseSingleFactionJson(o: JSONObject, index: Int): FtakFactionProfile {
        val name = o.optString("Name", o.optString("name", "STRONA-$index")).trim().ifEmpty { "STRONA-$index" }
        val colorHex = o.optString("ColorHex", o.optString("colorHex", "#1FE0C5")).trim().ifEmpty { "#1FE0C5" }
        val chName = o.optString("ChannelName", o.optString("channelName", name)).trim()
        val psk = o.optString("Psk", o.optString("psk", "")).trim()
        val chUrl = o.optString("ChannelUrl", o.optString("channelUrl", "")).trim()
        val region = o.optString("LoraRegion", o.optString("loraRegion", "EU_868")).trim().ifEmpty { "EU_868" }
        val preset = o.optString("LoraModemPreset", o.optString("loraModemPreset", "SHORT_FAST")).trim().ifEmpty { "SHORT_FAST" }
        val hop = o.optInt("HopLimit", o.optInt("hopLimit", 3)).coerceIn(1, 7)
        val ots = o.optString("OtsHost", o.optString("otsHost", "")).trim()
        val enroll = o.optString("EnrollmentUri", o.optString("enrollmentUri", "")).trim()
        val phone = o.optString("EmergencyPhone", o.optString("emergencyPhone", "")).trim()
        val pli = o.optString("PliProfile", o.optString("pliProfile", "ConstRateUnreliable")).trim()
        val mesh = o.optString("MeshProfile", o.optString("meshProfile", "StationaryMesh")).trim()

        val voices = mutableListOf<FtakVoiceChannel>()
        val vArr = o.optJSONArray("VoiceChannels") ?: o.optJSONArray("voiceChannels")
        if (vArr != null) {
            for (j in 0 until vArr.length()) {
                val vo = vArr.optJSONObject(j) ?: continue
                voices.add(
                    FtakVoiceChannel(
                        roleName = vo.optString("roleName", vo.optString("RoleName", "Kanał ${j + 1}")),
                        bandType = vo.optString("bandType", vo.optString("BandType", "PMR")),
                        freqMhz = vo.optString("freqMhz", vo.optString("FreqMhz", "-")),
                        toneOrCode = vo.optString("toneOrCode", vo.optString("ToneOrCode", "-"))
                    )
                )
            }
        }

        return FtakFactionProfile(
            name = name,
            colorHex = colorHex,
            channelName = chName,
            pskBase64 = psk,
            channelUrl = chUrl,
            loraRegion = region,
            loraPreset = preset,
            hopLimit = hop,
            otsHost = ots,
            enrollmentUri = enroll,
            emergencyPhone = phone,
            pliProfile = pli,
            meshProfile = mesh,
            voiceChannels = voices
        )
    }

    fun getFactionFilteredFiles(ctx: Context, faction: FtakFactionProfile): List<File> {
        val rootDir = getExtractedBundleDir(ctx)
        if (!rootDir.exists()) return emptyList()
        val facSlug = faction.name.lowercase(Locale.ROOT).replace(Regex("[^a-z0-9_-]"), "")

        return rootDir.walkTopDown().filter { file ->
            if (!file.isFile) return@filter false
            val ext = file.extension.lowercase(Locale.ROOT)
            if (ext !in setOf("mbtiles", "sqlite", "kmz", "kml", "gpx", "p12", "pref")) return@filter false

            val relPath = file.relativeTo(rootDir).path.replace('\\', '/').lowercase(Locale.ROOT)
            if (relPath.startsWith("factions/")) {
                val parts = relPath.split('/')
                parts.size >= 2 && parts[1] == facSlug
            } else {
                true
            }
        }.toList()
    }

    fun buildMeshtasticDeepLinkUrl(faction: FtakFactionProfile): String {
        if (faction.channelUrl.startsWith("https://meshtastic.org/e/#", ignoreCase = true) ||
            faction.channelUrl.startsWith("meshtastic://", ignoreCase = true)) {
            return faction.channelUrl
        }

        val pskBytes = try {
            if (faction.pskBase64.isNotBlank()) Base64.decode(faction.pskBase64, Base64.DEFAULT)
            else byteArrayOf(1)
        } catch (_: Exception) {
            byteArrayOf(1)
        }

        val cleanNameBytes = faction.channelName
            .take(11)
            .toByteArray(Charsets.UTF_8)

        val chSettingsOut = ByteArrayOutputStream()
        writeProtoBytes(chSettingsOut, 0x12, pskBytes)
        if (cleanNameBytes.isNotEmpty()) {
            writeProtoBytes(chSettingsOut, 0x1A, cleanNameBytes)
        }
        writeProtoVarintField(chSettingsOut, 0x28, 1)
        writeProtoVarintField(chSettingsOut, 0x30, 1)

        val loraConfigOut = ByteArrayOutputStream()
        writeProtoVarintField(loraConfigOut, 0x08, 1)
        val presetEnum = when (faction.loraPreset.uppercase(Locale.ROOT)) {
            "LONG_FAST" -> 0
            "LONG_SLOW" -> 1
            "VERY_LONG_SLOW" -> 2
            "MEDIUM_SLOW" -> 3
            "MEDIUM_FAST" -> 4
            "SHORT_SLOW" -> 5
            "SHORT_FAST" -> 6
            "LONG_MODERATE" -> 7
            "SHORT_TURBO" -> 8
            else -> 6
        }
        if (presetEnum != 0) writeProtoVarintField(loraConfigOut, 0x10, presetEnum)

        val regionEnum = when (faction.loraRegion.uppercase(Locale.ROOT)) {
            "US" -> 1
            "EU_433" -> 2
            "EU_868" -> 3
            "CN" -> 4
            "JP" -> 5
            "ANZ" -> 6
            "UA_433" -> 14
            "UA_868" -> 15
            else -> 3
        }
        writeProtoVarintField(loraConfigOut, 0x38, regionEnum)
        writeProtoVarintField(loraConfigOut, 0x40, faction.hopLimit.coerceIn(1, 7))
        writeProtoVarintField(loraConfigOut, 0x48, 1)

        val channelSetOut = ByteArrayOutputStream()
        writeProtoBytes(channelSetOut, 0x0A, chSettingsOut.toByteArray())
        writeProtoBytes(channelSetOut, 0x12, loraConfigOut.toByteArray())

        val base64Url = Base64.encodeToString(
            channelSetOut.toByteArray(),
            Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING
        )
        return "https://meshtastic.org/e/#$base64Url"
    }

    private fun writeProtoVarint(out: ByteArrayOutputStream, value: Int) {
        var v = value
        while ((v and 0x7F.inv()) != 0) {
            out.write((v and 0x7F) or 0x80)
            v = v ushr 7
        }
        out.write(v and 0x7F)
    }

    private fun writeProtoVarintField(out: ByteArrayOutputStream, tagByte: Int, value: Int) {
        out.write(tagByte)
        writeProtoVarint(out, value)
    }

    private fun writeProtoBytes(out: ByteArrayOutputStream, tagByte: Int, payload: ByteArray) {
        out.write(tagByte)
        writeProtoVarint(out, payload.size)
        out.write(payload)
    }

    fun provisionMeshtasticOneClick(activity: Activity): Boolean {
        val fac = getActiveFaction(activity)
        if (fac == null) {
            Toast.makeText(activity, "⚠️ Najpierw zeskanuj kod QR lub wczytaj plik .ftak!", Toast.LENGTH_LONG).show()
            return false
        }

        val deepLink = buildMeshtasticDeepLinkUrl(fac)
        try {
            val cb = activity.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("Meshtastic Channel URL", deepLink))
        } catch (_: Exception) { }

        return try {
            val meshIntent = Intent(Intent.ACTION_VIEW, Uri.parse(deepLink)).apply {
                setPackage("com.geeksville.mesh")
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            activity.startActivity(meshIntent)
            Toast.makeText(activity, "📡 Przekazano kanał '${fac.channelName}' (${fac.loraRegion}) do Meshtastic!", Toast.LENGTH_LONG).show()
            true
        } catch (_: Exception) {
            try {
                val genericIntent = Intent(Intent.ACTION_VIEW, Uri.parse(deepLink)).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                activity.startActivity(genericIntent)
                true
            } catch (_: Exception) {
                Toast.makeText(activity, "📋 Skopiowano link konfiguracyjny Meshtastic do schowka:\n$deepLink", Toast.LENGTH_LONG).show()
                false
            }
        }
    }

    private fun mapHexColorToAtakTeam(colorHex: String, factionName: String): String {
        val h = colorHex.uppercase(Locale.ROOT)
        val n = factionName.uppercase(Locale.ROOT)
        return when {
            n.contains("BLU") || h.contains("2684FF") || h.contains("0000FF") -> "Blue"
            n.contains("OPF") || n.contains("RED") || h.contains("EB4034") || h.contains("FF0000") -> "Red"
            n.contains("GREEN") || h.contains("2ECC71") -> "Green"
            n.contains("ORGA") || n.contains("YEL") || h.contains("FAD02C") || h.contains("F5B82E") -> "Yellow"
            h.contains("1FE0C5") -> "Cyan"
            else -> "Cyan"
        }
    }

    fun buildAtakMissionPackageZip(ctx: Context, faction: FtakFactionProfile, callsign: String): File {
        val outZip = File(ctx.cacheDir, "FieldTakHub_${faction.name}_ATAK.zip")
        if (outZip.exists()) outZip.delete()

        val teamColor = mapHexColorToAtakTeam(faction.colorHex, faction.name)
        val factionFiles = getFactionFilteredFiles(ctx, faction)
        val packageUid = UUID.randomUUID().toString()

        val prefSb = StringBuilder()
        prefSb.appendLine("<?xml version='1.0' standalone='yes'?>")
        prefSb.appendLine("<preferences>")
        prefSb.appendLine("  <preference version=\"1\" name=\"com.atakmap.app.civ_preferences\">")
        prefSb.appendLine("    <entry key=\"locationCallsign\" class=\"class java.lang.String\">${escapeXml(callsign)}</entry>")
        prefSb.appendLine("    <entry key=\"locationTeam\" class=\"class java.lang.String\">$teamColor</entry>")
        prefSb.appendLine("    <entry key=\"coord_display_pref\" class=\"class java.lang.String\">MGRS</entry>")
        prefSb.appendLine("  </preference>")

        if (faction.otsHost.isNotBlank()) {
            prefSb.appendLine("  <preference version=\"1\" name=\"cot_streams\">")
            prefSb.appendLine("    <entry key=\"count\" class=\"class java.lang.Integer\">1</entry>")
            prefSb.appendLine("    <entry key=\"description0\" class=\"class java.lang.String\">Serwer Taktyczny (${escapeXml(faction.name)})</entry>")
            prefSb.appendLine("    <entry key=\"enabled0\" class=\"class java.lang.Boolean\">true</entry>")
            prefSb.appendLine("    <entry key=\"connectString0\" class=\"class java.lang.String\">${escapeXml(faction.otsHost)}:8089:ssl</entry>")
            prefSb.appendLine("  </preference>")
        }
        prefSb.appendLine("</preferences>")

        val manSb = StringBuilder()
        manSb.appendLine("<MissionPackageManifest version=\"2\">")
        manSb.appendLine("  <Configuration>")
        manSb.appendLine("    <Parameter name=\"uid\" value=\"$packageUid\"/>")
        manSb.appendLine("    <Parameter name=\"name\" value=\"FieldTakHub_${escapeXml(faction.name)}\"/>")
        manSb.appendLine("    <Parameter name=\"onReceiveImport\" value=\"true\"/>")
        manSb.appendLine("    <Parameter name=\"onReceiveDelete\" value=\"false\"/>")
        manSb.appendLine("  </Configuration>")
        manSb.appendLine("  <Contents>")
        manSb.appendLine("    <Content ignore=\"false\" zipEntry=\"prefs/config.pref\"/>")

        val zipEntriesToWrite = mutableListOf<Pair<String, File>>()
        for (f in factionFiles) {
            val ext = f.extension.lowercase(Locale.ROOT)
            val folder = when (ext) {
                "mbtiles", "sqlite" -> "imagery"
                "p12" -> "certs"
                else -> "overlays"
            }
            val zPath = "$folder/${f.name}"
            manSb.appendLine("    <Content ignore=\"false\" zipEntry=\"${escapeXml(zPath)}\"/>")
            zipEntriesToWrite.add(zPath to f)
        }

        manSb.appendLine("  </Contents>")
        manSb.appendLine("</MissionPackageManifest>")

        ZipOutputStream(BufferedOutputStream(FileOutputStream(outZip))).use { zos ->
            zos.putNextEntry(ZipEntry("MANIFEST/manifest.xml"))
            zos.write(manSb.toString().toByteArray(Charsets.UTF_8))
            zos.closeEntry()

            zos.putNextEntry(ZipEntry("prefs/config.pref"))
            zos.write(prefSb.toString().toByteArray(Charsets.UTF_8))
            zos.closeEntry()

            for ((zPath, file) in zipEntriesToWrite) {
                zos.putNextEntry(ZipEntry(zPath))
                file.inputStream().use { it.copyTo(zos) }
                zos.closeEntry()
            }
        }
        return outZip
    }

    fun provisionAtakOneClick(activity: Activity): Boolean {
        val fac = getActiveFaction(activity)
        if (fac == null) {
            Toast.makeText(activity, "⚠️ Najpierw zeskanuj kod QR lub wczytaj plik .ftak!", Toast.LENGTH_LONG).show()
            return false
        }

        val callsign = getCallsign(activity)
        return try {
            val zipFile = buildAtakMissionPackageZip(activity, fac, callsign)
            val contentUri = FtakShareContentProvider.buildUriForFile(activity, zipFile)

            val pm = activity.packageManager
            val atakInstalled = try {
                pm.getPackageInfo("com.atakmap.app.civ", 0)
                true
            } catch (_: Exception) { false }

            val viewIntent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(contentUri, "application/zip")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
                if (atakInstalled) setPackage("com.atakmap.app.civ")
            }

            activity.startActivity(Intent.createChooser(viewIntent, "Zaimportuj pakiet misji w ATAK-CIV").apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            })
            Toast.makeText(
                activity,
                "🗺️ Wysłano pakiet misji do ATAK-CIV (Callsign: $callsign, Strona: ${fac.name})!",
                Toast.LENGTH_LONG
            ).show()
            true
        } catch (ex: Exception) {
            Toast.makeText(activity, "⚠️ Błąd wysyłania pakietu do ATAK: ${ex.message}", Toast.LENGTH_LONG).show()
            false
        }
    }

    @SuppressLint("MissingPermission")
    fun captureOnDemandMgrsFix(ctx: Context): OnDemandMgrsFix {
        var bestLoc: Location? = null
        try {
            val lm = ctx.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
            if (lm != null) {
                for (p in listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER, LocationManager.PASSIVE_PROVIDER)) {
                    try {
                        val loc = lm.getLastKnownLocation(p)
                        if (loc != null && (bestLoc == null || loc.accuracy < bestLoc.accuracy)) {
                            bestLoc = loc
                        }
                    } catch (_: SecurityException) { }
                }
            }
        } catch (_: Exception) { }

        if (bestLoc == null) {
            return OnDemandMgrsFix(
                fullMgrs = "BRAK FIXA GPS (Wyjdź na otwarty teren)",
                shortGrid100m = "--- / ---",
                keypadNum = 5,
                quarterChar = "-",
                wgs84Text = "Brak sygnału GPS",
                accuracyMeters = 0,
                hasValidFix = false
            )
        }

        val lat = bestLoc.latitude
        val lon = bestLoc.longitude
        val zone = floor((lon + 180.0) / 6.0).toInt() + 1
        val a = 6378137.0
        val eccSq = 0.00669438
        val k0 = 0.9996

        val latRad = lat * PI / 180.0
        val lonRad = lon * PI / 180.0
        val lonOriginRad = ((zone - 1) * 6 - 180 + 3) * PI / 180.0
        val eccPrimeSq = eccSq / (1.0 - eccSq)

        val nVal = a / sqrt(1.0 - eccSq * sin(latRad) * sin(latRad))
        val tVal = tan(latRad) * tan(latRad)
        val cVal = eccPrimeSq * cos(latRad) * cos(latRad)
        val aVal = cos(latRad) * (lonRad - lonOriginRad)

        val mVal = a * ((1.0 - eccSq / 4.0 - 3.0 * eccSq * eccSq / 64.0) * latRad
                - (3.0 * eccSq / 8.0 + 3.0 * eccSq * eccSq / 32.0) * sin(2.0 * latRad)
                + (15.0 * eccSq * eccSq / 256.0) * sin(4.0 * latRad))

        val easting = k0 * nVal * (aVal + (1.0 - tVal + cVal) * aVal.pow(3) / 6.0
                + (5.0 - 18.0 * tVal + tVal * tVal + 72.0 * cVal - 58.0 * eccPrimeSq) * aVal.pow(5) / 120.0) + 500000.0

        var northing = k0 * (mVal + nVal * tan(latRad) * (aVal * aVal / 2.0
                + (5.0 - tVal + 9.0 * cVal + 4.0 * cVal * cVal) * aVal.pow(4) / 24.0
                + (61.0 - 58.0 * tVal + tVal * tVal + 600.0 * cVal - 330.0 * eccPrimeSq) * aVal.pow(6) / 720.0))
        if (lat < 0.0) northing += 10000000.0

        val bands = "CDEFGHJKLMNPQRSTUVWXX"
        val bandIdx = ((lat + 80.0) / 8.0).toInt().coerceIn(0, bands.length - 1)
        val latBand = bands[bandIdx]

        val set = ((zone - 1) % 6) + 1
        val colLetters = when (set % 3) {
            1 -> "ABCDEFGH"
            2 -> "JKLMNPQR"
            else -> "STUVWXYZ"
        }
        val rowLetters = if (set % 2 == 1) "ABCDEFGHJKLMNPQRSTUV" else "FGHJKLMNPQRSTUVABCDE"

        val colIdx = (floor(easting / 100000.0).toInt() - 1).coerceIn(0, 7)
        val rowIdx = ((floor(northing / 100000.0).toInt() % 20) + 20) % 20
        val sq100k = "${colLetters[colIdx]}${rowLetters[rowIdx]}"

        val e5 = ((floor(easting).toInt() % 100000) + 100000) % 100000
        val n5 = ((floor(northing).toInt() % 100000) + 100000) % 100000

        val remE = ((easting % 100.0) + 100.0) % 100.0
        val remN = ((northing % 100.0) + 100.0) % 100.0

        val kpCol = floor(remE / (100.0 / 3.0)).toInt().coerceIn(0, 2)
        val kpRowFromBottom = floor(remN / (100.0 / 3.0)).toInt().coerceIn(0, 2)
        val kpMatrix = arrayOf(intArrayOf(1, 2, 3), intArrayOf(4, 5, 6), intArrayOf(7, 8, 9))
        val keypadNum = kpMatrix[kpRowFromBottom][kpCol]

        val qCol = if (remE >= 50.0) 1 else 0
        val qRowFromTop = if (remN >= 50.0) 0 else 1
        val qMatrix = arrayOf(arrayOf("A", "B"), arrayOf("C", "D"))
        val quarterChar = qMatrix[qRowFromTop][qCol]

        val e3 = (e5 / 100) % 1000
        val n3 = (n5 / 100) % 1000

        return OnDemandMgrsFix(
            fullMgrs = String.format(Locale.US, "%d%c %s %05d %05d", zone, latBand, sq100k, e5, n5),
            shortGrid100m = String.format(Locale.US, "%03d/%03d", e3, n3),
            keypadNum = keypadNum,
            quarterChar = quarterChar,
            wgs84Text = String.format(Locale.US, "%.6f°N, %.6f°E", lat, lon),
            accuracyMeters = bestLoc.accuracy.roundToInt(),
            hasValidFix = true
        )
    }

    private fun escapeXml(s: String): String =
        s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;").replace("'", "&apos;")
}