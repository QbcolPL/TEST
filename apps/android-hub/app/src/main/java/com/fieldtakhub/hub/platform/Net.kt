package com.fieldtakhub.hub.platform

import android.net.Network
import com.fieldtakhub.hub.core.FtakException
import com.fieldtakhub.hub.core.Hex
import com.fieldtakhub.hub.core.NetRules
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Socket
import java.net.URL
import java.security.MessageDigest

class DownloadResult(val file: File, val sha256: String, val bytes: Long, val fileName: String?)

object Downloader {
    private const val USER_AGENT = "FieldTakHub-Android/1.0"

    private fun open(url: String, network: Network?): HttpURLConnection {
        NetRules.downloadUrlProblem(url)?.let { throw FtakException(it) }
        val u = URL(url)
        val conn = (network?.openConnection(u) ?: u.openConnection()) as HttpURLConnection
        conn.connectTimeout = 15_000
        conn.readTimeout = 30_000
        conn.instanceFollowRedirects = true
        conn.setRequestProperty("User-Agent", USER_AGENT)
        return conn
    }

    suspend fun fetchText(url: String, network: Network? = null, maxBytes: Int = 4 * 1024 * 1024): String =
        withContext(Dispatchers.IO) {
            val conn = open(url, network)
            try {
                val code = conn.responseCode
                if (code !in 200..299) throw FtakException(httpMessage(code, url))
                val bytes = conn.inputStream.use { input ->
                    val out = java.io.ByteArrayOutputStream()
                    val buf = ByteArray(16 * 1024)
                    while (true) {
                        val n = input.read(buf)
                        if (n < 0) break
                        out.write(buf, 0, n)
                        if (out.size() > maxBytes) throw FtakException("Odpowiedź serwera jest za duża.")
                    }
                    out.toByteArray()
                }
                String(bytes, Charsets.UTF_8)
            } catch (e: IOException) {
                throw FtakException(networkMessage(e, url))
            } finally {
                conn.disconnect()
            }
        }

    /**
     * Streams the file to [dest] computing SHA-256 on the way. Rejects HTML pages (e.g. a Google Drive
     * "virus scan" page) and checks [expectedSha256] from the QR code when given.
     */
    suspend fun download(
        url: String,
        dest: File,
        network: Network?,
        expectedSha256: String?,
        expectedBytes: Long?,
        onProgress: (Long, Long?) -> Unit
    ): DownloadResult = withContext(Dispatchers.IO) {
        val conn = open(url, network)
        try {
            val code = conn.responseCode
            if (code !in 200..299) throw FtakException(httpMessage(code, url))
            val type = conn.contentType.orEmpty()
            if (type.contains("text/html", ignoreCase = true)) {
                throw FtakException(
                    if (url.contains("google.com")) "Dysk Google zwrócił stronę zamiast pliku. Plik .ftak musi mieć dostęp „Każdy, kto ma link”."
                    else "Serwer zwrócił stronę WWW zamiast pliku .ftak. Potrzebny jest bezpośredni link do pliku."
                )
            }
            val length = conn.contentLengthLong.takeIf { it > 0 } ?: expectedBytes
            val free = dest.parentFile?.usableSpace ?: Long.MAX_VALUE
            if (length != null && length + 64L * 1024 * 1024 > free) {
                throw FtakException("Za mało miejsca w telefonie: paczka ma ${com.fieldtakhub.hub.core.humanBytes(length)}.")
            }
            val md = MessageDigest.getInstance("SHA-256")
            var total = 0L
            dest.parentFile?.mkdirs()
            conn.inputStream.use { input ->
                dest.outputStream().use { out ->
                    val buf = ByteArray(128 * 1024)
                    var lastReport = 0L
                    while (true) {
                        ensureActive()
                        val n = input.read(buf)
                        if (n < 0) break
                        out.write(buf, 0, n)
                        md.update(buf, 0, n)
                        total += n
                        if (total - lastReport > 256 * 1024) {
                            lastReport = total
                            onProgress(total, length)
                        }
                    }
                }
            }
            onProgress(total, length)
            val sha = Hex.encode(md.digest())
            if (expectedSha256 != null && !expectedSha256.equals(sha, ignoreCase = true)) {
                dest.delete()
                throw FtakException("Suma SHA-256 pobranej paczki nie zgadza się z kodem QR. Plik mógł zostać podmieniony lub uszkodzony.")
            }
            if (expectedBytes != null && expectedBytes > 0 && expectedBytes != total) {
                dest.delete()
                throw FtakException("Pobrano niepełny plik ($total z $expectedBytes bajtów). Spróbuj ponownie.")
            }
            DownloadResult(dest, sha, total, fileName(conn.getHeaderField("Content-Disposition")) ?: url.substringAfterLast('/').substringBefore('?').takeIf { it.isNotBlank() })
        } catch (e: IOException) {
            dest.delete()
            throw FtakException(networkMessage(e, url))
        } finally {
            conn.disconnect()
        }
    }

    suspend fun reachable(host: String, port: Int, timeoutMs: Int = 3000): Boolean = withContext(Dispatchers.IO) {
        try {
            Socket().use { it.connect(InetSocketAddress(host, port), timeoutMs) }
            true
        } catch (e: Exception) {
            false
        }
    }

    private fun fileName(disposition: String?): String? {
        if (disposition.isNullOrBlank()) return null
        val m = Regex("filename\\*?=(?:UTF-8'')?\"?([^\";]+)\"?", RegexOption.IGNORE_CASE).find(disposition) ?: return null
        return m.groupValues[1].trim().takeIf { it.isNotEmpty() }
    }

    private fun httpMessage(code: Int, url: String): String = when (code) {
        404 -> "Nie znaleziono pliku na serwerze (HTTP 404). Organizator mógł zatrzymać udostępnianie."
        410 -> "Link wygasł (HTTP 410). Poproś organizatora o nowy kod QR."
        429 -> "Wyczerpano limit pobrań tej paczki (HTTP 429)."
        403 -> "Brak dostępu do pliku (HTTP 403)."
        else -> "Serwer odpowiedział błędem HTTP $code dla $url"
    }

    private fun networkMessage(e: IOException, url: String): String {
        val host = try { URL(url).host } catch (x: Exception) { url }
        return when (e) {
            is java.net.UnknownHostException -> "Nie można odnaleźć serwera $host. Sprawdź połączenie z siecią."
            is java.net.SocketTimeoutException -> "Serwer $host nie odpowiada. Czy telefon jest w tej samej sieci Wi-Fi co komputer z Builderem?"
            is java.net.ConnectException -> "Brak połączenia z $host. Czy Builder lub drugi telefon nadal udostępnia paczkę?"
            else -> "Błąd połączenia z $host: ${e.message ?: e.javaClass.simpleName}"
        }
    }
}
