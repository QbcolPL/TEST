package org.fieldtak.hub.util
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream
import kotlin.math.max
data class ConfigDiffEntry(val parameter: String, val currentValue: String, val targetValue: String, val changed: Boolean)
data class AtakMeshtasticPlan(
    val packageName: String, val detectedHardware: String, val targetRole: String,
    val channelName: String, val channelUrl: String, val targetRegion: String,
    val targetHopLimit: Int, val targetModemPreset: String, val targetTxPowerDbm: Int,
    val rxBoostedGain: Boolean, val targetPositionSec: Int, val smartMinDistM: Int, val smartMinIntervalSec: Int,
    val atakStrategy: String, val atakConstantReliableSec: Int, val atakConstantUnreliableSec: Int,
    val atakDynMinSec: Int, val atakDynMaxSec: Int, val atakDynStationaryReliableSec: Int, val atakDynStationaryUnreliableSec: Int,
    val tcpConnectTimeoutSec: Int, val udpNoDataTimeoutSec: Int, val multicastTtl: Int, val otsHost: String,
    val presetName: String, val fwMin: String, val fwMax: String, val diffs: List<ConfigDiffEntry>
)
object AtakMeshtasticConfigManager {
    @JvmStatic fun detectConnectedRadioHardware(context: Context): String {
        return try {
            val btm = context.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
            @Suppress("MissingPermission")
            val names = btm?.adapter?.bondedDevices?.mapNotNull { it.name?.uppercase() } ?: emptyList()
            when {
                names.any { it.contains("SUPREME") } -> "T_BEAM_SUPREME"
                names.any { if (it.contains("X1")) true else it.contains("TRACKER") } -> "MESH_TRACKER_X1"
                else -> "T_BEAM"
            }
        } catch (_: Exception) { "T_BEAM" }
    }
    @JvmStatic fun checkFirmwareVersion(appVersion: String, fwMin: String, fwMax: String): Boolean {
        if (fwMin.isBlank()) return true
        try {
            val cleanApp = appVersion.substringBefore("-").substringBefore(" ")
            val cParts = cleanApp.split(".").map { it.toIntOrNull() ?: 0 }
            val mParts = fwMin.split(".").map { it.toIntOrNull() ?: 0 }
            for (i in 0 until max(cParts.size, mParts.size)) {
                val c = cParts.getOrElse(i) { 0 }
                val m = mParts.getOrElse(i) { 0 }
                if (c > m) return true
                if (c < m) return false
            }
            return true
        } catch (_: Exception) { return true }
    }
    @JvmStatic fun buildConfigurationPlan(context: Context, overrideHw: String? = null, presetKey: String = "SHORT_FAST_DEFAULT"): AtakMeshtasticPlan {
        val hw = overrideHw ?: detectConnectedRadioHardware(context)
        var pkgTitle = "Czysty Profil (SHORT_FAST)"
        val role = if (hw.contains("X1")) "TAK_TRACKER" else "TAK"
        var chName = ""; var chUrl = ""; var region = "EU_868"; var hop = 3; var preset = "SHORT_FAST"; var txPower = 27; var rxBoost = true
        var posSec = 300; var smartDist = 30; var smartInt = 15
        var strategy = "Constant"; var constRel = 0; var constUnrel = 1; var dynMin = 20; var dynMax = 2; var dynStatRel = 180; var dynStatUnrel = 30
        var tcpTimeout = 20; var udpTimeout = 30; var ttl = 64; var host = ""
        var pName = "Domyslny SHORT_FAST (0s/1s)"; var fwMin = ""; var fwMax = ""
        when (presetKey.uppercase()) {
            "STRZELANKA" -> { pName = "Niedzielna Strzelanka (SHORT_FAST)"; preset = "SHORT_FAST"; hop = 3; strategy = "Constant"; constRel = 2; constUnrel = 8; smartDist = 30; smartInt = 15; posSec = 300 }
            "CQB" -> { pName = "Trening / CQB (SHORT_FAST)"; preset = "SHORT_FAST"; hop = 2; strategy = "Constant"; constRel = 1; constUnrel = 3; smartDist = 15; smartInt = 10; posSec = 120 }
            "MILSIM" -> { pName = "Milsim 24h+ (SHORT_FAST)"; preset = "SHORT_FAST"; hop = 3; strategy = "Dynamic"; constRel = 10; constUnrel = 20; dynMin = 20; dynMax = 5; dynStatRel = 180; dynStatUnrel = 60; smartDist = 50; smartInt = 30; posSec = 600 }
            "LOWBAND" -> { pName = "Low Bandwidth / Off-Grid"; preset = "SHORT_FAST"; hop = 4; strategy = "Dynamic"; constRel = 30; constUnrel = 30; dynMin = 45; dynMax = 15; dynStatRel = 300; dynStatUnrel = 120; smartDist = 100; smartInt = 45; posSec = 900 }
        }
        val ftak = FtakPackageLibraryManager.listAllPackages(context).firstOrNull { !it.isPartBuffer && it.isActive }?.file
        if (ftak != null && presetKey == "SHORT_FAST_DEFAULT") {
            pkgTitle = ftak.name
            try {
                ZipFile(ftak).use { zip ->
                    val entry = zip.getEntry("MESHTASTIC/tactical_config.json") ?: zip.getEntry("tactical_config.json")
                    if (entry != null) {
                        val root = JSONObject(zip.getInputStream(entry).bufferedReader().readText())
                        pName = root.optString("presetName", pName)
                        root.optJSONObject("atak")?.let { a ->
                            host = a.optString("otsHost", ""); strategy = a.optString("locationReportingStrategy", strategy)
                            constRel = a.optInt("constantReportingRateReliable", constRel); constUnrel = a.optInt("constantReportingRateUnreliable", constUnrel)
                            dynMin = a.optInt("dynamicReportingRateMinReliable", dynMin); dynMax = a.optInt("dynamicReportingRateMaxReliable", dynMax)
                            dynStatRel = a.optInt("dynamicReportingRateStationaryReliable", dynStatRel); dynStatUnrel = a.optInt("dynamicReportingRateStationaryUnreliable", dynStatUnrel)
                            tcpTimeout = a.optInt("tcpConnectTimeout", tcpTimeout); udpTimeout = a.optInt("udpNoDataTimeout", udpTimeout); ttl = a.optInt("multicastTTL", ttl)
                        }
                        root.optJSONObject("meshtastic")?.let { m ->
                            chName = m.optString("channelName", ""); chUrl = m.optString("channelUrl", "")
                            region = m.optString("region", region); preset = m.optString("modemPreset", "SHORT_FAST")
                            hop = m.optInt("hopLimit", hop); txPower = m.optInt("txPowerDbm", txPower)
                            rxBoost = m.optBoolean("sx126xRxBoostedGain", rxBoost); posSec = m.optInt("positionBroadcastSecs", posSec)
                            smartDist = m.optInt("smartMinimumDistance", smartDist); smartInt = m.optInt("smartMinimumIntervalSecs", smartInt)
                            fwMin = m.optString("firmwareMin", ""); fwMax = m.optString("firmwareMax", "")
                        }
                    }
                }
            } catch (_: Exception) {}
        }
        val diffs = mutableListOf(
            ConfigDiffEntry("Preset", "Lokalny", pName, true),
            ConfigDiffEntry("Rola ($hw)", "CLIENT", role, true),
            ConfigDiffEntry("Modem / Region", "Domyslny", "$preset / $region (Hop=$hop, ${txPower}dBm)", true),
            ConfigDiffEntry("ATAK Strategia PLI", "Lokalna", "$strategy (Rel=${constRel}s, Unrel=${constUnrel}s)", true)
        )
        if (fwMin.isNotBlank()) diffs.add(0, ConfigDiffEntry("Firmware Radia", "Weryfikacja", "Wymagane MIN: $fwMin", true))
        return AtakMeshtasticPlan(pkgTitle, hw, role, chName, chUrl, region, hop, preset, txPower, rxBoost, posSec, smartDist, smartInt, strategy, constRel, constUnrel, dynMin, dynMax, dynStatRel, dynStatUnrel, tcpTimeout, udpTimeout, ttl, host, pName, fwMin, fwMax, diffs)
    }
    @JvmStatic fun applyMeshtasticRadioConfigViaApp(context: Context, plan: AtakMeshtasticPlan): String {
        val dir = File(context.getExternalFilesDir(null) ?: context.filesDir, "meshtastic_config").apply { mkdirs() }
        val json = JSONObject().apply {
            put("profileVersion", 2); put("hardwareModel", plan.detectedHardware); put("role", plan.targetRole)
            put("region", plan.targetRegion); put("hopLimit", plan.targetHopLimit); put("modemPreset", plan.targetModemPreset)
            put("txPowerDbm", plan.targetTxPowerDbm); put("sx126xRxBoostedGain", plan.rxBoostedGain)
            put("positionBroadcastSecs", plan.targetPositionSec); put("smartMinimumDistance", plan.smartMinDistM); put("smartMinimumIntervalSecs", plan.smartMinIntervalSec)
            if (plan.channelName.isNotBlank()) put("channelName", plan.channelName)
            if (plan.channelUrl.isNotBlank()) put("channelUrl", plan.channelUrl)
            put("copyDeviceIdentity", false); put("copyPrivateKeys", false)
        }
        File(dir, "meshtastic-profile-2.4.json").writeText(json.toString(2), Charsets.UTF_8)
        if (!ApkInstallerManager.isMeshtasticInstalled(context)) return "Zainstaluj najpierw aplikacje Meshtastic!"
        return try {
            if (plan.channelUrl.startsWith("http", true)) {
                context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(plan.channelUrl)).apply { setPackage("com.geeksville.mesh"); addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) })
                "Przekazano profil (${plan.targetModemPreset}) i kanal do Meshtastic!"
            } else {
                context.packageManager.getLaunchIntentForPackage("com.geeksville.mesh")?.let { it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK); context.startActivity(it) }
                "Zapisano czysty profil (${plan.targetModemPreset}, Hop=${plan.targetHopLimit}) i otwarto Meshtastic."
            }
        } catch (_: Exception) { "Zapisano profil Meshtastic." }
    }
    @JvmStatic fun applyAtakCivConfigPackage(context: Context, plan: AtakMeshtasticPlan): String {
        val dir = File(context.getExternalFilesDir(null) ?: context.filesDir, "atak_config").apply { mkdirs() }
        val prefXml = buildString {
            appendLine("<?xml version=\"1.0\" standalone=\"yes\"?>")
            appendLine("<preferences>")
            appendLine("  <preference version=\"1\" name=\"com.atakmap.app.civ_preferences\">")
            appendLine("    <entry key=\"pref_import_pref_action\" class=\"class java.lang.String\">ALLOW</entry>")
            appendLine("    <entry key=\"locationReportingStrategy\" class=\"class java.lang.String\">${plan.atakStrategy}</entry>")
            appendLine("    <entry key=\"constantReportingRateReliable\" class=\"class java.lang.String\">${plan.atakConstantReliableSec}</entry>")
            appendLine("    <entry key=\"constantReportingRateUnreliable\" class=\"class java.lang.String\">${plan.atakConstantUnreliableSec}</entry>")
            appendLine("    <entry key=\"dynamicReportingRateMinReliable\" class=\"class java.lang.String\">${plan.atakDynMinSec}</entry>")
            appendLine("    <entry key=\"dynamicReportingRateMaxReliable\" class=\"class java.lang.String\">${plan.atakDynMaxSec}</entry>")
            appendLine("    <entry key=\"dynamicReportingRateStationaryReliable\" class=\"class java.lang.String\">${plan.atakDynStationaryReliableSec}</entry>")
            appendLine("    <entry key=\"dynamicReportingRateMinUnreliable\" class=\"class java.lang.String\">${plan.atakDynMinSec}</entry>")
            appendLine("    <entry key=\"dynamicReportingRateMaxUnreliable\" class=\"class java.lang.String\">${plan.atakDynMaxSec}</entry>")
            appendLine("    <entry key=\"dynamicReportingRateStationaryUnreliable\" class=\"class java.lang.String\">${plan.atakDynStationaryUnreliableSec}</entry>")
            appendLine("  </preference>")
            appendLine("</preferences>")
        }
        val manifestXml = "<MissionPackageManifest version=\"2\"><Configuration><Parameter name=\"uid\" value=\"fth-atak-clean-pli-24\"/><Parameter name=\"name\" value=\"FieldTAKHub_ATAK_Config_2.4.zip\"/><Parameter name=\"onReceiveImport\" value=\"true\"/></Configuration><Contents><Content ignore=\"false\" zipEntry=\"config.pref\"/></Contents></MissionPackageManifest>"
        val zipFile = File(dir, "FieldTAKHub_ATAK_Config_2.4.zip")
        ZipOutputStream(FileOutputStream(zipFile)).use { zos ->
            zos.putNextEntry(ZipEntry("MANIFEST/manifest.xml")); zos.write(manifestXml.toByteArray(Charsets.UTF_8)); zos.closeEntry()
            zos.putNextEntry(ZipEntry("config.pref")); zos.write(prefXml.toByteArray(Charsets.UTF_8)); zos.closeEntry()
        }
        if (!ApkInstallerManager.isAtakInstalled(context)) return "ATAK-CIV nie jest zainstalowany!"
        return try {
            val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", zipFile)
            context.startActivity(Intent(Intent.ACTION_VIEW).apply { setDataAndType(uri, "application/zip"); setPackage("com.atakmap.app.civ"); addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK) })
            "Wgrano czysty profil raportowania PLI (${plan.presetName}) do ATAK-CIV!"
        } catch (_: Exception) { "Zapisano ${zipFile.name}." }
    }
}