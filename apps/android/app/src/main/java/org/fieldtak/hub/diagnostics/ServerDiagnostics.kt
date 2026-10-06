package org.fieldtak.hub.diagnostics

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import org.fieldtak.hub.R
import org.fieldtak.hub.model.CheckState
import org.fieldtak.hub.model.ServerInfo
import org.fieldtak.hub.model.MeshtasticProfile
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.time.Instant

class ServerDiagnostics(private val context:Context) {
  private fun s(id:Int,vararg args:Any)=context.getString(id,*args)

  suspend fun run(server:ServerInfo,meshtastic:MeshtasticProfile=MeshtasticProfile()):ServiceReport = coroutineScope {
    if(server.host.isBlank()) return@coroutineScope ServiceReport(Instant.now().toString(),"", listOf(DiagnosticCheck("host",s(R.string.diag_server),CheckState.UNKNOWN,s(R.string.diag_host_missing))))

    val dns=async(Dispatchers.IO){
      val resolved=runCatching { InetAddress.getAllByName(server.host).joinToString { it.hostAddress ?: it.toString() } }
      if(resolved.isSuccess) DiagnosticCheck("dns","DNS",CheckState.READY,s(R.string.diag_dns_resolved),resolved.getOrNull().orEmpty())
      else DiagnosticCheck("dns","DNS",CheckState.PROBLEM,s(R.string.diag_dns_failed),resolved.exceptionOrNull()?.message.orEmpty())
    }
    fun port(id:String,label:String,port:Int,meaning:String)=async(Dispatchers.IO){
      if(port<=0) return@async DiagnosticCheck(id,label,CheckState.UNKNOWN,s(R.string.diag_port_unconfigured))
      val r=runCatching { Socket().use { it.connect(InetSocketAddress(server.host,port),3_000) } }
      if(r.isSuccess) DiagnosticCheck(id,label,CheckState.READY,s(R.string.diag_port_ok,port,meaning))
      else DiagnosticCheck(id,label,CheckState.PROBLEM,s(R.string.diag_port_fail,port),r.exceptionOrNull()?.message.orEmpty())
    }
    val api=port("api","8446 / API",server.apiPort,s(R.string.diag_api_meaning))
    val web=port("web","8443 / Web",server.webPort,s(R.string.diag_web_meaning))
    val cot=port("cot","8089 / CoT",server.cotPort,s(R.string.diag_cot_meaning))
    val mqttPort=meshtastic.gateway.mqttPort
    val mqtt=if(meshtastic.enabled && meshtastic.deviceRole=="gateway" && meshtastic.gateway.enabled) port("mqtt","8883 / MQTT-TLS",mqttPort,s(R.string.diag_mqtt_meaning))
      else async(Dispatchers.IO){ DiagnosticCheck("mqtt","MQTT-TLS",CheckState.UNKNOWN,s(R.string.diag_mqtt_disabled)) }
    val gateway=DiagnosticCheck(
      "meshtastic_gateway",
      s(R.string.diag_meshtastic_gateway),
      if(!meshtastic.enabled || (meshtastic.deviceRole=="tak" || meshtastic.deviceRole=="field_node")) CheckState.UNKNOWN else if(meshtastic.gateway.enabled && meshtastic.gateway.secretsExternal && meshtastic.gateway.transport.equals("mqtt_tls",true) && meshtastic.gateway.rootTopic.isNotBlank()) CheckState.READY else CheckState.PROBLEM,
      if(!meshtastic.enabled || (meshtastic.deviceRole=="tak" || meshtastic.deviceRole=="field_node")) s(R.string.diag_meshtastic_not_configured) else s(R.string.diag_gateway_config_detail,meshtastic.gateway.type,meshtastic.gateway.transport,meshtastic.gateway.mqttPort,meshtastic.gateway.rootTopic)
    )
    ServiceReport(Instant.now().toString(),server.host,listOf(dns.await(),api.await(),web.await(),cot.await(),mqtt.await(),gateway,
      DiagnosticCheck("atak_cert",s(R.string.diag_atak_certificate),CheckState.UNKNOWN,s(R.string.diag_atak_certificate_unknown))))
  }
}
