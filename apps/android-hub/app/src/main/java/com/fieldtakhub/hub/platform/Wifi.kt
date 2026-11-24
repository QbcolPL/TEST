package com.fieldtakhub.hub.platform

import android.annotation.SuppressLint
import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.wifi.SoftApConfiguration
import android.net.wifi.WifiManager
import android.net.wifi.WifiNetworkSpecifier
import android.os.Build
import android.os.Handler
import android.os.Looper
import com.fieldtakhub.hub.core.WifiCredentials
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeoutOrNull
import java.net.Inet4Address
import java.net.NetworkInterface
import kotlin.coroutines.resume

/** Phone-to-phone sharing: a local-only Wi-Fi hotspot created by the system (random name and password). */
class HotspotController(context: Context) {
    data class Info(val ssid: String, val password: String?, val security: String, val ip: String?)

    private val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
    private var reservation: WifiManager.LocalOnlyHotspotReservation? = null

    val isRunning: Boolean get() = reservation != null

    @SuppressLint("MissingPermission")
    suspend fun start(): Result<Info> {
        stop()
        return try {
            val r = withTimeoutOrNull(20_000) {
                suspendCancellableCoroutine<Result<WifiManager.LocalOnlyHotspotReservation>> { cont ->
                    wifi.startLocalOnlyHotspot(object : WifiManager.LocalOnlyHotspotCallback() {
                        override fun onStarted(res: WifiManager.LocalOnlyHotspotReservation) {
                            if (cont.isActive) cont.resume(Result.success(res)) else res.close()
                        }

                        override fun onStopped() {}

                        override fun onFailed(reason: Int) {
                            val msg = when (reason) {
                                ERROR_INCOMPATIBLE_MODE -> "Wyłącz zwykły hotspot (tethering) i spróbuj ponownie."
                                ERROR_TETHERING_DISALLOWED -> "Hotspot jest zablokowany na tym telefonie."
                                ERROR_NO_CHANNEL -> "Brak wolnego kanału Wi-Fi do hotspotu."
                                else -> "Telefon nie uruchomił hotspotu. Włącz Wi-Fi i lokalizację, potem spróbuj ponownie."
                            }
                            if (cont.isActive) cont.resume(Result.failure(IllegalStateException(msg)))
                        }
                    }, Handler(Looper.getMainLooper()))
                }
            } ?: return Result.failure(IllegalStateException("Hotspot nie wystartował w ciągu 20 s."))
            val res = r.getOrElse { return Result.failure(it) }
            reservation = res
            Result.success(describe(res))
        } catch (e: SecurityException) {
            Result.failure(IllegalStateException("Brak uprawnienia do hotspotu. Nadaj uprawnienia lokalizacji / urządzeń w pobliżu."))
        } catch (e: IllegalStateException) {
            Result.failure(IllegalStateException("Hotspot jest już uruchomiony przez inną aplikację."))
        }
    }

    @Suppress("DEPRECATION")
    private suspend fun describe(res: WifiManager.LocalOnlyHotspotReservation): Info {
        var ssid = ""
        var pass: String? = null
        var security = "wpa2"
        if (Build.VERSION.SDK_INT >= 30) {
            val cfg: SoftApConfiguration = res.softApConfiguration
            ssid = if (Build.VERSION.SDK_INT >= 33) cfg.wifiSsid?.toString().orEmpty() else cfg.ssid.orEmpty()
            pass = cfg.passphrase
            security = when (cfg.securityType) {
                SoftApConfiguration.SECURITY_TYPE_OPEN -> "open"
                SoftApConfiguration.SECURITY_TYPE_WPA3_SAE -> "wpa3"
                SoftApConfiguration.SECURITY_TYPE_WPA3_SAE_TRANSITION -> "wpa2"
                else -> "wpa2"
            }
        } else {
            val cfg = res.wifiConfiguration
            ssid = cfg?.SSID.orEmpty()
            pass = cfg?.preSharedKey
        }
        ssid = ssid.removeSurrounding("\"")
        pass = pass?.removeSurrounding("\"")
        var ip: String? = null
        repeat(10) {
            ip = hotspotAddress()
            if (ip != null) return Info(ssid, pass, security, ip)
            kotlinx.coroutines.delay(300)
        }
        return Info(ssid, pass, security, ip)
    }

    fun stop() {
        try { reservation?.close() } catch (e: Exception) { }
        reservation = null
    }

    companion object {
        /** IPv4 address of the hotspot interface (not the normal Wi-Fi client interface). */
        fun hotspotAddress(): String? {
            val candidates = ArrayList<Pair<String, String>>()
            try {
                for (nif in NetworkInterface.getNetworkInterfaces()) {
                    if (!nif.isUp || nif.isLoopback) continue
                    for (addr in nif.inetAddresses) {
                        if (addr is Inet4Address && addr.isSiteLocalAddress) candidates += nif.name to addr.hostAddress.orEmpty()
                    }
                }
            } catch (e: Exception) {
                return null
            }
            val apNames = listOf("ap", "swlan", "softap", "wlan1", "wlan2")
            return candidates.firstOrNull { (name, _) -> apNames.any { name.startsWith(it) } }?.second
                ?: candidates.firstOrNull { (name, ip) -> name != "wlan0" && ip.endsWith(".1") }?.second
        }
    }
}

/** Joins the sharing phone's hotspot for this app only (Android 10+), without touching saved networks. */
class WifiJoiner(context: Context) {
    private val cm = context.applicationContext.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
    private var callback: ConnectivityManager.NetworkCallback? = null

    val supported: Boolean get() = Build.VERSION.SDK_INT >= 29

    suspend fun join(creds: WifiCredentials, timeoutMs: Int = 60_000): Network? {
        if (Build.VERSION.SDK_INT < 29) return null
        release()
        val builder = WifiNetworkSpecifier.Builder().setSsid(creds.ssid)
        val pass = creds.password
        if (!pass.isNullOrEmpty() && creds.security?.lowercase() != "open") {
            if (creds.security?.lowercase() == "wpa3") builder.setWpa3Passphrase(pass) else builder.setWpa2Passphrase(pass)
        }
        val request = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .setNetworkSpecifier(builder.build())
            .build()
        return suspendCancellableCoroutine { cont ->
            val cb = object : ConnectivityManager.NetworkCallback() {
                override fun onAvailable(network: Network) {
                    if (cont.isActive) cont.resume(network)
                }

                override fun onUnavailable() {
                    if (cont.isActive) cont.resume(null)
                }
            }
            callback = cb
            cm.requestNetwork(request, cb, timeoutMs)
            cont.invokeOnCancellation { release() }
        }
    }

    /** Address of the phone that runs the hotspot, as seen from this phone. */
    fun gatewayAddress(network: Network): String? {
        val lp = cm.getLinkProperties(network) ?: return null
        if (Build.VERSION.SDK_INT >= 30) lp.dhcpServerAddress?.hostAddress?.let { return it }
        return lp.routes.firstOrNull { it.isDefaultRoute && it.gateway != null }?.gateway?.hostAddress
    }

    fun release() {
        callback?.let { runCatching { cm.unregisterNetworkCallback(it) } }
        callback = null
    }
}
