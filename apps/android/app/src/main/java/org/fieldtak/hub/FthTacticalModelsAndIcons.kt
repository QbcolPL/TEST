package org.fieldtak.hub

import android.content.Context
import android.graphics.*
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.View
import org.json.JSONArray
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.*
import kotlin.math.*

// =============================================================================
// 1. MOTYW GRAFICZNY DOPASOWANY 1:1 DO WINDOWS BUILDERA v3.4
// =============================================================================
object FthTheme {
    val BgDeep = Color.parseColor("#071115")
    val BgSidebar = Color.parseColor("#060F13")
    val BgCard = Color.parseColor("#0A181E")
    val BgInput = Color.parseColor("#0D2129")
    val BgElevated = Color.parseColor("#122C36")
    val BorderSubtle = Color.parseColor("#193844")
    val BorderInput = Color.parseColor("#224A59")
    val AccentCyan = Color.parseColor("#1FE0C5")
    val AccentGold = Color.parseColor("#FAD02C")
    val AccentBlue = Color.parseColor("#2684FF")
    val AccentGreen = Color.parseColor("#2ECC71")
    val AccentRed = Color.parseColor("#EB4034")
    val AccentOrange = Color.parseColor("#FF9F43")
    val TextPrimary = Color.parseColor("#FFFFFF")
    val TextSecondary = Color.parseColor("#D2E4EB")
    val TextMuted = Color.parseColor("#789BA8")
    val DarkFg = Color.parseColor("#051014")

    fun dp(ctx: Context, value: Float): Int =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, ctx.resources.displayMetrics).toInt()

    fun cardDrawable(
        fillColor: Int = BgCard,
        strokeColor: Int = BorderSubtle,
        cornerDp: Float = 10f,
        strokePx: Int = 3,
        ctx: Context
    ): GradientDrawable = GradientDrawable().apply {
        shape = GradientDrawable.RECTANGLE
        setColor(fillColor)
        setStroke(strokePx, strokeColor)
        cornerRadius = dp(ctx, cornerDp).toFloat()
    }

    fun buttonDrawable(
        fillColor: Int,
        strokeColor: Int = fillColor,
        cornerDp: Float = 8f,
        strokePx: Int = 3,
        ctx: Context
    ): GradientDrawable = GradientDrawable().apply {
        shape = GradientDrawable.RECTANGLE
        setColor(fillColor)
        setStroke(strokePx, strokeColor)
        cornerRadius = dp(ctx, cornerDp).toFloat()
    }

    fun parseColorSafe(hex: String?, fallback: Int = AccentCyan): Int {
        if (hex.isNullOrBlank()) return fallback
        return try {
            val clean = if (hex.trim().startsWith("#")) hex.trim() else "#${hex.trim()}"
            Color.parseColor(clean)
        } catch (_: Exception) {
            fallback
        }
    }
}

// =============================================================================
// 2. ALGORYTM KONWERSJI CALLSIGNU (NP. "Wilk-1" -> ATAK: "Wilk-1", MESH: "Wlk1")
// =============================================================================
object CallsignFormatter {

    private val PolishCharMap = mapOf(
        'ą' to 'a', 'ć' to 'c', 'ę' to 'e', 'ł' to 'l', 'ń' to 'n', 'ó' to 'o', 'ś' to 's', 'ź' to 'z', 'ż' to 'z',
        'Ą' to 'A', 'Ć' to 'C', 'Ę' to 'E', 'Ł' to 'L', 'Ń' to 'N', 'Ó' to 'O', 'Ś' to 'S', 'Ź' to 'Z', 'Ż' to 'Z'
    )

    fun normalizeAscii(input: String): String {
        val sb = StringBuilder(input.length)
        for (ch in input) {
            sb.append(PolishCharMap[ch] ?: ch)
        }
        return sb.toString()
    }

    fun formatAtakCallsign(rawInput: String): String {
        val clean = normalizeAscii(rawInput).trim()
            .replace(Regex("\\s+"), "-")
            .replace(Regex("[^a-zA-Z0-9_\\-]"), "")
        return if (clean.isEmpty()) "OPERATOR-1" else clean
    }

    /**
     * Inteligentnie skraca ksywkę taktyczną na potrzeby protokołu LoRa Meshtastic (Short Name 4 znaki).
     * Przykłady:
     *  - "Wilk-1"    -> "Wlk1"
     *  - "Alfa-2"    -> "Alf2"
     *  - "Szturm-12" -> "Sz12"
     *  - "Kruk"      -> "Kruk"
     */
    fun generateMeshtasticShortName(rawInput: String, maxChars: Int = 4): String {
        val ascii = normalizeAscii(rawInput).trim()
        if (ascii.isEmpty()) return "OPR1"

        val digitsMatch = Regex("(\\d+)$").find(ascii)
        val trailingDigits = digitsMatch?.groupValues?.getOrNull(1) ?: ""
        val prefixPart = if (trailingDigits.isNotEmpty()) {
            ascii.substring(0, ascii.length - trailingDigits.length)
        } else {
            ascii
        }

        val cleanLetters = prefixPart.replace(Regex("[^a-zA-Z0-9]"), "")
        if (cleanLetters.isEmpty()) {
            return trailingDigits.take(maxChars).padEnd(min(3, maxChars), 'X')
        }

        val targetLetterCount = (maxChars - trailingDigits.length).coerceAtLeast(1)
        val vowels = setOf('a', 'e', 'i', 'o', 'u', 'y', 'A', 'E', 'I', 'O', 'U', 'Y')

        val shortLetters = if (cleanLetters.length <= targetLetterCount) {
            cleanLetters
        } else {
            val sb = StringBuilder()
            sb.append(cleanLetters[0])
            for (i in 1 until cleanLetters.length) {
                if (sb.length >= targetLetterCount) break
                val c = cleanLetters[i]
                if (c !in vowels) {
                    sb.append(c)
                }
            }
            if (sb.length < targetLetterCount) {
                for (i in 1 until cleanLetters.length) {
                    if (sb.length >= targetLetterCount) break
                    val c = cleanLetters[i]
                    if (c in vowels) {
                        sb.append(c)
                    }
                }
            }
            sb.toString()
        }

        val combined = (shortLetters.take(targetLetterCount) + trailingDigits).take(maxChars)
        return combined
    }

    fun formatMeshtasticLongName(atakCallsign: String, factionName: String): String {
        val cleanCall = formatAtakCallsign(atakCallsign)
        val cleanFac = normalizeAscii(factionName).trim().uppercase(Locale.ROOT)
        return if (cleanFac.isNotEmpty()) "$cleanCall [$cleanFac]" else cleanCall
    }

    fun mapHexToAtakTeamColorName(colorHex: String?): String {
        val hex = (colorHex ?: "#2684FF").uppercase(Locale.ROOT)
        return when {
            hex.contains("2684FF") || hex.contains("0000FF") || hex.contains("3498DB") -> "Blue"
            hex.contains("EB4034") || hex.contains("FF0000") || hex.contains("E74C3C") -> "Red"
            hex.contains("2ECC71") || hex.contains("00FF00") || hex.contains("27AE60") -> "Green"
            hex.contains("FAD02C") || hex.contains("FFFF00") || hex.contains("F1C40F") -> "Yellow"
            hex.contains("FF9F43") || hex.contains("E67E22") -> "Orange"
            hex.contains("9B59B6") || hex.contains("8E44AD") -> "Purple"
            hex.contains("1FE0C5") || hex.contains("00FFFF") -> "Cyan"
            else -> "Cyan"
        }
    }
}

// =============================================================================
// 3. MODELE DANYCH PACZKI (.FTAK), KANAŁÓW RADIOWYCH, SARNEG ORAZ OPORD
// =============================================================================
data class FthVoiceChannel(
    val roleName: String,
    val bandType: String,
    val freqMhz: String,
    val toneOrCode: String
)

data class FthRosterMember(
    val operatorOrTeam: String,
    val primaryRole: String,
    val secondaryRole: String,
    val weaponAndGear: String
)

data class FthRoleDefinition(
    val roleName: String,
    val dutyMode: String,
    val description: String
)

data class FthOpordDayPlan(
    val dayTitle: String = "Dzień 1",
    val dateIso: String = SimpleDateFormat("yyyy-MM-dd", Locale.US).format(Date()),
    val classification: String = "JAWNE / UNCLASSIFIED — MILSIM OPORD",
    val isSecret: Boolean = false,
    val operationNumber: String = "OPORD 01-2026",
    val dtgIssued: String = "270800Z OCT 2026",
    val issuingHq: String = "TOC / SZTAB DRUŻYNY",
    val locationName: String = "Obszar AO (Czechowice / Bąków)",
    val locationLat: String = "49.8506",
    val locationLon: String = "18.8133",
    val daySchedule: String = "• 08:30 — Odprawa i skan QR Field TAK Hub\n• 09:00 — Wejście w rejon działań (Infil)\n• 14:00 — Realizacja zadań głównych",
    val aoiPoiDesc: String = "Granica Terenu Gry (AO), punkty Infil, Kod oraz Parking zsynchronizowane z mapą 1:5000.",
    val terrainAndMetoc: String = "Las mieszany z rowami melioracyjnymi (widoczne na warstwie LiDAR). Wschód (SR): 06:28 | Zachód (SS): 16:45.",
    val enemyForces: String = "Patrole 4-6 osobowe w rejonie punktów kodowych. Możliwe zasadzki na skrzyżowaniach.",
    val friendlyForces: String = "Sekcje własne działające na kanale głównym PMR/VHF oraz w sieci LoRa Meshtastic.",
    val missionFullText: String = "Drużyna zadaniowa zlokalizuje i zabezpieczy punkty kodowe w wyznaczonym sektorze AO, utrzymując łączność ATAK/Mesh.",
    val additionalTasks: String = "• Dokumentacja fotograficzna kodów\n• Zabezpieczenie rekwizytów sztabowych",
    val commandersIntent: String = "Pełna świadomość sytuacyjna w ATAK, unikanie zbędnego kontaktu w marszu, szybkie zrywanie kontaktu.",
    val chainOfCommand: String = "Lider (Dowódca) -> 1. Zastępca (Navi) -> 2. Zastępca (RTO)",
    val sarnegKeyword10: String = "PRZEDMIOTY",
    val targetCodeNames: String = "• OBJ ALPHA = 'ZAMEK'  |  • Punkt Zbiórki = 'KUŹNIA'  |  • Respawn = 'BAZA'",
    val challengePassword: String = "a) Werbalne: GROM - BŁYSK - TARCZA (Suma: 7)\nb) Niewerbalne: 2 błyski czerwonym światłem - 1 błysk",
    val medevacRules: String = "1. trafienie: opatrunek (2 min). 2. trafienie: odejście na Respawn. Wypadek: przycisk 🚨 SOS MGRS.",
    val gearChecklist: String = "SZPEJLISTA: Ochrona wzroku, dead-rag, latarka czerwona, radio PMR/VHF, telefon z ATAK & Field TAK Hub, płytka LoRa.",
    val squadRoster: List<FthRosterMember> = emptyList(),
    val rolesAnnex: List<FthRoleDefinition> = emptyList()
)

data class FthFactionProfile(
    var eventName: String = "MILSIM 2026",
    var factionName: String = "BLUFOR",
    var colorHex: String = "#2684FF",
    var operatorCallsign: String = "Wilk-1",
    var meshtasticShortName: String = "Wlk1",
    var channelName: String = "BLUFOR-MESH",
    var pskBase64: String = "YCVv8+pxmmV26ff2U0M4mami64OL7A9Z0Nyv6VgsdmE=",
    var channelUrl: String = "",
    var loraRegion: String = "EU_868",
    var loraModemPreset: String = "SHORT_FAST",
    var hopLimit: Int = 3,
    var otsHost: String = "",
    var enrollmentUri: String = "",
    var emergencyPhone: String = "+48 600 000 000",
    var voiceChannels: MutableList<FthVoiceChannel> = mutableListOf(
        FthVoiceChannel("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"),
        FthVoiceChannel("Taktyczny Alfa", "VHF/UHF", "145.500 MHz", "CTCSS 88.5 Hz"),
        FthVoiceChannel("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
    ),
    var opordDays: MutableList<FthOpordDayPlan> = mutableListOf(FthOpordDayPlan()),
    var activeDayIndex: Int = 0,
    var attachedMapFiles: MutableList<String> = mutableListOf("Mapa_Taktyczna_MGRS_1_5000.kmz", "Punkty_AO_Infil_Kody.gpx")
) {
    fun toCompactQrJson(): String {
        val root = JSONObject()
        root.put("schemaVersion", "3.4")
        root.put("eventName", eventName)
        root.put("Name", factionName)
        root.put("ColorHex", colorHex)
        root.put("ChannelName", channelName)
        root.put("Psk", pskBase64)
        root.put("LoraRegion", loraRegion)
        root.put("LoraModemPreset", loraModemPreset)
        root.put("HopLimit", hopLimit)
        root.put("OtsHost", otsHost)
        root.put("EmergencyPhone", emergencyPhone)

        val vcArr = JSONArray()
        for (vc in voiceChannels) {
            vcArr.put(JSONObject().apply {
                put("roleName", vc.roleName)
                put("bandType", vc.bandType)
                put("freqMhz", vc.freqMhz)
                put("toneOrCode", vc.toneOrCode)
            })
        }
        root.put("VoiceChannels", vcArr)

        val daysArr = JSONArray()
        for (d in opordDays) {
            daysArr.put(JSONObject().apply {
                put("DayTitle", d.dayTitle)
                put("DateIso", d.dateIso)
                put("Classification", d.classification)
                put("IsSecretClassification", d.isSecret)
                put("OperationNumber", d.operationNumber)
                put("DtgIssued", d.dtgIssued)
                put("IssuingHq", d.issuingHq)
                put("LocationName", d.locationName)
                put("LocationLat", d.locationLat)
                put("LocationLon", d.locationLon)
                put("DaySpecificTasks", d.daySchedule)
                put("AreaOfInterestPoiDesc", d.aoiPoiDesc)
                put("TerrainAndWeather", d.terrainAndMetoc)
                put("EnemyForces", d.enemyForces)
                put("FriendlyForces", d.friendlyForces)
                put("MissionFullStatement", d.missionFullText)
                put("AdditionalTasksSummary", d.additionalTasks)
                put("CommandersIntent", d.commandersIntent)
                put("SuccessionOfCommand", d.chainOfCommand)
                put("SarnegKeyWord10", d.sarnegKeyword10)
                put("TargetCodeNames", d.targetCodeNames)
                put("ChallengeAndPassword", d.challengePassword)
                put("MedevacRules", d.medevacRules)
                put("SquadGearChecklist", d.gearChecklist)
            })
        }
        root.put("OpordDays", daysArr)
        return root.toString()
    }
}

// =============================================================================
// 4. KONWERTER GEODEZYJNY WGS84 (LAT, LON) -> WOJSKOWY MELDUNEK MGRS + SARNEG
// =============================================================================
object TacticalGeoAndSarnegEngine {

    val BuiltInSarnegWords = listOf(
        "ABSOLUTNIE", "DOSTANIEMY", "DWUKROTNIE", "FAKTYCZNIE", "INFORMACJE",
        "INSTRUKCJE", "MIASTECZKO", "MIASTECZKU", "NAJGORSZYM", "NAJLEPSZYM",
        "OBRZYDLIWE", "OBSERWACJI", "PIERWSZYCH", "PODEJRZANY", "PORUCZNIKA",
        "POTWIERDZA", "PROWADZIMY", "PRYSZNICEM", "PRZECIWNYM", "PRZEDMIOTY",
        "PRZEKONAMY", "PRZYJACIEL", "PRZYSTANEK", "PUBLICZNEJ", "SPODZIEWAM",
        "SPRAWDZIMY", "STWORZENIA", "STYPENDIUM", "SZACUNKIEM", "TAJEMNICZY",
        "TRZYMAJCIE", "UTRZYMANIE", "ZACHODNIEJ", "ZOSTANIEMY",
        "AFTERSHOCK", "ALGORITHMS", "ARTICHOKES", "BACKGROUND", "BANKRUPTCY",
        "BEACHFRONT", "BENCHMARKS", "BINOCULARS", "BIRTHPLACE", "BLACKSMITH",
        "BLOCKHEADS", "BLUEPRINTS", "BOYFRIENDS", "BREAKDOWNS", "CAMPGROUND",
        "COMPATIBLE", "COMPLAINTS", "CORNFIELDS", "DECORATING", "DOWNSTREAM",
        "EARTHLINGS", "EXHAUSTING", "FLAVORINGS", "FLOWCHARTS", "FRUITCAKES",
        "HARVESTING", "HEADSTRONG", "HYPERLINKS", "HYPOCRITES", "IMPORTANCE",
        "JOURNALISM", "JOURNALIST", "LIFEGUARDS", "LUMBERJACK", "MANUSCRIPT",
        "MATCHBOXES", "METHODICAL", "MICROWAVES", "MISFORTUNE", "NIGHTCLUBS",
        "NIGHTMARES", "OVERTAKING", "PAINTBRUSH", "PATHFINDER", "PLAYGROUND",
        "PLAYTHINGS", "PODCASTING", "PRECAUTION", "PREVIOUSLY", "PRODUCTIVE",
        "PROFITABLE", "PURCHASING", "REGULATION", "SCHEDULING", "SCRAMBLING",
        "SMARTPHONE", "SOUNDTRACK", "STAMPEDING", "STOCKPILED", "SUPERWOMAN",
        "TAMBOURINE", "TIMBERLAND", "TRAMPOLINE", "TRUCKLOADS", "WANDERLUST",
        "WORKPLACES"
    )

    private val ColSets = arrayOf("ABCDEFGH", "JKLMNPQR", "STUVWXYZ")
    private val RowSets = arrayOf("ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE")

    fun latLonToMgrs(lat: Double, lon: Double): String {
        return try {
            val clampedLat = lat.coerceIn(-80.0, 84.0)
            val normLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0
            val zoneNumber = floor((normLon + 180.0) / 6.0).toInt() + 1

            val latRad = Math.toRadians(clampedLat)
            val lonRad = Math.toRadians(normLon)
            val lonOriginRad = Math.toRadians(((zoneNumber - 1) * 6 - 180 + 3).toDouble())

            val a = 6378137.0
            val eccSquared = 0.00669438
            val k0 = 0.9996
            val eccPrimeSquared = eccSquared / (1.0 - eccSquared)

            val n = a / sqrt(1.0 - eccSquared * sin(latRad) * sin(latRad))
            val t = tan(latRad) * tan(latRad)
            val c = eccPrimeSquared * cos(latRad) * cos(latRad)
            val aCoeff = cos(latRad) * (lonRad - lonOriginRad)

            val m = a * ((1.0 - eccSquared / 4.0 - 3.0 * eccSquared * eccSquared / 64.0) * latRad
                    - (3.0 * eccSquared / 8.0 + 3.0 * eccSquared * eccSquared / 32.0) * sin(2.0 * latRad)
                    + (15.0 * eccSquared * eccSquared / 256.0) * sin(4.0 * latRad))

            val utmEasting = k0 * n * (aCoeff + (1.0 - t + c) * aCoeff.pow(3) / 6.0
                    + (5.0 - 18.0 * t + t * t + 72.0 * c - 58.0 * eccPrimeSquared) * aCoeff.pow(5) / 120.0) + 500000.0

            var utmNorthing = k0 * (m + n * tan(latRad) * (aCoeff * aCoeff / 2.0
                    + (5.0 - t + 9.0 * c + 4.0 * c * c) * aCoeff.pow(4) / 24.0))
            if (clampedLat < 0) utmNorthing += 10000000.0

            val bandLetters = "CDEFGHJKLMNPQRSTUVWXX"
            val bandIdx = floor((clampedLat + 80.0) / 8.0).toInt().coerceIn(0, bandLetters.length - 1)
            val bandChar = bandLetters[bandIdx]

            val colIdx = (floor(utmEasting / 100000.0).toInt() - 1).coerceIn(0, 7)
            val colChar = ColSets[(zoneNumber - 1) % 3][colIdx]

            val rowIdx = (floor(utmNorthing / 100000.0).toInt() % 20).coerceIn(0, 19)
            val rowChar = RowSets[(zoneNumber - 1) % 2][rowIdx]

            val east5 = (utmEasting.roundToInt() % 100000).coerceIn(0, 99999)
            val north5 = (utmNorthing.roundToInt() % 100000).coerceIn(0, 99999)

            String.format(Locale.US, "%02d%c %c%c %05d %05d", zoneNumber, bandChar, colChar, rowChar, east5, north5)
        } catch (_: Exception) {
            "34U CA 42815 24190"
        }
    }

    fun encodeOrDecodeSarneg(input: String, sarnegWord10: String): String {
        val cleanKey = CallsignFormatter.normalizeAscii(sarnegWord10).uppercase(Locale.ROOT).trim()
            .padEnd(10, 'X').substring(0, 10)
        val sb = StringBuilder()
        for (ch in input.uppercase(Locale.ROOT)) {
            if (ch in '0'..'9') {
                val digit = ch - '0'
                sb.append(cleanKey[digit])
            } else {
                val asciiCh = CallsignFormatter.normalizeAscii(ch.toString()).firstOrNull() ?: ch
                val idx = cleanKey.indexOf(asciiCh)
                if (idx >= 0) {
                    sb.append(idx)
                } else {
                    sb.append(ch)
                }
            }
        }
        return sb.toString()
    }
}

// =============================================================================
// 5. WEKTOROWE IKONY TAKTYCZNE (RYSOWANE NA CANVAS — STYL MILSIM / BUILDER)
// =============================================================================
enum class TacticalIconType {
    RADAR_HUB,
    QR_SCANNER,
    CALLSIGN_BADGE,
    RADIO_COMMS,
    OPORD_CLIPBOARD,
    WIFI_HOTSPOT,
    SOS_BEACON,
    LORA_CHIP,
    MAP_GRID
}

class TacticalVectorIconView(
    context: Context,
    private var iconType: TacticalIconType,
    private var tintColor: Int = FthTheme.AccentCyan,
    sizeDp: Float = 24f
) : View(context) {

    private val paintStroke = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    private val paintFill = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
    }

    init {
        val px = FthTheme.dp(context, sizeDp)
        layoutParams = android.view.ViewGroup.LayoutParams(px, px)
    }

    fun setIconTint(newColor: Int) {
        tintColor = newColor
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 0f || h <= 0f) return

        val cx = w / 2f
        val cy = h / 2f
        val r = min(w, h) * 0.42f

        paintStroke.color = tintColor
        paintStroke.strokeWidth = max(2f, min(w, h) * 0.085f)
        paintFill.color = tintColor

        when (iconType) {
            TacticalIconType.RADAR_HUB -> {
                canvas.drawCircle(cx, cy, r, paintStroke)
                canvas.drawCircle(cx, cy, r * 0.55f, paintStroke)
                canvas.drawCircle(cx, cy, r * 0.16f, paintFill)
                canvas.drawLine(cx - r * 1.1f, cy, cx - r * 0.65f, cy, paintStroke)
                canvas.drawLine(cx + r * 0.65f, cy, cx + r * 1.1f, cy, paintStroke)
                canvas.drawLine(cx, cy - r * 1.1f, cx, cy - r * 0.65f, paintStroke)
                canvas.drawLine(cx, cy + r * 0.65f, cx, cy + r * 1.1f, paintStroke)
            }
            TacticalIconType.QR_SCANNER -> {
                val b = r * 0.95f
                val cLen = b * 0.45f
                canvas.drawLine(cx - b, cy - b, cx - b + cLen, cy - b, paintStroke)
                canvas.drawLine(cx - b, cy - b, cx - b, cy - b + cLen, paintStroke)
                canvas.drawLine(cx + b, cy - b, cx + b - cLen, cy - b, paintStroke)
                canvas.drawLine(cx + b, cy - b, cx + b, cy - b + cLen, paintStroke)
                canvas.drawLine(cx - b, cy + b, cx - b + cLen, cy + b, paintStroke)
                canvas.drawLine(cx - b, cy + b, cx - b, cy + b - cLen, paintStroke)
                canvas.drawLine(cx + b, cy + b, cx + b - cLen, cy + b, paintStroke)
                canvas.drawLine(cx + b, cy + b, cx + b, cy + b - cLen, paintStroke)
                canvas.drawLine(cx - b * 0.7f, cy, cx + b * 0.7f, cy, paintStroke)
            }
            TacticalIconType.CALLSIGN_BADGE -> {
                val path = Path().apply {
                    moveTo(cx, cy - r)
                    lineTo(cx + r * 0.85f, cy - r * 0.55f)
                    lineTo(cx + r * 0.85f, cy + r * 0.15f)
                    quadTo(cx + r * 0.85f, cy + r * 0.85f, cx, cy + r)
                    quadTo(cx - r * 0.85f, cy + r * 0.85f, cx - r * 0.85f, cy + r * 0.15f)
                    lineTo(cx - r * 0.85f, cy - r * 0.55f)
                    close()
                }
                canvas.drawPath(path, paintStroke)
                canvas.drawLine(cx - r * 0.35f, cy, cx + r * 0.35f, cy, paintStroke)
                canvas.drawLine(cx, cy - r * 0.35f, cx, cy + r * 0.35f, paintStroke)
            }
            TacticalIconType.RADIO_COMMS -> {
                canvas.drawLine(cx - r * 0.35f, cy - r, cx - r * 0.35f, cy - r * 0.3f, paintStroke)
                val rect = RectF(cx - r * 0.65f, cy - r * 0.3f, cx + r * 0.65f, cy + r)
                canvas.drawRoundRect(rect, 6f, 6f, paintStroke)
                canvas.drawLine(cx - r * 0.35f, cy + r * 0.1f, cx + r * 0.35f, cy + r * 0.1f, paintStroke)
                canvas.drawLine(cx - r * 0.35f, cy + r * 0.45f, cx + r * 0.35f, cy + r * 0.45f, paintStroke)
            }
            TacticalIconType.OPORD_CLIPBOARD -> {
                val rect = RectF(cx - r * 0.75f, cy - r * 0.8f, cx + r * 0.75f, cy + r)
                canvas.drawRoundRect(rect, 6f, 6f, paintStroke)
                canvas.drawLine(cx - r * 0.4f, cy - r * 0.25f, cx + r * 0.4f, cy - r * 0.25f, paintStroke)
                canvas.drawLine(cx - r * 0.4f, cy + r * 0.15f, cx + r * 0.4f, cy + r * 0.15f, paintStroke)
                canvas.drawLine(cx - r * 0.4f, cy + r * 0.55f, cx + r * 0.15f, cy + r * 0.55f, paintStroke)
            }
            TacticalIconType.WIFI_HOTSPOT -> {
                canvas.drawCircle(cx, cy + r * 0.55f, r * 0.16f, paintFill)
                val arc1 = RectF(cx - r * 0.55f, cy - r * 0.05f, cx + r * 0.55f, cy + r * 1.05f)
                val arc2 = RectF(cx - r * 0.95f, cy - r * 0.5f, cx + r * 0.95f, cy + r * 1.35f)
                canvas.drawArc(arc1, 215f, 110f, false, paintStroke)
                canvas.drawArc(arc2, 215f, 110f, false, paintStroke)
            }
            TacticalIconType.SOS_BEACON -> {
                val path = Path().apply {
                    moveTo(cx, cy - r)
                    lineTo(cx + r, cy + r * 0.85f)
                    lineTo(cx - r, cy + r * 0.85f)
                    close()
                }
                canvas.drawPath(path, paintStroke)
                canvas.drawLine(cx, cy - r * 0.3f, cx, cy + r * 0.25f, paintStroke)
                canvas.drawCircle(cx, cy + r * 0.55f, r * 0.1f, paintFill)
            }
            TacticalIconType.LORA_CHIP -> {
                val rect = RectF(cx - r * 0.6f, cy - r * 0.6f, cx + r * 0.6f, cy + r * 0.6f)
                canvas.drawRoundRect(rect, 5f, 5f, paintStroke)
                canvas.drawLine(cx - r, cy - r * 0.25f, cx - r * 0.6f, cy - r * 0.25f, paintStroke)
                canvas.drawLine(cx - r, cy + r * 0.25f, cx - r * 0.6f, cy + r * 0.25f, paintStroke)
                canvas.drawLine(cx + r * 0.6f, cy - r * 0.25f, cx + r, cy - r * 0.25f, paintStroke)
                canvas.drawLine(cx + r * 0.6f, cy + r * 0.25f, cx + r, cy + r * 0.25f, paintStroke)
            }
            TacticalIconType.MAP_GRID -> {
                val rect = RectF(cx - r * 0.85f, cy - r * 0.85f, cx + r * 0.85f, cy + r * 0.85f)
                canvas.drawRect(rect, paintStroke)
                canvas.drawLine(cx - r * 0.85f, cy, cx + r * 0.85f, cy, paintStroke)
                canvas.drawLine(cx, cy - r * 0.85f, cx, cy + r * 0.85f, paintStroke)
            }
        }
    }
}
