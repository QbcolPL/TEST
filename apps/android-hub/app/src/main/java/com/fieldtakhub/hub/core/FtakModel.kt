package com.fieldtakhub.hub.core

/** Which Builder output a package came from. */
enum class PackageKind(val label: String) {
    FTAK_V2("FTAK v2 · podpisana"),
    FTAK_V24("FTAK v2.4 · frakcja"),
    FTAK_V30("FTAK v3.0 · frakcja"),
    OFFLINE_QR("QR offline · ustawienia"),
    OPORD_ONLY("OPORD")
}

enum class SignatureState { VERIFIED, UNSIGNED, INVALID }

data class SignatureInfo(val state: SignatureState, val fingerprint: String?, val detail: String) {
    val shortFingerprint: String
        get() = fingerprint?.take(16)?.chunked(4)?.joinToString(" ").orEmpty()

    companion object {
        val UNSIGNED = SignatureInfo(SignatureState.UNSIGNED, null, "Paczka bez podpisu cyfrowego")
    }
}

enum class AppRole(val label: String) { ATAK("ATAK"), MESHTASTIC("Meshtastic"), PLUGIN("Plugin ATAK") }

data class AppRequirement(
    val role: AppRole,
    val displayName: String,
    /** Package name declared by Builder. For v3.0 plugins Builder guesses it from the file name. */
    val packageName: String?,
    val packageNameReliable: Boolean,
    val requirement: VersionRequirement?,
    /** Path of the bundled APK inside the .ftak, if any. */
    val bundledEntry: String?,
    val bundledSize: Long
)

data class ServerInfo(val name: String, val host: String, val cotPort: Int, val type: String) {
    val endpoint: String get() = "$host:$cotPort"
}

data class MeshConfig(
    val channelName: String,
    val pskBase64: String?,
    val channelUrl: String?,
    val region: String?,
    val modemPreset: String?,
    val hopLimit: Int?
) {
    /** Channel link to hand to the Meshtastic app: Builder's link if present, otherwise built here. */
    fun effectiveUrl(): String? = channelUrl?.takeIf { MeshChannel.isChannelUrl(it) }
        ?: if (channelName.isNotBlank() || !pskBase64.isNullOrBlank())
            MeshChannel.buildUrl(channelName, pskBase64, region, modemPreset, hopLimit) else null
}

data class VoiceChannel(val role: String, val band: String, val frequency: String, val tone: String)

data class AtakSetting(val key: String, val type: String, val value: String, val label: String)

data class HubPackage(
    val kind: PackageKind,
    val packageId: String,
    val title: String,
    val eventName: String,
    val faction: String,
    val factionColor: String?,
    val version: String,
    val createdUtc: String?,
    /** ISO-8601, null = never expires. */
    val expiresUtc: String?,
    val signature: SignatureInfo,
    val server: ServerInfo?,
    val enrollmentUri: String?,
    val mesh: MeshConfig?,
    val emergencyPhone: String?,
    val voiceChannels: List<VoiceChannel>,
    val requirements: List<AppRequirement>,
    /** Zip entries ATAK should import: data/mission packages (.zip) and preference files (.pref). */
    val atakImports: List<String>,
    /** .kml / .kmz / .gpx overlays. */
    val overlays: List<String>,
    /** .mbtiles / .sqlite offline maps. */
    val mapLayers: List<String>,
    /** .p12 certificates (imported by ATAK through data packages). */
    val certificates: List<String>,
    val opord: Opord?,
    val opordHtmlEntry: String?,
    val atakSettings: List<AtakSetting>,
    val atakTarget: VersionRequirement?,
    val notes: List<String>
) {
    val hasDeployment: Boolean
        get() = kind != PackageKind.OPORD_ONLY

    val hasOpord: Boolean get() = opord != null && opord.days.isNotEmpty()

    fun expiresAtMillis(): Long? = expiresUtc?.let { Iso.parseMillis(it) }

    fun isExpired(now: Long = System.currentTimeMillis()): Boolean = expiresAtMillis()?.let { it < now } ?: false
}

class FtakException(message: String) : Exception(message)

object Iso {
    fun parseMillis(text: String): Long? {
        val t = text.trim()
        if (t.isEmpty()) return null
        return try {
            java.time.OffsetDateTime.parse(t).toInstant().toEpochMilli()
        } catch (e: Exception) {
            try {
                java.time.LocalDateTime.parse(t).toInstant(java.time.ZoneOffset.UTC).toEpochMilli()
            } catch (e2: Exception) {
                null
            }
        }
    }

    /** Builder writes "never expires" as now + 100 years. */
    fun isEffectivelyNever(text: String?): Boolean {
        val millis = text?.let { parseMillis(it) } ?: return true
        return millis - System.currentTimeMillis() > 50L * 365 * 24 * 3600 * 1000
    }
}
