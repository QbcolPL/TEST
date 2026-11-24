package com.fieldtakhub.hub.core

data class WifiCredentials(val ssid: String, val password: String?, val security: String?)

/** Everything a Field TAK QR code (or pasted link) can mean. */
sealed class QrPayload {
    /** Builder LAN distribution: fieldtak://provision?url=http://IP:8765/d/TOKEN.json */
    data class Descriptor(val url: String) : QrPayload()

    /** Builder cloud QR, or a phone sharing over its hotspot. */
    data class Download(
        val url: String,
        val sha256: String?,
        val bytes: Long?,
        val label: String?,
        val expiresUtc: String?,
        val wifi: WifiCredentials?
    ) : QrPayload()

    /** Builder v3.0 "OPORD (LAN)" link: http://IP:8766/opord/FACTION.html */
    data class OpordLink(val jsonUrl: String, val htmlUrl: String) : QrPayload()

    /** Builder v3.0 "QR offline (JSON)". */
    data class OfflineConfig(val json: String) : QrPayload()

    /** Builder v2.4 poster code: ftak://provision?event=&faction=&channel=&sha256= (no download address). */
    data class LegacyPoster(val event: String?, val faction: String?, val channel: String?, val shaPrefix: String?) : QrPayload()

    data class AtakEnroll(val uri: String) : QrPayload()

    data class MeshtasticChannel(val url: String) : QrPayload()

    data class Unsupported(val text: String, val reason: String) : QrPayload()
}

object QrParser {
    fun parse(raw: String): QrPayload {
        val text = raw.trim().removePrefix("﻿")
        if (text.isEmpty()) return QrPayload.Unsupported(raw, "Pusty kod.")
        if (text.startsWith("{")) return QrPayload.OfflineConfig(text)
        val lower = text.lowercase()

        if (lower.startsWith("fieldtak://") || lower.startsWith("ftak://")) {
            val afterScheme = text.substringAfter("://")
            val action = afterScheme.substringBefore('?').trim('/').lowercase()
            val q = Codec.parseQuery(afterScheme.substringAfter('?', "").substringBefore('#'))
            if (action == "provision" || action == "p" || action.isEmpty()) {
                q["url"]?.takeIf { it.isNotBlank() }?.let { return QrPayload.Descriptor(it) }
                val pkg = q["packageurl"] ?: q["u"]
                if (!pkg.isNullOrBlank()) {
                    val ssid = q["wifissid"] ?: q["w"]
                    return QrPayload.Download(
                        url = NetRules.normalizeCloudUrl(pkg),
                        sha256 = (q["sha256"] ?: q["h"])?.takeIf { Hex.isHex(it) && it.length == 64 }?.lowercase(),
                        bytes = (q["packagebytes"] ?: q["n"])?.toLongOrNull(),
                        label = q["label"] ?: q["l"],
                        expiresUtc = q["expiresutc"] ?: q["x"],
                        wifi = ssid?.takeIf { it.isNotBlank() }?.let { WifiCredentials(it, q["wifipass"] ?: q["wp"], q["wifisec"] ?: q["ws"]) }
                    )
                }
                if (q.containsKey("event") || q.containsKey("faction")) {
                    return QrPayload.LegacyPoster(q["event"], q["faction"], q["channel"], q["sha256"]?.lowercase())
                }
            }
            return QrPayload.Unsupported(text, "Nieznany link Field TAK.")
        }

        if (Atak.isEnrollUri(text)) return QrPayload.AtakEnroll(text)
        if (MeshChannel.isChannelUrl(text)) return QrPayload.MeshtasticChannel(text)

        if (lower.startsWith("http://") || lower.startsWith("https://")) {
            val path = text.substringAfter("://").substringAfter('/', "").substringBefore('?').substringBefore('#').lowercase()
            if (path.startsWith("opord/") || path.contains("/opord/")) {
                val base = text.substringBefore('?').substringBefore('#')
                val json = when {
                    base.endsWith(".html", true) -> base.dropLast(5) + ".json"
                    base.endsWith(".json", true) -> base
                    else -> "$base.json"
                }
                val html = if (json.endsWith(".json", true)) json.dropLast(5) + ".html" else base
                return QrPayload.OpordLink(json, html)
            }
            if (path.endsWith(".json") && path.contains("d/")) return QrPayload.Descriptor(text)
            return QrPayload.Download(NetRules.normalizeCloudUrl(text), null, null, null, null, null)
        }
        return QrPayload.Unsupported(text, "Ten kod nie dotyczy Field TAK.")
    }

    /** Hotspot share code produced by this app (also readable by future Builder versions). */
    fun buildShareLink(
        packageUrl: String,
        sha256: String,
        bytes: Long,
        label: String,
        wifi: WifiCredentials?
    ): String {
        val sb = StringBuilder("fieldtak://provision?packageUrl=").append(Codec.percentEncode(packageUrl))
        sb.append("&sha256=").append(sha256)
        sb.append("&packageBytes=").append(bytes)
        sb.append("&label=").append(Codec.percentEncode(label))
        if (wifi != null) {
            sb.append("&wifiSsid=").append(Codec.percentEncode(wifi.ssid))
            wifi.password?.let { sb.append("&wifiPass=").append(Codec.percentEncode(it)) }
            wifi.security?.let { sb.append("&wifiSec=").append(Codec.percentEncode(it)) }
        }
        return sb.toString()
    }
}

/** Builder LAN descriptor JSON (schema fieldtak.provision v1). */
object ProvisionDescriptor {
    fun parse(text: String): QrPayload.Download {
        val o = try { Json.parseObject(text) } catch (e: JsonException) { throw FtakException("Serwer Buildera zwrócił niepoprawny opis paczki.") }
        if (!o.text("schema").equals("fieldtak.provision", true)) throw FtakException("To nie jest opis paczki Field TAK (schema=${o.text("schema")}).")
        val url = o.text("packageUrl").ifBlank { throw FtakException("Opis paczki nie zawiera adresu pliku.") }
        return QrPayload.Download(
            url = url,
            sha256 = o.text("packageSha256").lowercase().takeIf { it.length == 64 && Hex.isHex(it) },
            bytes = o.long("packageBytes"),
            label = o.text("label").ifBlank { null },
            expiresUtc = o.text("expiresUtc").ifBlank { null },
            wifi = null
        )
    }
}
