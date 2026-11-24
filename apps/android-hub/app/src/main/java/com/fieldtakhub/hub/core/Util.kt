package com.fieldtakhub.hub.core

import java.io.InputStream
import java.security.MessageDigest
import java.text.Normalizer
import java.util.Base64

object Hex {
    private const val DIGITS = "0123456789abcdef"

    fun encode(bytes: ByteArray): String {
        val sb = StringBuilder(bytes.size * 2)
        for (b in bytes) {
            val v = b.toInt() and 0xFF
            sb.append(DIGITS[v ushr 4]).append(DIGITS[v and 0x0F])
        }
        return sb.toString()
    }

    fun isHex(s: String): Boolean = s.isNotEmpty() && s.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }
}

object Codec {
    fun sha256(bytes: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(bytes)

    fun sha256Hex(input: InputStream): String {
        val md = MessageDigest.getInstance("SHA-256")
        val buf = ByteArray(64 * 1024)
        while (true) {
            val n = input.read(buf)
            if (n < 0) break
            md.update(buf, 0, n)
        }
        return Hex.encode(md.digest())
    }

    /** Accepts standard and URL-safe Base64, with or without padding and whitespace. */
    fun base64Decode(text: String?): ByteArray? {
        if (text == null) return null
        val clean = text.trim().replace("\\s".toRegex(), "").replace('-', '+').replace('_', '/')
        if (clean.isEmpty()) return null
        val padded = when (clean.length % 4) {
            2 -> "$clean=="
            3 -> "$clean="
            1 -> return null
            else -> clean
        }
        return try { Base64.getDecoder().decode(padded) } catch (e: IllegalArgumentException) { null }
    }

    fun base64Encode(bytes: ByteArray): String = Base64.getEncoder().encodeToString(bytes)

    fun base64UrlNoPad(bytes: ByteArray): String = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)

    private const val UNRESERVED = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~"

    /** RFC 3986 percent-encoding (spaces become %20, never '+'). */
    fun percentEncode(value: String): String {
        val sb = StringBuilder()
        for (b in value.toByteArray(Charsets.UTF_8)) {
            val c = (b.toInt() and 0xFF).toChar()
            if (c in UNRESERVED) sb.append(c) else sb.append('%').append(String.format("%02X", b.toInt() and 0xFF))
        }
        return sb.toString()
    }

    fun percentDecode(value: String, plusIsSpace: Boolean = false): String {
        val out = java.io.ByteArrayOutputStream()
        var i = 0
        var plainStart = 0
        fun flushPlain(end: Int) {
            if (end > plainStart) {
                var chunk = value.substring(plainStart, end)
                if (plusIsSpace) chunk = chunk.replace('+', ' ')
                out.write(chunk.toByteArray(Charsets.UTF_8))
            }
        }
        while (i < value.length) {
            if (value[i] == '%' && i + 2 < value.length) {
                val v = value.substring(i + 1, i + 3).toIntOrNull(16)
                if (v != null) {
                    flushPlain(i)
                    out.write(v)
                    i += 3
                    plainStart = i
                    continue
                }
            }
            i++
        }
        flushPlain(value.length)
        return String(out.toByteArray(), Charsets.UTF_8)
    }

    /** Parses "a=1&b=2" into a map with lower-case keys. The last value wins. */
    fun parseQuery(query: String?): Map<String, String> {
        if (query.isNullOrEmpty()) return emptyMap()
        val map = LinkedHashMap<String, String>()
        for (part in query.split('&')) {
            if (part.isEmpty()) continue
            val eq = part.indexOf('=')
            val k = if (eq >= 0) part.substring(0, eq) else part
            val v = if (eq >= 0) part.substring(eq + 1) else ""
            map[percentDecode(k, true).lowercase()] = percentDecode(v, true)
        }
        return map
    }
}

object TextFold {
    /** "Żbik Łoś-2" -> "Zbik Los-2". */
    fun ascii(text: String): String {
        val replaced = text.replace('ł', 'l').replace('Ł', 'L').replace('đ', 'd').replace('Đ', 'D').replace('ø', 'o').replace('Ø', 'O')
        val nfd = Normalizer.normalize(replaced, Normalizer.Form.NFD)
        return nfd.replace("\\p{Mn}+".toRegex(), "")
    }
}

object NetRules {
    private val ipv4 = Regex("^(\\d{1,3})\\.(\\d{1,3})\\.(\\d{1,3})\\.(\\d{1,3})$")

    fun ipv4Octets(host: String): IntArray? {
        val m = ipv4.matchEntire(host) ?: return null
        val o = IntArray(4) { m.groupValues[it + 1].toInt() }
        return if (o.all { it in 0..255 }) o else null
    }

    /** Private, link-local or loopback IPv4 literal, or an mDNS .local name. */
    fun isPrivateHost(host: String): Boolean {
        val h = host.trim().trim('[', ']').lowercase()
        if (h == "localhost" || h.endsWith(".local")) return true
        val o = ipv4Octets(h) ?: return false
        return o[0] == 10 || o[0] == 127 ||
            (o[0] == 172 && o[1] in 16..31) ||
            (o[0] == 192 && o[1] == 168) ||
            (o[0] == 169 && o[1] == 254) ||
            (o[0] == 100 && o[1] in 64..127)
    }

    /**
     * Same rule as Builder's LAN server: HTTPS anywhere, plain HTTP only inside a private network.
     * Returns null when the URL may be downloaded, otherwise a message for the user.
     */
    fun downloadUrlProblem(url: String): String? {
        val u = try { java.net.URI(url.trim()) } catch (e: Exception) { return "Nieprawidłowy adres: $url" }
        val scheme = u.scheme?.lowercase()
        val host = u.host ?: return "Adres nie zawiera nazwy serwera: $url"
        return when (scheme) {
            "https" -> null
            "http" -> if (isPrivateHost(host)) null else
                "Niezaszyfrowany adres http:// jest dozwolony tylko w sieci lokalnej (np. 192.168.x.x). Ten wskazuje na Internet: $host"
            else -> "Nieobsługiwany adres: $url"
        }
    }

    /** Google Drive share link -> direct download, exactly like Builder's CloudDistributionService. */
    fun normalizeCloudUrl(url: String): String {
        val u = try { java.net.URI(url.trim()) } catch (e: Exception) { return url }
        val host = u.host?.lowercase() ?: return url
        if (host == "drive.google.com" || host.endsWith(".drive.google.com")) {
            val path = u.rawPath.orEmpty()
            val q = Codec.parseQuery(u.rawQuery)
            val id = Regex("/file/d/([^/]+)").find(path)?.groupValues?.get(1)?.let { Codec.percentDecode(it) } ?: q["id"]
            if (!id.isNullOrBlank() && !path.contains("/folders/")) {
                var direct = "https://drive.usercontent.google.com/download?id=" + Codec.percentEncode(id) + "&export=download&confirm=t"
                q["resourcekey"]?.takeIf { it.isNotBlank() }?.let { direct += "&resourcekey=" + Codec.percentEncode(it) }
                return direct
            }
        }
        return url.trim()
    }
}

/** A short, safe file or folder name. */
fun safeName(text: String, fallback: String = "paczka"): String {
    val folded = TextFold.ascii(text).replace("[^A-Za-z0-9._-]+".toRegex(), "_").trim('_', '.')
    return folded.take(60).ifEmpty { fallback }
}

fun humanBytes(bytes: Long): String {
    if (bytes < 1024) return "$bytes B"
    val kb = bytes / 1024.0
    if (kb < 1024) return String.format(java.util.Locale.ROOT, "%.0f KB", kb)
    val mb = kb / 1024.0
    if (mb < 1024) return String.format(java.util.Locale.ROOT, "%.1f MB", mb)
    return String.format(java.util.Locale.ROOT, "%.2f GB", mb / 1024.0)
}
