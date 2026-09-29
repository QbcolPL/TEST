package org.fieldtak.hub.diagnostics

import org.fieldtak.hub.model.CheckState

data class DiagnosticCheck(
  val id:String,
  val label:String,
  val state:CheckState,
  val detail:String,
  val technical:String = ""
)

data class ServiceReport(
  val generatedUtc:String,
  val host:String,
  val checks:List<DiagnosticCheck>
) {
  fun asText():String = buildString {
    appendLine("FIELD TAK HUB 2.3.0 — SERVICE REPORT")
    appendLine("UTC time: $generatedUtc")
    appendLine("Host: $host")
    appendLine()
    checks.forEach { c ->
      appendLine("${c.label.padEnd(24)} ${c.state.name}")
      if(c.detail.isNotBlank()) appendLine("  ${c.detail}")
      if(c.technical.isNotBlank()) appendLine("  tech: ${c.technical}")
    }
    appendLine()
    appendLine("Note: ATAK private certificate store is not directly accessible to Field TAK Hub on stock Android.")
  }
}
