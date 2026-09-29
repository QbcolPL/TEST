package org.fieldtak.hub.deployment

enum class DeploymentStage {
  NEW,
  QR_SCANNED,
  DESCRIPTOR_VERIFIED,
  DOWNLOADING,
  BUNDLE_DOWNLOADED,
  BUNDLE_VERIFIED,
  PREFLIGHT,
  ATAK_READY,
  MESHTASTIC_APP_INSTALLING,
  MESHTASTIC_APP_READY,
  MESHTASTIC_CONFIG,
  PLUGINS_INSTALLING,
  PLUGINS_READY,
  MAPS_IMPORTING,
  MAPS_READY,
  OTS_ENROLLMENT,
  OTS_VERIFYING,
  OTS_CONNECTED,
  FINAL_VERIFY,
  COMPLETE,
  PAUSED,
  WAITING_FOR_USER,
  FAILED,
  CANCELLED
}

enum class StepResult { PENDING, RUNNING, READY, ACTION_REQUIRED, WARNING, FAILED, SKIPPED }

data class DeploymentStep(
  val stage:DeploymentStage,
  val result:StepResult,
  val detail:String = "",
  val updatedUtc:String
)

data class DeploymentSession(
  val id:String,
  val packageId:String,
  val packageName:String,
  val packageVersion:String,
  val sourceDescriptor:String?,
  val packageUrl:String?,
  val localPackagePath:String?,
  val extractedRoot:String?,
  val stage:DeploymentStage,
  val startedUtc:String,
  val updatedUtc:String,
  val completedUtc:String? = null,
  val steps:List<DeploymentStep> = emptyList()
) {
  val finished:Boolean get() = stage == DeploymentStage.COMPLETE || stage == DeploymentStage.CANCELLED
}


enum class DeploymentTaskKind { CORE, PLUGIN, MESHTASTIC, OTS, ENROLLMENT, MAPS, OVERLAY, VERIFY }
enum class DeploymentTaskState { READY, ACTION_REQUIRED, OPTIONAL, SKIPPED, PROBLEM, UNKNOWN }

data class DeploymentTask(
  val id:String,
  val label:String,
  val kind:DeploymentTaskKind,
  val state:DeploymentTaskState,
  val optional:Boolean = false,
  val detail:String = ""
)

data class DeploymentPlan(
  val tasks:List<DeploymentTask>,
  val requiredReady:Boolean,
  val optionalPending:Int
) {
  val readyPercent:Int get() = if(tasks.isEmpty()) 100 else (tasks.count { it.state==DeploymentTaskState.READY || it.state==DeploymentTaskState.SKIPPED } * 100 / tasks.size)
}
