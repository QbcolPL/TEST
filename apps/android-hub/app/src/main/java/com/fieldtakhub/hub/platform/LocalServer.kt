package com.fieldtakhub.hub.platform

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import java.io.BufferedInputStream
import java.io.File
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketException
import java.security.SecureRandom
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors

/**
 * Tiny HTTP file server (GET/HEAD, byte ranges) used for:
 *  - handing data packages to ATAK through tak://com.atakmap.app/import?url=http://127.0.0.1:PORT/...
 *  - sharing a .ftak with another phone over the Wi-Fi hotspot.
 * Every file gets a random 128-bit token in its path; nothing else is served.
 */
object LocalServer {
    class Share(
        val token: String,
        val file: File,
        val name: String,
        val contentType: String,
        val expiresAt: Long,
        val maxDownloads: Int,
        val deleteAfter: Boolean
    ) {
        @Volatile var downloads: Int = 0
        val path: String get() = "/p/$token/" + java.net.URLEncoder.encode(name, "UTF-8").replace("+", "%20")
    }

    private val shares = ConcurrentHashMap<String, Share>()
    private val random = SecureRandom()
    private val pool = Executors.newCachedThreadPool { r -> Thread(r, "hub-http").apply { isDaemon = true } }
    @Volatile private var server: ServerSocket? = null

    private val _downloads = MutableStateFlow<Map<String, Int>>(emptyMap())
    val downloads: StateFlow<Map<String, Int>> = _downloads

    val port: Int get() = server?.localPort ?: 0
    val hasShares: Boolean get() = shares.isNotEmpty()

    @Synchronized
    fun ensureStarted(): Int {
        server?.let { if (!it.isClosed) return it.localPort }
        var socket: ServerSocket? = null
        for (p in 8780..8789) {
            socket = try { ServerSocket(p, 16, InetAddress.getByName("0.0.0.0")) } catch (e: Exception) { null }
            if (socket != null) break
        }
        val s = socket ?: ServerSocket(0, 16, InetAddress.getByName("0.0.0.0"))
        server = s
        pool.execute { acceptLoop(s) }
        return s.localPort
    }

    fun publish(
        file: File,
        name: String,
        contentType: String,
        ttlMs: Long,
        maxDownloads: Int = Int.MAX_VALUE,
        deleteAfter: Boolean = false
    ): Share {
        ensureStarted()
        val bytes = ByteArray(16).also { random.nextBytes(it) }
        val token = bytes.joinToString("") { String.format("%02x", it) }
        val share = Share(token, file, name, contentType, System.currentTimeMillis() + ttlMs, maxDownloads, deleteAfter)
        shares[token] = share
        return share
    }

    fun unpublish(token: String) {
        shares.remove(token)?.let { if (it.deleteAfter) it.file.delete() }
        publishCounts()
        stopIfIdle()
    }

    fun unpublishAll(predicate: (Share) -> Boolean) {
        shares.values.filter(predicate).forEach { unpublish(it.token) }
    }

    @Synchronized
    fun stopIfIdle() {
        purgeExpired()
        if (shares.isEmpty()) {
            try { server?.close() } catch (e: Exception) { }
            server = null
        }
    }

    private fun purgeExpired() {
        val now = System.currentTimeMillis()
        shares.values.filter { it.expiresAt < now }.forEach { s ->
            shares.remove(s.token)
            if (s.deleteAfter) s.file.delete()
        }
    }

    private fun publishCounts() {
        _downloads.value = shares.values.associate { it.token to it.downloads }
    }

    private fun acceptLoop(s: ServerSocket) {
        while (!s.isClosed) {
            val client = try { s.accept() } catch (e: SocketException) { break } catch (e: Exception) { continue }
            pool.execute { handle(client) }
        }
    }

    private fun handle(socket: Socket) {
        socket.use { sock ->
            try {
                sock.soTimeout = 30_000
                val input = BufferedInputStream(sock.getInputStream())
                val requestLine = readLine(input) ?: return
                val headers = HashMap<String, String>()
                while (true) {
                    val line = readLine(input) ?: break
                    if (line.isEmpty()) break
                    val colon = line.indexOf(':')
                    if (colon > 0) headers[line.substring(0, colon).trim().lowercase()] = line.substring(colon + 1).trim()
                }
                val parts = requestLine.split(' ')
                val out = sock.getOutputStream()
                if (parts.size < 2) return
                val method = parts[0].uppercase()
                if (method != "GET" && method != "HEAD") { status(out, 405, "Method Not Allowed"); return }
                val path = parts[1].substringBefore('?')
                val token = path.removePrefix("/p/").substringBefore('/')
                purgeExpired()
                val share = shares[token]
                if (!path.startsWith("/p/") || share == null || !share.file.isFile) { status(out, 404, "Not Found"); return }
                val length = share.file.length()
                val range = parseRange(headers["range"], length)
                val partial = range != null
                if (!partial && method == "GET") {
                    if (share.downloads >= share.maxDownloads) { status(out, 429, "Too Many Requests"); return }
                    share.downloads++
                    publishCounts()
                }
                val start = range?.first ?: 0L
                val end = range?.second ?: (length - 1)
                val count = if (length == 0L) 0L else end - start + 1
                val head = StringBuilder()
                    .append(if (partial) "HTTP/1.1 206 Partial Content\r\n" else "HTTP/1.1 200 OK\r\n")
                    .append("Content-Type: ").append(share.contentType).append("\r\n")
                    .append("Content-Length: ").append(count).append("\r\n")
                    .append("Accept-Ranges: bytes\r\n")
                    .append("Cache-Control: no-store\r\n")
                    .append("Connection: close\r\n")
                    .append("Content-Disposition: attachment; filename=\"").append(share.name.replace("\"", "")).append("\"\r\n")
                if (partial) head.append("Content-Range: bytes ").append(start).append('-').append(end).append('/').append(length).append("\r\n")
                head.append("\r\n")
                out.write(head.toString().toByteArray(Charsets.US_ASCII))
                if (method == "GET" && count > 0) {
                    share.file.inputStream().use { fis ->
                        var skipped = 0L
                        while (skipped < start) {
                            val n = fis.skip(start - skipped)
                            if (n <= 0) break
                            skipped += n
                        }
                        val buf = ByteArray(128 * 1024)
                        var remaining = count
                        while (remaining > 0) {
                            val n = fis.read(buf, 0, minOf(buf.size.toLong(), remaining).toInt())
                            if (n < 0) break
                            out.write(buf, 0, n)
                            remaining -= n
                        }
                    }
                }
                out.flush()
            } catch (e: Exception) {
                // client went away; nothing to do
            }
        }
    }

    private fun parseRange(header: String?, length: Long): Pair<Long, Long>? {
        if (header == null || !header.startsWith("bytes=") || length == 0L) return null
        val first = header.removePrefix("bytes=").split(',')[0].trim()
        val a = first.substringBefore('-').toLongOrNull() ?: return null
        val b = first.substringAfter('-', "").toLongOrNull() ?: (length - 1)
        if (a < 0 || a >= length || b < a) return null
        return a to minOf(b, length - 1)
    }

    private fun status(out: OutputStream, code: Int, reason: String) {
        val body = "$code $reason\n".toByteArray()
        out.write("HTTP/1.1 $code $reason\r\nContent-Type: text/plain\r\nContent-Length: ${body.size}\r\nConnection: close\r\n\r\n".toByteArray())
        out.write(body)
        out.flush()
    }

    private fun readLine(input: BufferedInputStream): String? {
        val sb = StringBuilder()
        while (true) {
            val c = input.read()
            if (c < 0) return if (sb.isEmpty()) null else sb.toString()
            if (c == '\n'.code) return sb.toString().trimEnd('\r')
            if (sb.length > 8192) return null
            sb.append(c.toChar())
        }
    }
}
