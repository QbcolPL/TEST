package org.fieldtak.hub
import org.fieldtak.hub.meshtastic.MeshtasticManager
import org.fieldtak.hub.meshtastic.MeshtasticLinkState

import android.app.Application
import android.content.Intent
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.fieldtak.hub.data.PackageRepository
import org.fieldtak.hub.data.PublisherTrustStore
import org.fieldtak.hub.data.SimpleJson
import org.fieldtak.hub.deployment.*
import org.fieldtak.hub.diagnostics.ServerDiagnostics
import org.fieldtak.hub.diagnostics.DiagnosticLogger
import org.fieldtak.hub.diagnostics.ServiceReport
import org.fieldtak.hub.model.*
import org.fieldtak.hub.provision.ProvisioningController
import org.fieldtak.hub.security.UrlPolicy
import org.fieldtak.hub.util.VersionUtil
import org.fieldtak.hub.storage.StorageMaintenance
import org.fieldtak.hub.update.AppUpdateInfo
import org.fieldtak.hub.update.UpdateService
import java.io.File
import java.security.MessageDigest
import java.time.Instant


data class AppUiState(
  val busyMessage:String? = null,
  val error:String? = null,
  val pkg:VerifiedPackage? = null,
  val trusted:Boolean = false,
  val readiness:PhoneReadiness? = null,
  val deploymentPlan:DeploymentPlan? = null,
  val session:DeploymentSession? = null,
  val history:List<DeploymentSession> = emptyList(),
  val report:ServiceReport? = null,
  val downloadCurrent:Long = 0,
  val downloadTotal:Long? = null,
  val updateNotice:String? = null,
  val appUpdate:AppUpdateInfo? = null,
  val updateMessage:String? = null,
  val storageBytes:Long = 0,
  val advancedMode:Boolean = false,
  val meshtasticLinkState:MeshtasticLinkState = MeshtasticLinkState.UNKNOWN,
  val diagnosticLog:List<String> = emptyList()
)

class MainViewModel(app:Application):AndroidViewModel(app){
  private val repo=PackageRepository(app)
  private val trust=PublisherTrustStore(app)
  val provisioning=ProvisioningController(app)
  private val store=DeploymentStore(app)
  private val engine=DeploymentEngine(store)
  private val diagnostics=ServerDiagnostics(app)
  private val diagnosticLogger=DiagnosticLogger(app)
  private val updates=UpdateService(app)
  private val storage=StorageMaintenance(app)
  private val meshtastic=MeshtasticManager(app)
  private val prefs=app.getSharedPreferences("fieldtak_hub", android.content.Context.MODE_PRIVATE)
  private val _state=MutableStateFlow(AppUiState(history=historySnapshot(),session=store.active(),storageBytes=storage.totalBytes(),advancedMode=prefs.getBoolean("advanced_mode",false),meshtasticLinkState=meshtastic.linkState.value))
  val state=_state.asStateFlow()

  init {
    diagnosticLogger.info("APP_START", "version=2.3.0")
    _state.value=_state.value.copy(diagnosticLog=diagnosticLogger.recent())
    viewModelScope.launch {
      meshtastic.linkState.collect { link ->
        _state.value = _state.value.copy(meshtasticLinkState = link)
      }
    }
    storage.cleanup(store.retainedPaths())
    _state.value=_state.value.copy(storageBytes=storage.totalBytes())
    resumeActive()
    checkForUpdates(silent=true)
  }

  private fun text(id:Int,vararg args:Any)=getApplication<Application>().getString(id,*args)
  private fun logInfo(event:String,detail:String=""){ diagnosticLogger.info(event,detail); _state.value=_state.value.copy(diagnosticLog=diagnosticLogger.recent()) }
  private fun logWarning(event:String,detail:String=""){ diagnosticLogger.warning(event,detail); _state.value=_state.value.copy(diagnosticLog=diagnosticLogger.recent()) }
  private fun logError(event:String,detail:String=""){ diagnosticLogger.error(event,detail); _state.value=_state.value.copy(diagnosticLog=diagnosticLogger.recent()) }
  fun diagnosticLogText():String = diagnosticLogger.all()
  fun clearDiagnosticLog(){ diagnosticLogger.clear(); _state.value=_state.value.copy(diagnosticLog=emptyList()) }
  fun toggleAdvancedMode(){
    val next=!_state.value.advancedMode
    prefs.edit().putBoolean("advanced_mode",next).apply()
    _state.value=_state.value.copy(advancedMode=next)
  }
  private fun humanBytes(bytes:Long):String {
    val unit=1024.0; if(bytes<1024) return "$bytes B"
    val exp=(kotlin.math.ln(bytes.toDouble())/kotlin.math.ln(unit)).toInt().coerceIn(1,4)
    val prefix="KMGT"[exp-1]
    return String.format(java.util.Locale.getDefault(),"%.1f %sB",bytes/Math.pow(unit,exp.toDouble()),prefix)
  }

  /** Dedicated provisioning QR entry point: Field TAK packages/descriptors only. */
  fun handleProvisioningQr(value:String){
    val t=value.trim()
    logInfo("QR_PROVISIONING_RECEIVED")
    if(t.isBlank()){ _state.value=_state.value.copy(error=text(R.string.vm_empty_qr)); return }
    runCatching {
      when {
        t.startsWith("fieldtak://provision",true) -> {
          val uri=Uri.parse(t)
          val direct=uri.getQueryParameter("packageUrl")
          if(!direct.isNullOrBlank()) {
            val sha=uri.getQueryParameter("sha256")?.takeIf{it.isNotBlank()}
            val bytes=uri.getQueryParameter("packageBytes")?.toLongOrNull()
            val expires=uri.getQueryParameter("expiresUtc")?.takeIf{it.isNotBlank()}
            fromDirectPackageUrl(direct,sha,bytes,expires,t)
          } else fromDescriptor(t)
        }
        t.startsWith("https://",true) || t.startsWith("http://",true) -> {
          val uri=Uri.parse(t)
          val name=(uri.lastPathSegment ?: "").lowercase()
          if(name.endsWith(".ftak")) fromDirectPackageUrl(t,null,null,null,t) else fromDescriptor(t)
        }
        else -> error(text(R.string.vm_expected_provisioning_qr))
      }
    }.onFailure { fail(it) }
  }

  /** Dedicated OpenTAK/ATAK enrollment QR entry point. The token is never persisted or logged. */
  fun handleEnrollmentQr(value:String){
    val t=value.trim()
    logInfo("QR_ENROLLMENT_RECEIVED", "uri=REDACTED")
    if(t.isBlank()){ _state.value=_state.value.copy(error=text(R.string.vm_empty_qr)); return }
    runCatching {
      val uri=Uri.parse(t)
      val action=uri.pathSegments.firstOrNull()?.lowercase()
      require(uri.scheme.equals("tak",true) && uri.host.equals("com.atakmap.app",true) && action=="enroll") {
        text(R.string.vm_expected_enrollment_qr)
      }
      handOffTakUri(t)
    }.onFailure { fail(it) }
  }

  /** Dedicated OpenTAK Data Package QR entry point. Accepts ATAK import URIs or direct safe URLs. */
  fun handleDataPackageQr(value:String){
    val t=value.trim()
    logInfo("QR_DATA_PACKAGE_RECEIVED")
    if(t.isBlank()){ _state.value=_state.value.copy(error=text(R.string.vm_empty_qr)); return }
    runCatching {
      when {
        t.startsWith("tak://",true) -> {
          val uri=Uri.parse(t)
          val action=uri.pathSegments.firstOrNull()?.lowercase()
          require(uri.host.equals("com.atakmap.app",true) && action=="import") { text(R.string.vm_expected_data_package_qr) }
          handOffTakUri(t)
        }
        t.startsWith("https://",true) || t.startsWith("http://",true) -> handOffDataPackageUrl(t)
        else -> error(text(R.string.vm_expected_data_package_qr))
      }
    }.onFailure { fail(it) }
  }

  /** Universal input router used by deep links and the manual link field.
   * Dedicated scanner buttons above use stricter routing so an enrollment QR cannot be
   * accidentally treated as a phone-provisioning package (and vice versa).
   */
  fun handleInput(value:String){
    val t=value.trim()
    logInfo("INPUT_RECEIVED", "type=external")
    if(t.isBlank()){ _state.value=_state.value.copy(error=text(R.string.vm_empty_qr)); return }
    runCatching {
      when {
        t.startsWith("fieldtak://provision",true) -> {
          val uri=Uri.parse(t)
          val direct=uri.getQueryParameter("packageUrl")
          if(!direct.isNullOrBlank()) {
            val sha=uri.getQueryParameter("sha256")?.takeIf{it.isNotBlank()}
            val bytes=uri.getQueryParameter("packageBytes")?.toLongOrNull()
            val expires=uri.getQueryParameter("expiresUtc")?.takeIf{it.isNotBlank()}
            fromDirectPackageUrl(direct,sha,bytes,expires,t)
          } else fromDescriptor(t)
        }
        t.startsWith("tak://",true) -> handOffTakUri(t)
        t.startsWith("https://meshtastic.org/e/",true) -> handOffMeshtasticChannel(t)
        t.startsWith("http://meshtastic.org/e/",true) -> error(text(R.string.vm_meshtastic_https_required))
        t.startsWith("https://",true) || t.startsWith("http://",true) -> {
          val uri=Uri.parse(t)
          val name=(uri.lastPathSegment ?: "").lowercase()
          val path=(uri.path ?: "").lowercase()
          when {
            name.endsWith(".ftak") -> fromDirectPackageUrl(t,null,null,null,t)
            name.endsWith(".zip") || name.endsWith(".dpk") || name.endsWith(".tak") || path.contains("/api/data_packages") -> handOffDataPackageUrl(t)
            else -> fromDescriptor(t)
          }
        }
        else -> error(text(R.string.vm_unsupported_qr))
      }
    }.onFailure { fail(it) }
  }

  private fun handOffTakUri(value:String)=viewModelScope.launch(Dispatchers.Main){
    logInfo("ATAK_HANDOFF_STARTED")
    runCatching {
      require(provisioning.openTakUri(value)){text(R.string.vm_atak_required_for_qr)}
    }.onFailure { fail(it) }
  }

  private fun handOffDataPackageUrl(value:String)=viewModelScope.launch(Dispatchers.Main){
    logInfo("ATAK_DATA_PACKAGE_HANDOFF_STARTED")
    runCatching {
      require(provisioning.openTakImportUrl(value)){text(R.string.vm_atak_required_for_qr)}
    }.onFailure { fail(it) }
  }

  private fun handOffMeshtasticChannel(value:String)=viewModelScope.launch(Dispatchers.Main){
    runCatching {
      require(isMeshtasticInstalled()){text(R.string.vm_meshtastic_not_installed)}
      val intent=Intent(Intent.ACTION_VIEW,Uri.parse(value)).setPackage("com.geeksville.mesh")
      getApplication<Application>().startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }.onFailure { fail(it) }
  }

  private fun fromDirectPackageUrl(packageUrl:String,expectedSha256:String?,packageBytes:Long?,expiresUtc:String?,source:String)=viewModelScope.launch(Dispatchers.IO){
    runCatching {
      val safeUrl=UrlPolicy.requireProvisioningUrl(packageUrl)
      expiresUtc?.let { require(Instant.parse(it).isAfter(Instant.now())) { text(R.string.vm_descriptor_expired) } }
      val freeBytes=provisioning.freeStorageBytes()
      val requiredBytes=packageBytes?.takeIf{it>0}?.let{maxOf(512L*1024*1024,it*3)}
      if(requiredBytes!=null) require(freeBytes>=requiredBytes){text(R.string.disk_space_problem,humanBytes(requiredBytes),humanBytes(freeBytes))}
      var session=engine.start(source,safeUrl)
      session=engine.transition(session,DeploymentStage.QR_SCANNED,StepResult.READY,text(R.string.vm_qr_accepted))
      session=engine.transition(session,DeploymentStage.DESCRIPTOR_VERIFIED,StepResult.READY,text(R.string.vm_cloud_link_valid))
      session=engine.transition(session,DeploymentStage.DOWNLOADING,StepResult.RUNNING,text(R.string.vm_downloading_resume))
      update(session=session,busy=text(R.string.vm_downloading_package),progress=0L to null)
      val key=stableKey(safeUrl)
      val f=repo.downloadResumable(safeUrl,key,expectedSha256){cur,total->_state.value=_state.value.copy(downloadCurrent=cur,downloadTotal=total)}
      session=engine.transition(session,DeploymentStage.BUNDLE_DOWNLOADED,StepResult.READY,text(R.string.vm_package_downloaded))
      loadPackage(f,session)
    }.onFailure { fail(it) }
  }

  fun fromDescriptor(value:String)=viewModelScope.launch(Dispatchers.IO){
    runCatching {
      val descriptorUrl=normalize(value)
      var session=engine.start(descriptorUrl,null)
      session=engine.transition(session,DeploymentStage.QR_SCANNED,StepResult.READY,text(R.string.vm_qr_accepted))
      update(session=session,busy=text(R.string.vm_downloading_descriptor))
      val descriptor=SimpleJson.descriptor(repo.fetchDescriptor(descriptorUrl))
      descriptor.expiresUtc?.let { require(Instant.parse(it).isAfter(Instant.now())) { text(R.string.vm_descriptor_expired) } }
      val freeBytes=provisioning.freeStorageBytes()
      val requiredBytes=descriptor.recommendedFreeBytes?.takeIf{it>0}
        ?: descriptor.packageBytes?.takeIf{it>0}?.let{maxOf(512L*1024*1024,it*3)}
      if(requiredBytes!=null) require(freeBytes>=requiredBytes){text(R.string.disk_space_problem,humanBytes(requiredBytes),humanBytes(freeBytes))}
      session=session.copy(packageUrl=descriptor.packageUrl,updatedUtc=Instant.now().toString()).also(store::saveActive)
      session=engine.transition(session,DeploymentStage.DESCRIPTOR_VERIFIED,StepResult.READY,text(R.string.vm_descriptor_valid))
      session=engine.transition(session,DeploymentStage.DOWNLOADING,StepResult.RUNNING,text(R.string.vm_downloading_resume))
      update(session=session,busy=text(R.string.vm_downloading_package),progress=0L to null)
      val key=stableKey(descriptor.packageUrl)
      val f=repo.downloadResumable(descriptor.packageUrl,key,descriptor.packageSha256){cur,total->_state.value=_state.value.copy(downloadCurrent=cur,downloadTotal=total)}
      session=engine.transition(session,DeploymentStage.BUNDLE_DOWNLOADED,StepResult.READY,text(R.string.vm_package_downloaded))
      loadPackage(f,session)
    }.onFailure { fail(it) }
  }

  fun fromFile(uri:Uri)=viewModelScope.launch(Dispatchers.IO){
    runCatching {
      var session=engine.start(null,null)
      update(session=session,busy=text(R.string.vm_importing))
      val f=repo.copyFromUri(uri)
      session=engine.transition(session,DeploymentStage.BUNDLE_DOWNLOADED,StepResult.READY,text(R.string.vm_local_imported))
      loadPackage(f,session)
    }.onFailure { fail(it) }
  }

  private suspend fun loadPackage(f:File,starting:DeploymentSession){
    var session=starting
    update(session=session,busy=text(R.string.vm_verifying))
    val p=repo.verify(f)
    require(p.manifest.schema=="fieldtak.package" && p.manifest.schemaVersion==2){text(R.string.vm_unsupported_schema)}
    if(p.manifest.distribution.expiresUtc.isNotBlank()) require(Instant.parse(p.manifest.distribution.expiresUtc).isAfter(Instant.now())){text(R.string.vm_package_expired)}
    require(p.signatureValid){text(R.string.vm_bad_signature)}; require(p.hashesValid){text(R.string.vm_bad_hash)}
    session=engine.attachPackage(session,p.manifest.packageInfo.id,p.manifest.packageInfo.name,p.manifest.packageInfo.version,f.absolutePath,p.root.absolutePath)
    session=engine.transition(session,DeploymentStage.BUNDLE_VERIFIED,StepResult.READY,text(R.string.vm_bundle_verified))
    val trusted=trust.isTrusted(p.publisherFingerprint)
    val archived=store.history()
    val history=historySnapshot()
    val previous=archived.firstOrNull{it.packageId==p.manifest.packageInfo.id && it.packageVersion.isNotBlank()}
    val notice=previous?.takeIf{VersionUtil.compare(p.manifest.packageInfo.version,it.packageVersion)>0}?.let{text(R.string.vm_newer_config,it.packageVersion,p.manifest.packageInfo.version)}
    _state.value=_state.value.copy(pkg=p,trusted=trusted,session=session,busyMessage=null,error=null,history=history,updateNotice=notice,storageBytes=storage.totalBytes(),deploymentPlan=null)
    preflight()
  }

  fun trustPublisher(){
    val p=_state.value.pkg ?: return
    trust.trust(p.publisherFingerprint)
    _state.value=_state.value.copy(trusted=true)
    preflight()
  }

  fun deploymentPlan(sessionOverride:DeploymentSession? = _state.value.session, reportOverride:ServiceReport? = _state.value.report):DeploymentPlan? {
    val p=_state.value.pkg ?: return null
    val s=sessionOverride
    val atak=provisioning.detectAtak(p.manifest.target)
    val tasks=mutableListOf<DeploymentTask>()
    tasks += DeploymentTask("atak","ATAK",DeploymentTaskKind.CORE,
      if(atak.installed && atak.compatibility==CheckState.READY) DeploymentTaskState.READY else DeploymentTaskState.ACTION_REQUIRED,
      !p.manifest.deployment.atakRequired,atak.compatibilityMessage.ifBlank { atak.versionName ?: text(R.string.pc_atak_missing) })
    provisioning.pluginStatuses(p.root).forEachIndexed { index,pl ->
      val state=when {
        pl.state==PluginInstallState.CURRENT -> DeploymentTaskState.READY
        pl.state==PluginInstallState.NOT_INSTALLED || pl.state==PluginInstallState.UPDATE_REQUIRED -> DeploymentTaskState.ACTION_REQUIRED
        else -> DeploymentTaskState.UNKNOWN
      }
      tasks += DeploymentTask("plugin_$index",pl.label,DeploymentTaskKind.PLUGIN,state,!p.manifest.deployment.pluginsRequired,
        "${pl.installedVersion ?: "—"} → ${pl.sourceVersion ?: "?"}")
    }
    val meshEnabled=p.manifest.meshtastic.enabled || provisioning.meshtasticChannelUrl(p.root)!=null
    if(meshEnabled){
      val skipped=s?.steps?.any{it.stage==DeploymentStage.MESHTASTIC_APP_READY && it.result==StepResult.SKIPPED}==true
      val installed=provisioning.isMeshtasticInstalled()
      val channel=provisioning.meshtasticChannelUrl(p.root)
      tasks += DeploymentTask("meshtastic","Meshtastic",DeploymentTaskKind.MESHTASTIC,
        when { skipped -> DeploymentTaskState.SKIPPED; !installed -> DeploymentTaskState.ACTION_REQUIRED; else -> DeploymentTaskState.READY },
        !p.manifest.deployment.meshtasticRequired, if(!installed) text(R.string.status_meshtastic_missing) else if(channel!=null) text(R.string.status_meshtastic_channel_ready) else text(R.string.status_meshtastic_ready))
      if(channel!=null){
        val cfgSkipped=s?.steps?.any{it.stage==DeploymentStage.MESHTASTIC_CONFIG && it.result==StepResult.SKIPPED}==true
        val cfgDone=s?.steps?.any{it.stage==DeploymentStage.MESHTASTIC_CONFIG && it.result==StepResult.READY}==true
        tasks += DeploymentTask("meshtastic_config","Meshtastic channel",DeploymentTaskKind.MESHTASTIC,
          when { cfgSkipped -> DeploymentTaskState.SKIPPED; cfgDone -> DeploymentTaskState.READY; else -> DeploymentTaskState.ACTION_REQUIRED },true,
          if(cfgDone) text(R.string.vm_meshtastic_config_done) else text(R.string.vm_meshtastic_config_pending))
      }
    }
    val enrollment=provisioning.enrollmentUri(p.root)
    if(enrollment!=null){
      val skipped=s?.steps?.any{it.stage==DeploymentStage.OTS_ENROLLMENT && it.result==StepResult.SKIPPED}==true
      val handed=s?.steps?.any{it.stage==DeploymentStage.OTS_ENROLLMENT && it.result in setOf(StepResult.READY,StepResult.WARNING)}==true
      tasks += DeploymentTask("enrollment","OpenTAK enrollment",DeploymentTaskKind.ENROLLMENT,
        when { skipped -> DeploymentTaskState.SKIPPED; handed -> DeploymentTaskState.READY; else -> DeploymentTaskState.ACTION_REQUIRED },!p.manifest.deployment.enrollmentRequired,
        if(handed) text(R.string.vm_enrollment_done) else text(R.string.vm_enrollment_handoff))
    }
    val mission=provisioning.missionPackage(p.root)
    if(mission!=null){
      val skipped=s?.steps?.any{it.stage==DeploymentStage.MAPS_READY && it.result==StepResult.SKIPPED}==true
      val handed=s?.steps?.any{it.stage==DeploymentStage.MAPS_READY && it.result in setOf(StepResult.READY,StepResult.WARNING)}==true
      tasks += DeploymentTask("maps","ATAK data / maps",DeploymentTaskKind.MAPS,
        when { skipped -> DeploymentTaskState.SKIPPED; handed -> DeploymentTaskState.READY; else -> DeploymentTaskState.OPTIONAL },true,
        if(handed) text(R.string.vm_mission_done) else text(R.string.vm_mission_optional))
    }
    val report=reportOverride
    val net=report?.checks?.filter{it.id in setOf("dns","api","web","cot")}.orEmpty()
    if(net.isNotEmpty()) tasks += DeploymentTask("ots","OpenTAK Server",DeploymentTaskKind.OTS,
      if(net.all{it.state==CheckState.READY}) DeploymentTaskState.READY else DeploymentTaskState.ACTION_REQUIRED,!p.manifest.deployment.serverRequired,
      if(net.all{it.state==CheckState.READY}) text(R.string.vm_ots_reachable) else text(R.string.vm_server_attention))
    val required=tasks.filterNot{it.optional}.all{it.state==DeploymentTaskState.READY || it.state==DeploymentTaskState.SKIPPED}
    return DeploymentPlan(tasks,required,tasks.count{it.optional && it.state==DeploymentTaskState.OPTIONAL})
  }

  fun preflight()=viewModelScope.launch(Dispatchers.IO){
    val p=_state.value.pkg ?: return@launch
    var s=_state.value.session ?: return@launch
    if(s.stage==DeploymentStage.COMPLETE){
      val readiness=applySessionReadiness(withServerReadiness(provisioning.computeReadiness(p,_state.value.trusted),_state.value.report),s)
      update(session=s,readiness=readiness,busy=null); return@launch
    }
    s=engine.transition(s,DeploymentStage.PREFLIGHT,StepResult.RUNNING,text(R.string.vm_preflight_phone))
    update(session=s,busy=text(R.string.vm_preflight_busy))
    val report=diagnostics.run(p.manifest.server,p.manifest.meshtastic)
    val readiness=provisioning.computeReadiness(p,_state.value.trusted)
    val atak=provisioning.detectAtak(p.manifest.target)
    s=if(atak.installed && atak.compatibility==CheckState.READY) engine.transition(s,DeploymentStage.ATAK_READY,StepResult.READY,"ATAK ${atak.versionName ?: "?"}")
      else engine.transition(s,DeploymentStage.ATAK_READY,StepResult.ACTION_REQUIRED,atak.compatibilityMessage)
    val pending=provisioning.pendingPlugins(p.root)
    if(pending.isEmpty()) s=engine.transition(s,DeploymentStage.PLUGINS_READY,StepResult.READY,text(R.string.vm_plugins_current))
    update(session=s,readiness=applySessionReadiness(withServerReadiness(readiness,report),s),report=report,plan=deploymentPlan(s,report),busy=null)
  }

  fun preparePhone()=viewModelScope.launch(Dispatchers.IO){
    val p=_state.value.pkg ?: return@launch
    var s=_state.value.session ?: return@launch
    try {
      if(s.stage==DeploymentStage.COMPLETE){
        val report=diagnostics.run(p.manifest.server,p.manifest.meshtastic)
        val readiness=applySessionReadiness(withServerReadiness(provisioning.computeReadiness(p,_state.value.trusted),report),s)
        update(session=s,report=report,readiness=readiness,busy=null); return@launch
      }
      if(!_state.value.trusted){ s=engine.transition(s,DeploymentStage.WAITING_FOR_USER,StepResult.ACTION_REQUIRED,text(R.string.vm_trust_publisher)); update(session=s); return@launch }
      val atak=provisioning.detectAtak(p.manifest.target)
      if(!atak.installed || atak.compatibility!=CheckState.READY){
        val apk=provisioning.atakApk(p.root)
        if(apk==null){
          s=engine.transition(s,DeploymentStage.WAITING_FOR_USER,StepResult.ACTION_REQUIRED,atak.compatibilityMessage); update(session=s); return@launch
        }
        if(!provisioning.canInstallPackages()){
          s=engine.transition(s,DeploymentStage.WAITING_FOR_USER,StepResult.ACTION_REQUIRED,text(R.string.vm_allow_unknown_sources)); update(session=s)
          withContext(Dispatchers.Main){provisioning.openUnknownSourcesSettings()}
          return@launch
        }
        s=engine.transition(s,DeploymentStage.ATAK_READY,StepResult.ACTION_REQUIRED,text(R.string.vm_atak_installer_started)); update(session=s)
        withContext(Dispatchers.Main){provisioning.installApk(apk)}
        return@launch
      }
      val meshtasticSkipped=s.steps.any{it.stage==DeploymentStage.MESHTASTIC_APP_READY && it.result==StepResult.SKIPPED}
      val meshtasticApk=provisioning.meshtasticApk(p.root)
      if(!meshtasticSkipped && meshtasticApk!=null && provisioning.meshtasticApkNeedsInstall(p.root)){
        if(!provisioning.canInstallPackages()){
          s=engine.transition(s,DeploymentStage.WAITING_FOR_USER,StepResult.ACTION_REQUIRED,text(R.string.vm_allow_unknown_sources)); update(session=s)
          withContext(Dispatchers.Main){provisioning.openUnknownSourcesSettings()}
          return@launch
        }
        s=engine.transition(s,DeploymentStage.MESHTASTIC_APP_INSTALLING,StepResult.ACTION_REQUIRED,text(R.string.vm_meshtastic_app_installer_started)); update(session=s)
        withContext(Dispatchers.Main){provisioning.installApk(meshtasticApk)}
        return@launch
      }
      if(meshtasticApk!=null && !meshtasticSkipped){
        s=engine.transition(s,DeploymentStage.MESHTASTIC_APP_READY,StepResult.READY,text(R.string.vm_meshtastic_app_ready))
      }
      val meshChannel=provisioning.meshtasticChannelUrl(p.root)
      val meshConfigDone=s.steps.any{it.stage==DeploymentStage.MESHTASTIC_CONFIG && it.result in setOf(StepResult.READY,StepResult.SKIPPED)}
      if(meshChannel!=null && !meshConfigDone){
        if(!provisioning.isMeshtasticInstalled()){
          s=engine.transition(s,DeploymentStage.MESHTASTIC_CONFIG,StepResult.ACTION_REQUIRED,text(R.string.vm_meshtastic_not_installed)); update(session=s,plan=deploymentPlan(s)); return@launch
        }
        s=engine.transition(s,DeploymentStage.MESHTASTIC_CONFIG,StepResult.ACTION_REQUIRED,text(R.string.vm_meshtastic_config_started)); update(session=s,plan=deploymentPlan(s))
        withContext(Dispatchers.Main){require(provisioning.openMeshtasticChannel(meshChannel)){text(R.string.vm_meshtastic_open_failed)}}
        return@launch
      }
      val pending=provisioning.pendingPlugins(p.root)
      if(pending.isNotEmpty()){
        if(!provisioning.canInstallPackages()){ s=engine.transition(s,DeploymentStage.WAITING_FOR_USER,StepResult.ACTION_REQUIRED,text(R.string.vm_allow_unknown_sources)); update(session=s); withContext(Dispatchers.Main){provisioning.openUnknownSourcesSettings()}; return@launch }
        val next=pending.first(); s=engine.transition(s,DeploymentStage.PLUGINS_INSTALLING,StepResult.ACTION_REQUIRED,"${next.label}: ${next.sourceVersion ?: next.file.name}"); update(session=s)
        withContext(Dispatchers.Main){provisioning.installApk(next.file)}; return@launch
      }
      s=engine.transition(s,DeploymentStage.PLUGINS_READY,StepResult.READY,text(R.string.vm_plugins_ready))
      val enrollment=provisioning.enrollmentUri(p.root)
      val enrollmentAlreadyHanded=s.steps.any{it.stage==DeploymentStage.OTS_ENROLLMENT && (it.result==StepResult.READY || it.result==StepResult.WARNING)}
      if(enrollment!=null && !enrollmentAlreadyHanded){
        s=engine.transition(s,DeploymentStage.OTS_ENROLLMENT,StepResult.ACTION_REQUIRED,text(R.string.vm_enrollment_handoff)); update(session=s)
        withContext(Dispatchers.Main){require(provisioning.openEnrollmentUri(enrollment)){text(R.string.vm_atak_required_for_qr)}}
        return@launch
      }
      val mission=provisioning.missionPackage(p.root)
      val alreadyHanded=s.steps.any{it.stage==DeploymentStage.MAPS_READY && (it.result==StepResult.READY || it.result==StepResult.WARNING)}
      if(mission!=null && !alreadyHanded){ s=engine.transition(s,DeploymentStage.MAPS_IMPORTING,StepResult.ACTION_REQUIRED,text(R.string.vm_handoff_mission)); update(session=s); withContext(Dispatchers.Main){provisioning.openMissionPackage(mission)}; return@launch }
      verifyOtsAndFinish(s)
    } catch(t:Throwable){ fail(t) }
  }

  fun onExternalReturn(){
    viewModelScope.launch(Dispatchers.IO){
      val p=_state.value.pkg ?: return@launch
      var s=_state.value.session ?: return@launch
      if(s.stage==DeploymentStage.COMPLETE){
        val readiness=applySessionReadiness(withServerReadiness(provisioning.computeReadiness(p,_state.value.trusted),_state.value.report),s)
        update(session=s,readiness=readiness,busy=null); return@launch
      }
      when(s.stage){
        DeploymentStage.PLUGINS_INSTALLING -> if(provisioning.pendingPlugins(p.root).isEmpty()) s=engine.transition(s,DeploymentStage.PLUGINS_READY,StepResult.READY,text(R.string.vm_plugin_install_complete))
        DeploymentStage.MAPS_IMPORTING -> s=engine.transition(s,DeploymentStage.MAPS_READY,StepResult.READY,text(R.string.vm_mission_handed))
        DeploymentStage.MESHTASTIC_CONFIG -> s=engine.transition(s,DeploymentStage.MESHTASTIC_CONFIG,StepResult.READY,text(R.string.vm_meshtastic_config_done))
        DeploymentStage.OTS_ENROLLMENT -> s=engine.transition(s,DeploymentStage.OTS_ENROLLMENT,StepResult.READY,text(R.string.vm_enrollment_done))
        else -> Unit
      }
      update(session=s)

      // Returning from the per-app "Install unknown apps" settings or from the Android
      // package installer should continue the deployment automatically when the required
      // condition is now satisfied. Do not reopen settings if the user denied permission.
      val atak=provisioning.detectAtak(p.manifest.target)
      val canContinueInstall=s.stage==DeploymentStage.WAITING_FOR_USER && provisioning.canInstallPackages() && (
        (!atak.installed && provisioning.atakApk(p.root)!=null) ||
        (provisioning.meshtasticApk(p.root)!=null && provisioning.meshtasticApkNeedsInstall(p.root)) ||
        (atak.installed && provisioning.pendingPlugins(p.root).isNotEmpty())
      )
      val atakJustInstalled=atak.installed && s.stage==DeploymentStage.ATAK_READY
      val meshtasticJustInstalled=s.stage==DeploymentStage.MESHTASTIC_APP_INSTALLING
      val pluginStepDone=s.stage==DeploymentStage.PLUGINS_READY
      val missionReturned=s.stage==DeploymentStage.MAPS_READY
      val meshConfigReturned=s.stage==DeploymentStage.MESHTASTIC_CONFIG
      val enrollmentReturned=s.steps.any{it.stage==DeploymentStage.OTS_ENROLLMENT && it.result==StepResult.WARNING}
      if(_state.value.trusted && (canContinueInstall || atakJustInstalled || pluginStepDone || missionReturned || meshConfigReturned || enrollmentReturned)){
        preparePhone()
      } else preflight()
    }
  }

  fun onPackageChanged(){ onExternalReturn() }

  fun runSmartDiagnosis()=viewModelScope.launch(Dispatchers.IO){
    val p=_state.value.pkg
    if(p==null){ _state.value=_state.value.copy(error=text(R.string.vm_diagnostics_package_required)); return@launch }
    update(busy=text(R.string.smart_diagnosis))
    val report=diagnostics.run(p.manifest.server,p.manifest.meshtastic)
    val readiness=provisioning.computeReadiness(p,_state.value.trusted)
    val newLink=meshtastic.refreshFromLaunchState()
    _state.value=_state.value.copy(report=report,readiness=applySessionReadiness(withServerReadiness(readiness,report),_state.value.session),meshtasticLinkState=newLink,busyMessage=null,updateMessage=text(R.string.smart_recovery_done))
  }

  fun recoverCurrentSetup()=viewModelScope.launch(Dispatchers.IO){
    val p=_state.value.pkg
    if(p==null){ _state.value=_state.value.copy(error=text(R.string.recovery_no_package)); return@launch }
    if(!_state.value.trusted){ _state.value=_state.value.copy(error=text(R.string.vm_trust_publisher)); return@launch }
    update(busy=text(R.string.smart_recovery_started))
    preparePhone()
  }

  fun runDiagnostics()=viewModelScope.launch(Dispatchers.IO){
    logInfo("DIAGNOSTIC_STARTED")
    val p=_state.value.pkg
    if(p==null){
      _state.value=_state.value.copy(error=text(R.string.vm_diagnostics_package_required))
      return@launch
    }
    update(busy=text(R.string.vm_ots_diagnostics))
    val r=diagnostics.run(p.manifest.server,p.manifest.meshtastic)
    r.checks.filter{it.state==CheckState.PROBLEM}.forEach{ logError("DIAGNOSTIC_CHECK_FAILED", "check=${it.id}") }
    if(r.checks.none{it.state==CheckState.PROBLEM}) logInfo("DIAGNOSTIC_COMPLETED", "result=PASS")
    val base=provisioning.computeReadiness(p,_state.value.trusted)
    update(report=r,readiness=applySessionReadiness(withServerReadiness(base,r),_state.value.session),busy=null)
  }

  private suspend fun verifyOtsAndFinish(start:DeploymentSession){
    val p=_state.value.pkg ?: return
    var s=engine.transition(start,DeploymentStage.OTS_VERIFYING,StepResult.RUNNING,text(R.string.vm_checking_ports))
    update(session=s,busy=text(R.string.vm_checking_ots))
    val report=diagnostics.run(p.manifest.server,p.manifest.meshtastic)
    _state.value=_state.value.copy(report=report)
    val networkOk=report.checks.filter{it.id in setOf("dns","api","web","cot")}.all{it.state==CheckState.READY}
    s=if(networkOk) engine.transition(s,DeploymentStage.OTS_CONNECTED,StepResult.WARNING,text(R.string.vm_server_reachable))
      else engine.transition(s,DeploymentStage.OTS_CONNECTED,StepResult.ACTION_REQUIRED,text(R.string.vm_server_attention))
    val readiness=applySessionReadiness(withServerReadiness(provisioning.computeReadiness(p,_state.value.trusted),report),s)
    val plan=deploymentPlan()
    if(networkOk && readiness.items.none{it.state==CheckState.PROBLEM} && provisioning.pendingPlugins(p.root).isEmpty() && (plan?.requiredReady != false)){
      s=engine.transition(s,DeploymentStage.FINAL_VERIFY,StepResult.READY,text(R.string.vm_local_checks_complete))
      val detail=if(plan?.optionalPending ?: 0 > 0) text(R.string.vm_phone_prepared_optional) else text(R.string.vm_phone_prepared)
      s=engine.complete(s,detail)
    }
    _state.value=_state.value.copy(session=s,report=report,readiness=applySessionReadiness(readiness,s),busyMessage=null,error=null,history=historySnapshot())
  }

  fun checkForUpdates(silent:Boolean=false)=viewModelScope.launch(Dispatchers.IO){
    runCatching { updates.check() }.onSuccess { info ->
      _state.value=_state.value.copy(appUpdate=info,updateMessage=if(info==null && !silent) text(R.string.no_update) else null)
    }.onFailure { t -> if(!silent) _state.value=_state.value.copy(updateMessage=text(R.string.update_check_failed,t.message ?: t::class.java.simpleName)) }
  }

  fun installAppUpdate()=viewModelScope.launch(Dispatchers.IO){
    val info=_state.value.appUpdate ?: return@launch
    runCatching {
      update(busy=text(R.string.vm_downloading_package),progress=0L to null)
      val apk=updates.download(info){cur,total->_state.value=_state.value.copy(downloadCurrent=cur,downloadTotal=total)}
      update(busy=null)
      if(!provisioning.canInstallPackages()){
        _state.value=_state.value.copy(updateMessage=text(R.string.update_install_permission)); withContext(Dispatchers.Main){provisioning.openUnknownSourcesSettings()}
      } else {
        _state.value=_state.value.copy(updateMessage=text(R.string.update_downloaded)); withContext(Dispatchers.Main){provisioning.installApk(apk)}
      }
    }.onFailure { t -> _state.value=_state.value.copy(busyMessage=null,updateMessage=text(R.string.update_download_failed,t.message ?: t::class.java.simpleName)) }
  }

  fun cleanupStorage(){
    viewModelScope.launch {
      meshtastic.linkState.collect { link ->
        _state.value = _state.value.copy(meshtasticLinkState = link)
      }
    }
    storage.cleanup(store.retainedPaths())
    _state.value=_state.value.copy(storageBytes=storage.totalBytes(),updateMessage=text(R.string.cleanup_done))
  }

  fun retryCurrent(){ preparePhone() }

  fun isMeshtasticSkipped():Boolean = _state.value.session?.steps?.any{
    (it.stage==DeploymentStage.MESHTASTIC_APP_READY || it.stage==DeploymentStage.MESHTASTIC_CONFIG) && it.result==StepResult.SKIPPED
  } == true

  fun optionalStepLabel():String? {
    val s=_state.value.session ?: return null
    return when {
      s.stage==DeploymentStage.OTS_ENROLLMENT -> text(R.string.skip_enrollment_label)
      s.stage==DeploymentStage.MAPS_IMPORTING -> text(R.string.skip_data_package_label)
      s.stage==DeploymentStage.WAITING_FOR_USER &&
        provisioning.detectAtak(_state.value.pkg?.manifest?.target).installed &&
        provisioning.meshtasticApk(_state.value.pkg?.root ?: File(""))?.let{provisioning.meshtasticApkNeedsInstall(_state.value.pkg!!.root)} == true -> text(R.string.skip_meshtastic_label)
      else -> null
    }
  }

  fun skipMeshtastic(){
    viewModelScope.launch(Dispatchers.IO){
      val p=_state.value.pkg ?: return@launch
      var s=_state.value.session ?: return@launch
      logInfo("OPTIONAL_STEP_SKIPPED","step=meshtastic")
      s=engine.transition(s,DeploymentStage.MESHTASTIC_APP_READY,StepResult.SKIPPED,text(R.string.vm_meshtastic_skipped))
      update(session=s,updateMessage=text(R.string.vm_meshtastic_skipped))
      preparePhone()
    }
  }

  fun skipOptionalStep(){
    viewModelScope.launch(Dispatchers.IO){
      val p=_state.value.pkg ?: return@launch
      var s=_state.value.session ?: return@launch
      when(s.stage){
        DeploymentStage.OTS_ENROLLMENT -> {
          logInfo("OPTIONAL_STEP_SKIPPED","step=enrollment")
          s=engine.transition(s,DeploymentStage.OTS_ENROLLMENT,StepResult.SKIPPED,text(R.string.vm_enrollment_skipped))
        }
        DeploymentStage.MAPS_IMPORTING -> {
          logInfo("OPTIONAL_STEP_SKIPPED","step=data_package")
          s=engine.transition(s,DeploymentStage.MAPS_READY,StepResult.SKIPPED,text(R.string.vm_data_package_skipped))
        }
        DeploymentStage.WAITING_FOR_USER -> {
          val apk=provisioning.meshtasticApk(p.root)
          if(apk!=null && provisioning.meshtasticApkNeedsInstall(p.root)){
            logInfo("OPTIONAL_STEP_SKIPPED","step=meshtastic")
            s=engine.transition(s,DeploymentStage.MESHTASTIC_APP_READY,StepResult.SKIPPED,text(R.string.vm_meshtastic_skipped))
          } else return@launch
        }
        else -> return@launch
      }
      update(session=s,updateMessage=text(R.string.optional_step_skipped))
      preparePhone()
    }
  }

  fun skipMeshtasticSetup(){
    viewModelScope.launch(Dispatchers.IO){
      val s0=_state.value.session ?: return@launch
      logInfo("OPTIONAL_STEP_SKIPPED","step=meshtastic_radio")
      val s=engine.transition(s0,DeploymentStage.MESHTASTIC_CONFIG,StepResult.SKIPPED,text(R.string.vm_meshtastic_skipped))
      update(session=s,updateMessage=text(R.string.vm_meshtastic_skipped))
      preparePhone()
    }
  }

  fun isAtakInstalled():Boolean = provisioning.detectAtak().installed

  fun openAtak(){
    runCatching { require(provisioning.openAtak()) { text(R.string.vm_atak_open_failed) } }
      .onFailure { _state.value=_state.value.copy(error=it.message ?: text(R.string.vm_atak_open_failed)) }
  }

  fun enrollmentUri():String? = _state.value.pkg?.let { provisioning.enrollmentUri(it.root) }

  fun openEnrollment(){
    val uri=enrollmentUri() ?: return
    runCatching { require(provisioning.openEnrollmentUri(uri)){text(R.string.vm_atak_required_for_qr)} }.onFailure{_state.value=_state.value.copy(error=it.message ?: text(R.string.vm_enrollment_invalid))}
  }

  fun meshtasticChannelUrl():String? = _state.value.pkg?.let { provisioning.meshtasticChannelUrl(it.root) }
  fun meshtasticProfile():org.fieldtak.hub.model.MeshtasticProfile = _state.value.pkg?.manifest?.meshtastic ?: org.fieldtak.hub.model.MeshtasticProfile()

  fun meshtasticStatus() = meshtastic.status(meshtasticChannelUrl()!=null, meshtasticProfile().mode, meshtasticProfile().deviceRole, meshtasticProfile().gateway)
  fun refreshMeshtasticStatus() { _state.value = _state.value.copy(meshtasticLinkState = meshtastic.refreshFromLaunchState(), updateMessage = text(R.string.vm_meshtastic_status_refreshed)) }

  fun openMeshtasticApp(){
    runCatching {
      val i=getApplication<Application>().packageManager.getLaunchIntentForPackage(MeshtasticManager.MESHTASTIC_PACKAGE)
      requireNotNull(i){ text(R.string.vm_meshtastic_not_installed) }
      getApplication<Application>().startActivity(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }.onFailure { _state.value=_state.value.copy(error=it.message ?: text(R.string.vm_meshtastic_open_failed)) }
  }

  fun testMeshtastic(){
    val installed=isMeshtasticInstalled()
    val url=meshtasticChannelUrl()
    when {
      !installed -> _state.value=_state.value.copy(error=text(R.string.vm_meshtastic_not_installed))
      url.isNullOrBlank() -> _state.value=_state.value.copy(updateMessage=text(R.string.vm_meshtastic_no_channel))
      else -> _state.value=_state.value.copy(updateMessage=text(R.string.vm_meshtastic_test_ready))
    }
  }

  fun openMeshtasticChannel(){
    val url=meshtasticChannelUrl() ?: return
    if(!isMeshtasticInstalled()){ _state.value=_state.value.copy(error=text(R.string.vm_meshtastic_not_installed)); return }
    runCatching { require(provisioning.openMeshtasticChannel(url)) { text(R.string.vm_meshtastic_open_failed) } }
      .onFailure { _state.value=_state.value.copy(error=it.message ?: text(R.string.vm_meshtastic_open_failed)) }
  }

  /**
   * Starts the user-facing one-tap Meshtastic setup flow.
   * The Hub never talks to BLE/USB itself: the official Meshtastic app or ATAK
   * plugin remains the owner of the radio link. The channel deep-link is the
   * supported automation point; device/profile settings are then verified by
   * Smart Diagnosis rather than falsely reported as written by Hub.
   */
  fun configureMeshtastic(){
    logInfo("MESHTASTIC_CONFIGURE_REQUESTED")
    val profile=meshtasticProfile()
    val url=meshtasticChannelUrl()
    if(!profile.enabled || url.isNullOrBlank()){
      _state.value=_state.value.copy(error=text(R.string.vm_meshtastic_setup_missing))
      return
    }
    if(isMeshtasticInstalled()){
      _state.value=_state.value.copy(updateMessage=text(R.string.vm_meshtastic_setup_starting))
      runCatching {
        require(provisioning.openMeshtasticChannel(url)) { text(R.string.vm_meshtastic_open_failed) }
      }.onFailure { _state.value=_state.value.copy(error=it.message ?: text(R.string.vm_meshtastic_open_failed)) }
      return
    }
    val direct=meshtastic.directPluginHint()
    if(direct && isAtakInstalled()){
      _state.value=_state.value.copy(updateMessage=text(R.string.vm_meshtastic_direct_plugin))
      openAtak()
      return
    }
    _state.value=_state.value.copy(error=text(R.string.vm_meshtastic_not_installed))
  }

  fun isMeshtasticInstalled():Boolean = provisioning.isMeshtasticInstalled()

  override fun onCleared(){
    meshtastic.close()
    super.onCleared()
  }

  fun resetPackage(){
    _state.value=_state.value.copy(pkg=null,trusted=false,readiness=null,session=null,report=null,error=null,busyMessage=null,downloadCurrent=0,downloadTotal=null,updateNotice=null,storageBytes=storage.totalBytes())
    store.clearActive()
  }

  fun clearError(){ _state.value=_state.value.copy(error=null,busyMessage=null) }

  fun clearHistory(){ store.clearHistory(); _state.value=_state.value.copy(history=historySnapshot()) }

  fun restoreHistory(id:String)=viewModelScope.launch(Dispatchers.IO){
    val old=historySnapshot().firstOrNull{it.id==id} ?: return@launch
    val path=old.localPackagePath ?: return@launch
    val f=File(path); if(!f.isFile){ _state.value=_state.value.copy(error=text(R.string.vm_history_missing)); return@launch }
    runCatching {
      val session=engine.start(old.sourceDescriptor,old.packageUrl)
      loadPackage(f,engine.transition(session,DeploymentStage.BUNDLE_DOWNLOADED,StepResult.READY,text(R.string.vm_history_reuse)))
    }.onFailure{fail(it)}
  }

  private fun historySnapshot():List<DeploymentSession> {
    val archived=store.history()
    val active=store.active()?.takeIf{it.stage!=DeploymentStage.COMPLETE}
    return listOfNotNull(active)+archived.filterNot{it.id==active?.id}
  }

  private fun resumeActive(){
    val s=store.active() ?: return
    val path=s.localPackagePath
    if(path.isNullOrBlank()){
      if(s.stage in setOf(DeploymentStage.DOWNLOADING,DeploymentStage.DESCRIPTOR_VERIFIED,DeploymentStage.QR_SCANNED) && !s.sourceDescriptor.isNullOrBlank()){
        handleInput(s.sourceDescriptor); return
      }
      return
    }
    val f=File(path); if(!f.isFile) return
    viewModelScope.launch(Dispatchers.IO){ runCatching { loadPackage(f,s) }.onFailure { fail(it) } }
  }

  private fun fail(t:Throwable){
    val message=t.message ?: t::class.java.simpleName
    logError("OPERATION_FAILED", "type=${t::class.java.simpleName} message=$message")
    val s=_state.value.session?.let{engine.fail(it,message)}
    _state.value=_state.value.copy(error=message,busyMessage=null,session=s,history=historySnapshot())
  }

  private fun applySessionReadiness(base:PhoneReadiness,session:DeploymentSession?):PhoneReadiness {
    if(session==null) return base
    val missionDone=session.steps.any{it.stage==DeploymentStage.MAPS_READY && it.result in setOf(StepResult.READY,StepResult.WARNING)}
    val complete=session.stage==DeploymentStage.COMPLETE
    val items=base.items.map { item ->
      when {
        item.id=="mission" && missionDone -> item.copy(state=CheckState.READY,detail=text(R.string.vm_mission_done))
        item.id=="mission" && session.steps.any{it.stage==DeploymentStage.MAPS_READY && it.result==StepResult.SKIPPED} -> item.copy(state=CheckState.READY,detail=text(R.string.vm_data_package_skipped))
        item.id=="enrollment" && session.steps.any{it.stage==DeploymentStage.OTS_ENROLLMENT && it.result==StepResult.SKIPPED} -> item.copy(state=CheckState.READY,detail=text(R.string.vm_enrollment_skipped))
        item.id=="meshtastic" && session.steps.any{it.stage==DeploymentStage.MESHTASTIC_APP_READY && it.result==StepResult.SKIPPED} -> item.copy(state=CheckState.READY,detail=text(R.string.vm_meshtastic_skipped))
        item.id=="ots" && complete -> item.copy(state=CheckState.READY,detail=text(R.string.vm_ots_tests_done))
        else -> item
      }
    }
    val score=items.sumOf{when(it.state){CheckState.READY->100;CheckState.UNKNOWN->55;CheckState.ACTION_REQUIRED->25;CheckState.PROBLEM->0}}/maxOf(1,items.size)
    val ready=items.isNotEmpty() && items.all{it.state==CheckState.READY}
    return PhoneReadiness(score,items,ready)
  }

  private fun withServerReadiness(base:PhoneReadiness,report:ServiceReport?):PhoneReadiness {
    val p = _state.value.pkg ?: return base
    if(report==null) return base
    val net=report.checks.filter{it.id in setOf("dns","api","web","cot")}
    val state=when { net.any{it.state==CheckState.PROBLEM}->CheckState.ACTION_REQUIRED; net.isNotEmpty() && net.all{it.state==CheckState.READY}->CheckState.READY; else->CheckState.UNKNOWN }
    val items=base.items.filterNot{it.id in setOf("ots","atak_auth")}+listOf(
      ReadinessItem("ots","OpenTAK Server",state,if(state==CheckState.READY)text(R.string.vm_ots_reachable) else text(R.string.vm_check_diagnostics)),
      ReadinessItem("atak_auth",text(R.string.atak_connection),CheckState.UNKNOWN,text(R.string.vm_atak_auth_detail))
    )
    val score=items.sumOf{when(it.state){CheckState.READY->100;CheckState.UNKNOWN->55;CheckState.ACTION_REQUIRED->25;CheckState.PROBLEM->0}}/maxOf(1,items.size)
    val serverReady = !p.manifest.deployment.serverRequired || state==CheckState.READY
    return PhoneReadiness(score,items,base.ready && serverReady)
  }

  private fun update(session:DeploymentSession?=_state.value.session,readiness:PhoneReadiness?=_state.value.readiness,report:ServiceReport?=_state.value.report,plan:DeploymentPlan?=_state.value.deploymentPlan,busy:String?=_state.value.busyMessage,progress:Pair<Long,Long?>?=null,updateMessage:String?=_state.value.updateMessage){
    _state.value=_state.value.copy(session=session,readiness=readiness,report=report,deploymentPlan=plan,busyMessage=busy,error=null,updateMessage=updateMessage,
      downloadCurrent=progress?.first ?: _state.value.downloadCurrent,downloadTotal=progress?.second ?: _state.value.downloadTotal,history=historySnapshot())
  }

  private fun normalize(v:String):String {
    val t=v.trim()
    return if(t.startsWith("fieldtak://provision",true)) {
      val uri=Uri.parse(t)
      // Uri.getQueryParameter already URL-decodes once. A second decode corrupts valid % characters.
      uri.getQueryParameter("url") ?: error(text(R.string.vm_qr_missing_url))
    } else t
  }

  private fun stableKey(v:String):String = MessageDigest.getInstance("SHA-256").digest(v.toByteArray()).take(12).joinToString(""){"%02x".format(it)}
}
