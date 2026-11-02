package org.fieldtak.hub.model

import java.io.File

data class ProvisionDescriptor(
  val packageUrl: String,
  val expiresUtc: String?,
  val packageSha256: String? = null,
  val label: String? = null,
  val packageBytes: Long? = null,
  val recommendedFreeBytes: Long? = null
)

data class PackageManifest(
  val schema: String = "",
  val schemaVersion: Int = 0,
  val packageInfo: PackageInfo = PackageInfo(),
  val target: TargetInfo = TargetInfo(),
  val server: ServerInfo = ServerInfo(),
  val enrollment: EnrollmentInfo = EnrollmentInfo(),
  val deployment: DeploymentPolicy = DeploymentPolicy(),
  val meshtastic: MeshtasticProfile = MeshtasticProfile(),
  val components: ComponentInfo = ComponentInfo(),
  val security: SecurityInfo = SecurityInfo(),
  val distribution: DistributionInfo = DistributionInfo(),
  val size: PackageSizeInfo = PackageSizeInfo()
)

data class PackageInfo(
  val id:String="",
  val name:String="",
  val version:String="",
  val publisher:String="",
  val createdUtc:String=""
)

data class EnrollmentInfo(
  val enabled:Boolean=false,
  val required:Boolean=false,
  val label:String=""
)

data class DeploymentPolicy(
  val atakRequired:Boolean=true,
  val pluginsRequired:Boolean=true,
  val serverRequired:Boolean=true,
  val meshtasticRequired:Boolean=false,
  val enrollmentRequired:Boolean=false,
  val missionPackageRequired:Boolean=false,
  val mapsRequired:Boolean=false,
  val overlaysRequired:Boolean=false
)

data class TargetInfo(
  val platform:String="",
  val minVersion:String="",
  val maxVersion:String=""
)

data class ServerInfo(
  val type:String="",
  val name:String="",
  val host:String="",
  val cotPort:Int=0,
  val apiPort:Int=0,
  val webPort:Int=0
)

data class MeshtasticProfile(
  val enabled:Boolean=false,
  val mode:String="hybrid",
  val deviceRole:String="auto",
  val deviceModel:String="generic_meshtastic",
  val channelName:String="",
  val region:String="EU_868",
  val modemPreset:String="SHORT_FAST",
  val hopLimit:Int=3,
  val pli:Boolean=true,
  val geoChat:Boolean=true,
  val otsRelay:Boolean=true,
  val fileTransfer:Boolean=false,
  val gateway:MeshtasticGatewayProfile=MeshtasticGatewayProfile()
)

data class MeshtasticGatewayProfile(
  val enabled:Boolean=false,
  val type:String="generic_meshtastic",
  val role:String="ots_mqtt",
  val transport:String="mqtt_tls",
  val mqttPort:Int=8883,
  val rootTopic:String="opentakserver",
  val secretsExternal:Boolean=true
)

data class ComponentInfo(
  val missionPackage:Boolean=false,
  val plugins:Int=0,
  val files:Int=0,
  val meshtastic:Boolean=false,
  val enrollment:Boolean=false
)

data class SecurityInfo(
  val hash:String="",
  val signature:String="",
  val publisherFingerprintSha256:String=""
)

data class DistributionInfo(val expiresUtc:String="")

data class PackageSizeInfo(
  val payloadBytes:Long=0,
  val uncompressedBytes:Long=0,
  val recommendedFreeBytes:Long=0
)

data class VerifiedPackage(
  val root: File,
  val sourceFile: File,
  val manifest: PackageManifest,
  val publisherFingerprint:String,
  val signatureValid:Boolean,
  val hashesValid:Boolean
)

enum class CheckState { READY, ACTION_REQUIRED, PROBLEM, UNKNOWN }

data class AtakStatus(
  val installed:Boolean,
  val versionName:String?,
  val versionCode:Long? = null,
  val compatibility:CheckState = CheckState.UNKNOWN,
  val compatibilityMessage:String = ""
)

enum class PluginInstallState { CURRENT, UPDATE_REQUIRED, NOT_INSTALLED, UNKNOWN }

data class PluginStatus(
  val file: File,
  val packageName:String?,
  val label:String,
  val sourceVersion:String?,
  val sourceVersionCode:Long?,
  val installedVersion:String?,
  val installedVersionCode:Long?,
  val state:PluginInstallState
)

data class ReadinessItem(
  val id:String,
  val label:String,
  val state:CheckState,
  val detail:String = ""
)

data class PhoneReadiness(
  val percent:Int,
  val items:List<ReadinessItem>,
  val ready:Boolean
)
