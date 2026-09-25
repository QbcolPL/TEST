package org.fieldtak.hub.security

import java.net.InetAddress
import java.net.URI

object UrlPolicy {
  fun requireProvisioningUrl(value:String):String {
    val uri = URI(value.trim())
    val scheme = uri.scheme?.lowercase() ?: error("FTH-NET-001: URL has no scheme")
    require(uri.host?.isNotBlank() == true) { "FTH-NET-002: URL has no host" }
    when(scheme) {
      "https" -> Unit
      "http" -> require(isPrivateHost(uri.host)) { "FTH-NET-003: Cleartext HTTP is allowed only for private/loopback LAN hosts" }
      else -> error("FTH-NET-004: Unsupported URL scheme: $scheme")
    }
    return uri.toString()
  }

  fun requireHttps(value:String):String {
    val uri=URI(value.trim())
    require(uri.scheme.equals("https",true)) { "FTH-UPD-001: Update URL must use HTTPS" }
    require(!uri.host.isNullOrBlank()) { "FTH-UPD-002: Update URL has no host" }
    return uri.toString()
  }

  fun isPrivateHost(host:String):Boolean = runCatching {
    val addresses=InetAddress.getAllByName(host)
    addresses.isNotEmpty() && addresses.all { a -> a.isLoopbackAddress || a.isSiteLocalAddress || a.isLinkLocalAddress }
  }.getOrDefault(false)
}
