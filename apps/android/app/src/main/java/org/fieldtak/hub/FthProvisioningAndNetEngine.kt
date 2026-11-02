package org.fieldtak.hub

import android.annotation.SuppressLint
import android.bluetooth.*
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.*
import java.net.*
import java.nio.charset.StandardCharsets
import java.util.*
import java.util.concurrent.Executors
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

// =============================================================================
// 1. ZARZĄDCA SESJI I PAMIĘCI PODRĘCZNEJ OPERATORA (SHARED PREFERENCES)
// =============================================================================
object FthSessionManager {
    private const val PREFS_NAME = "fth_companion_v34_prefs"
    private const val KEY_ACTIVE_JSON = "active_faction_json"
    private const val KEY_CALLSIGN = "operator_callsign"
    private const val KEY_SHORT_NAME = "mesh_short_name"
    private const val KEY_UI_LANG = "ui_lang"

    var currentLang: String = "PL"
    var activeProfile: FthFactionProfile = FthFactionProfile()

    fun init(ctx: Context) {
        val prefs = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        currentLang = prefs.getString(KEY_UI_LANG, "PL") ?: "PL"
        val savedCall = prefs.getString(KEY_CALLSIGN, "Wilk-1") ?: "Wilk-1"
        val savedShort = prefs.getString(KEY_SHORT_NAME, "Wlk1") ?: "Wlk1"
        val rawJson = prefs.getString(KEY_ACTIVE_JSON, null)

        if (!rawJson.isNullOrBlank()) {
            val parsed = FthPayloadParser.parseFactionFromJsonString(rawJson)
            if (parsed != null) {
                activeProfile = parsed
            }
        }
        activeProfile.operatorCallsign = savedCall
        activeProfile.meshtasticShortName = savedShort
    }

    fun saveActiveProfile(ctx: Context, profile: FthFactionProfile) {
        activeProfile = profile
        val prefs = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        prefs.edit()
            .putString(KEY_ACTIVE_JSON, profile.toCompactQrJson())
            .putString(KEY_CALLSIGN, profile.operatorCallsign)
            .putString(KEY_SHORT_NAME, profile.meshtasticShortName)
            .apply()
    }

    fun toggleLanguage(ctx: Context): String {
        currentLang = if (currentLang == "EN") "PL" else "EN"
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_UI_LANG, currentLang)
            .apply()
        return currentLang
    }

    fun t(pl: String, en: String): String = if (currentLang == "EN") en else pl
}

// =============================================================================
// 2. PARSER KODÓW QR, LINKÓW WI-FI/LAN ORAZ PLIKÓW PACZEK (.FTAK / .FTHPROJ)
// =============================================================================
object FthPayloadParser {
    private val bgExecutor = Executors.newCachedThreadPool()
    private val mainHandler = Handler(Looper.getMainLooper())

    fun processScannedOrImportedPayload(
        ctx: Context,
        rawPayload: String,
        onStatus: (String) -> Unit,
        onProfileReadyForCallsign: (FthFactionProfile) -> Unit
    ) {
        val trimmed = rawPayload.trim()
        if (trimmed.isEmpty()) {
            onStatus(FthSessionManager.t("⚠️ Pusty kod QR lub ładunek.", "⚠️ Empty QR code or payload."))
            return
        }

        // 1. Jeśli zeskanowano link HTTP (np. z lokalnego serwera Buildera lub Hotspotu dowódcy w lesie)
        if (trimmed.startsWith("http://", ignoreCase = true) || trimmed.startsWith("https://", ignoreCase = true)) {
            onStatus(FthSessionManager.t("📡 Pobieranie paczki przez Wi-Fi / LAN: $trimmed ...", "📡 Downloading package over Wi-Fi / LAN: $trimmed ..."))
            bgExecutor.execute {
                try {
                    val targetUrl = if (trimmed.endsWith(".html", ignoreCase = true)) {
                        trimmed.substring(0, trimmed.length - 5) + ".json"
                    } else {
                        trimmed
                    }
                    val conn = URL(targetUrl).openConnection() as HttpURLConnection
                    conn.connectTimeout = 6000
                    conn.readTimeout = 8000
                    conn.requestMethod = "GET"

                    val bytes = conn.inputStream.use { it.readBytes() }
                    val parsed = parseBytesOrJson(ctx, bytes)
                    mainHandler.post {
                        if (parsed != null) {
                            onStatus(FthSessionManager.t("✅ Pobrano profil strony: ${parsed.factionName}", "✅ Downloaded faction profile: ${parsed.factionName}"))
                            onProfileReadyForCallsign(parsed)
                        } else {
                            onStatus(FthSessionManager.t("⚠️ Nie udało się odczytać danych z adresu: $trimmed", "⚠️ Failed to parse data from URL: $trimmed"))
                        }
                    }
                } catch (ex: Exception) {
                    mainHandler.post {
                        onStatus(FthSessionManager.t("❌ Błąd pobierania Wi-Fi/LAN: ${ex.message}", "❌ Wi-Fi/LAN download error: ${ex.message}"))
                    }
                }
            }
            return
        }

        // 2. Jeśli zeskanowano bezpośredni JSON lub Base64 JSON z kodu QR
        val directProfile = parseFactionFromJsonString(trimmed)
        if (directProfile != null) {
            onStatus(FthSessionManager.t("✅ Rozpoznano kod QR strony: ${directProfile.factionName}", "✅ Recognized faction QR: ${directProfile.factionName}"))
            onProfileReadyForCallsign(directProfile)
            return
        }

        // 3. Próba odkodowania Base64
        try {
            val decoded = String(Base64.decode(trimmed, Base64.DEFAULT), StandardCharsets.UTF_8)
            val b64Profile = parseFactionFromJsonString(decoded)
            if (b64Profile != null) {
                onStatus(FthSessionManager.t("✅ Rozpoznano zakodowany profil: ${b64Profile.factionName}", "✅ Recognized encoded profile: ${b64Profile.factionName}"))
                onProfileReadyForCallsign(b64Profile)
                return
            }
        } catch (_: Exception) {
        }

        // 4. Jeśli zeskanowano URL Meshtastic (https://meshtastic.org/e/#... lub meshtastic://...)
        if (trimmed.contains("meshtastic", ignoreCase = true)) {
            val copy = FthSessionManager.activeProfile.copy(channelUrl = trimmed)
            onStatus(FthSessionManager.t("✅ Wykryto link kanału Meshtastic!", "✅ Detected Meshtastic channel URL!"))
            onProfileReadyForCallsign(copy)
            return
        }

        onStatus(FthSessionManager.t("⚠️ Nieznany format danych QR. Używam domyślnego szablonu z tym kluczem.", "⚠️ Unrecognized QR format."))
    }

    fun parseBytesOrJson(ctx: Context, bytes: ByteArray): FthFactionProfile? {
        if (bytes.isEmpty()) return null

        // Sprawdź czy to archiwum ZIP (.ftak) — nagłówek PK (0x50, 0x4B)
        if (bytes.size > 4 && bytes[0] == 0x50.toByte() && bytes[1] == 0x4B.toByte()) {
            return parseFtakZipBytes(ctx, bytes)
        }

        val text = String(bytes, StandardCharsets.UTF_8).replace("\u200B", "")
        return parseFactionFromJsonString(text)
    }

    private fun parseFtakZipBytes(ctx: Context, zipBytes: ByteArray): FthFactionProfile? {
        var foundProfile: FthFactionProfile? = null
        val extractedMapNames = mutableListOf<String>()
        val mapsDir = File(ctx.filesDir, "imported_maps").apply { mkdirs() }

        try {
            ZipInputStream(ByteArrayInputStream(zipBytes)).use { zis ->
                var entry: ZipEntry? = zis.nextEntry
                while (entry != null) {
                    val name = entry.name ?: ""
                    if (!entry.isDirectory) {
                        val entryBytes = zis.readBytes()
                        val lower = name.lowercase(Locale.ROOT)
                        if (lower.endsWith(".json") || lower.endsWith(".fthproj")) {
                            val text = String(entryBytes, StandardCharsets.UTF_8).replace("\u200B", "")
                            val p = parseFactionFromJsonString(text)
                            if (p != null) {
                                foundProfile = p
                            }
                        } else if (lower.endsWith(".kmz") || lower.endsWith(".gpx") || lower.endsWith(".mbtiles") || lower.endsWith(".sqlitedb")) {
                            val simpleName = File(name).name
                            val outFile = File(mapsDir, simpleName)
                            outFile.writeBytes(entryBytes)
                            extractedMapNames.add(simpleName)
                        }
                    }
                    zis.closeEntry()
                    entry = zis.nextEntry
                }
            }
        } catch (_: Exception) {
        }

        if (foundProfile != null && extractedMapNames.isNotEmpty()) {
            foundProfile!!.attachedMapFiles = extractedMapNames
        }
        return foundProfile
    }

    fun parseFactionFromJsonString(rawJson: String): FthFactionProfile? {
        return try {
            val clean = rawJson.trim().replace("\u200B", "")
            if (!clean.startsWith("{")) return null
            val root = JSONObject(clean)

            val eventName = root.optString("eventName", root.optString("EventName", "MILSIM 2026"))

            // Obsługa pełnego pliku projektu .fthproj (z tablicą "factions") lub pojedynczego obiektu frakcji
            val facObj: JSONObject = when {
                root.has("factions") && root.optJSONArray("factions")?.length() ?: 0 > 0 ->
                    root.getJSONArray("factions").getJSONObject(0)
                root.has("Factions") && root.optJSONArray("Factions")?.length() ?: 0 > 0 ->
                    root.getJSONArray("Factions").getJSONObject(0)
                else -> root
            }

            // Sprawdź czy to plik samego OPORDu (zawiera TargetFaction i Days)
            val isOpordOnly = root.has("TargetFaction") && root.has("Days")
            val factionName = if (isOpordOnly) {
                root.optString("TargetFaction", "BLUFOR")
            } else {
                facObj.optString("Name", facObj.optString("name", facObj.optString("faction", "BLUFOR")))
            }

            val colorHex = facObj.optString("ColorHex", facObj.optString("colorHex", "#2684FF"))
            val channelName = facObj.optString("ChannelName", facObj.optString("channelName", "$factionName-TAK"))
            val psk = facObj.optString("Psk", facObj.optString("psk", ""))
            val channelUrl = facObj.optString("ChannelUrl", facObj.optString("channelUrl", ""))
            val region = facObj.optString("LoraRegion", facObj.optString("loraRegion", "EU_868"))
            val preset = facObj.optString("LoraModemPreset", facObj.optString("loraModemPreset", "SHORT_FAST"))
            val hopLimit = facObj.optInt("HopLimit", facObj.optInt("hopLimit", 3))
            val otsHost = facObj.optString("OtsHost", facObj.optString("otsHost", ""))
            val enrollUri = facObj.optString("EnrollmentUri", facObj.optString("enrollmentUri", ""))
            val emergencyPhone = facObj.optString("EmergencyPhone", facObj.optString("emergencyPhone", "+48 600 000 000"))

            val voiceList = mutableListOf<FthVoiceChannel>()
            val vcArr = facObj.optJSONArray("VoiceChannels") ?: facObj.optJSONArray("voiceChannels")
            if (vcArr != null) {
                for (i in 0 until vcArr.length()) {
                    val vo = vcArr.optJSONObject(i) ?: continue
                    voiceList.add(
                        FthVoiceChannel(
                            roleName = vo.optString("roleName", vo.optString("RoleName", "Kanał ${i + 1}")),
                            bandType = vo.optString("bandType", vo.optString("BandType", "PMR")),
                            freqMhz = vo.optString("freqMhz", vo.optString("FreqMhz", "-")),
                            toneOrCode = vo.optString("toneOrCode", vo.optString("ToneOrCode", "-"))
                        )
                    )
                }
            }
            if (voiceList.isEmpty()) {
                voiceList.add(FthVoiceChannel("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"))
            }

            val daysList = mutableListOf<FthOpordDayPlan>()
            val daysArr = root.optJSONArray("OpordDays") ?: root.optJSONArray("Days") ?: facObj.optJSONArray("OpordDays")
            if (daysArr != null && daysArr.length() > 0) {
                for (i in 0 until daysArr.length()) {
                    val dObj = daysArr.optJSONObject(i) ?: continue
                    val roster = mutableListOf<FthRosterMember>()
                    val rArr = dObj.optJSONArray("SquadRoster")
                    if (rArr != null) {
                        for (rIdx in 0 until rArr.length()) {
                            val ro = rArr.optJSONObject(rIdx) ?: continue
                            roster.add(
                                FthRosterMember(
                                    operatorOrTeam = ro.optString("OperatorOrTeam", "ALFA-${rIdx + 1}"),
                                    primaryRole = ro.optString("PrimaryRole", "Szturmowiec"),
                                    secondaryRole = ro.optString("SecondaryRole", "-"),
                                    weaponAndGear = ro.optString("WeaponAndGear", "Karabinek")
                                )
                            )
                        }
                    }

                    daysList.add(
                        FthOpordDayPlan(
                            dayTitle = dObj.optString("DayTitle", "Dzień ${i + 1}"),
                            dateIso = dObj.optString("DateIso", "2026-10-27"),
                            classification = dObj.optString("Classification", "JAWNE / UNCLASSIFIED"),
                            isSecret = dObj.optBoolean("IsSecretClassification", false),
                            operationNumber = dObj.optString("OperationNumber", "OPORD 0${i + 1}-2026"),
                            dtgIssued = dObj.optString("DtgIssued", "270800Z OCT 2026"),
                            issuingHq = dObj.optString("IssuingHq", "TOC / SZTAB"),
                            locationName = dObj.optString("LocationName", "Rejon AO"),
                            locationLat = dObj.optString("LocationLat", "49.8506"),
                            locationLon = dObj.optString("LocationLon", "18.8133"),
                            daySchedule = dObj.optString("DaySpecificTasks", "• 08:30 — Odprawa\n• 09:00 — Wejście w AO"),
                            aoiPoiDesc = dObj.optString("AreaOfInterestPoiDesc", "Punkty zdefiniowane w paczce .KMZ / .GPX."),
                            terrainAndMetoc = dObj.optString("TerrainAndWeather", "Analiza terenu LiDAR + METOC."),
                            enemyForces = dObj.optString("EnemyForces", "Patrole przeciwnika w rejonie AO."),
                            friendlyForces = dObj.optString("FriendlyForces", "Siły własne: $factionName."),
                            missionFullText = dObj.optString("MissionFullStatement", "Zabezpieczyć wyznaczone cele w sektorze AO."),
                            additionalTasks = dObj.optString("AdditionalTasksSummary", "-"),
                            commandersIntent = dObj.optString("CommandersIntent", "Zgranie zespołowe i nawigacja ATAK."),
                            chainOfCommand = dObj.optString("SuccessionOfCommand", "Lider -> Navi -> RTO"),
                            sarnegKeyword10 = dObj.optString("SarnegKeyWord10", "PRZEDMIOTY"),
                            targetCodeNames = dObj.optString("TargetCodeNames", "• OBJ ALPHA = 'ZAMEK'"),
                            challengePassword = dObj.optString("ChallengeAndPassword", "GROM - BŁYSK - TARCZA"),
                            medevacRules = dObj.optString("MedevacRules", "1. trafienie: opatrunek 2 min. 2. trafienie: Respawn."),
                            gearChecklist = dObj.optString("SquadGearChecklist", "Ochrona wzroku, dead-rag, radio, ATAK."),
                            squadRoster = roster
                        )
                    )
                }
            }
            if (daysList.isEmpty()) {
                daysList.add(FthOpordDayPlan(friendlyForces = "Siły własne: $factionName"))
            }

            FthFactionProfile(
                eventName = eventName,
                factionName = factionName,
                colorHex = colorHex,
                operatorCallsign = FthSessionManager.activeProfile.operatorCallsign,
                meshtasticShortName = FthSessionManager.activeProfile.meshtasticShortName,
                channelName = channelName,
                pskBase64 = psk,
                channelUrl = channelUrl,
                loraRegion = region,
                loraModemPreset = preset,
                hopLimit = hopLimit,
                otsHost = otsHost,
                enrollmentUri = enrollUri,
                emergencyPhone = emergencyPhone,
                voiceChannels = voiceList,
                opordDays = daysList
            )
        } catch (_: Exception) {
            null
        }
    }
}

// =============================================================================
// 3. WSTRZYKIWANIE CALLSIGNU ("Wilk-1"), KOLORU I SERWERA OTS DO ATAK-CIV
// =============================================================================
object AtakProvisioningEngine {

    private const val ATAK_CIV_PACKAGE = "com.atakmap.app.civ"

    fun injectCallsignAndConfigIntoAtak(
        ctx: Context,
        profile: FthFactionProfile,
        onLog: (String) -> Unit
    ): Boolean {
        val callsign = CallsignFormatter.formatAtakCallsign(profile.operatorCallsign)
        val teamColor = CallsignFormatter.mapHexToAtakTeamColorName(profile.colorHex)
        val deviceUid = "ANDROID-FTH-${callsign.uppercase(Locale.ROOT)}-${UUID.randomUUID().toString().take(6)}"

        onLog(FthSessionManager.t(
            "🛡️ [ATAK-CIV] Generowanie profilu: Callsign='$callsign', Zespół='$teamColor', OTS='${profile.otsHost.ifBlank { "Off-Grid" }}'...",
            "🛡️ [ATAK-CIV] Building profile: Callsign='$callsign', Team='$teamColor', OTS='${profile.otsHost.ifBlank { "Off-Grid" }}'..."
        ))

        return try {
            // 1. Zbuduj plik XML preferencji ATAK (fth_operator.pref) wstrzykujący Callsign, kolor drużyny i serwer OTS
            val prefXml = buildAtakPreferencesXml(callsign, teamColor, profile)

            // 2. Zbuduj manifest.xml dla ATAK DataPackage
            val pkgUid = UUID.randomUUID().toString()
            val pkgName = "FTH_${callsign}_${profile.factionName}"
            val mapsDir = File(ctx.filesDir, "imported_maps")
            val mapFiles = if (mapsDir.exists()) {
                mapsDir.listFiles()?.filter { it.isFile } ?: emptyList()
            } else {
                emptyList()
            }

            val manifestXml = buildAtakManifestXml(pkgUid, pkgName, callsign, mapFiles)

            // 3. Spakuj paczkę ZIP w pamięci podręcznej aplikacji oraz w katalogu publicznym ATAK (jeśli dostępny)
            val exportDir = File(ctx.getExternalFilesDir(null) ?: ctx.filesDir, "atak_datapackages").apply { mkdirs() }
            val zipFile = File(exportDir, "$pkgName.zip")

            ZipOutputStream(FileOutputStream(zipFile)).use { zos ->
                // MANIFEST/manifest.xml
                zos.putNextEntry(ZipEntry("MANIFEST/manifest.xml"))
                zos.write(manifestXml.toByteArray(StandardCharsets.UTF_8))
                zos.closeEntry()

                // prefs/fth_operator.pref
                zos.putNextEntry(ZipEntry("prefs/fth_operator.pref"))
                zos.write(prefXml.toByteArray(StandardCharsets.UTF_8))
                zos.closeEntry()

                // Dołącz zaimportowane pliki map (.kmz / .gpx / .mbtiles)
                for (mf in mapFiles) {
                    zos.putNextEntry(ZipEntry("maps/${mf.name}"))
                    zos.write(mf.readBytes())
                    zos.closeEntry()
                }
            }

            // Spróbuj również zapisać kopię w standardowym katalogu /sdcard/atak/tools/datapackage
            try {
                val sdAtakDir = File(Environment.getExternalStorageDirectory(), "atak/tools/datapackage")
                if (sdAtakDir.exists() || sdAtakDir.mkdirs()) {
                    val sdCopy = File(sdAtakDir, "$pkgName.zip")
                    zipFile.copyTo(sdCopy, overwrite = true)
                    onLog(FthSessionManager.t(
                        "📂 [ATAK] Zapisano paczkę bezpośrednio w /sdcard/atak/tools/datapackage/${sdCopy.name}",
                        "📂 [ATAK] Saved package directly to /sdcard/atak/tools/datapackage/${sdCopy.name}"
                    ))
                }
            } catch (_: Exception) {
            }

            // 4. Wyślij Intent do ATAK-CIV lub otwórz aplikację ATAK
            val launched = dispatchAtakImportIntent(ctx, zipFile, callsign, teamColor, deviceUid)
            if (launched) {
                onLog(FthSessionManager.t(
                    "✅ [ATAK-CIV] Wstrzyknięto Callsign '$callsign' [$teamColor] i przekazano paczkę do ATAK!",
                    "✅ [ATAK-CIV] Injected Callsign '$callsign' [$teamColor] and dispatched to ATAK!"
                ))
            } else {
                onLog(FthSessionManager.t(
                    "ℹ️ [ATAK-CIV] Wygenerowano gotowy profil '$pkgName.zip' (Callsign: $callsign, Zespół: $teamColor). Aplikacja ATAK nie jest zainstalowana lub działa w tle.",
                    "ℹ️ [ATAK-CIV] Generated profile '$pkgName.zip' (Callsign: $callsign, Team: $teamColor)."
                ))
            }
            true
        } catch (ex: Exception) {
            onLog(FthSessionManager.t(
                "❌ [ATAK-CIV] Błąd generowania paczki: ${ex.message}",
                "❌ [ATAK-CIV] Error building package: ${ex.message}"
            ))
            false
        }
    }

    private fun buildAtakPreferencesXml(callsign: String, teamColor: String, p: FthFactionProfile): String {
        val sb = StringBuilder()
        sb.appendLine("<?xml version='1.0' encoding='utf-8' standalone='yes'?>")
        sb.appendLine("<preferences>")
        sb.appendLine("  <preference version=\"1\" name=\"com.atakmap.app_preferences\">")
        sb.appendLine("    <entry key=\"locationCallsign\" class=\"class java.lang.String\">${escapeXml(callsign)}</entry>")
        sb.appendLine("    <entry key=\"locationTeam\" class=\"class java.lang.String\">${escapeXml(teamColor)}</entry>")
        sb.appendLine("    <entry key=\"atakRoleType\" class=\"class java.lang.String\">Team Member</entry>")
        sb.appendLine("    <entry key=\"coord_display_pref\" class=\"class java.lang.String\">MGRS</entry>")
        sb.appendLine("  </preference>")

        if (p.otsHost.isNotBlank()) {
            val cleanHost = p.otsHost.trim().removePrefix("https://").removePrefix("http://").trimEnd('/')
            sb.appendLine("  <preference version=\"1\" name=\"cot_streams\">")
            sb.appendLine("    <entry key=\"count\" class=\"class java.lang.Integer\">1</entry>")
            sb.appendLine("    <entry key=\"description0\" class=\"class java.lang.String\">FTH_${escapeXml(p.factionName)}_OTS</entry>")
            sb.appendLine("    <entry key=\"enabled0\" class=\"class java.lang.Boolean\">true</entry>")
            sb.appendLine("    <entry key=\"connectString0\" class=\"class java.lang.String\">${escapeXml(cleanHost)}:8089:ssl</entry>")
            sb.appendLine("  </preference>")
        }
        sb.appendLine("</preferences>")
        return sb.toString()
    }

    private fun buildAtakManifestXml(uid: String, pkgName: String, callsign: String, mapFiles: List<File>): String {
        val sb = StringBuilder()
        sb.appendLine("<MissionPackageManifest version=\"2\">")
        sb.appendLine("  <Configuration>")
        sb.appendLine("    <Parameter name=\"uid\" value=\"$uid\"/>")
        sb.appendLine("    <Parameter name=\"name\" value=\"${escapeXml(pkgName)}\"/>")
        sb.appendLine("    <Parameter name=\"onReceiveImport\" value=\"true\"/>")
        sb.appendLine("    <Parameter name=\"onReceiveDelete\" value=\"false\"/>")
        sb.appendLine("    <Parameter name=\"callsign\" value=\"${escapeXml(callsign)}\"/>")
        sb.appendLine("  </Configuration>")
        sb.appendLine("  <Contents>")
        sb.appendLine("    <Content ignore=\"false\" zipEntry=\"prefs/fth_operator.pref\"/>")
        for (mf in mapFiles) {
            sb.appendLine("    <Content ignore=\"false\" zipEntry=\"maps/${escapeXml(mf.name)}\"/>")
        }
        sb.appendLine("  </Contents>")
        sb.appendLine("</MissionPackageManifest>")
        return sb.toString()
    }

    private fun dispatchAtakImportIntent(
        ctx: Context,
        zipFile: File,
        callsign: String,
        teamColor: String,
        deviceUid: String
    ): Boolean {
        return try {
            // Wyślij broadcast konfiguracyjny dla wtyczek ATAK
            val broadcastIntent = Intent("com.atakmap.app.IMPORT_MISSION_PACKAGE").apply {
                putExtra("filepath", zipFile.absolutePath)
                putExtra("callsign", callsign)
                putExtra("team", teamColor)
                putExtra("uid", deviceUid)
            }
            ctx.sendBroadcast(broadcastIntent)

            // Jeśli ATAK-CIV jest zainstalowany, uruchom go z parametrami
            val pm = ctx.packageManager
            val launchIntent = pm.getLaunchIntentForPackage(ATAK_CIV_PACKAGE)
            if (launchIntent != null) {
                launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                launchIntent.putExtra("callsign", callsign)
                launchIntent.putExtra("team", teamColor)
                launchIntent.putExtra("filepath", zipFile.absolutePath)
                ctx.startActivity(launchIntent)
                true
            } else {
                false
            }
        } catch (_: Exception) {
            false
        }
    }

    private fun escapeXml(s: String): String =
        s.replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace("\"", "&quot;")
            .replace("'", "&apos;")
}

// =============================================================================
// 4. PROGRAMOWANIE PŁYTKI MESHTASTIC PRZEZ BLUETOOTH LE GATT + DEEP LINK
//    (Long Name = "Wilk-1", Short Name = "Wlk1", Kanał, PSK, Region EU_868)
// =============================================================================
object MeshtasticBleProvisioner {

    val MESHTASTIC_SERVICE_UUID: UUID = UUID.fromString("6ba1b218-15a8-461f-9fa8-5dcae273eafd")
    val TORADIO_CHAR_UUID: UUID = UUID.fromString("f75c76d2-129e-4dad-a1dd-7866124401e7")

    private val mainHandler = Handler(Looper.getMainLooper())

    @SuppressLint("MissingPermission")
    fun provisionMeshtasticNode(
        ctx: Context,
        profile: FthFactionProfile,
        onLog: (String) -> Unit,
        onFinished: (Boolean) -> Unit
    ) {
        val longName = CallsignFormatter.formatAtakCallsign(profile.operatorCallsign)
        val shortName = profile.meshtasticShortName.ifBlank {
            CallsignFormatter.generateMeshtasticShortName(longName, 4)
        }.take(4)

        onLog(FthSessionManager.t(
            "📻 [Meshtastic] Przygotowanie węzła: LongName='$longName', ShortName='$shortName', Kanał='${profile.channelName}' (${profile.loraRegion}/${profile.loraModemPreset})...",
            "📻 [Meshtastic] Preparing node: LongName='$longName', ShortName='$shortName', Channel='${profile.channelName}' (${profile.loraRegion}/${profile.loraModemPreset})..."
        ))

        // Skopiuj skróconą i pełną nazwę do schowka jako wsparcie natychmiastowe
        try {
            val cb = ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("Meshtastic Callsign", "$longName ($shortName)"))
        } catch (_: Exception) {
        }

        val btManager = ctx.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
        val adapter = btManager?.adapter

        if (adapter == null || !adapter.isEnabled) {
            onLog(FthSessionManager.t(
                "⚠️ [BLE] Bluetooth jest wyłączony lub niedostępny. Uruchamiam konfigurację przez protokół Meshtastic URL...",
                "⚠️ [BLE] Bluetooth disabled. Launching Meshtastic URL configuration..."
            ))
            val urlLaunched = launchMeshtasticChannelUrlIntent(ctx, profile, onLog)
            onFinished(urlLaunched)
            return
        }

        try {
            // 1. Sprawdź czy płytka Meshtastic jest już wśród sparowanych urządzeń Bluetooth
            val bondedMeshtastic = adapter.bondedDevices?.firstOrNull { dev ->
                val n = dev.name ?: ""
                n.contains("Meshtastic", ignoreCase = true) ||
                        n.contains("Heltec", ignoreCase = true) ||
                        n.contains("T-Beam", ignoreCase = true) ||
                        n.contains("RAK", ignoreCase = true)
            }

            if (bondedMeshtastic != null) {
                onLog(FthSessionManager.t(
                    "🔗 [BLE] Znaleziono sparowaną płytkę '${bondedMeshtastic.name}' (${bondedMeshtastic.address}). Łączenie GATT...",
                    "🔗 [BLE] Found paired board '${bondedMeshtastic.name}' (${bondedMeshtastic.address}). Connecting GATT..."
                ))
                connectAndWriteGattConfig(ctx, bondedMeshtastic, longName, shortName, profile, onLog, onFinished)
                return
            }

            // 2. Krótkie skanowanie BLE (3.5 sekundy) w poszukiwaniu usługi Meshtastic GATT
            val scanner = adapter.bluetoothLeScanner
            if (scanner == null) {
                val ok = launchMeshtasticChannelUrlIntent(ctx, profile, onLog)
                onFinished(ok)
                return
            }

            var deviceFound = false
            val scanCallback = object : ScanCallback() {
                override fun onScanResult(callbackType: Int, result: ScanResult?) {
                    val device = result?.device ?: return
                    val devName = device.name ?: result.scanRecord?.deviceName ?: ""
                    val hasService = result.scanRecord?.serviceUuids?.any { it.uuid == MESHTASTIC_SERVICE_UUID } == true
                    if (!deviceFound && (hasService || devName.contains("Meshtastic", ignoreCase = true) || devName.contains("Heltec", ignoreCase = true))) {
                        deviceFound = true
                        try { scanner.stopScan(this) } catch (_: Exception) {}
                        mainHandler.post {
                            onLog(FthSessionManager.t(
                                "📡 [BLE] Wykryto węzeł LoRa '${devName.ifBlank { device.address }}'. Wstrzykiwanie '$longName' ($shortName)...",
                                "📡 [BLE] Detected LoRa node '${devName.ifBlank { device.address }}'. Injecting '$longName' ($shortName)..."
                            ))
                            connectAndWriteGattConfig(ctx, device, longName, shortName, profile, onLog, onFinished)
                        }
                    }
                }
            }

            onLog(FthSessionManager.t(
                "🔍 [BLE] Skanowanie płytek Meshtastic w pobliżu (3.5s)...",
                "🔍 [BLE] Scanning for nearby Meshtastic boards (3.5s)..."
            ))
            val settings = ScanSettings.Builder()
                .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
                .build()
            scanner.startScan(null, settings, scanCallback)

            mainHandler.postDelayed({
                if (!deviceFound) {
                    try { scanner.stopScan(scanCallback) } catch (_: Exception) {}
                    onLog(FthSessionManager.t(
                        "ℹ️ [BLE] Nie wykryto bezpośredniego połączenia GATT płytki. Przekazuję kanał '${profile.channelName}' i klucz PSK do aplikacji Meshtastic...",
                        "ℹ️ [BLE] No direct GATT board found. Passing channel '${profile.channelName}' and PSK to Meshtastic app..."
                    ))
                    val ok = launchMeshtasticChannelUrlIntent(ctx, profile, onLog)
                    onFinished(ok)
                }
            }, 3500L)

        } catch (secEx: SecurityException) {
            onLog(FthSessionManager.t(
                "⚠️ [BLE] Brak uprawnień Bluetooth (nadaj uprawnienia w ustawieniach). Uruchamiam link Meshtastic...",
                "⚠️ [BLE] Bluetooth permission missing. Launching Meshtastic link..."
            ))
            val ok = launchMeshtasticChannelUrlIntent(ctx, profile, onLog)
            onFinished(ok)
        } catch (ex: Exception) {
            val ok = launchMeshtasticChannelUrlIntent(ctx, profile, onLog)
            onFinished(ok)
        }
    }

    @SuppressLint("MissingPermission")
    private fun connectAndWriteGattConfig(
        ctx: Context,
        device: BluetoothDevice,
        longName: String,
        shortName: String,
        profile: FthFactionProfile,
        onLog: (String) -> Unit,
        onFinished: (Boolean) -> Unit
    ) {
        val gattCallback = object : BluetoothGattCallback() {
            override fun onConnectionStateChange(gatt: BluetoothGatt, status: Int, newState: Int) {
                if (newState == BluetoothProfile.STATE_CONNECTED) {
                    gatt.discoverServices()
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                    try { gatt.close() } catch (_: Exception) {}
                }
            }

            override fun onServicesDiscovered(gatt: BluetoothGatt, status: Int) {
                val service = gatt.getService(MESHTASTIC_SERVICE_UUID)
                val toRadioChar = service?.getCharacteristic(TORADIO_CHAR_UUID)
                if (toRadioChar != null) {
                    val ownerPayload = buildMeshtasticOwnerProtobufPacket(longName, shortName)
                    toRadioChar.value = ownerPayload
                    val writeOk = gatt.writeCharacteristic(toRadioChar)
                    mainHandler.post {
                        onLog(FthSessionManager.t(
                            "✅ [BLE GATT] Wysłano ramkę Owner Protobuf do płytki: Long='$longName', Short='$shortName' (Status: $writeOk)",
                            "✅ [BLE GATT] Sent Owner Protobuf frame to board: Long='$longName', Short='$shortName' (Status: $writeOk)"
                        ))
                        launchMeshtasticChannelUrlIntent(ctx, profile, onLog)
                        onFinished(true)
                    }
                    mainHandler.postDelayed({
                        try { gatt.disconnect(); gatt.close() } catch (_: Exception) {}
                    }, 800L)
                } else {
                    try { gatt.disconnect(); gatt.close() } catch (_: Exception) {}
                    mainHandler.post {
                        launchMeshtasticChannelUrlIntent(ctx, profile, onLog)
                        onFinished(true)
                    }
                }
            }
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            device.connectGatt(ctx, false, gattCallback, BluetoothDevice.TRANSPORT_LE)
        } else {
            device.connectGatt(ctx, false, gattCallback)
        }
    }

    /**
     * Buduje zwięzłą ramkę Protobuf (Meshtastic User/Owner message: id=1, long_name=2, short_name=3)
     */
    private fun buildMeshtasticOwnerProtobufPacket(longName: String, shortName: String): ByteArray {
        val longBytes = longName.toByteArray(StandardCharsets.UTF_8)
        val shortBytes = shortName.toByteArray(StandardCharsets.UTF_8)
        val bos = ByteArrayOutputStream()

        // Field 2 (long_name, wire type 2 = length-delimited -> tag 0x12)
        bos.write(0x12)
        bos.write(longBytes.size)
        bos.write(longBytes)

        // Field 3 (short_name, wire type 2 = length-delimited -> tag 0x1A)
        bos.write(0x1A)
        bos.write(shortBytes.size)
        bos.write(shortBytes)

        return bos.toByteArray()
    }

    fun buildMeshtasticDeepLinkUrl(profile: FthFactionProfile): String {
        if (profile.channelUrl.isNotBlank() && profile.channelUrl.startsWith("http")) {
            return profile.channelUrl.trim()
        }
        // Zbuduj kompatybilny payload URL https://meshtastic.org/e/#...
        val chBytes = profile.channelName.take(11).toByteArray(StandardCharsets.UTF_8)
        val pskBytes = try {
            if (profile.pskBase64.isNotBlank()) Base64.decode(profile.pskBase64.trim(), Base64.DEFAULT)
            else ByteArray(0)
        } catch (_: Exception) {
            ByteArray(0)
        }

        val bos = ByteArrayOutputStream()
        if (pskBytes.isNotEmpty()) {
            bos.write(0x12)
            bos.write(pskBytes.size)
            bos.write(pskBytes)
        }
        if (chBytes.isNotEmpty()) {
            bos.write(0x1A)
            bos.write(chBytes.size)
            bos.write(chBytes)
        }
        val inner = bos.toByteArray()
        val outer = ByteArrayOutputStream()
        outer.write(0x0A)
        outer.write(inner.size)
        outer.write(inner)

        val b64Url = Base64.encodeToString(outer.toByteArray(), Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
        return "https://meshtastic.org/e/#$b64Url"
    }

    private fun launchMeshtasticChannelUrlIntent(
        ctx: Context,
        profile: FthFactionProfile,
        onLog: (String) -> Unit
    ): Boolean {
        val url = buildMeshtasticDeepLinkUrl(profile)
        return try {
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url)).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            ctx.startActivity(intent)
            onLog(FthSessionManager.t(
                "✅ [Meshtastic] Przekazano konfigurację kanału '${profile.channelName}' (PSK AES-256) oraz skrót '${profile.meshtasticShortName}' (w schowku)!",
                "✅ [Meshtastic] Dispatched channel '${profile.channelName}' (AES-256 PSK) and short name '${profile.meshtasticShortName}'!"
            ))
            true
        } catch (_: Exception) {
            onLog(FthSessionManager.t(
                "ℹ️ [Meshtastic] Wygenerowano link kanału: $url (Nazwa '${profile.operatorCallsign}' / '${profile.meshtasticShortName}' skopiowana do schowka).",
                "ℹ️ [Meshtastic] Generated channel link: $url"
            ))
            false
        }
    }
}

// =============================================================================
// 5. LOKALNY SERWER P2P HOTSPOT WI-FI (UDOSTĘPNIANIE PACZKI I OPORD W LESIE)
// =============================================================================
object FthHotspotP2pServer {
    private var serverSocket: ServerSocket? = null
    @Volatile
    var isRunning: Boolean = false
        private set
    var activePort: Int = 8787
        private set

    private val serverExecutor = Executors.newCachedThreadPool()

    fun startServer(): String {
        if (!isRunning || serverSocket == null || serverSocket!!.isClosed) {
            for (candidatePort in listOf(8787, 8788, 8789)) {
                try {
                    val ss = ServerSocket(candidatePort)
                    serverSocket = ss
                    activePort = candidatePort
                    isRunning = true
                    serverExecutor.execute { acceptClientsLoop(ss) }
                    break
                } catch (_: Exception) {
                }
            }
        }
        val ip = getLocalHotspotOrWifiIp()
        return "http://$ip:$activePort/ftak.json"
    }

    fun stopServer() {
        isRunning = false
        try { serverSocket?.close() } catch (_: Exception) {}
        serverSocket = null
    }

    private fun acceptClientsLoop(ss: ServerSocket) {
        while (isRunning && !ss.isClosed) {
            try {
                val client = ss.accept()
                serverExecutor.execute { handleClient(client) }
            } catch (_: Exception) {
                if (!isRunning) break
            }
        }
    }

    private fun handleClient(socket: Socket) {
        socket.use { s ->
            try {
                s.soTimeout = 5000
                val reader = BufferedReader(InputStreamReader(s.getInputStream(), StandardCharsets.UTF_8))
                val reqLine = reader.readLine() ?: return
                val payloadJson = FthSessionManager.activeProfile.toCompactQrJson()
                val bodyBytes = payloadJson.toByteArray(StandardCharsets.UTF_8)

                val headers = "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: application/json; charset=utf-8\r\n" +
                        "Content-Length: ${bodyBytes.size}\r\n" +
                        "Access-Control-Allow-Origin: *\r\n" +
                        "Connection: close\r\n\r\n"

                val out = s.getOutputStream()
                out.write(headers.toByteArray(StandardCharsets.UTF_8))
                out.write(bodyBytes)
                out.flush()
            } catch (_: Exception) {
            }
        }
    }

    fun getLocalHotspotOrWifiIp(): String {
        try {
            val interfaces = NetworkInterface.getNetworkInterfaces()
            var fallbackIp = "192.168.43.1" // Standardowy adres bramy Android Hotspot
            while (interfaces.hasMoreElements()) {
                val nif = interfaces.nextElement()
                if (!nif.isUp || nif.isLoopback) continue
                val addrs = nif.inetAddresses
                while (addrs.hasMoreElements()) {
                    val addr = addrs.nextElement()
                    if (addr is Inet4Address && !addr.isLoopbackAddress) {
                        val ip = addr.hostAddress ?: continue
                        // Preferuj interfejsy ap/wlan/rndis (Hotspot Wi-Fi)
                        if (ip.startsWith("192.168.43.") || ip.startsWith("192.168.49.") || ip.startsWith("192.168.")) {
                            return ip
                        }
                        fallbackIp = ip
                    }
                }
            }
            return fallbackIp
        } catch (_: Exception) {
            return "192.168.43.1"
        }
    }
}
