package org.fieldtak.hub.data

import org.fieldtak.hub.model.*
import org.json.JSONObject

object SimpleJson {
  fun descriptor(s:String):ProvisionDescriptor {
    val o=JSONObject(s)
    return ProvisionDescriptor(
      packageUrl=o.getString("packageUrl"),
      expiresUtc=o.optString("expiresUtc").ifBlank{null},
      packageSha256=o.optString("packageSha256").ifBlank{null},
      label=o.optString("label").ifBlank{null},
      packageBytes=o.optLong("packageBytes",-1L).takeIf{it>=0},
      recommendedFreeBytes=o.optLong("recommendedFreeBytes",-1L).takeIf{it>=0}
    )
  }

  fun manifest(s:String):PackageManifest {
    val o=JSONObject(s); val p=o.getJSONObject("package"); val t=o.getJSONObject("target"); val srv=o.getJSONObject("server"); val c=o.getJSONObject("components"); val sec=o.getJSONObject("security"); val d=o.getJSONObject("distribution"); val size=o.optJSONObject("size"); val en=o.optJSONObject("enrollment"); val dep=o.optJSONObject("deployment")
    val mesh=o.optJSONObject("meshtastic")
    val gateway=mesh?.optJSONObject("gateway")
    val meshProfile=MeshtasticProfile(
      enabled=mesh?.optBoolean("enabled",false) ?: false,
      mode=mesh?.optString("mode","hybrid") ?: "hybrid",
      deviceRole=mesh?.optString("deviceRole","auto") ?: "auto",
      deviceModel=mesh?.optString("deviceModel","generic_meshtastic") ?: "generic_meshtastic",
      channelName=mesh?.optString("channelName","") ?: "",
      region=mesh?.optString("region","EU_868") ?: "EU_868",
      modemPreset=mesh?.optString("modemPreset","SHORT_FAST") ?: "SHORT_FAST",
      hopLimit=mesh?.optInt("hopLimit",3) ?: 3,
      pli=mesh?.optBoolean("pli",true) ?: true,
      geoChat=mesh?.optBoolean("geoChat",true) ?: true,
      otsRelay=mesh?.optBoolean("otsRelay",true) ?: true,
      fileTransfer=mesh?.optBoolean("fileTransfer",false) ?: false,
      gateway=MeshtasticGatewayProfile(
        enabled=gateway?.optBoolean("enabled",false) ?: false,
        type=gateway?.optString("type","generic_meshtastic") ?: "generic_meshtastic",
        role=gateway?.optString("role","ots_mqtt") ?: "ots_mqtt",
        transport=gateway?.optString("transport","mqtt_tls") ?: "mqtt_tls",
        mqttPort=gateway?.optInt("mqttPort",8883) ?: 8883,
        rootTopic=gateway?.optString("rootTopic","opentakserver") ?: "opentakserver",
        secretsExternal=gateway?.optBoolean("secretsExternal",true) ?: true
      )
    )
    return PackageManifest(
      schema=o.optString("schema"), schemaVersion=o.optInt("schemaVersion"),
      packageInfo=PackageInfo(p.optString("id"),p.optString("name"),p.optString("version"),p.optString("publisher"),p.optString("createdUtc")),
      target=TargetInfo(t.optString("platform"),t.optString("minVersion"),t.optString("maxVersion")),
      server=ServerInfo(srv.optString("type"),srv.optString("name"),srv.optString("host"),srv.optInt("cotPort"),srv.optInt("apiPort"),srv.optInt("webPort")),
      enrollment=EnrollmentInfo(en?.optBoolean("enabled",false) ?: false, en?.optBoolean("required",false) ?: false, en?.optString("label","") ?: ""),
      deployment=DeploymentPolicy(
        atakRequired=dep?.optBoolean("atakRequired",true) ?: true,
        pluginsRequired=dep?.optBoolean("pluginsRequired",true) ?: true,
        serverRequired=dep?.optBoolean("serverRequired",true) ?: true,
        meshtasticRequired=dep?.optBoolean("meshtasticRequired",false) ?: false,
        enrollmentRequired=dep?.optBoolean("enrollmentRequired",en?.optBoolean("required",false) ?: false) ?: (en?.optBoolean("required",false) ?: false),
        missionPackageRequired=dep?.optBoolean("missionPackageRequired",false) ?: false,
        mapsRequired=dep?.optBoolean("mapsRequired",false) ?: false,
        overlaysRequired=dep?.optBoolean("overlaysRequired",false) ?: false
      ),
      meshtastic=meshProfile,
      components=ComponentInfo(c.optBoolean("missionPackage"),c.optInt("plugins"),c.optInt("files"),c.optBoolean("meshtastic"),c.optBoolean("enrollment")),
      security=SecurityInfo(sec.optString("hash"),sec.optString("signature"),sec.optString("publisherFingerprintSha256")),
      distribution=DistributionInfo(d.optString("expiresUtc")),
      size=PackageSizeInfo(size?.optLong("payloadBytes") ?: 0L, size?.optLong("uncompressedBytes") ?: 0L, size?.optLong("recommendedFreeBytes") ?: 0L)
    )
  }
}
