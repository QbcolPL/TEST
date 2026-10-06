package org.fieldtak.hub.provision
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
object ResumablePackageDownloader {
    @JvmStatic
    fun downloadToFileWithResume(url: String, targetFile: File, expectedSha256: String? = null, onProgressBytes: ((Long, Long) -> Unit)? = null): File {
        targetFile.parentFile?.mkdirs()
        val part = File(targetFile.parentFile, "${targetFile.name}.part")
        var att = 0
        var err: IOException? = null
        while (att < 4) {
            att++
            try {
                val ex = if (part.exists()) part.length() else 0L
                val c = (URL(url).openConnection() as HttpURLConnection).apply {
                    connectTimeout = 15000; readTimeout = 20000; instanceFollowRedirects = true; requestMethod = "GET"
                    setRequestProperty("Accept-Encoding", "identity")
                    if (ex > 0L) setRequestProperty("Range", "bytes=$ex-")
                }
                try {
                    c.connect()
                    val code = c.responseCode
                    val skipBody = (code == 416 && ex > 0L)
                    if (!skipBody) {
                        val partOk = (code == HttpURLConnection.HTTP_PARTIAL)
                        if (!partOk && code != HttpURLConnection.HTTP_OK) throw IOException("HTTP $code")
                        val st = if (partOk) ex else 0L
                        val tot = if (c.contentLengthLong > 0) st + c.contentLengthLong else -1L
                        c.inputStream.use { inp ->
                            FileOutputStream(part, partOk).use { out ->
                                val b = ByteArray(32768); var cur = st; onProgressBytes?.invoke(cur, tot)
                                while (true) {
                                    val r = inp.read(b)
                                    if (r == -1) break
                                    out.write(b, 0, r)
                                    cur += r
                                    onProgressBytes?.invoke(cur, tot)
                                }
                            }
                        }
                    }
                } finally { c.disconnect() }
                if (!expectedSha256.isNullOrBlank()) {
                    val md = MessageDigest.getInstance("SHA-256")
                    FileInputStream(part).use { fis ->
                        val b = ByteArray(32768)
                        while (true) {
                            val r = fis.read(b)
                            if (r <= 0) break
                            md.update(b, 0, r)
                        }
                    }
                    val s = md.digest().joinToString("") { "%02x".format(it) }
                    if (!s.equals(expectedSha256.trim(), true)) {
                        part.delete()
                        throw IOException("Blad SHA-256")
                    }
                }
                if (targetFile.exists()) targetFile.delete()
                if (!part.renameTo(targetFile)) { part.copyTo(targetFile, true); part.delete() }
                return targetFile
            } catch (e: IOException) {
                err = e
                if (att < 4) try { Thread.sleep(1500L * att) } catch (_: Exception) {}
            }
        }
        throw IOException("Przerwano pobieranie.", err)
    }
}