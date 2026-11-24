package com.fieldtakhub.hub.core

import java.io.ByteArrayOutputStream
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/**
 * ATAK integration points that need no ATAK SDK:
 *  - tak://com.atakmap.app/preference?key1=&type1=&value1=  (ATAK 5.1+, user confirms; cot_streams are blocked)
 *  - tak://com.atakmap.app/import?url=                        (ATAK downloads and imports a data package)
 *  - tak://com.atakmap.app/enroll?host=&username=&token=      (server enrollment link from OpenTAK Server)
 *  - data packages (MANIFEST/manifest.xml + .pref) for the server connection.
 */
object Atak {
    const val CIV_PACKAGE = "com.atakmap.app.civ"
    val PACKAGE_CANDIDATES = listOf("com.atakmap.app.civ", "com.atakmap.app.mil", "com.atakmap.app")
    val DEEP_LINK_MIN = Version(listOf(5, 1))

    data class TeamColor(val name: String, val rgb: Int)

    val TEAM_COLORS = listOf(
        TeamColor("White", 0xFFFFFF), TeamColor("Yellow", 0xFFFF00), TeamColor("Orange", 0xFF8000),
        TeamColor("Magenta", 0xFF00FF), TeamColor("Red", 0xFF0000), TeamColor("Maroon", 0x800000),
        TeamColor("Purple", 0x800080), TeamColor("Dark Blue", 0x00008B), TeamColor("Blue", 0x0000FF),
        TeamColor("Cyan", 0x00FFFF), TeamColor("Teal", 0x008080), TeamColor("Green", 0x00FF00),
        TeamColor("Dark Green", 0x006400), TeamColor("Brown", 0xA52A2A)
    )

    fun parseHexColor(hex: String?): Int? {
        val h = hex?.trim()?.removePrefix("#") ?: return null
        val six = when (h.length) {
            6 -> h
            8 -> h.substring(2)
            3 -> h.map { "$it$it" }.joinToString("")
            else -> return null
        }
        return six.toIntOrNull(16)
    }

    /** Hue (0-360), saturation and value (0-1). */
    private fun hsv(rgb: Int): DoubleArray {
        val r = ((rgb shr 16) and 0xFF) / 255.0
        val g = ((rgb shr 8) and 0xFF) / 255.0
        val b = (rgb and 0xFF) / 255.0
        val max = maxOf(r, g, b)
        val min = minOf(r, g, b)
        val d = max - min
        val h = when {
            d == 0.0 -> 0.0
            max == r -> 60.0 * (((g - b) / d) % 6.0)
            max == g -> 60.0 * ((b - r) / d + 2.0)
            else -> 60.0 * ((r - g) / d + 4.0)
        }.let { if (it < 0) it + 360.0 else it }
        return doubleArrayOf(h, if (max == 0.0) 0.0 else d / max, max)
    }

    /** ATAK team (locationTeam) closest to the faction colour from Builder, compared by hue first. */
    fun nearestTeam(hex: String?): String? {
        val rgb = parseHexColor(hex) ?: return null
        val c = hsv(rgb)
        if (c[1] < 0.2) return "White"
        return TEAM_COLORS.filter { it.name != "White" }.minByOrNull { team ->
            val t = hsv(team.rgb)
            val dh = Math.abs(c[0] - t[0]).let { if (it > 180) 360 - it else it }
            dh / 180.0 * 3.0 + Math.abs(c[2] - t[2]) + Math.abs(c[1] - t[1]) * 0.5
        }?.name
    }

    fun preferenceUri(settings: List<AtakSetting>): String {
        val sb = StringBuilder("tak://com.atakmap.app/preference?")
        settings.forEachIndexed { i, s ->
            val n = i + 1
            if (i > 0) sb.append('&')
            sb.append("key").append(n).append('=').append(Codec.percentEncode(s.key))
            sb.append("&type").append(n).append('=').append(s.type)
            sb.append("&value").append(n).append('=').append(Codec.percentEncode(s.value))
        }
        return sb.toString()
    }

    fun importUri(url: String): String = "tak://com.atakmap.app/import?url=" + Codec.percentEncode(url)

    fun isEnrollUri(text: String?): Boolean {
        val t = text?.trim() ?: return false
        if (!t.startsWith("tak://com.atakmap.app/enroll", ignoreCase = true)) return false
        val q = Codec.parseQuery(t.substringAfter('?', ""))
        return !q["host"].isNullOrBlank() && !q["username"].isNullOrBlank() && !q["token"].isNullOrBlank()
    }

    fun enrollHost(text: String?): String? =
        text?.let { Codec.parseQuery(it.substringAfter('?', ""))["host"] }

    /** Callsign and team colour. */
    fun identitySettings(callsign: String, factionColor: String?): List<AtakSetting> {
        val out = ArrayList<AtakSetting>()
        if (callsign.isNotBlank()) out += AtakSetting("locationCallsign", "string", callsign, "Callsign")
        nearestTeam(factionColor)?.let { out += AtakSetting("locationTeam", "string", it, "Kolor zespołu") }
        return out
    }

    private val REPORTING_KEYS = linkedMapOf(
        "locationReportingStrategy" to "Strategia raportowania",
        "constantReportingRateReliable" to "Stałe: serwer (s)",
        "constantReportingRateUnreliable" to "Stałe: sieć mesh/UDP (s)",
        "dynamicReportingRateMinReliable" to "Dynamiczne: min. serwer (s)",
        "dynamicReportingRateMaxReliable" to "Dynamiczne: maks. serwer (s)",
        "dynamicReportingRateMinUnreliable" to "Dynamiczne: min. mesh (s)",
        "dynamicReportingRateMaxUnreliable" to "Dynamiczne: maks. mesh (s)",
        "dynamicReportingRateStationaryReliable" to "Postój: serwer (s)",
        "dynamicReportingRateStationaryUnreliable" to "Postój: mesh (s)"
    )

    private val BOOLEAN_KEYS = linkedMapOf(
        "dispatchLocationCotExternal" to "Wysyłaj pozycję na zewnątrz",
        "enableNonStreamingConnections" to "Połączenia bez strumienia (mesh)"
    )

    /** Explicit values from a v2.4 tactical_config.json "atak" block. Zero or negative rates are skipped. */
    fun reportingFromConfig(atak: JsonObj): List<AtakSetting> {
        val out = ArrayList<AtakSetting>()
        for ((key, label) in REPORTING_KEYS) {
            val raw = atak.str(key)?.trim() ?: continue
            if (key == "locationReportingStrategy") {
                val v = when (raw.lowercase()) { "dynamic" -> "Dynamic"; "constant" -> "Constant"; else -> null } ?: continue
                out += AtakSetting(key, "string", v, label)
            } else {
                val n = raw.toDoubleOrNull()?.toLong() ?: continue
                if (n > 0) out += AtakSetting(key, "string", n.toString(), label)
            }
        }
        for ((key, label) in BOOLEAN_KEYS) {
            val b = atak.bool(key) ?: continue
            out += AtakSetting(key, "boolean", b.toString(), label)
        }
        return out
    }

    /**
     * v3.0 packages carry only profile names (PliProfile / MeshProfile), not numbers.
     * These are the values the app applies for them; they are shown to the user before injection.
     */
    fun reportingFromProfiles(pliProfile: String?, meshProfile: String?): List<AtakSetting> {
        val out = ArrayList<AtakSetting>()
        when (pliProfile?.trim()?.lowercase()) {
            null, "" -> Unit
            "dynamic", "dynrate", "dynamicrate" -> {
                out += AtakSetting("locationReportingStrategy", "string", "Dynamic", "Strategia raportowania")
                out += AtakSetting("dynamicReportingRateMinReliable", "string", "2", "Dynamiczne: min. serwer (s)")
                out += AtakSetting("dynamicReportingRateMaxReliable", "string", "20", "Dynamiczne: maks. serwer (s)")
            }
            else -> {
                out += AtakSetting("locationReportingStrategy", "string", "Constant", "Strategia raportowania")
                out += AtakSetting("constantReportingRateReliable", "string", "5", "Stałe: serwer (s)")
                out += AtakSetting("constantReportingRateUnreliable", "string", "15", "Stałe: sieć mesh/UDP (s)")
            }
        }
        if (meshProfile?.trim()?.lowercase() == "stationarymesh") {
            out += AtakSetting("dynamicReportingRateStationaryUnreliable", "string", "30", "Postój: mesh (s)")
        }
        return out
    }

    /** Server connection as an ATAK data package, same content Builder's MissionPackageBuilder writes. */
    fun serverDataPackage(server: ServerInfo, packageName: String): ByteArray {
        val pref = buildString {
            append("<?xml version='1.0' encoding='UTF-8' standalone='yes'?>\n")
            append("<preferences>\n")
            append("  <preference version=\"1\" name=\"cot_streams\">\n")
            append(entry("count", "Integer", "1"))
            append(entry("description0", "String", server.name.ifBlank { "TAK Server" }))
            append(entry("enabled0", "Boolean", "true"))
            append(entry("connectString0", "String", "${server.host}:${server.cotPort}:ssl"))
            append(entry("useAuth0", "Boolean", "true"))
            append(entry("enrollForCertificateWithTrust0", "Boolean", "true"))
            append("  </preference>\n")
            append("</preferences>\n")
        }
        return dataPackage(packageName, mapOf("server.pref" to pref.toByteArray(Charsets.UTF_8)))
    }

    /** Preferences as a data package, for ATAK older than 5.1 (no preference deep link). */
    fun preferenceDataPackage(settings: List<AtakSetting>, packageName: String, atakPackage: String): ByteArray {
        val pref = buildString {
            append("<?xml version='1.0' encoding='UTF-8' standalone='yes'?>\n")
            append("<preferences>\n")
            append("  <preference version=\"1\" name=\"").append(xml(atakPackage)).append("_preferences\">\n")
            for (s in settings) {
                val cls = when (s.type) { "boolean" -> "Boolean"; "int" -> "Integer"; "long" -> "Long"; else -> "String" }
                append(entry(s.key, cls, s.value))
            }
            append("  </preference>\n")
            append("</preferences>\n")
        }
        return dataPackage(packageName, mapOf("fieldtakhub.pref" to pref.toByteArray(Charsets.UTF_8)))
    }

    private fun entry(key: String, cls: String, value: String) =
        "    <entry key=\"${xml(key)}\" class=\"class java.lang.$cls\">${xml(value)}</entry>\n"

    private fun xml(s: String) = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;").replace("'", "&apos;")

    fun dataPackage(name: String, files: Map<String, ByteArray>): ByteArray {
        val manifest = buildString {
            append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
            append("<MissionPackageManifest version=\"2\">\n")
            append("  <Configuration>\n")
            append("    <Parameter name=\"uid\" value=\"").append(UUID.randomUUID()).append("\"/>\n")
            append("    <Parameter name=\"name\" value=\"").append(xml(name)).append("\"/>\n")
            append("    <Parameter name=\"onReceiveDelete\" value=\"true\"/>\n")
            append("  </Configuration>\n")
            append("  <Contents>\n")
            for (f in files.keys) append("    <Content ignore=\"false\" zipEntry=\"").append(xml(f)).append("\"/>\n")
            append("  </Contents>\n")
            append("</MissionPackageManifest>\n")
        }
        val bos = ByteArrayOutputStream()
        ZipOutputStream(bos).use { zip ->
            zip.putNextEntry(ZipEntry("MANIFEST/manifest.xml"))
            zip.write(manifest.toByteArray(Charsets.UTF_8))
            zip.closeEntry()
            for ((path, bytes) in files) {
                zip.putNextEntry(ZipEntry(path))
                zip.write(bytes)
                zip.closeEntry()
            }
        }
        return bos.toByteArray()
    }
}
