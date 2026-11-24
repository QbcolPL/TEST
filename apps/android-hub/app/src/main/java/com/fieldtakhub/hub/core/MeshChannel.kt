package com.fieldtakhub.hub.core

import java.io.ByteArrayOutputStream

/**
 * Meshtastic channel link (https://meshtastic.org/e/#...) = base64url of the ChannelSet protobuf:
 *   ChannelSet { repeated ChannelSettings settings = 1; LoRaConfig lora_config = 2; }
 *   ChannelSettings { bytes psk = 2; string name = 3; bool uplink_enabled = 5; bool downlink_enabled = 6; }
 *   LoRaConfig { bool use_preset = 1; ModemPreset modem_preset = 2; RegionCode region = 7;
 *                uint32 hop_limit = 8; bool tx_enabled = 9; }
 * Encoding matches Builder's BuildMeshtasticChannelSetUrl.
 */
data class MeshChannelSettings(
    val name: String,
    val psk: ByteArray,
    val uplink: Boolean,
    val downlink: Boolean
)

data class MeshLora(
    val usePreset: Boolean,
    val modemPreset: Int,
    val region: Int,
    val hopLimit: Int,
    val txEnabled: Boolean
)

data class MeshChannelSet(val channels: List<MeshChannelSettings>, val lora: MeshLora?) {
    val primary: MeshChannelSettings? get() = channels.firstOrNull()
}

object MeshChannel {
    val REGIONS: Map<Int, String> = mapOf(
        0 to "UNSET", 1 to "US", 2 to "EU_433", 3 to "EU_868", 4 to "CN", 5 to "JP", 6 to "ANZ", 7 to "KR",
        8 to "TW", 9 to "RU", 10 to "IN", 11 to "NZ_865", 12 to "TH", 13 to "LORA_24", 14 to "UA_433",
        15 to "UA_868", 16 to "MY_433", 17 to "MY_919", 18 to "SG_923", 19 to "PH_433", 20 to "PH_868",
        21 to "PH_915", 22 to "ANZ_433", 23 to "KZ_433", 24 to "KZ_863", 25 to "NP_865", 26 to "BR_902"
    )

    val PRESETS: Map<Int, String> = mapOf(
        0 to "LONG_FAST", 1 to "LONG_SLOW", 2 to "VERY_LONG_SLOW", 3 to "MEDIUM_SLOW", 4 to "MEDIUM_FAST",
        5 to "SHORT_SLOW", 6 to "SHORT_FAST", 7 to "LONG_MODERATE", 8 to "SHORT_TURBO"
    )

    fun regionCode(name: String?): Int =
        REGIONS.entries.firstOrNull { it.value.equals(name?.trim(), ignoreCase = true) }?.key ?: 3

    fun presetCode(name: String?): Int =
        PRESETS.entries.firstOrNull { it.value.equals(name?.trim(), ignoreCase = true) }?.key ?: 6

    fun isChannelUrl(text: String): Boolean {
        val t = text.trim().lowercase()
        return t.startsWith("https://meshtastic.org/e/") && t.contains('#')
    }

    fun decodeUrl(url: String): MeshChannelSet? {
        if (!isChannelUrl(url)) return null
        val bytes = Codec.base64Decode(url.substringAfter('#')) ?: return null
        return try { decode(bytes) } catch (e: Exception) { null }
    }

    fun decode(bytes: ByteArray): MeshChannelSet {
        val channels = ArrayList<MeshChannelSettings>()
        var lora: MeshLora? = null
        val r = Reader(bytes)
        while (!r.done()) {
            val (field, wire) = r.tag()
            when {
                field == 1 && wire == 2 -> channels += decodeSettings(r.bytes())
                field == 2 && wire == 2 -> lora = decodeLora(r.bytes())
                else -> r.skip(wire)
            }
        }
        return MeshChannelSet(channels, lora)
    }

    private fun decodeSettings(bytes: ByteArray): MeshChannelSettings {
        var psk = ByteArray(0)
        var name = ""
        var up = false
        var down = false
        val r = Reader(bytes)
        while (!r.done()) {
            val (field, wire) = r.tag()
            when {
                field == 2 && wire == 2 -> psk = r.bytes()
                field == 3 && wire == 2 -> name = String(r.bytes(), Charsets.UTF_8)
                field == 5 && wire == 0 -> up = r.varint() != 0L
                field == 6 && wire == 0 -> down = r.varint() != 0L
                else -> r.skip(wire)
            }
        }
        return MeshChannelSettings(name, psk, up, down)
    }

    private fun decodeLora(bytes: ByteArray): MeshLora {
        var usePreset = false
        var preset = 0
        var region = 0
        var hop = 3
        var tx = false
        val r = Reader(bytes)
        while (!r.done()) {
            val (field, wire) = r.tag()
            when {
                field == 1 && wire == 0 -> usePreset = r.varint() != 0L
                field == 2 && wire == 0 -> preset = r.varint().toInt()
                field == 7 && wire == 0 -> region = r.varint().toInt()
                field == 8 && wire == 0 -> hop = r.varint().toInt()
                field == 9 && wire == 0 -> tx = r.varint() != 0L
                else -> r.skip(wire)
            }
        }
        return MeshLora(usePreset, preset, region, hop, tx)
    }

    /** Same bytes as Builder for the same inputs. Uplink/downlink are off unless asked. */
    fun buildUrl(
        channelName: String,
        pskBase64: String?,
        region: String?,
        preset: String?,
        hopLimit: Int?,
        mqttUplink: Boolean = false
    ): String {
        val psk = Codec.base64Decode(pskBase64)?.takeIf { it.isNotEmpty() } ?: byteArrayOf(1)
        val name = channelName.trim().let { if (it.length > 11) it.substring(0, 11) else it }
        val settings = ByteArrayOutputStream()
        writeBytes(settings, 2, psk)
        if (name.isNotEmpty()) writeBytes(settings, 3, name.toByteArray(Charsets.UTF_8))
        if (mqttUplink) {
            writeVarintField(settings, 5, 1)
            writeVarintField(settings, 6, 1)
        }
        val lora = ByteArrayOutputStream()
        writeVarintField(lora, 1, 1)
        val p = presetCode(preset)
        if (p != 0) writeVarintField(lora, 2, p.toLong())
        writeVarintField(lora, 7, regionCode(region).toLong())
        writeVarintField(lora, 8, (hopLimit ?: 3).coerceIn(1, 7).toLong())
        writeVarintField(lora, 9, 1)
        val set = ByteArrayOutputStream()
        writeBytes(set, 1, settings.toByteArray())
        writeBytes(set, 2, lora.toByteArray())
        return "https://meshtastic.org/e/#" + Codec.base64UrlNoPad(set.toByteArray())
    }

    /** Human description of the channel key, with a warning for public default keys. */
    fun describePsk(psk: ByteArray): String = when {
        psk.isEmpty() || (psk.size == 1 && psk[0].toInt() == 0) -> "brak szyfrowania"
        psk.size == 1 && psk[0].toInt() == 1 -> "domyślny klucz publiczny (każdy może czytać)"
        psk.size == 1 -> "prosty klucz publiczny #${psk[0].toInt() and 0xFF}"
        psk.size == 16 -> "AES-128 (prywatny)"
        psk.size == 32 -> "AES-256 (prywatny)"
        else -> "nietypowy klucz (${psk.size} B)"
    }

    fun isPrivatePsk(psk: ByteArray): Boolean = psk.size == 16 || psk.size == 32

    private fun writeVarint(out: ByteArrayOutputStream, value: Long) {
        var v = value
        while (v and 0x7FL.inv() != 0L) {
            out.write(((v and 0x7F) or 0x80).toInt())
            v = v ushr 7
        }
        out.write(v.toInt())
    }

    private fun writeVarintField(out: ByteArrayOutputStream, field: Int, value: Long) {
        writeVarint(out, (field shl 3).toLong())
        writeVarint(out, value)
    }

    private fun writeBytes(out: ByteArrayOutputStream, field: Int, data: ByteArray) {
        writeVarint(out, ((field shl 3) or 2).toLong())
        writeVarint(out, data.size.toLong())
        out.write(data)
    }

    private class Reader(private val b: ByteArray) {
        private var pos = 0
        fun done() = pos >= b.size

        fun varint(): Long {
            var shift = 0
            var result = 0L
            while (true) {
                if (pos >= b.size) throw IllegalArgumentException("truncated varint")
                val x = b[pos++].toInt() and 0xFF
                result = result or ((x and 0x7F).toLong() shl shift)
                if (x and 0x80 == 0) return result
                shift += 7
                if (shift > 63) throw IllegalArgumentException("varint too long")
            }
        }

        fun tag(): Pair<Int, Int> {
            val t = varint()
            return Pair((t ushr 3).toInt(), (t and 7).toInt())
        }

        fun bytes(): ByteArray {
            val len = varint().toInt()
            if (len < 0 || pos + len > b.size) throw IllegalArgumentException("bad length")
            val out = b.copyOfRange(pos, pos + len)
            pos += len
            return out
        }

        fun skip(wire: Int) {
            when (wire) {
                0 -> varint()
                1 -> pos += 8
                2 -> bytes()
                5 -> pos += 4
                else -> throw IllegalArgumentException("unsupported wire type $wire")
            }
        }
    }
}
