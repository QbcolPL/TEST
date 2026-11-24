package com.fieldtakhub.hub.core

/** Operation order (OPORD) as written by Builder's OPORD engine (opord.json, PascalCase keys). */
data class OpordCodeword(val word: String, val letter: String, val digit: String, val duress: String)

data class OpordRosterRow(val callsign: String, val role: String, val gear: String)

data class OpordSection(val title: String, val items: List<Pair<String, String>>)

data class OpordDay(
    val title: String,
    val dateIso: String,
    val classification: String,
    val operationNumber: String,
    val dtg: String,
    val locationName: String,
    val lat: Double?,
    val lon: Double?,
    val mission: String,
    val schedule: String,
    val primaryFreq: String,
    val backupFreq: String,
    val hqFreq: String,
    val primaryChannel: String,
    val alternateChannel: String,
    val hqChannel: String,
    val commsSummary: String,
    val customChannelsTable: String,
    val metoc: List<Pair<String, String>>,
    val sections: List<OpordSection>,
    val codewords: List<OpordCodeword>,
    val challenge: String,
    val sarneg: String,
    val targetCodeNames: String,
    val roster: List<OpordRosterRow>,
    val mapNotes: String
)

data class Opord(
    val eventName: String,
    val faction: String,
    val operationNumber: String,
    val days: List<OpordDay>
) {
    val codewords: List<OpordCodeword> get() = days.flatMap { it.codewords }.distinct()
}

object OpordParser {
    /** opord.json (OpordParagraphModel). Days that are excluded from export are skipped. */
    fun parse(root: JsonObj): Opord {
        val event = root.text("EventName", "eventName")
        val faction = root.text("TargetFaction", "targetFaction", "faction")
        val dayObjs = root.objects("Days", "days").filter { it.bool("IncludeInExport", "includeInExport") != false }
        val days = if (dayObjs.isEmpty()) listOf(day(root, root, 0)) else dayObjs.mapIndexed { i, d -> day(d, root, i) }
        val opNumber = days.firstOrNull()?.operationNumber?.ifBlank { null } ?: root.text("OperationNumber", "operationNumber")
        return Opord(event, faction, opNumber, days)
    }

    fun looksLikeOpord(root: JsonObj): Boolean =
        root.has("Days", "MissionWho", "OperationNumber", "CommandersIntent") && !root.has("RequiredPackages", "schemaVersion")

    /** Minimal OPORD from the OpordSummary block of a v3.0 config. */
    fun fromSummary(summary: JsonObj, event: String, faction: String): Opord {
        val lat = summary.double("locationLat")
        val lon = summary.double("locationLon")
        val metoc = listOfNotNull(
            pair("Pogoda", summary.text("weatherCondition")),
            pair("Temperatura", summary.text("temperature")),
            pair("Wiatr", summary.text("wind")),
            pair("Słońce", summary.text("sunTimes")),
            pair("Księżyc", summary.text("moonPhase"))
        )
        val day = OpordDay(
            title = "Dzień 1",
            dateIso = summary.text("operationDate"),
            classification = "",
            operationNumber = summary.text("operationNumber"),
            dtg = "",
            locationName = summary.text("locationName"),
            lat = lat,
            lon = lon,
            mission = summary.text("missionStatement"),
            schedule = "",
            primaryFreq = "", backupFreq = "", hqFreq = "",
            primaryChannel = "", alternateChannel = "", hqChannel = "",
            commsSummary = "", customChannelsTable = "",
            metoc = metoc,
            sections = emptyList(),
            codewords = emptyList(),
            challenge = summary.text("challengePassword"),
            sarneg = summary.text("sarnegKey"),
            targetCodeNames = "",
            roster = emptyList(),
            mapNotes = ""
        )
        return Opord(event, faction, day.operationNumber, listOf(day))
    }

    private fun pair(label: String, value: String): Pair<String, String>? =
        if (value.isBlank() || value == "-") null else label to value

    private fun day(d: JsonObj, root: JsonObj, index: Int): OpordDay {
        fun v(vararg keys: String): String {
            val own = d.text(*keys)
            if (own.isNotBlank() && own != "-") return own
            val top = root.text(*keys)
            return if (top == "-") "" else top
        }

        val who = v("MissionWho")
        val what = v("MissionWhat")
        val whenText = v("MissionWhen")
        val where = v("MissionWhere")
        val why = v("MissionWhy")
        val composed = listOf(who, what, whenText, where, why).filter { it.isNotBlank() }.joinToString(" ")
        val mission = v("MissionFullStatement").ifBlank { if (composed.isBlank()) "" else composed.trimEnd('.') + "." }

        val metoc = listOfNotNull(
            pair("Pogoda", v("MetocCondition").takeUnless { it.startsWith("Brak pobranego") }.orEmpty()),
            pair("Temperatura", v("MetocTemperature")),
            pair("Wiatr", v("MetocWind")),
            pair("Wilgotność / opady", v("MetocHumidityPrecip")),
            pair("Słońce", v("MetocSunTimes")),
            pair("Księżyc", v("MetocMoonPhase")),
            pair("Skrót", v("MetocQuickSummary"))
        )

        fun section(title: String, vararg items: Pair<String, String>): OpordSection? {
            val kept = items.filter { it.second.isNotBlank() && it.second != "-" && it.second != "https://" }
            return if (kept.isEmpty()) null else OpordSection(title, kept)
        }

        val sections = listOfNotNull(
            section(
                "1. Sytuacja",
                "Teren i warunki" to v("TerrainAndWeather"),
                "Siły przeciwnika" to v("EnemyForces"),
                "Siły własne" to v("FriendlyForces"),
                "Punkty kluczowe" to v("AreaOfInterestPoiDesc"),
                "Tło fabularne" to v("MissionLoreDesc")
            ),
            section(
                "2. Misja",
                "Zadanie" to mission,
                "Zadania dodatkowe" to v("AdditionalTasksSummary")
            ),
            section(
                "3. Wykonanie",
                "Zamiar dowódcy" to v("CommandersIntent"),
                "Koncepcja działania" to v("ConceptOfOperations"),
                "Zadania pododdziałów" to v("TasksToSubordinateUnits"),
                "Koordynacja i ROE" to v("CoordinatingAndRoe", "CoordinationMeasures"),
                "Harmonogram dnia" to v("DaySpecificTasks")
            ),
            section(
                "4. Logistyka",
                "MEDEVAC" to v("MedevacRules"),
                "Respawn i zaopatrzenie" to v("RespawnAndSupply"),
                "Wyposażenie" to v("SquadGearChecklist"),
                "Wyposażenie specjalne" to v("SpecialEquipment"),
                "Dojazd i parking" to v("TravelAndParking"),
                "Zasady gry" to v("GameRulesLink")
            ),
            section(
                "5. Dowodzenie i łączność",
                "Następstwo dowodzenia" to v("SuccessionOfCommand"),
                "Kryptonimy obiektów" to v("TargetCodeNames"),
                "Hasło i odzew" to v("ChallengeAndPassword"),
                "Klucz SARNEG" to v("SarnegKeyWord10")
            ),
            section(
                "Załącznik 2 — zadania",
                "Opracowanie zadań" to v("Annex2TasksAnalysis")
            )
        )

        val codewordObjs = d.objects("CodewordsTable").ifEmpty { root.objects("CodewordsTable") }
        val codewords = codewordObjs.map {
            OpordCodeword(it.text("CodeWord"), it.text("CodeLetter"), it.text("CodeDigit"), it.text("DuressWord"))
        }.filter { it.word.isNotBlank() || it.duress.isNotBlank() }

        val rosterEnabled = (d.bool("EnableSquadRosterTable") ?: root.bool("EnableSquadRosterTable")) != false
        val rosterObjs = if (rosterEnabled) d.objects("SquadRoster").ifEmpty { root.objects("SquadRoster") } else emptyList()
        val roster = rosterObjs.map {
            OpordRosterRow(
                it.text("Callsign", "OperatorOrTeam"),
                listOf(it.text("PrimaryRole"), it.text("SecondaryRole")).filter { s -> s.isNotBlank() }.joinToString(" / "),
                it.text("WeaponAndGear", "GearAndTasks")
            )
        }.filter { it.callsign.isNotBlank() }

        val custom = if (d.bool("IncludeCustomChannelsTable") == true) d.text("CustomChannelsTableText") else ""

        return OpordDay(
            title = d.text("DayTitle").ifBlank { "Dzień ${index + 1}" },
            dateIso = v("DateIso", "OperationDateIso"),
            classification = v("Classification"),
            operationNumber = v("OperationNumber"),
            dtg = v("DtgIssued"),
            locationName = v("LocationName"),
            lat = d.double("LocationLat") ?: root.double("LocationLat"),
            lon = d.double("LocationLon") ?: root.double("LocationLon"),
            mission = mission,
            schedule = v("DaySpecificTasks"),
            primaryFreq = v("PrimaryRadioFreq"),
            backupFreq = v("BackupRadioFreq"),
            hqFreq = v("HqRadioFreq"),
            primaryChannel = d.text("PrimaryChannel"),
            alternateChannel = d.text("AlternateChannel"),
            hqChannel = d.text("HqChannel"),
            commsSummary = d.text("SyncedCommsMatrixSummary"),
            customChannelsTable = custom,
            metoc = metoc,
            sections = sections,
            codewords = codewords,
            challenge = v("ChallengeAndPassword"),
            sarneg = v("SarnegKeyWord10"),
            targetCodeNames = v("TargetCodeNames"),
            roster = roster,
            mapNotes = if (d.bool("IncludeAnnex3Map") == true) d.text("Annex3MapNotes") else ""
        )
    }

    /** Images embedded by Builder in opord.html (Annex 3 map) as data: URIs. */
    fun extractImages(html: String, maxImages: Int = 6): List<ByteArray> {
        val out = ArrayList<ByteArray>()
        var from = 0
        while (out.size < maxImages) {
            val start = html.indexOf("data:image/", from, ignoreCase = true)
            if (start < 0) break
            val marker = html.indexOf(";base64,", start, ignoreCase = true)
            if (marker < 0 || marker - start > 40) { from = start + 11; continue }
            val dataStart = marker + 8
            var end = dataStart
            while (end < html.length) {
                val c = html[end]
                if (c == '\'' || c == '"' || c == ')' || c == '>' || c == ' ') break
                end++
            }
            Codec.base64Decode(html.substring(dataStart, end))?.let { if (it.size > 64) out += it }
            from = end
        }
        return out
    }
}
