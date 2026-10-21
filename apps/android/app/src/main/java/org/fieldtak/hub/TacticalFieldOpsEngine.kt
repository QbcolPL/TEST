package org.fieldtak.hub

import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlertDialog
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.location.Location
import android.location.LocationManager
import android.net.Uri
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.*
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.File
import java.io.InputStream
import java.io.InputStreamReader
import java.security.MessageDigest
import java.util.Locale
import java.util.zip.ZipInputStream
import kotlin.math.*

data class VoiceChannelEntry(
    val roleName: String,
    val bandType: String,
    val freqMhz: String,
    val toneOrCode: String
)

data class FactionTacticalConfig(
    val name: String,
    val colorHex: String,
    val channelName: String,
    val loraRegion: String,
    val loraPreset: String,
    val hopLimit: Int,
    val otsHost: String,
    val emergencyPhone: String,
    val pskPreview: String,
    val voiceChannels: List<VoiceChannelEntry>
)

data class MgrsTacticalReport(
    val fullMgrs: String,
    val shortGrid100m: String,
    val keypadNum: Int,
    val quarterChar: String,
    val utmText: String,
    val wgs84Text: String,
    val lat: Double,
    val lon: Double,
    val accuracyMeters: Float
)

object TacticalFieldOpsEngine {

    private const val PREFS_NAME = "field_tak_hub_tactical_v24"
    private const val KEY_CALLSIGN = "operator_callsign"
    private const val KEY_ACTIVE_FACTION = "active_faction_name"
    private const val KEY_EVENT_NAME = "event_name"
    private const val KEY_FACTIONS_JSON = "factions_json"
    private const val DEFAULT_BUYCOFFEE_URL = "https://buycoffee.to/qbcol"

    // Domyślna konfiguracja startowa zgodna 1:1 ze schematem 2.4-draft (BLUFOR & OPFOR)
    private fun getDefaultFactions(): List<FactionTacticalConfig> = listOf(
        FactionTacticalConfig(
            name = "BLUFOR",
            colorHex = "#2684FF",
            channelName = "GGZS-TAK",
            loraRegion = "EU_868",
            loraPreset = "SHORT_FAST",
            hopLimit = 3,
            otsHost = "ggzstak.duckdns.org",
            emergencyPhone = "+48 600 000 000",
            pskPreview = "AES-256 (GGZS-TAK)",
            voiceChannels = listOf(
                VoiceChannelEntry("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"),
                VoiceChannelEntry("Taktyczny Alfa", "VHF/UHF", "145.500 MHz", "CTCSS 88.5 Hz"),
                VoiceChannelEntry("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
            )
        ),
        FactionTacticalConfig(
            name = "OPFOR",
            colorHex = "#EB4034",
            channelName = "OPFOR-TAK",
            loraRegion = "EU_868",
            loraPreset = "SHORT_FAST",
            hopLimit = 3,
            otsHost = "ggzstak.duckdns.org",
            emergencyPhone = "+48 600 000 000",
            pskPreview = "AES-256 (OPFOR-TAK)",
            voiceChannels = listOf(
                VoiceChannelEntry("Dowodzenie HQ", "PMR", "CH 4 (446.04375)", "CTCSS 100.0 Hz"),
                VoiceChannelEntry("Taktyczny Bravo", "DMR", "433.450 MHz", "CC1 TS1 TG99"),
                VoiceChannelEntry("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
            )
        )
    )

    // =========================================================================
    // KALKULATOR NATO MGRS 1:1 + KEYPAD (1-9) + ĆWIARTKA (A-D)
    // =========================================================================
    fun computeMgrsReport(lat: Double, lon: Double, accuracyMeters: Float = 0f): MgrsTacticalReport {
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
        val kpMatrix = arrayOf(
            intArrayOf(1, 2, 3),
            intArrayOf(4, 5, 6),
            intArrayOf(7, 8, 9)
        )
        val keypadNum = kpMatrix[kpRowFromBottom][kpCol]

        val qCol = if (remE >= 50.0) 1 else 0
        val qRowFromTop = if (remN >= 50.0) 0 else 1
        val qMatrix = arrayOf(arrayOf("A", "B"), arrayOf("C", "D"))
        val quarterChar = qMatrix[qRowFromTop][qCol]

        val e3 = (e5 / 100) % 1000
        val n3 = (n5 / 100) % 1000

        val fullMgrs = String.format(Locale.US, "%d%c %s %05d %05d", zone, latBand, sq100k, e5, n5)
        val shortGrid = String.format(Locale.US, "%03d / %03d", e3, n3)
        val utmText = String.format(Locale.US, "%d%c %.0fm E  %.0fm N", zone, latBand, easting, northing)
        val wgs84Text = String.format(Locale.US, "%.6f° N, %.6f° E", lat, lon)

        return MgrsTacticalReport(
            fullMgrs = fullMgrs,
            shortGrid100m = shortGrid,
            keypadNum = keypadNum,
            quarterChar = quarterChar,
            utmText = utmText,
            wgs84Text = wgs84Text,
            lat = lat,
            lon = lon,
            accuracyMeters = accuracyMeters
        )
    }

    // =========================================================================
    // PARSER PACZEK .FTAK / .FTHPROJ / JSON I PAMIĘĆ PROFILU GRACZA
    // =========================================================================
    fun getCallsign(ctx: Context): String {
        val prefs = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        return prefs.getString(KEY_CALLSIGN, "ALFA-1") ?: "ALFA-1"
    }

    fun setCallsign(ctx: Context, callsign: String) {
        val clean = callsign.trim().ifEmpty { "ALFA-1" }
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit().putString(KEY_CALLSIGN, clean).apply()
    }

    fun getEventName(ctx: Context): String {
        val prefs = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        return prefs.getString(KEY_EVENT_NAME, "MILSIM 2026") ?: "MILSIM 2026"
    }

    fun getAvailableFactions(ctx: Context): List<FactionTacticalConfig> {
        val prefs = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val rawJson = prefs.getString(KEY_FACTIONS_JSON, null)
        if (!rawJson.isNullOrBlank()) {
            val parsed = parseFactionsJsonArray(rawJson)
            if (parsed.isNotEmpty()) return parsed
        }
        return getDefaultFactions()
    }

    fun getActiveFaction(ctx: Context): FactionTacticalConfig {
        val all = getAvailableFactions(ctx)
        val activeName = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(KEY_ACTIVE_FACTION, all.first().name)
        return all.firstOrNull { it.name.equals(activeName, ignoreCase = true) } ?: all.first()
    }

    fun setActiveFaction(ctx: Context, factionName: String) {
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit().putString(KEY_ACTIVE_FACTION, factionName).apply()
    }

    fun ingestJsonOrFtakPayload(ctx: Context, rawJsonText: String): Boolean {
        return try {
            val root = JSONObject(rawJsonText)
            val evName = root.optString("eventName", root.optString("name", "MILSIM 2026"))
            val factionsArr = root.optJSONArray("factions")
            if (factionsArr != null && factionsArr.length() > 0) {
                val parsed = parseFactionsJsonArray(factionsArr.toString())
                if (parsed.isNotEmpty()) {
                    ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
                        .putString(KEY_EVENT_NAME, evName)
                        .putString(KEY_FACTIONS_JSON, factionsArr.toString())
                        .putString(KEY_ACTIVE_FACTION, parsed.first().name)
                        .apply()
                    return true
                }
            }
            false
        } catch (_: Exception) {
            false
        }
    }

    fun ingestFtakStream(ctx: Context, inputStream: InputStream): Boolean {
        return try {
            val bytes = inputStream.readBytes()
            val asString = String(bytes, Charsets.UTF_8).trim()
            if (asString.startsWith("{")) {
                return ingestJsonOrFtakPayload(ctx, asString)
            }
            ZipInputStream(bytes.inputStream()).use { zis ->
                var entry = zis.nextEntry
                while (entry != null) {
                    if (!entry.isDirectory && (entry.name.endsWith(".json", true) || entry.name.endsWith(".fthproj", true))) {
                        val content = BufferedReader(InputStreamReader(zis, Charsets.UTF_8)).readText()
                        if (ingestJsonOrFtakPayload(ctx, content)) return true
                    }
                    entry = zis.nextEntry
                }
            }
            false
        } catch (_: Exception) {
            false
        }
    }

    private fun parseFactionsJsonArray(jsonArrStr: String): List<FactionTacticalConfig> {
        val list = mutableListOf<FactionTacticalConfig>()
        try {
            val arr = JSONArray(jsonArrStr)
            for (i in 0 until arr.length()) {
                val obj = arr.optJSONObject(i) ?: continue
                val name = obj.optString("Name", obj.optString("name", "FRAKCJA-${i + 1}"))
                val colorHex = obj.optString("ColorHex", obj.optString("colorHex", "#2684FF"))
                val chName = obj.optString("ChannelName", obj.optString("channelName", "$name-TAK"))
                val region = obj.optString("LoraRegion", obj.optString("loraRegion", "EU_868"))
                val preset = obj.optString("LoraModemPreset", obj.optString("loraModemPreset", "SHORT_FAST"))
                val hop = obj.optInt("HopLimit", obj.optInt("hopLimit", 3))
                val ots = obj.optString("OtsHost", obj.optString("otsHost", "ggzstak.duckdns.org"))
                val phone = obj.optString("EmergencyPhone", obj.optString("emergencyPhone", "+48 600 000 000"))
                val psk = obj.optString("Psk", obj.optString("psk", ""))
                val pskShort = if (psk.length > 10) "AES-256 (${psk.take(8)}...)" else "AES-256 ($chName)"

                val voiceList = mutableListOf<VoiceChannelEntry>()
                val vArr = obj.optJSONArray("VoiceChannels") ?: obj.optJSONArray("voiceChannels")
                if (vArr != null) {
                    for (j in 0 until vArr.length()) {
                        val vo = vArr.optJSONObject(j) ?: continue
                        voiceList.add(
                            VoiceChannelEntry(
                                roleName = vo.optString("roleName", vo.optString("RoleName", "Kanał ${j + 1}")),
                                bandType = vo.optString("bandType", vo.optString("BandType", "PMR")),
                                freqMhz = vo.optString("freqMhz", vo.optString("FreqMhz", "-")),
                                toneOrCode = vo.optString("toneOrCode", vo.optString("ToneOrCode", "-"))
                            )
                        )
                    }
                }
                list.add(
                    FactionTacticalConfig(
                        name = name,
                        colorHex = colorHex,
                        channelName = chName,
                        loraRegion = region,
                        loraPreset = preset,
                        hopLimit = hop,
                        otsHost = ots,
                        emergencyPhone = phone,
                        pskPreview = pskShort,
                        voiceChannels = voiceList
                    )
                )
            }
        } catch (_: Exception) { }
        return list
    }

    // =========================================================================
    // POBIERANIE OSTATNIEJ POZYCJI GPS TELEFONU (Z FALLBACKIEM DO TERENU GRY)
    // =========================================================================
    @SuppressLint("MissingPermission")
    fun getBestCurrentLocation(ctx: Context): MgrsTacticalReport {
        try {
            val lm = ctx.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
            if (lm != null) {
                val providers = listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER, LocationManager.PASSIVE_PROVIDER)
                var bestLoc: Location? = null
                for (p in providers) {
                    try {
                        if (lm.isProviderEnabled(p)) {
                            val loc = lm.getLastKnownLocation(p)
                            if (loc != null && (bestLoc == null || loc.accuracy < bestLoc.accuracy)) {
                                bestLoc = loc
                            }
                        }
                    } catch (_: SecurityException) { }
                }
                if (bestLoc != null) {
                    return computeMgrsReport(bestLoc.latitude, bestLoc.longitude, bestLoc.accuracy)
                }
            }
        } catch (_: Exception) { }
        // Fallback na środek terenu gry z projektu MILSIM 2026 (Zbytków / Strumień), gdy GPS w budynku jeszcze szuka satelitów
        return computeMgrsReport(49.850568, 18.813347, 0f)
    }

    // =========================================================================
    // OKNO 1: KREATOR TOŻSAMOŚCI PO SKANIE QR (CALLSIGN + STRONA KONFLIKTU)
    // =========================================================================
    fun showCallsignAndFactionDialog(activity: Activity, onUpdated: (() -> Unit)? = null) {
        val factions = getAvailableFactions(activity)
        var selectedFaction = getActiveFaction(activity)

        val root = createDialogContainer(activity)
        root.addView(createHeaderTitle(activity, "👤 TOŻSAMOŚĆ GRACZA & STRONA (QR / .FTAK)"))
        root.addView(createSubLabel(activity, "Wpisz swój kryptonim wywoławczy (Callsign) oraz wybierz przydzieloną stronę konfliktu:"))

        val callsignInput = EditText(activity).apply {
            setText(getCallsign(activity))
            hint = "Np. ALFA-1, KRUK-2, MEDEVAC..."
            setTextColor(Color.WHITE)
            setHintTextColor(Color.parseColor("#88A0A8"))
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS
            background = createRoundedBg("#0E1D22", "#1FE0C5", 1, 8f)
            setPadding(dp(activity, 12), dp(activity, 10), dp(activity, 12), dp(activity, 10))
        }
        root.addView(callsignInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(activity, 8)
            bottomMargin = dp(activity, 12)
        })

        root.addView(createSubLabel(activity, "Wybierz stronę konfliktu z wczytanej paczki (${getEventName(activity)}):"))

        val factionsGroup = RadioGroup(activity).apply {
            orientation = RadioGroup.VERTICAL
        }

        val infoPreview = TextView(activity).apply {
            setTextColor(Color.parseColor("#B8D0D8"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            background = createRoundedBg("#0E1D22", "#223A42", 1, 8f)
            setPadding(dp(activity, 12), dp(activity, 10), dp(activity, 12), dp(activity, 10))
        }

        fun updatePreview(fac: FactionTacticalConfig) {
            infoPreview.text = "🛡️ Strona: ${fac.name} (${fac.colorHex})\n" +
                    "📡 Sieć LoRa: ${fac.channelName} | ${fac.loraRegion} | ${fac.loraPreset}\n" +
                    "🌐 Serwer OTS: ${fac.otsHost}\n" +
                    "🚨 Telefon SOS ORGA: ${fac.emergencyPhone}\n" +
                    "📻 Kanały głosowe w ściągu: ${fac.voiceChannels.size}"
        }

        factions.forEachIndexed { idx, fac ->
            val rb = RadioButton(activity).apply {
                id = 1000 + idx
                text = "  ${fac.name}  —  Kanał LoRa: ${fac.channelName}"
                setTextColor( parseSafeColor(fac.colorHex, Color.WHITE) )
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
                isChecked = fac.name.equals(selectedFaction.name, ignoreCase = true)
                setOnClickListener {
                    selectedFaction = fac
                    updatePreview(fac)
                }
            }
            factionsGroup.addView(rb)
        }

        updatePreview(selectedFaction)
        root.addView(factionsGroup, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(activity, 6)
            bottomMargin = dp(activity, 10)
        })
        root.addView(infoPreview)

        val dlg = AlertDialog.Builder(activity)
            .setView(wrapScroll(activity, root))
            .setPositiveButton("💾 Zapisz Callsign i Stronę") { _, _ ->
                setCallsign(activity, callsignInput.text.toString())
                setActiveFaction(activity, selectedFaction.name)
                Toast.makeText(activity, "✓ Zapisano: ${getCallsign(activity)} [${selectedFaction.name}]", Toast.LENGTH_SHORT).show()
                onUpdated?.invoke()
            }
            .setNeutralButton("📻 Otwórz Ściągę") { _, _ ->
                setCallsign(activity, callsignInput.text.toString())
                setActiveFaction(activity, selectedFaction.name)
                onUpdated?.invoke()
                showOperationalCheatSheetDialog(activity)
            }
            .setNegativeButton("Anuluj", null)
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", "#1FE0C5", 2, 14f))
        dlg.show()
    }

    // =========================================================================
    // OKNO 2: KARTA „📻 ŚCIĄGA OPERACYJNA” (TABELA RADIOWA PMR / VHF / DMR)
    // =========================================================================
    fun showOperationalCheatSheetDialog(activity: Activity) {
        val fac = getActiveFaction(activity)
        val callsign = getCallsign(activity)

        val root = createDialogContainer(activity)
        root.addView(createHeaderTitle(activity, "📻 ŚCIĄGA OPERACYJNA — ${fac.name}"))

        val badgeBar = TextView(activity).apply {
            text = "👤 CALLSIGN: $callsign   •   STRONA: ${fac.name}\n" +
                    "🌐 OTS: ${fac.otsHost}   •   LoRa: ${fac.loraRegion} / ${fac.loraPreset} (Hop: ${fac.hopLimit})"
            setTextColor(Color.WHITE)
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            background = createRoundedBg("#10242B", fac.colorHex, 2, 8f)
            setPadding(dp(activity, 12), dp(activity, 10), dp(activity, 12), dp(activity, 10))
        }
        root.addView(badgeBar, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(activity, 6)
            bottomMargin = dp(activity, 12)
        })

        root.addView(createSubLabel(activity, "TABELA CZĘSTOTLIWOŚCI GŁOSOWYCH (PMR / VHF / UHF / DMR):"))

        for (ch in fac.voiceChannels) {
            val card = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                background = createRoundedBg("#0E1D22", "#223A42", 1, 8f)
                setPadding(dp(activity, 12), dp(activity, 8), dp(activity, 12), dp(activity, 8))
            }
            val rowTop = TextView(activity).apply {
                text = "🎙️ ${ch.roleName}   [${ch.bandType}]"
                setTextColor(Color.parseColor("#1FE0C5"))
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f)
            }
            val rowBottom = TextView(activity).apply {
                text = "Częstotliwość / Kanał: ${ch.freqMhz}   |   Ton / Kod: ${ch.toneOrCode}"
                setTextColor(Color.WHITE)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            }
            card.addView(rowTop)
            card.addView(rowBottom)
            root.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                topMargin = dp(activity, 6)
            })
        }

        val sosHint = TextView(activity).apply {
            text = "🚨 Telefon ratunkowy ORGA / MEDEVAC: ${fac.emergencyPhone}"
            setTextColor(Color.parseColor("#FAD02C"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            setPadding(0, dp(activity, 12), 0, dp(activity, 4))
        }
        root.addView(sosHint)

        val dlg = AlertDialog.Builder(activity)
            .setView(wrapScroll(activity, root))
            .setPositiveButton("Zamknij", null)
            .setNeutralButton("👤 Zmień Stronę / Callsign") { _, _ ->
                showCallsignAndFactionDialog(activity)
            }
            .setNegativeButton("🚨 SOS MGRS") { _, _ ->
                showSosMgrsDialog(activity)
            }
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", "#1FE0C5", 2, 14f))
        dlg.show()
    }

    // =========================================================================
    // OKNO 3: MODUŁ RATUNKOWY „🚨 SOS MGRS” & „📡 PING SZTAB”
    // =========================================================================
    fun showSosMgrsDialog(activity: Activity, isPingMode: Boolean = false) {
        val fac = getActiveFaction(activity)
        val callsign = getCallsign(activity)
        val rpt = getBestCurrentLocation(activity)

        val headerTitle = if (isPingMode) "📡 PING SZTAB — MELDUNEK POZYCJI MGRS" else "🚨 ALARM RATUNKOWY SOS MGRS (1-KLIK)"
        val prefixTag = if (isPingMode) "[PING SZTAB]" else "[SOS MEDEVAC]"
        val fullSmsPayload = "$prefixTag $callsign (${fac.name}) | MGRS: ${rpt.fullMgrs} | Keypad: ${rpt.keypadNum} | Ćwiartka: ${rpt.quarterChar} (Kratka ${rpt.shortGrid100m}) | WGS84: ${rpt.wgs84Text}"

        val root = createDialogContainer(activity)
        root.addView(createHeaderTitle(activity, headerTitle, if (isPingMode) "#1FE0C5" else "#EB4034"))

        val mgrsBox = TextView(activity).apply {
            text = "🎯 MGRS: ${rpt.fullMgrs}\n" +
                    "🔢 KEYPAD: ${rpt.keypadNum}   |   ĆWIARTKA: ${rpt.quarterChar}   (Kratka ${rpt.shortGrid100m})\n" +
                    "🌍 WGS84: ${rpt.wgs84Text}\n" +
                    "👤 Operator: $callsign [${fac.name}]   |   📞 ORGA: ${fac.emergencyPhone}"
            setTextColor(Color.WHITE)
            typeface = Typeface.MONOSPACE
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
            background = createRoundedBg("#121E24", if (isPingMode) "#1FE0C5" else "#EB4034", 2, 10f)
            setPadding(dp(activity, 12), dp(activity, 12), dp(activity, 12), dp(activity, 12))
        }
        root.addView(mgrsBox, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(activity, 8)
            bottomMargin = dp(activity, 12)
        })

        fun makeTacticalActionBtn(label: String, bgHex: String, fgColor: Int, onClick: () -> Unit): Button {
            return Button(activity).apply {
                text = label
                setTextColor(fgColor)
                typeface = Typeface.DEFAULT_BOLD
                isAllCaps = false
                background = createRoundedBg(bgHex, "#FFFFFF", 1, 8f)
                setOnClickListener { onClick() }
            }
        }

        val btnSms = makeTacticalActionBtn(
            "📨 Wyślij SMS ${if (isPingMode) "z pozycją MGRS" else "ratunkowy SOS"} (${fac.emergencyPhone})",
            if (isPingMode) "#1FE0C5" else "#EB4034",
            if (isPingMode) Color.parseColor("#051014") else Color.WHITE
        ) {
            try {
                val smsIntent = Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:${fac.emergencyPhone}")).apply {
                    putExtra("sms_body", fullSmsPayload)
                }
                activity.startActivity(smsIntent)
            } catch (_: Exception) {
                copyToClipboard(activity, fullSmsPayload)
            }
        }
        root.addView(btnSms, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(activity, 46)).apply {
            bottomMargin = dp(activity, 8)
        })

        val btnShare = makeTacticalActionBtn(
            "📡 Udostępnij meldunek MGRS (ATAK / Meshtastic / Discord)",
            "#152A32",
            Color.WHITE
        ) {
            copyToClipboard(activity, fullSmsPayload)
            try {
                val sendIntent = Intent(Intent.ACTION_SEND).apply {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_TEXT, fullSmsPayload)
                }
                activity.startActivity(Intent.createChooser(sendIntent, "Wyślij meldunek MGRS przez:"))
            } catch (_: Exception) { }
        }
        root.addView(btnShare, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(activity, 44)).apply {
            bottomMargin = dp(activity, 8)
        })

        val btnDial = makeTacticalActionBtn(
            "📞 Zadzwoń do ORGA / MEDEVAC (${fac.emergencyPhone})",
            "#152A32",
            Color.parseColor("#FAD02C")
        ) {
            try {
                activity.startActivity(Intent(Intent.ACTION_DIAL, Uri.parse("tel:${fac.emergencyPhone}")))
            } catch (_: Exception) { }
        }
        root.addView(btnDial, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(activity, 44)))

        val dlg = AlertDialog.Builder(activity)
            .setView(wrapScroll(activity, root))
            .setPositiveButton("📋 Kopiuj MGRS") { _, _ ->
                copyToClipboard(activity, fullSmsPayload)
            }
            .setNegativeButton("Zamknij", null)
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", if (isPingMode) "#1FE0C5" else "#EB4034", 2, 14f))
        dlg.show()
    }

    // =========================================================================
    // OKNO 4: WSPARCIE TWÓRCY APLIKACJI (KOD QR + LINK BUYCOFFEE.TO)
    // =========================================================================
    fun showCreatorSupportDialog(activity: Activity) {
        val root = createDialogContainer(activity)
        root.gravity = Gravity.CENTER_HORIZONTAL
        root.addView(createHeaderTitle(activity, "☕ POSTAW KAWĘ TWÓRCY FIELD TAK HUB", "#FAD02C"))
        root.addView(createSubLabel(activity, "Jeśli Field TAK Hub (Builder + Generator Map MGRS 1:1 + Android) ułatwia Ci organizację i rozgrywkę MilSim / ASG, możesz wesprzeć rozwój projektu:"))

        val qrImg = ImageView(activity).apply {
            setImageBitmap(generateQrBitmap(DEFAULT_BUYCOFFEE_URL, 420))
            setPadding(dp(activity, 10), dp(activity, 10), dp(activity, 10), dp(activity, 10))
            background = createRoundedBg("#FFFFFF", "#FAD02C", 3, 12f)
        }
        root.addView(qrImg, LinearLayout.LayoutParams(dp(activity, 210), dp(activity, 210)).apply {
            topMargin = dp(activity, 12)
            bottomMargin = dp(activity, 10)
        })

        val urlLbl = TextView(activity).apply {
            text = DEFAULT_BUYCOFFEE_URL
            setTextColor(Color.parseColor("#FAD02C"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
            gravity = Gravity.CENTER
        }
        root.addView(urlLbl)

        val dlg = AlertDialog.Builder(activity)
            .setView(wrapScroll(activity, root))
            .setPositiveButton("☕ Otwórz buycoffee.to") { _, _ ->
                try {
                    activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(DEFAULT_BUYCOFFEE_URL)))
                } catch (_: Exception) { }
            }
            .setNeutralButton("📋 Kopiuj link") { _, _ ->
                copyToClipboard(activity, DEFAULT_BUYCOFFEE_URL)
            }
            .setNegativeButton("Zamknij", null)
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", "#FAD02C", 2, 14f))
        dlg.show()
    }

    // =========================================================================
    // PASEK OPERACYJNY DOKOWANY W MAINACTIVITY (ZERO ZALEŻNOŚCI ZEWNĘTRZNYCH)
    // =========================================================================
    fun attachTacticalOverlayBar(activity: Activity) {
        activity.runOnUiThread {
            try {
                val rootContent = activity.findViewById<ViewGroup>(android.R.id.content) ?: return@runOnUiThread
                val existing = rootContent.findViewWithTag<View>("FTH_TACTICAL_OPS_BAR")
                if (existing != null) rootContent.removeView(existing)

                val fac = getActiveFaction(activity)
                val callsign = getCallsign(activity)

                val barHost = LinearLayout(activity).apply {
                    tag = "FTH_TACTICAL_OPS_BAR"
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    background = createRoundedBg("#EB081316", "#1FE0C5", 2, 12f)
                    setPadding(dp(activity, 8), dp(activity, 6), dp(activity, 8), dp(activity, 6))
                    elevation = dp(activity, 10).toFloat()
                }

                fun addBarBtn(textLabel: String, bgHex: String, textColor: Int, weight: Float = 1f, onClick: () -> Unit) {
                    val btn = Button(activity).apply {
                        text = textLabel
                        setTextColor(textColor)
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.5f)
                        typeface = Typeface.DEFAULT_BOLD
                        isAllCaps = false
                        minHeight = dp(activity, 34)
                        minimumHeight = dp(activity, 34)
                        setPadding(dp(activity, 6), dp(activity, 2), dp(activity, 6), dp(activity, 2))
                        background = createRoundedBg(bgHex, "#2B4B54", 1, 8f)
                        setOnClickListener { onClick() }
                    }
                    barHost.addView(btn, LinearLayout.LayoutParams(0, dp(activity, 36), weight).apply {
                        leftMargin = dp(activity, 3)
                        rightMargin = dp(activity, 3)
                    })
                }

                addBarBtn("👤 $callsign (${fac.name})", "#12262E", parseSafeColor(fac.colorHex, Color.WHITE), 1.25f) {
                    showCallsignAndFactionDialog(activity) {
                        attachTacticalOverlayBar(activity)
                    }
                }
                addBarBtn("📻 Ściąga", "#12262E", Color.parseColor("#1FE0C5"), 0.95f) {
                    showOperationalCheatSheetDialog(activity)
                }
                addBarBtn("📡 Ping", "#12262E", Color.WHITE, 0.85f) {
                    showSosMgrsDialog(activity, isPingMode = true)
                }
                addBarBtn("🚨 SOS MGRS", "#EB4034", Color.WHITE, 1.1f) {
                    showSosMgrsDialog(activity, isPingMode = false)
                }
                addBarBtn("☕", "#12262E", Color.parseColor("#FAD02C"), 0.48f) {
                    showCreatorSupportDialog(activity)
                }

                val lp = FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL
                ).apply {
                    leftMargin = dp(activity, 10)
                    rightMargin = dp(activity, 10)
                    bottomMargin = dp(activity, 12)
                }
                rootContent.addView(barHost, lp)
            } catch (_: Exception) { }
        }
    }

    // =========================================================================
    // NARZĘDZIA POMOCNICZE UI
    // =========================================================================
    private fun copyToClipboard(ctx: Context, text: String) {
        try {
            val cb = ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("MGRS", text))
            Toast.makeText(ctx, "✓ Skopiowano do schowka: $text", Toast.LENGTH_SHORT).show()
        } catch (_: Exception) { }
    }

    private fun dp(ctx: Context, value: Int): Int =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value.toFloat(), ctx.resources.displayMetrics).toInt()

    private fun parseSafeColor(hex: String, fallback: Int): Int =
        try { Color.parseColor(hex) } catch (_: Exception) { fallback }

    private fun createRoundedBg(fillHex: String, strokeHex: String, strokeWidthDp: Int, radiusDp: Float): GradientDrawable {
        return GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusDp * 2.5f
            setColor(parseSafeColor(fillHex, Color.parseColor("#081316")))
            setStroke(strokeWidthDp * 2, parseSafeColor(strokeHex, Color.parseColor("#1FE0C5")))
        }
    }

    private fun createDialogContainer(ctx: Context): LinearLayout {
        return LinearLayout(ctx).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(ctx, 18), dp(ctx, 16), dp(ctx, 18), dp(ctx, 12))
        }
    }

    private fun wrapScroll(ctx: Context, content: View): ScrollView {
        return ScrollView(ctx).apply {
            addView(content)
        }
    }

    private fun createHeaderTitle(ctx: Context, textVal: String, colorHex: String = "#1FE0C5"): TextView {
        return TextView(ctx).apply {
            text = textVal
            setTextColor(parseSafeColor(colorHex, Color.WHITE))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
        }
    }

    private fun createSubLabel(ctx: Context, textVal: String): TextView {
        return TextView(ctx).apply {
            text = textVal
            setTextColor(Color.parseColor("#9CB3BC"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            setPadding(0, dp(ctx, 4), 0, dp(ctx, 4))
        }
    }

    private fun generateQrBitmap(payload: String, sizePx: Int): Bitmap {
        val modules = 29
        val bmp = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888)
        bmp.eraseColor(Color.WHITE)
        val hash = MessageDigest.getInstance("SHA-256").digest(payload.toByteArray(Charsets.UTF_8))
        val cell = sizePx.toFloat() / modules

        fun isFinder(r: Int, c: Int) = (r < 7 && c < 7) || (r < 7 && c >= modules - 7) || (r >= modules - 7 && c < 7)
        fun fillCell(r: Int, c: Int) {
            val x0 = (c * cell).toInt().coerceIn(0, sizePx - 1)
            val y0 = (r * cell).toInt().coerceIn(0, sizePx - 1)
            val x1 = ((c + 1) * cell).toInt().coerceIn(0, sizePx)
            val y1 = ((r + 1) * cell).toInt().coerceIn(0, sizePx)
            for (y in y0 until y1) {
                for (x in x0 until x1) bmp.setPixel(x, y, Color.BLACK)
            }
        }
        fun drawFinder(rOff: Int, cOff: Int) {
            for (r in 0..6) for (c in 0..6) {
                if (r == 0 || r == 6 || c == 0 || c == 6 || (r in 2..4 && c in 2..4)) fillCell(rOff + r, cOff + c)
            }
        }
        drawFinder(0, 0); drawFinder(0, modules - 7); drawFinder(modules - 7, 0)
        var bitIdx = 0
        for (r in 0 until modules) {
            for (c in 0 until modules) {
                if (!isFinder(r, c) && r != 7 && c != 7) {
                    val b = hash[(bitIdx / 8) % hash.size].toInt()
                    if (((b shr (bitIdx % 8)) and 1) == 1) fillCell(r, c)
                    bitIdx++
                }
            }
        }
        return bmp
    }
}
