package org.fieldtak.hub.provision
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
object ResumablePackageDownloader {
    private const val BUF = 32 * 1024
    private const val RETRIES = 4
    @JvmStatic
    fun downloadToFileWithResume(
        url: String,
        targetFile: File,
        expectedSha256: String? = null,
        onProgressBytes: ((Long, Long) -> Unit)? = null
    ): File {
        targetFile.parentFile?.mkdirs()
        val part = File(
            targetFile.parentFile, "${targetFile.name}.part"
        )
        var att = 0
        var err: IOException? = null
        while (att < RETRIES) {
            att++
            try {
                chunk(url, part, onProgressBytes)
                if (!expectedSha256.isNullOrBlank()) {
                    val s = sha256(part)
                    if (!s.equals(expectedSha256.trim(), true)) {
                        part.delete()
                        throw IOException("Niezgodna suma SHA-256")
                    }
                }
                if (targetFile.exists()) targetFile.delete()
                if (!part.renameTo(targetFile)) {
                    part.copyTo(targetFile, overwrite = true)
                    part.delete()
                }
                return targetFile
            } catch (e: IOException) {
                err = e
                if (att < RETRIES) {
                    try { Thread.sleep(1500L * att) }
                    catch (_: Exception) {}
                }
            }
        }
        throw IOException("Przerwano pobieranie.", err)
    }
    private fun chunk(
        url: String,
        part: File,
        prog: ((Long, Long) -> Unit)?
    ) {
        val ex = if (part.exists()) part.length() else 0L
        val c = (URL(url).openConnection() as HttpURLConnection)
        c.connectTimeout = 15000
        c.readTimeout = 20000
        c.instanceFollowRedirects = true
        c.requestMethod = "GET"
        c.setRequestProperty("Accept-Encoding", "identity")
        if (ex > 0L) c.setRequestProperty("Range", "bytes=$ex-")
        try {
            c.connect()
            val code = c.responseCode
            if (code == 416 && ex > 0L) return
            val partOk = code == HttpURLConnection.HTTP_PARTIAL
            if (!partOk && code != HttpURLConnection.HTTP_OK) {
                throw IOException("HTTP $code")
            }
            val st = if (partOk) ex else 0L
            val tot = if (c.contentLengthLong > 0)
                st + c.contentLengthLong else -1L
            c.inputStream.use { inp ->
                FileOutputStream(part, partOk).use { out ->
                    val b = ByteArray(BUF)
                    var cur = st
                    prog?.invoke(cur, tot)
                    while (true) {
                        val r = inp.read(b)
                        if (r == -1) break
                        out.write(b, 0, r)
                        cur += r
                        prog?.invoke(cur, tot)
                    }
                    out.flush()
                }
            }
        } finally { c.disconnect() }
    }
    private fun sha256(f: File): String {
        val md = MessageDigest.getInstance("SHA-256")
        FileInputStream(f).use { fis ->
            val b = ByteArray(BUF)
            while (true) {
                val r = fis.read(b)
                if (r <= 0) break
                md.update(b, 0, r)
            }
        }
        return md.digest().joinToString("") { "%02x".format(it) }
    }
}