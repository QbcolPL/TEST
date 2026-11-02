package org.fieldtak.hub.diagnostics

import android.content.Context
import java.io.File
import java.time.Instant

/** Local, privacy-conscious diagnostic log for Field TAK Hub.
 * Secrets and sensitive URI query values must never be passed to this logger.
 */
class DiagnosticLogger(context: Context) {
  enum class Level { INFO, WARNING, ERROR }

  private val file = File(context.filesDir, "fieldtak-hub-diagnostic.log")
  private val lock = Any()
  private val maxBytes = 5L * 1024L * 1024L
  private val maxLines = 5000

  fun info(event: String, detail: String = "") = write(Level.INFO, event, detail)
  fun warning(event: String, detail: String = "") = write(Level.WARNING, event, detail)
  fun error(event: String, detail: String = "") = write(Level.ERROR, event, detail)

  fun recent(limit: Int = 250): List<String> = synchronized(lock) {
    if (!file.isFile) return@synchronized emptyList()
    runCatching { file.readLines().takeLast(limit.coerceIn(1, maxLines)) }.getOrDefault(emptyList())
  }

  fun all(): String = synchronized(lock) {
    if (!file.isFile) return@synchronized ""
    runCatching { file.readText() }.getOrDefault("")
  }

  fun clear() = synchronized(lock) { runCatching { file.delete() } }

  private fun write(level: Level, event: String, detail: String) = synchronized(lock) {
    runCatching {
      val safeEvent = sanitize(event).take(160)
      val safeDetail = sanitize(detail).take(600)
      file.parentFile?.mkdirs()
      file.appendText(buildString {
        append(Instant.now())
        append(' ')
        append(level.name)
        append(' ')
        append(safeEvent)
        if (safeDetail.isNotBlank()) { append(' '); append(safeDetail) }
        append('\n')
      })
      rotateIfNeeded()
    }
  }

  private fun rotateIfNeeded() {
    if (!file.isFile || file.length() <= maxBytes) return
    val kept = file.readLines().takeLast(maxLines)
    file.writeText(kept.joinToString("\n") + if (kept.isNotEmpty()) "\n" else "")
  }

  private fun sanitize(value: String): String = value
    .replace(Regex("(?i)(password|passwd|token|secret|private[_ -]?key|mqtt[_ -]?password)\\s*[:=]\\s*[^\\s]+"), "$1=[REDACTED]")
    .replace(Regex("(?i)(tak://com\\.atakmap\\.app/enroll\\?[^\\s]+)"), "[ENROLLMENT_URI_REDACTED]")
    .replace(Regex("(?i)(https://meshtastic\\.org/e/[^\\s]+)"), "[MESHTASTIC_CHANNEL_REDACTED]")
    .replace(Regex("(?i)(mqtt://|mqtts://)[^\\s]+"), "[MQTT_URI_REDACTED]")
}
