package com.fieldtak.mobile.data

import java.io.File

data class RequiredApkItem(
    val role: String,             // "ATAK-CIV", "MESHTASTIC", "PLUGIN"
    val displayName: String,
    val packageName: String,      // np. "com.atakmap.app.civ", "com.geeksville.mesh"
    val requiredVersion: String,  // np. "5.2.0"
    val bundledZipPath: String    // sciezka wewnatrz archiwum .ftak, np. "apk/ATAK.apk"
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
    val hasEmbeddedOpordHtml: Boolean
) {
    val formattedSize: String
        get() {
            val mb = sizeBytes.toDouble() / (1024.0 * 1024.0)
            return if (mb >= 1.0) String.format("%.1f MB", mb)
            else String.format("%.0f KB", sizeBytes.toDouble() / 1024.0)
        }
}