package org.fieldtak.hub

import org.fieldtak.hub.deployment.DeploymentTaskState
import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import android.os.Bundle
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import org.fieldtak.hub.deployment.DeploymentStage
import org.fieldtak.hub.deployment.StepResult
import org.fieldtak.hub.meshtastic.MeshtasticLinkState
import org.fieldtak.hub.i18n.LanguageManager
import org.fieldtak.hub.model.CheckState
import org.fieldtak.hub.model.TargetInfo
import org.fieldtak.hub.model.PluginInstallState
import org.fieldtak.hub.ui.QrScanner
import org.fieldtak.hub.ui.FieldTakHubTheme
import org.fieldtak.hub.ui.FieldBrandHeader
import org.fieldtak.hub.ui.FieldPrimaryButton
import org.fieldtak.hub.ui.FieldOutlineButton
import org.fieldtak.hub.ui.FieldTonalButton
import org.fieldtak.hub.ui.FieldBg
import org.fieldtak.hub.ui.FieldCyan
import org.fieldtak.hub.ui.FieldGood
import org.fieldtak.hub.ui.FieldRed
import org.fieldtak.hub.ui.FieldMuted
import org.fieldtak.hub.ui.FieldPanel

class MainActivity:AppCompatActivity(){
  private val vm:MainViewModel by viewModels()
  private val packageReceiver=object:BroadcastReceiver(){ override fun onReceive(context:Context?,intent:Intent?){ vm.onPackageChanged() } }

  override fun onCreate(savedInstanceState:Bundle?){
    super.onCreate(savedInstanceState)
    val filter=IntentFilter().apply { addAction(Intent.ACTION_PACKAGE_ADDED); addAction(Intent.ACTION_PACKAGE_REPLACED); addAction(Intent.ACTION_PACKAGE_REMOVED); addDataScheme("package") }
    ContextCompat.registerReceiver(this,packageReceiver,filter,ContextCompat.RECEIVER_EXPORTED)
    setContent{FieldTakHubTheme{FieldTakApp(vm,this)}}
    handleIntent(intent)
  }
  override fun onNewIntent(intent:Intent){super.onNewIntent(intent);setIntent(intent);handleIntent(intent)}
  override fun onResume(){super.onResume();vm.onExternalReturn()}
  override fun onDestroy(){runCatching{unregisterReceiver(packageReceiver)};super.onDestroy()}
  private fun handleIntent(intent:Intent?){
    intent?.data?.let { data ->
      if(intent.action==Intent.ACTION_VIEW) vm.handleInput(data.toString())
    }
  }
}

enum class AppScreen { HOME, SMART_DIAGNOSIS, RECOVERY, DEVICE_STATUS, SCAN_UNIVERSAL, SCAN_PROVISIONING, SCAN_ENROLLMENT, SCAN_DATA_PACKAGE, DETAILS, SERVICE, LOGS, HISTORY, SETTINGS }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FieldTakApp(vm:MainViewModel,activity:MainActivity){
  val state by vm.state.collectAsStateWithLifecycle()
  val stack=remember{mutableStateListOf(AppScreen.HOME)}
  val screen=stack.last()
  fun push(s:AppScreen){if(stack.lastOrNull()!=s) stack.add(s)}
  fun back(){if(stack.size>1) stack.removeAt(stack.lastIndex)}

  var lastBackAt by remember { mutableLongStateOf(0L) }
  BackHandler(enabled=true){
    if(stack.size>1){
      back()
    } else {
      val now=System.currentTimeMillis()
      if(now-lastBackAt <= 2000L){
        activity.moveTaskToBack(true)
      } else {
        lastBackAt=now
        android.widget.Toast.makeText(activity,R.string.press_back_again,android.widget.Toast.LENGTH_SHORT).show()
      }
    }
  }

  val picker=rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()){u:Uri?->
    u?.let{
      vm.fromFile(it)
      while(stack.size>1)stack.removeAt(stack.lastIndex)
    }
  }

  Scaffold(containerColor=FieldBg,topBar={
    TopAppBar(
      title={Text(stringResource(R.string.app_title),fontWeight=FontWeight.Bold)},
      navigationIcon={
        if(stack.size>1){
          IconButton(onClick={back()}){
            Text("‹",style=MaterialTheme.typography.headlineMedium,modifier=Modifier.semantics{contentDescription=activity.getString(R.string.back)})
          }
        }
      },
      actions={
        IconButton(onClick={push(AppScreen.SETTINGS)}){
          Text("⚙",modifier=Modifier.semantics{contentDescription=activity.getString(R.string.settings)})
        }
      },
      colors=TopAppBarDefaults.topAppBarColors(
        containerColor=FieldBg,
        titleContentColor=MaterialTheme.colorScheme.onBackground,
        navigationIconContentColor=FieldCyan,
        actionIconContentColor=FieldCyan
      )
    )
  }){pad->
    Box(Modifier.padding(pad).fillMaxSize()){
      FieldTakScreenContent(
        screen=screen,
        state=state,
        vm=vm,
        activity=activity,
        onPush=::push,
        onBack=::back,
        picker=picker
      )
      state.busyMessage?.let{BusyOverlay(it,state.downloadCurrent,state.downloadTotal)}
      state.error?.let{ErrorDialog(it,onDismiss=vm::clearError)}
    }
  }
}

@Composable
private fun FieldTakScreenContent(
  screen:AppScreen,
  state:AppUiState,
  vm:MainViewModel,
  activity:MainActivity,
  onPush:(AppScreen)->Unit,
  onBack:()->Unit,
  picker:androidx.activity.compose.ManagedActivityResultLauncher<Array<String>,Uri?>
){
  when(screen){
    AppScreen.HOME->HomeScreen(
      state,vm,
      onSmartDiagnosis={onPush(AppScreen.SMART_DIAGNOSIS)},
      onRecovery={onPush(AppScreen.RECOVERY)},
      onStatus={onPush(AppScreen.DEVICE_STATUS)},
      onImport={picker.launch(arrayOf("application/zip","application/octet-stream","application/vnd.fieldtak.package"))},
      onHistory={onPush(AppScreen.HISTORY)},
      onLogs={onPush(AppScreen.LOGS)},
      onProvisionQr={onPush(AppScreen.SCAN_PROVISIONING)},
      onEnrollment={onPush(AppScreen.SCAN_ENROLLMENT)},
      onDataPackage={onPush(AppScreen.SCAN_DATA_PACKAGE)}
    )
    AppScreen.SMART_DIAGNOSIS->SmartDiagnosisScreen(state,vm,onRecovery={onPush(AppScreen.RECOVERY)},onDetails={onPush(AppScreen.DEVICE_STATUS)})
    AppScreen.RECOVERY->RecoveryScreen(state,vm,onDiagnosis={onPush(AppScreen.SMART_DIAGNOSIS)})
    AppScreen.DEVICE_STATUS->DeviceStatusScreen(
      state,vm,
      onScan={onPush(AppScreen.SCAN_UNIVERSAL)},
      onDetails={onPush(AppScreen.DETAILS)},
      onService={onPush(AppScreen.SERVICE)},
      onHistory={onPush(AppScreen.HISTORY)}
    )
    AppScreen.SCAN_UNIVERSAL->QrPurposeScreen(
      title=stringResource(R.string.scan_universal_title),
      hint=stringResource(R.string.scan_universal_hint),
      onCancel=onBack,
      onValue={v->onBack();vm.handleInput(v)}
    )
    AppScreen.SCAN_PROVISIONING->QrPurposeScreen(
      title=stringResource(R.string.scan_provisioning_title),
      hint=stringResource(R.string.scan_provisioning_hint),
      onCancel=onBack,
      onValue={v->onBack();vm.handleProvisioningQr(v)}
    )
    AppScreen.SCAN_ENROLLMENT->QrPurposeScreen(
      title=stringResource(R.string.scan_enrollment_title),
      hint=stringResource(R.string.scan_enrollment_hint),
      onCancel=onBack,
      onValue={v->onBack();vm.handleEnrollmentQr(v)}
    )
    AppScreen.SCAN_DATA_PACKAGE->QrPurposeScreen(
      title=stringResource(R.string.scan_data_package_title),
      hint=stringResource(R.string.scan_data_package_hint),
      onCancel=onBack,
      onValue={v->onBack();vm.handleDataPackageQr(v)}
    )
    AppScreen.DETAILS->DetailsScreen(state,vm)
    AppScreen.SERVICE->ServiceScreen(state,vm,activity)
    AppScreen.LOGS->LogsScreen(state,vm,activity)
    AppScreen.HISTORY->HistoryScreen(state,vm)
    AppScreen.SETTINGS->SettingsScreen(state,vm)
  }
}

@Composable
private fun QrPurposeScreen(title:String,hint:String,onCancel:()->Unit,onValue:(String)->Unit){
  Box(Modifier.fillMaxSize()){
    QrScanner(onValue=onValue)
    Surface(
      modifier=Modifier.align(Alignment.TopCenter).fillMaxWidth().padding(16.dp),
      color=MaterialTheme.colorScheme.surface.copy(alpha=.94f),
      shape=MaterialTheme.shapes.medium,
      tonalElevation=6.dp
    ){
      Column(Modifier.padding(14.dp),verticalArrangement=Arrangement.spacedBy(4.dp)){
        Text(title,fontWeight=FontWeight.Bold,style=MaterialTheme.typography.titleMedium)
        Text(hint,style=MaterialTheme.typography.bodySmall)
      }
    }
    FilledTonalButton(onClick=onCancel,modifier=Modifier.align(Alignment.BottomCenter).padding(24.dp).heightIn(min=48.dp)){
      Text(stringResource(R.string.cancel))
    }
  }
}

@Composable
private fun HomeScreen(state:AppUiState,vm:MainViewModel,onSmartDiagnosis:()->Unit,onRecovery:()->Unit,onStatus:()->Unit,onImport:()->Unit,onHistory:()->Unit,onLogs:()->Unit,onProvisionQr:()->Unit,onEnrollment:()->Unit,onDataPackage:()->Unit){
  Column(
    Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal=20.dp,vertical=16.dp),
    verticalArrangement=Arrangement.spacedBy(14.dp)
  ){
    if(!state.advancedMode){
      // Simple Mode: one clear task path, secondary actions kept compact.
      SimpleReadinessBanner(state, vm)

      val mesh=vm.meshtasticStatus()
      val radioNeeded = state.pkg!=null && vm.meshtasticChannelUrl()!=null && mesh.linkState!=MeshtasticLinkState.CONNECTED

      when {
        state.pkg==null -> FieldPrimaryButton(onClick=onProvisionQr,modifier=Modifier.fillMaxWidth().heightIn(min=58.dp)){
          Text(stringResource(R.string.prepare_phone_action))
        }
        radioNeeded && !vm.isMeshtasticSkipped() -> FieldPrimaryButton(onClick=vm::configureMeshtastic,modifier=Modifier.fillMaxWidth().heightIn(min=58.dp)){
          Text(stringResource(R.string.configure_radio))
        }
        else -> FieldPrimaryButton(onClick=onSmartDiagnosis,modifier=Modifier.fillMaxWidth().heightIn(min=58.dp)){
          Text(stringResource(R.string.run_smart_diagnosis))
        }
      }

      if(radioNeeded && !vm.isMeshtasticSkipped()){
        FieldOutlineButton(onClick=vm::skipMeshtasticSetup,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){
          Text(stringResource(R.string.skip_optional_action))
        }
      }

      val optionalLabel=vm.optionalStepLabel()
      if(optionalLabel!=null){
        FieldOutlineButton(onClick=vm::skipOptionalStep,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){
          Text(optionalLabel)
        }
      }

      if(state.pkg!=null && (state.error!=null || state.readiness?.items?.any{it.state!=CheckState.READY}==true)){
        FieldOutlineButton(onClick=onRecovery,modifier=Modifier.fillMaxWidth().heightIn(min=50.dp)){
          Text(stringResource(R.string.recovery))
        }
      }

      FieldOutlineButton(onClick=onImport,modifier=Modifier.fillMaxWidth().heightIn(min=50.dp)){
        Text(stringResource(R.string.import_ftak))
      }
      TextButton(onClick=onStatus,modifier=Modifier.fillMaxWidth().heightIn(min=44.dp)){
        Text(stringResource(R.string.device_status_action),fontWeight=FontWeight.SemiBold)
      }

      TextButton(onClick=vm::toggleAdvancedMode,modifier=Modifier.fillMaxWidth().heightIn(min=46.dp)){
        Text(stringResource(R.string.switch_advanced_mode),fontWeight=FontWeight.SemiBold)
      }

      BuyCoffeeSupportPanel()
    } else {
      Text(stringResource(R.string.advanced_home_title),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
      Text(stringResource(R.string.advanced_home_subtitle),style=MaterialTheme.typography.bodySmall,color=FieldMuted)

      AdvancedSection(title=stringResource(R.string.advanced_section_prepare)){
        FieldPrimaryButton(onClick=onProvisionQr,modifier=Modifier.fillMaxWidth().heightIn(min=56.dp)){
          Text(stringResource(R.string.prepare_phone_action))
        }
        FieldOutlineButton(onClick=onImport,modifier=Modifier.fillMaxWidth().heightIn(min=50.dp)){
          Text(stringResource(R.string.import_ftak))
        }
        FieldOutlineButton(onClick=onSmartDiagnosis,modifier=Modifier.fillMaxWidth().heightIn(min=50.dp)){
          Text(stringResource(R.string.run_smart_diagnosis))
        }
      }

      AdvancedSection(title=stringResource(R.string.advanced_section_ots)){
        FieldOutlineButton(onClick=onEnrollment,modifier=Modifier.fillMaxWidth().heightIn(min=52.dp)){
          Text(stringResource(R.string.enrollment_user_action))
        }
        FieldOutlineButton(onClick=onDataPackage,modifier=Modifier.fillMaxWidth().heightIn(min=52.dp)){
          Text(stringResource(R.string.data_packages_action))
        }
      }

      AdvancedSection(title=stringResource(R.string.advanced_section_radio)){
        val mesh=vm.meshtasticStatus()
        val channelReady=vm.meshtasticChannelUrl()!=null
        FieldOutlineButton(
          onClick=vm::configureMeshtastic,
          modifier=Modifier.fillMaxWidth().heightIn(min=52.dp),
          enabled=channelReady
        ){
          Text(stringResource(R.string.configure_radio))
        }
        FieldTonalButton(onClick=vm::openMeshtasticApp,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){
          Text(stringResource(R.string.meshtastic_open_app))
        }
        if(mesh.linkState==MeshtasticLinkState.CONNECTED){
          Text(stringResource(R.string.advanced_radio_connected),style=MaterialTheme.typography.bodySmall,color=FieldGood)
        } else if(!channelReady){
          Text(stringResource(R.string.advanced_radio_no_channel),style=MaterialTheme.typography.bodySmall,color=FieldMuted)
        }
      }

      AdvancedSection(title=stringResource(R.string.advanced_section_diagnostics)){
        Row(horizontalArrangement=Arrangement.spacedBy(10.dp),modifier=Modifier.fillMaxWidth()){
          FieldTonalButton(onClick=onStatus,modifier=Modifier.weight(1f).heightIn(min=48.dp)){
            Text(stringResource(R.string.device_status_action))
          }
          FieldTonalButton(onClick=onHistory,modifier=Modifier.weight(1f).heightIn(min=48.dp)){
            Text(stringResource(R.string.history))
          }
        }
        FieldOutlineButton(onClick=onLogs,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){
          Text(stringResource(R.string.diagnostic_logs))
        }
      }

      HorizontalDivider()
      FieldOutlineButton(onClick=vm::toggleAdvancedMode,modifier=Modifier.fillMaxWidth().heightIn(min=46.dp)){
        Text(stringResource(R.string.switch_simple_mode))
      }
      BuyCoffeeSupportPanel()
      state.appUpdate?.let{UpdateCard(it.version,vm::installAppUpdate)}
    }
  }
}

@Composable
private fun BuyCoffeeSupportPanel(){
  val context=LocalContext.current
  Surface(
    modifier=Modifier.fillMaxWidth(),
    color=FieldPanel.copy(alpha=.72f),
    shape=MaterialTheme.shapes.medium,
    border=BorderStroke(1.dp,FieldCyan.copy(alpha=.20f))
  ){
    Row(
      modifier=Modifier.padding(14.dp),
      verticalAlignment=Alignment.CenterVertically,
      horizontalArrangement=Arrangement.spacedBy(14.dp)
    ){
      Image(
        painter=painterResource(R.drawable.buycoffee_qr_qbcol),
        contentDescription=stringResource(R.string.support_author_qr),
        modifier=Modifier.size(88.dp)
      )
      Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(4.dp)){
        Text(
          stringResource(R.string.support_author_title),
          style=MaterialTheme.typography.titleMedium,
          fontWeight=FontWeight.Bold,
          color=FieldCyan
        )
        Text(
          stringResource(R.string.support_author_detail),
          style=MaterialTheme.typography.bodySmall,
          color=FieldMuted
        )
        TextButton(
          onClick={
            runCatching{
              context.startActivity(Intent(Intent.ACTION_VIEW,Uri.parse("https://buycoffee.to/qbcol")))
            }
          },
          contentPadding=PaddingValues(0.dp)
        ){
          Text(stringResource(R.string.support_author_action),fontWeight=FontWeight.SemiBold)
        }
      }
    }
  }
}

@Composable
private fun AdvancedSection(title:String, content:@Composable ColumnScope.()->Unit){
  Surface(
    modifier=Modifier.fillMaxWidth(),
    color=FieldPanel.copy(alpha=.72f),
    shape=MaterialTheme.shapes.medium,
    border=BorderStroke(1.dp,FieldMuted.copy(alpha=.22f))
  ){
    Column(Modifier.padding(14.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
      Text(title,style=MaterialTheme.typography.titleSmall,fontWeight=FontWeight.Bold,color=FieldCyan)
      content()
    }
  }
}


@Composable
private fun SimpleReadinessBanner(state:AppUiState,vm:MainViewModel){
  val pkg=state.pkg
  val mesh=vm.meshtasticStatus()
  val ready = pkg!=null && state.trusted && vm.isAtakInstalled() &&
    mesh.linkState==MeshtasticLinkState.CONNECTED
  val needsRadio = pkg!=null && mesh.linkState!=MeshtasticLinkState.CONNECTED && vm.meshtasticChannelUrl()!=null

  val title = when {
    ready -> stringResource(R.string.simple_ready_title)
    needsRadio -> stringResource(R.string.simple_radio_action_title)
    pkg==null -> stringResource(R.string.simple_start_title)
    else -> stringResource(R.string.simple_attention_title)
  }
  val detail = when {
    ready -> stringResource(R.string.simple_ready_detail)
    needsRadio -> stringResource(R.string.simple_radio_action_detail)
    pkg==null -> stringResource(R.string.simple_start_detail)
    else -> stringResource(R.string.simple_attention_detail)
  }
  val statusColor=when { ready -> FieldGood; needsRadio -> FieldCyan; else -> FieldMuted }
  val statusMark=when { ready -> "✓"; needsRadio -> "!"; else -> "•" }

  Surface(
    modifier=Modifier.fillMaxWidth(),
    color=FieldPanel.copy(alpha=.78f),
    shape=MaterialTheme.shapes.medium,
    border=BorderStroke(1.dp,statusColor.copy(alpha=.34f))
  ){
    Row(
      modifier=Modifier.padding(horizontal=14.dp,vertical=11.dp),
      verticalAlignment=Alignment.CenterVertically,
      horizontalArrangement=Arrangement.spacedBy(11.dp)
    ){
      Surface(
        modifier=Modifier.size(30.dp),
        shape=MaterialTheme.shapes.small,
        color=statusColor.copy(alpha=.12f)
      ){
        Box(contentAlignment=Alignment.Center){
          Text(statusMark,fontWeight=FontWeight.Black,color=statusColor,style=MaterialTheme.typography.titleSmall)
        }
      }
      Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(1.dp)){
        Text(title,fontWeight=FontWeight.Bold,style=MaterialTheme.typography.titleSmall)
        Text(detail,style=MaterialTheme.typography.bodySmall,color=FieldMuted,maxLines=2)
      }
    }
  }
}

@Composable
private fun SmartDiagnosisScreen(state:AppUiState,vm:MainViewModel,onRecovery:()->Unit,onDetails:()->Unit){
  val context=LocalContext.current
  val pkg=state.pkg
  val atak=vm.provisioning.detectAtak(pkg?.manifest?.target ?: TargetInfo())
  val network=runCatching{context.getSystemService(android.net.ConnectivityManager::class.java).activeNetwork!=null}.getOrDefault(false)
  val mesh=vm.meshtasticStatus()
  val serverChecks=state.report?.checks?.filter{it.id!="atak_cert"}.orEmpty()
  val serverState=when {
    serverChecks.isEmpty()->CheckState.UNKNOWN
    serverChecks.any{it.state==CheckState.PROBLEM}->CheckState.PROBLEM
    serverChecks.any{it.state==CheckState.ACTION_REQUIRED}->CheckState.ACTION_REQUIRED
    serverChecks.all{it.state==CheckState.READY}->CheckState.READY
    else->CheckState.UNKNOWN
  }
  val meshSkipped=vm.isMeshtasticSkipped()
  val allGood=pkg!=null && atak.installed && atak.compatibility==CheckState.READY && network && serverState==CheckState.READY && (meshSkipped || (mesh.state==CheckState.READY && mesh.linkState!=MeshtasticLinkState.DISCONNECTED))
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.smart_diagnosis),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    Text(if(allGood) stringResource(R.string.smart_ok) else stringResource(R.string.smart_attention),style=MaterialTheme.typography.bodyMedium)
    StatusCard(stringResource(R.string.smart_package),if(pkg!=null && state.trusted)CheckState.READY else if(pkg!=null)CheckState.ACTION_REQUIRED else CheckState.PROBLEM,if(pkg!=null) stringResource(R.string.smart_package_ok) else stringResource(R.string.smart_package_missing))
    StatusCard(stringResource(R.string.smart_atak),if(atak.installed && atak.compatibility==CheckState.READY)CheckState.READY else if(atak.installed)CheckState.ACTION_REQUIRED else CheckState.PROBLEM,if(atak.installed && atak.compatibility==CheckState.READY)stringResource(R.string.smart_atak_ready) else stringResource(R.string.smart_atak_missing))
    StatusCard(stringResource(R.string.smart_network),if(network)CheckState.READY else CheckState.PROBLEM,if(network)stringResource(R.string.smart_network_ok) else stringResource(R.string.smart_network_bad))
    StatusCard(stringResource(R.string.smart_server),serverState,when(serverState){CheckState.READY->stringResource(R.string.smart_server_ok);CheckState.PROBLEM,CheckState.ACTION_REQUIRED->stringResource(R.string.smart_server_problem);else->stringResource(R.string.smart_server_unknown)})
    StatusCard(stringResource(R.string.smart_meshtastic),mesh.state,when(mesh.linkState){MeshtasticLinkState.CONNECTED->stringResource(R.string.smart_mesh_connected);MeshtasticLinkState.CONNECTING->stringResource(R.string.smart_mesh_connecting);MeshtasticLinkState.DISCONNECTED->stringResource(R.string.smart_mesh_disconnected);else->if(vm.isMeshtasticInstalled())stringResource(R.string.smart_mesh_waiting) else stringResource(R.string.smart_mesh_app_missing)})
    HorizontalDivider()
    FieldPrimaryButton(onClick=vm::runSmartDiagnosis,modifier=Modifier.fillMaxWidth().heightIn(min=58.dp)){Text(stringResource(R.string.run_smart_diagnosis))}
    if(mesh.linkState!=MeshtasticLinkState.CONNECTED && vm.meshtasticChannelUrl()!=null && !meshSkipped) FieldPrimaryButton(onClick=vm::configureMeshtastic,modifier=Modifier.fillMaxWidth().heightIn(min=56.dp)){Text(stringResource(R.string.configure_radio))}
    if(mesh.linkState!=MeshtasticLinkState.CONNECTED && vm.meshtasticChannelUrl()!=null && !meshSkipped) FieldOutlineButton(onClick=vm::skipMeshtasticSetup,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.skip_optional_action))}
    if(!allGood) FieldOutlineButton(onClick=onRecovery,modifier=Modifier.fillMaxWidth().heightIn(min=50.dp)){Text(stringResource(R.string.recovery))}
    FieldTonalButton(onClick=onDetails,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.smart_open_detailed))}
  }
}

@Composable
private fun RecoveryScreen(state:AppUiState,vm:MainViewModel,onDiagnosis:()->Unit){
  val hasPackage=state.pkg!=null
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.recovery),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    Text(stringResource(R.string.recovery_subtitle),style=MaterialTheme.typography.bodyMedium)
    if(!hasPackage) {
      StatusCard(stringResource(R.string.smart_package),CheckState.PROBLEM,stringResource(R.string.recovery_no_package))
    } else {
      StatusCard(stringResource(R.string.smart_package),if(state.trusted)CheckState.READY else CheckState.ACTION_REQUIRED,stringResource(R.string.smart_package_ok))
      FieldPrimaryButton(onClick=vm::recoverCurrentSetup,modifier=Modifier.fillMaxWidth().heightIn(min=58.dp)){Text(stringResource(R.string.run_recovery))}
      FieldOutlineButton(onClick=onDiagnosis,modifier=Modifier.fillMaxWidth().heightIn(min=52.dp)){Text(stringResource(R.string.run_smart_diagnosis))}
    }
  }
}

@Composable
private fun DeviceStatusScreen(state:AppUiState,vm:MainViewModel,onScan:()->Unit,onDetails:()->Unit,onService:()->Unit,onHistory:()->Unit){
  if(!state.advancedMode){ SimpleStatusScreen(state,vm); return }
  val context=LocalContext.current
  val pkg=state.pkg
  val atak=vm.provisioning.detectAtak(pkg?.manifest?.target ?: TargetInfo())
  val meshtasticInstalled=vm.isMeshtasticInstalled()
  val meshtasticChannel=vm.meshtasticChannelUrl()
  val meshRuntime=vm.meshtasticStatus()
  val enrollment=vm.enrollmentUri()
  val network=runCatching{context.getSystemService(android.net.ConnectivityManager::class.java).activeNetwork!=null}.getOrDefault(false)
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.device_status_title),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    Text(stringResource(R.string.device_status_subtitle),style=MaterialTheme.typography.bodySmall)

    StatusCard(stringResource(R.string.status_atak),if(atak.installed && atak.compatibility==CheckState.READY)CheckState.READY else if(atak.installed)CheckState.ACTION_REQUIRED else CheckState.PROBLEM,
      if(atak.installed) "ATAK ${atak.versionName ?: "?"} — ${atak.compatibilityMessage}" else stringResource(R.string.status_atak_missing))
    StatusCard(stringResource(R.string.status_package),if(pkg!=null && state.trusted)CheckState.READY else if(pkg!=null)CheckState.ACTION_REQUIRED else CheckState.UNKNOWN,
      pkg?.let{stringResource(R.string.status_package_detail,it.manifest.packageInfo.name,it.manifest.packageInfo.version)} ?: stringResource(R.string.status_package_none))
    StatusCard(stringResource(R.string.status_network),if(network)CheckState.READY else CheckState.PROBLEM,if(network)stringResource(R.string.status_network_ok) else stringResource(R.string.status_network_bad))
    val serverChecks=state.report?.checks?.filter{it.id!="atak_cert"}.orEmpty()
    val serverState=when {
      serverChecks.isEmpty()->CheckState.UNKNOWN
      serverChecks.any{it.state==CheckState.PROBLEM}->CheckState.PROBLEM
      serverChecks.any{it.state==CheckState.ACTION_REQUIRED}->CheckState.ACTION_REQUIRED
      serverChecks.all{it.state==CheckState.READY}->CheckState.READY
      else->CheckState.UNKNOWN
    }
    val serverDetail=state.report?.let{r->"${r.host}: ${serverChecks.count{it.state==CheckState.READY}}/${serverChecks.size} testów sieciowych OK"} ?: stringResource(R.string.status_server_unchecked)
    StatusCard(stringResource(R.string.status_server),serverState,serverDetail)
    StatusCard(stringResource(R.string.status_meshtastic),meshRuntime.state,when {
      !meshtasticInstalled -> stringResource(R.string.status_meshtastic_missing)
      meshRuntime.linkState==org.fieldtak.hub.meshtastic.MeshtasticLinkState.CONNECTED -> stringResource(R.string.status_meshtastic_radio_connected)
      meshRuntime.linkState==org.fieldtak.hub.meshtastic.MeshtasticLinkState.CONNECTING -> stringResource(R.string.status_meshtastic_radio_connecting)
      meshRuntime.linkState==org.fieldtak.hub.meshtastic.MeshtasticLinkState.DISCONNECTED -> stringResource(R.string.status_meshtastic_radio_disconnected)
      meshRuntime.state==CheckState.READY -> "${stringResource(R.string.status_meshtastic_ready)} — ${meshRuntime.gatewayDetail}"
      meshRuntime.state==CheckState.UNKNOWN -> stringResource(R.string.status_meshtastic_not_verified)
      !meshtasticChannel.isNullOrBlank() -> stringResource(R.string.status_meshtastic_channel_ready)
      else -> meshRuntime.detail
    },onClick=if(!meshtasticChannel.isNullOrBlank() && meshtasticInstalled) vm::openMeshtasticChannel else null)
    FieldTonalButton(onClick=vm::openMeshtasticApp,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.meshtastic_open_app))}
    state.updateMessage?.let { Text(it, style=MaterialTheme.typography.bodySmall, color=FieldCyan) }
    FieldOutlineButton(onClick=vm::refreshMeshtasticStatus,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.meshtastic_test))}
    StatusCard(stringResource(R.string.status_enrollment),if(enrollment!=null) CheckState.ACTION_REQUIRED else CheckState.READY,if(enrollment!=null) stringResource(R.string.status_enrollment_ready) else stringResource(R.string.status_enrollment_none),onClick=if(enrollment!=null && atak.installed) vm::openEnrollment else null)
    state.readiness?.let{r->
      HorizontalDivider()
      Text(stringResource(R.string.status_readiness),style=MaterialTheme.typography.titleMedium,fontWeight=FontWeight.Bold)
      ReadinessGauge(r.percent)
      Text(stringResource(R.string.phone_status,r.percent),fontWeight=FontWeight.SemiBold)
      r.items.forEach{ReadinessRow(it.label,it.state,it.detail)}
    }
    HorizontalDivider()
    FieldPrimaryButton(onClick=onScan,modifier=Modifier.fillMaxWidth().heightIn(min=56.dp)){Text(stringResource(R.string.scan_any_qr))}
    if(pkg!=null){
      if(!state.trusted) FieldTonalButton(onClick=vm::trustPublisher,modifier=Modifier.fillMaxWidth().heightIn(min=52.dp)){Text(stringResource(R.string.trust_publisher))}
      FieldPrimaryButton(onClick=vm::preparePhone,modifier=Modifier.fillMaxWidth().heightIn(min=56.dp)){Text(if(state.session?.stage==DeploymentStage.COMPLETE)stringResource(R.string.check_again) else stringResource(R.string.finish_configuration))}
    }
    Row(horizontalArrangement=Arrangement.spacedBy(10.dp),modifier=Modifier.fillMaxWidth()){
      FieldTonalButton(onClick=onDetails,modifier=Modifier.weight(1f).heightIn(min=48.dp)){Text(stringResource(R.string.details_checklist))}
      FieldTonalButton(onClick=onService,modifier=Modifier.weight(1f).heightIn(min=48.dp)){Text(stringResource(R.string.diagnostics))}
    }
    FieldOutlineButton(onClick=onHistory,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.history))}
    if(pkg!=null) TextButton(onClick=vm::resetPackage,modifier=Modifier.align(Alignment.CenterHorizontally).heightIn(min=48.dp)){Text(stringResource(R.string.close_current_package))}
  }
}


@Composable
private fun SimpleStatusScreen(state:AppUiState,vm:MainViewModel){
  val context=LocalContext.current
  val pkg=state.pkg
  val atak=vm.provisioning.detectAtak(pkg?.manifest?.target ?: TargetInfo())
  val network=runCatching{context.getSystemService(android.net.ConnectivityManager::class.java).activeNetwork!=null}.getOrDefault(false)
  val mesh=vm.meshtasticStatus()
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.simple_status_title),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    Text(stringResource(R.string.simple_status_subtitle),style=MaterialTheme.typography.bodySmall)
    StatusCard(stringResource(R.string.status_atak),if(atak.installed && atak.compatibility==CheckState.READY) CheckState.READY else if(atak.installed) CheckState.ACTION_REQUIRED else CheckState.PROBLEM,if(atak.installed) "ATAK ${atak.versionName ?: "?"}" else stringResource(R.string.status_atak_missing))
    StatusCard(stringResource(R.string.status_package),if(pkg!=null && state.trusted) CheckState.READY else if(pkg!=null) CheckState.ACTION_REQUIRED else CheckState.UNKNOWN,pkg?.let{stringResource(R.string.status_package_detail,it.manifest.packageInfo.name,it.manifest.packageInfo.version)} ?: stringResource(R.string.status_package_none))
    StatusCard(stringResource(R.string.status_network),if(network) CheckState.READY else CheckState.PROBLEM,if(network) stringResource(R.string.status_network_ok) else stringResource(R.string.status_network_bad))
    StatusCard(stringResource(R.string.status_meshtastic),mesh.state,when {
      mesh.linkState==org.fieldtak.hub.meshtastic.MeshtasticLinkState.CONNECTED -> stringResource(R.string.status_meshtastic_radio_connected)
      mesh.linkState==org.fieldtak.hub.meshtastic.MeshtasticLinkState.CONNECTING -> stringResource(R.string.status_meshtastic_radio_connecting)
      mesh.linkState==org.fieldtak.hub.meshtastic.MeshtasticLinkState.DISCONNECTED -> stringResource(R.string.status_meshtastic_radio_disconnected)
      mesh.state==CheckState.READY -> stringResource(R.string.status_meshtastic_ready)
      else -> mesh.detail
    })
    state.deploymentPlan?.let { plan ->
      HorizontalDivider()
      Text(stringResource(R.string.deployment_20_title),style=MaterialTheme.typography.titleMedium,fontWeight=FontWeight.Bold,color=FieldCyan)
      Text(stringResource(R.string.deployment_plan),fontWeight=FontWeight.SemiBold)
      plan.tasks.forEach { task ->
        val mark=when(task.state){DeploymentTaskState.READY,DeploymentTaskState.SKIPPED->"✓";DeploymentTaskState.ACTION_REQUIRED->"!";DeploymentTaskState.OPTIONAL->"○";DeploymentTaskState.PROBLEM->"×";DeploymentTaskState.UNKNOWN->"?"}
        val color=when(task.state){DeploymentTaskState.READY,DeploymentTaskState.SKIPPED->FieldGood;DeploymentTaskState.ACTION_REQUIRED->FieldCyan;DeploymentTaskState.PROBLEM->FieldRed;else->FieldMuted}
        Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(8.dp),verticalAlignment=Alignment.Top){
          Text(mark,color=color,fontWeight=FontWeight.Bold)
          Column(Modifier.weight(1f)){Text(task.label,fontWeight=FontWeight.SemiBold);Text(task.detail,style=MaterialTheme.typography.bodySmall,color=FieldMuted)}
          if(task.optional) Text(stringResource(R.string.deployment_optional),style=MaterialTheme.typography.labelSmall,color=FieldMuted)
        }
      }
      if(plan.optionalPending>0) Text(stringResource(R.string.deployment_pending_optional,plan.optionalPending),style=MaterialTheme.typography.bodySmall,color=FieldMuted)
    }
    HorizontalDivider()
    Text(stringResource(R.string.simple_status_help),style=MaterialTheme.typography.bodySmall)
    FieldOutlineButton(onClick=vm::toggleAdvancedMode,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.switch_advanced_mode))}
  }
}

@Composable
private fun StatusCard(label:String,state:CheckState,detail:String,onClick:(()->Unit)?=null){
  val modifier=if(onClick!=null) Modifier.fillMaxWidth().clickable(onClick=onClick) else Modifier.fillMaxWidth()
  Card(modifier){Row(Modifier.padding(14.dp),horizontalArrangement=Arrangement.spacedBy(12.dp),verticalAlignment=Alignment.Top){Text(statusMark(state),fontWeight=FontWeight.Bold,color=when(state){CheckState.READY->FieldGood;CheckState.ACTION_REQUIRED->FieldCyan;CheckState.PROBLEM->FieldRed;CheckState.UNKNOWN->FieldMuted});Column(Modifier.weight(1f)){Text(label,fontWeight=FontWeight.Bold);Text(detail,style=MaterialTheme.typography.bodySmall)}}}
}

@Composable
private fun UpdateCard(version:String,onInstall:()->Unit){
  Card(Modifier.fillMaxWidth()){Column(Modifier.padding(14.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){
    Text(stringResource(R.string.update),fontWeight=FontWeight.Bold)
    Text(stringResource(R.string.update_available,version))
    FieldPrimaryButton(onClick=onInstall,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.download_update))}
  }}
}

@Composable
private fun DetailsScreen(state:AppUiState,vm:MainViewModel){
  val pkg=state.pkg
  if(pkg==null){Box(Modifier.fillMaxSize(),contentAlignment=Alignment.Center){Text(stringResource(R.string.no_active_package))};return}
  val plugins=remember(pkg.root,state.session?.updatedUtc){vm.provisioning.pluginStatuses(pkg.root)}
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.deployment_checklist),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    state.readiness?.items?.forEach{ReadinessRow(it.label,it.state,it.detail)}
    HorizontalDivider()
    Text(stringResource(R.string.atak),style=MaterialTheme.typography.titleMedium,fontWeight=FontWeight.Bold)
    val atak=vm.provisioning.detectAtak(pkg.manifest.target); Text("${statusMark(atak.compatibility)} ATAK ${atak.versionName ?: stringResource(R.string.missing)} — ${atak.compatibilityMessage}")
    Text(stringResource(R.string.plugins),style=MaterialTheme.typography.titleMedium,fontWeight=FontWeight.Bold)
    if(plugins.isEmpty()) Text(stringResource(R.string.no_plugins))
    plugins.forEach{p->
      val label=when(p.state){PluginInstallState.CURRENT->stringResource(R.string.state_ready);PluginInstallState.UPDATE_REQUIRED->stringResource(R.string.state_update);PluginInstallState.NOT_INSTALLED->stringResource(R.string.state_install);PluginInstallState.UNKNOWN->stringResource(R.string.state_unknown)}
      Text("• ${p.label}: ${p.installedVersion ?: "—"} → ${p.sourceVersion ?: "?"} [$label]")
    }
    HorizontalDivider()
    Text(stringResource(R.string.state_machine),style=MaterialTheme.typography.titleMedium,fontWeight=FontWeight.Bold)
    state.session?.steps?.sortedBy{it.updatedUtc}?.forEach{st->Text("${stepMark(st.result)} ${st.stage.name} — ${st.detail}",style=MaterialTheme.typography.bodySmall)}
    FieldPrimaryButton(onClick=vm::preparePhone,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.repair_continue))}
    FieldOutlineButton(onClick=vm::preflight,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.refresh_preflight))}
  }
}

@Composable
private fun ServiceScreen(state:AppUiState,vm:MainViewModel,activity:MainActivity){
  val report=state.report
  val context=LocalContext.current
  val saveReport=rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/plain")){uri->
    if(uri!=null && report!=null) runCatching{context.contentResolver.openOutputStream(uri)?.bufferedWriter()?.use{it.write(report.asText())}}
  }
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.service_mode),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    Text(stringResource(R.string.service_explanation))
    FieldPrimaryButton(onClick=vm::runDiagnostics,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.run_diagnostics))}
    report?.checks?.forEach{ReadinessRow(it.label,it.state,it.detail+(if(it.technical.isNotBlank())"\n${it.technical}" else ""))}
    if(report!=null){
      OutlinedButton(onClick={ val cb=context.getSystemService(ClipboardManager::class.java); cb.setPrimaryClip(ClipData.newPlainText(context.getString(R.string.report_title),report.asText())) },modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.copy_report))}
      OutlinedButton(onClick={saveReport.launch("fieldtak-report-${System.currentTimeMillis()}.txt")},modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.save_report))}
      TextButton(onClick={vm.provisioning.shareText(activity,context.getString(R.string.report_title),report.asText())},modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.share_report)) }
    }
    FieldOutlineButton(onClick=vm::openAtak,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.open_atak))}
  }
}

@Composable
private fun LogsScreen(state:AppUiState,vm:MainViewModel,activity:MainActivity){
  val context=LocalContext.current
  val saveLog=rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/plain")){uri->
    if(uri!=null) runCatching{context.contentResolver.openOutputStream(uri)?.bufferedWriter()?.use{it.write(vm.diagnosticLogText())}}
  }
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.diagnostic_logs),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    Text(stringResource(R.string.diagnostic_logs_explanation),style=MaterialTheme.typography.bodySmall,color=FieldMuted)
    if(state.diagnosticLog.isEmpty()){
      Text(stringResource(R.string.diagnostic_logs_empty))
    } else {
      Surface(modifier=Modifier.fillMaxWidth(),color=FieldPanel.copy(alpha=.72f),shape=MaterialTheme.shapes.medium){
        Text(state.diagnosticLog.takeLast(250).joinToString("\n"),modifier=Modifier.padding(12.dp),style=MaterialTheme.typography.bodySmall)
      }
    }
    FieldPrimaryButton(onClick={saveLog.launch("fieldtak-diagnostic-${System.currentTimeMillis()}.txt")},modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){
      Text(stringResource(R.string.export_diagnostic_log))
    }
    FieldOutlineButton(onClick=vm::clearDiagnosticLog,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){
      Text(stringResource(R.string.clear_diagnostic_log))
    }
    Text(stringResource(R.string.diagnostic_log_security),style=MaterialTheme.typography.bodySmall,color=FieldMuted)
  }
}

@Composable
private fun HistoryScreen(state:AppUiState,vm:MainViewModel){
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
    Text(stringResource(R.string.recent_deployments),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    if(state.history.isEmpty()) Text(stringResource(R.string.history_empty))
    state.history.forEach{s->Card(Modifier.fillMaxWidth()){Column(Modifier.padding(14.dp),verticalArrangement=Arrangement.spacedBy(6.dp)){Text(s.packageName.ifBlank{stringResource(R.string.unknown_package)},fontWeight=FontWeight.Bold);Text("${s.packageVersion} • ${s.stage.name}");Text(s.completedUtc ?: s.updatedUtc,style=MaterialTheme.typography.bodySmall);TextButton(onClick={vm.restoreHistory(s.id)},modifier=Modifier.heightIn(min=48.dp)){Text(stringResource(R.string.open_redeploy))}}}}
    if(state.history.isNotEmpty()) TextButton(onClick=vm::clearHistory,modifier=Modifier.align(Alignment.End).heightIn(min=48.dp)){Text(stringResource(R.string.clear_history))}
  }
}

@Composable
private fun SettingsScreen(state:AppUiState,vm:MainViewModel){
  val context=LocalContext.current
  var langMenu by remember{mutableStateOf(false)}
  val current=LanguageManager.currentTag()
  Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(14.dp)){
    Text(stringResource(R.string.settings_title),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold)
    Text(stringResource(R.string.version_label,BuildConfig.VERSION_NAME))
    Text(stringResource(R.string.update_channel,BuildConfig.RELEASE_CHANNEL))
    Text(stringResource(R.string.accessibility_note))
    Text(stringResource(R.string.app_mode),fontWeight=FontWeight.SemiBold)
    Text(if(state.advancedMode) stringResource(R.string.mode_advanced_detail) else stringResource(R.string.mode_simple_detail),style=MaterialTheme.typography.bodySmall)
    FieldOutlineButton(onClick=vm::toggleAdvancedMode,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(if(state.advancedMode) stringResource(R.string.switch_simple_mode) else stringResource(R.string.switch_advanced_mode))}
    Text(stringResource(R.string.language),fontWeight=FontWeight.SemiBold)
    Box{
      OutlinedButton(onClick={langMenu=true},modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(when(current){"pl"->stringResource(R.string.language_polish);"en"->stringResource(R.string.language_english);else->stringResource(R.string.language_system)})}
      DropdownMenu(expanded=langMenu,onDismissRequest={langMenu=false}){
        DropdownMenuItem(text={Text(stringResource(R.string.language_system))},onClick={langMenu=false;LanguageManager.apply("system")})
        DropdownMenuItem(text={Text(stringResource(R.string.language_polish))},onClick={langMenu=false;LanguageManager.apply("pl")})
        DropdownMenuItem(text={Text(stringResource(R.string.language_english))},onClick={langMenu=false;LanguageManager.apply("en")})
      }
    }
    HorizontalDivider()
    FieldOutlineButton(onClick={
      val intent=Intent(Intent.ACTION_VIEW,Uri.parse("https://github.com/Qbcol/TAKFieldHub"))
      runCatching{context.startActivity(intent)}
    },modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.about_author))}
    AboutAuthorCard()
    HorizontalDivider()
    Text(stringResource(R.string.storage),fontWeight=FontWeight.SemiBold)
    Text(stringResource(R.string.storage_usage,humanBytes(state.storageBytes)))
    FieldOutlineButton(onClick=vm::cleanupStorage,modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.cleanup_storage))}
    HorizontalDivider()
    FieldPrimaryButton(onClick={vm.checkForUpdates(false)},modifier=Modifier.fillMaxWidth().heightIn(min=48.dp)){Text(stringResource(R.string.check_updates))}
    state.appUpdate?.let{UpdateCard(it.version,vm::installAppUpdate)}
    state.updateMessage?.let{Text(it)}
    Text(stringResource(R.string.local_http_warning),style=MaterialTheme.typography.bodySmall)
  }
}

@Composable
private fun AboutAuthorCard(){
  Card(modifier=Modifier.fillMaxWidth(),colors=CardDefaults.cardColors(containerColor=MaterialTheme.colorScheme.surfaceVariant)){
    Column(Modifier.padding(16.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){
      Text(stringResource(R.string.about_title),style=MaterialTheme.typography.titleLarge,fontWeight=FontWeight.Bold)
      Text(stringResource(R.string.about_author_name),style=MaterialTheme.typography.titleMedium,fontWeight=FontWeight.SemiBold)
      Text(stringResource(R.string.about_description))
      Text(stringResource(R.string.about_independence),style=MaterialTheme.typography.bodySmall)
      Text(stringResource(R.string.about_version,BuildConfig.VERSION_NAME),style=MaterialTheme.typography.bodySmall)
    }
  }
}

@Composable
private fun ReadinessGauge(percent:Int){
  val desc=stringResource(R.string.progress_description,percent.coerceIn(0,100))
  LinearProgressIndicator(progress={percent.coerceIn(0,100)/100f},modifier=Modifier.fillMaxWidth().height(10.dp).semantics { contentDescription=desc; progressBarRangeInfo=ProgressBarRangeInfo(percent.coerceIn(0,100)/100f,0f..1f) })
}

@Composable
private fun ReadinessRow(label:String,state:CheckState,detail:String){
  val stateLabel=when(state){CheckState.READY->stringResource(R.string.status_ready);CheckState.ACTION_REQUIRED->stringResource(R.string.status_action_required);CheckState.PROBLEM->stringResource(R.string.status_problem);CheckState.UNKNOWN->stringResource(R.string.status_unknown)}
  val description=stringResource(R.string.accessibility_readiness_row,label,stateLabel,detail)
  val statusColor=when(state){CheckState.READY->FieldGood;CheckState.ACTION_REQUIRED->FieldCyan;CheckState.PROBLEM->FieldRed;CheckState.UNKNOWN->FieldMuted}
  Row(Modifier.fillMaxWidth().semantics(mergeDescendants=true){contentDescription=description},horizontalArrangement=Arrangement.spacedBy(10.dp),verticalAlignment=Alignment.Top){Text("${statusMark(state)} $stateLabel",fontWeight=FontWeight.Bold,color=statusColor);Column(Modifier.weight(1f)){Text(label,fontWeight=FontWeight.SemiBold);if(detail.isNotBlank())Text(detail,style=MaterialTheme.typography.bodySmall)}}
}

private fun statusMark(s:CheckState)=when(s){CheckState.READY->"✓";CheckState.ACTION_REQUIRED->"!";CheckState.PROBLEM->"✕";CheckState.UNKNOWN->"?"}
private fun stepMark(s:StepResult)=when(s){StepResult.READY->"✓";StepResult.ACTION_REQUIRED->"!";StepResult.WARNING->"~";StepResult.FAILED->"✕";StepResult.RUNNING->"…";StepResult.PENDING->"○";StepResult.SKIPPED->"-"}

@Composable
private fun BusyOverlay(message:String,current:Long,total:Long?){
  Surface(Modifier.fillMaxSize(),color=MaterialTheme.colorScheme.scrim.copy(alpha=.55f)){
    Box(contentAlignment=Alignment.Center){Card{Column(Modifier.padding(24.dp).widthIn(min=260.dp,max=360.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(12.dp)){CircularProgressIndicator();Text(message);if(current>0){val mb=current/1024.0/1024.0;Text(if(total!=null)"%.1f / %.1f MB".format(mb,total/1024.0/1024.0) else "%.1f MB".format(mb),style=MaterialTheme.typography.bodySmall)}}}}
  }
}

@Composable
private fun ErrorDialog(message:String,onDismiss:()->Unit){AlertDialog(onDismissRequest=onDismiss,confirmButton={TextButton(onClick=onDismiss){Text("OK")}},title={Text(stringResource(R.string.problem))},text={Text(message)})}

private fun humanBytes(bytes:Long):String=when{bytes>=1024L*1024*1024->"%.1f GB".format(bytes/1024.0/1024.0/1024.0);bytes>=1024L*1024->"%.1f MB".format(bytes/1024.0/1024.0);else->"$bytes B"}
