package org.fieldtak.hub.meshtastic

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.net.Uri
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.fieldtak.hub.model.CheckState
import org.fieldtak.hub.model.MeshtasticGatewayProfile

enum class MeshtasticLinkState {
  UNKNOWN,
  CONNECTING,
  CONNECTED,
  DISCONNECTED
}

data class MeshtasticRuntimeStatus(
  val appInstalled:Boolean,
  val directPluginInstalled:Boolean,
  val channelConfigured:Boolean,
  val mode:String,
  val state:CheckState,
  val detail:String,
  val gatewayConfigured:Boolean=false,
  val gatewayDetail:String="",
  val linkState:MeshtasticLinkState = MeshtasticLinkState.UNKNOWN,
  val linkDetail:String = ""
)

/**
 * Hybrid orchestrator.
 *
 * Field TAK Hub does not own Meshtastic radio I/O. It can, however, observe
 * connection-state broadcasts exposed by Meshtastic-compatible Android apps.
 * This is intentionally optional: when no supported broadcast is available,
 * Hub reports the radio link as UNKNOWN rather than pretending it is connected.
 *
 * The legacy Meshtastic AIDL service integration was removed upstream in 2026;
 * this adapter therefore avoids binding to IMeshService and remains compatible
 * with current Meshtastic Android builds. The broadcast bridge is best-effort.
 */
class MeshtasticManager(private val context:Context) : AutoCloseable {
  companion object {
    const val MESHTASTIC_PACKAGE = "com.geeksville.mesh"
    const val DIRECT_PLUGIN_MARKER = "pluginmeshtastic"

    private const val ACTION_CONNECTED = "com.geeksville.mesh.MESH_CONNECTED"
    private const val ACTION_DISCONNECTED = "com.geeksville.mesh.MESH_DISCONNECTED"
    private const val ACTION_STATE_CHANGED = "com.geeksville.mesh.CONNECTION_STATE_CHANGED"
    private const val EXTRA_CONNECTED = "com.geeksville.mesh.Connected"
    private const val EXTRA_STATE = "com.geeksville.mesh.ConnectionState"
  }

  private val _linkState = MutableStateFlow(MeshtasticLinkState.UNKNOWN)
  val linkState: StateFlow<MeshtasticLinkState> = _linkState.asStateFlow()

  private val receiver = object : BroadcastReceiver() {
    override fun onReceive(context:Context, intent:Intent) {
      if (intent.`package` != null && intent.`package` != MESHTASTIC_PACKAGE) return
      when (intent.action) {
        ACTION_CONNECTED -> _linkState.value = MeshtasticLinkState.CONNECTED
        ACTION_DISCONNECTED -> _linkState.value = MeshtasticLinkState.DISCONNECTED
        ACTION_STATE_CHANGED -> {
          val explicit = when {
            intent.hasExtra(EXTRA_CONNECTED) -> if (intent.getBooleanExtra(EXTRA_CONNECTED,false)) MeshtasticLinkState.CONNECTED else MeshtasticLinkState.DISCONNECTED
            else -> null
          }
          _linkState.value = explicit ?: parseState(intent.getStringExtra(EXTRA_STATE))
        }
      }
    }
  }

  init {
    registerBroadcastBridge()
  }

  private fun registerBroadcastBridge() {
    val filter = IntentFilter().apply {
      addAction(ACTION_CONNECTED)
      addAction(ACTION_DISCONNECTED)
      addAction(ACTION_STATE_CHANGED)
    }
    runCatching {
      if (android.os.Build.VERSION.SDK_INT >= 33) {
        context.registerReceiver(receiver, filter, Context.RECEIVER_EXPORTED)
      } else {
        @Suppress("UnspecifiedRegisterReceiverFlag")
        context.registerReceiver(receiver, filter)
      }
    }
  }

  private fun parseState(raw:String?):MeshtasticLinkState = when (raw?.lowercase()) {
    "connected", "ready", "active" -> MeshtasticLinkState.CONNECTED
    "connecting", "configuring", "reconnecting" -> MeshtasticLinkState.CONNECTING
    "disconnected", "offline", "closed" -> MeshtasticLinkState.DISCONNECTED
    else -> MeshtasticLinkState.UNKNOWN
  }

  fun appInstalled():Boolean = runCatching {
    context.packageManager.getPackageInfo(MESHTASTIC_PACKAGE,0); true
  }.getOrDefault(false)

  fun directPluginHint():Boolean = runCatching {
    val ai=context.packageManager.getApplicationInfo("com.atakmap.app.civ",PackageManager.GET_META_DATA)
    ai.metaData?.keySet()?.any { it.contains(DIRECT_PLUGIN_MARKER,true) } == true
  }.getOrDefault(false)

  fun refreshFromLaunchState():MeshtasticLinkState = linkState.value

  fun openChannel(url:String):Boolean {
    require(url.startsWith("https://meshtastic.org/e/",true))
    val i=Intent(Intent.ACTION_VIEW,Uri.parse(url)).setPackage(MESHTASTIC_PACKAGE).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    return runCatching { context.startActivity(i); true }.getOrDefault(false)
  }

  fun status(channelConfigured:Boolean,mode:String,deviceRole:String="auto",gateway:MeshtasticGatewayProfile=MeshtasticGatewayProfile()):MeshtasticRuntimeStatus {
    val app=appInstalled(); val direct=directPluginHint()
    val gatewayExpected=deviceRole.equals("gateway",true)
    val gatewayConfigured=gateway.enabled && gateway.type.isNotBlank() && gateway.transport.equals("mqtt_tls",true) && gateway.mqttPort in 1..65535 && gateway.rootTopic.isNotBlank() && gateway.secretsExternal
    val link = if (app) linkState.value else MeshtasticLinkState.UNKNOWN
    val state=when {
      !app -> CheckState.PROBLEM
      gatewayExpected && !gatewayConfigured -> CheckState.ACTION_REQUIRED
      mode.equals("direct",true) && !direct -> CheckState.ACTION_REQUIRED
      !channelConfigured -> CheckState.UNKNOWN
      link == MeshtasticLinkState.CONNECTED -> CheckState.READY
      link == MeshtasticLinkState.CONNECTING || link == MeshtasticLinkState.DISCONNECTED -> CheckState.ACTION_REQUIRED
      else -> CheckState.UNKNOWN
    }
    val detail=when {
      !app -> "Aplikacja Meshtastic nie jest zainstalowana."
      !channelConfigured -> "Aplikacja Meshtastic jest zainstalowana, ale brak aktywnej konfiguracji kanału w pakiecie."
      link == MeshtasticLinkState.CONNECTED -> "Radio jest zgłoszone jako połączone przez most statusu Meshtastic Android."
      link == MeshtasticLinkState.CONNECTING -> "Meshtastic zgłasza próbę połączenia z radiem."
      link == MeshtasticLinkState.DISCONNECTED -> "Meshtastic zgłasza brak aktywnego połączenia z radiem."
      else -> "Konfiguracja HYBRID jest gotowa. Stan radia nie został jeszcze potwierdzony przez Meshtastic."
    }
    val gatewayDetail=if(gatewayConfigured) "Gateway ${gateway.type} → ${gateway.transport.uppercase()} :${gateway.mqttPort} → ${gateway.rootTopic}" else "Gateway nie jest kompletnie skonfigurowany lub sekrety nie są oznaczone jako zewnętrzne."
    val linkDetail = when(link) {
      MeshtasticLinkState.CONNECTED -> "CONNECTED"
      MeshtasticLinkState.CONNECTING -> "CONNECTING"
      MeshtasticLinkState.DISCONNECTED -> "DISCONNECTED"
      MeshtasticLinkState.UNKNOWN -> "UNKNOWN"
    }
    return MeshtasticRuntimeStatus(app,direct,channelConfigured,mode,state,detail,gatewayConfigured,gatewayDetail,link,linkDetail)
  }

  override fun close() {
    runCatching { context.unregisterReceiver(receiver) }
  }
}
