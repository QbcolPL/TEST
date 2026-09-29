package org.fieldtak.hub.ui
import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.fieldtak.hub.diagnostics.ExtendedDiagnosticsManager
import org.fieldtak.hub.diagnostics.GeneratedLogReport
import org.fieldtak.hub.util.ApkInstallerManager
import org.fieldtak.hub.util.AtakMeshtasticConfigManager
import org.fieldtak.hub.util.AtakMeshtasticPlan
import org.fieldtak.hub.util.DetectedAppEntry
import org.fieldtak.hub.util.FirstRunPermissionHelper
import org.fieldtak.hub.util.FtakLibraryEntry
import org.fieldtak.hub.util.FtakPackageLibraryManager
import java.io.File
import java.io.FileOutputStream
private val LocalGateActive = compositionLocalOf { false }
private val BgDark = Color(0xFF071214)
private val CardBg = Color(0xFF0D1E22)
private val BorderTeal = Color(0xFF1F484E)
private val AccentCyan = Color(0xFF2CE5D0)
private val TextLight = Color(0xFFE6F4F1)
private val TextMuted = Color(0xFF8AA8A4)
private val StatusRed = Color(0xFFFF5252)
@Composable
fun FirstRunAndDiagnosticsHost(content: @Composable () -> Unit) {
    if (LocalGateActive.current) { content(); return }
    CompositionLocalProvider(LocalGateActive provides true) {
        val ctx = LocalContext.current; val scope = rememberCoroutineScope()
        val prefs = remember { ctx.getSharedPreferences("fth_setup_24", 0) }
        var camOk by remember { mutableStateOf(FirstRunPermissionHelper.isCameraReady(ctx)) }
        var locOk by remember { mutableStateOf(FirstRunPermissionHelper.isLocationReady(ctx)) }
        var bleOk by remember { mutableStateOf(FirstRunPermissionHelper.isNearbyBluetoothReady(ctx)) }
        var stgOk by remember { mutableStateOf(FirstRunPermissionHelper.isStorageAccessReady(ctx)) }
        var insOk by remember { mutableStateOf(FirstRunPermissionHelper.canInstallUnknownApps(ctx)) }
        val allOk = camOk && locOk && bleOk && stgOk && insOk
        var showSetup by remember { mutableStateOf(!allOk && !prefs.getBoolean("done", false)) }
        var manualOpen by remember { mutableStateOf(false) }
        var logRep by remember { mutableStateOf<GeneratedLogReport?>(null) }
        var cfgPlan by remember { mutableStateOf<AtakMeshtasticPlan?>(null) }
        var showApkDialog by remember { mutableStateOf(false) }
        var appEntries by remember { mutableStateOf<List<DetectedAppEntry>>(emptyList()) }
        var showLibraryDialog by remember { mutableStateOf(false) }
        var pkgEntries by remember { mutableStateOf<List<FtakLibraryEntry>>(emptyList()) }
        var isQrBusy by remember { mutableStateOf(false) }
        var statusMsg by remember { mutableStateOf<String?>(null) }
        var fwWarnDialog by remember { mutableStateOf<AtakMeshtasticPlan?>(null) }
        fun refresh() {
            camOk = FirstRunPermissionHelper.isCameraReady(ctx); locOk = FirstRunPermissionHelper.isLocationReady(ctx)
            bleOk = FirstRunPermissionHelper.isNearbyBluetoothReady(ctx); stgOk = FirstRunPermissionHelper.isStorageAccessReady(ctx)
            insOk = FirstRunPermissionHelper.canInstallUnknownApps(ctx)
            if (camOk && locOk && bleOk && stgOk && insOk) { prefs.edit().putBoolean("done", true).apply(); if (!manualOpen) showSetup = false }
            scope.launch { pkgEntries = withContext(Dispatchers.IO) { FtakPackageLibraryManager.listAllPackages(ctx) } }
        }
        val lo = LocalLifecycleOwner.current
        DisposableEffect(lo) {
            val obs = LifecycleEventObserver { _, ev -> if (ev == Lifecycle.Event.ON_RESUME) refresh() }
            lo.lifecycle.addObserver(obs); onDispose { lo.lifecycle.removeObserver(obs) }
        }
        val permLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { refresh() }
        val apkFilePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
            if (uri == null) return@rememberLauncherForActivityResult
            scope.launch {
                statusMsg = withContext(Dispatchers.IO) {
                    val tmp = File(ctx.cacheDir, "picked_install.apk")
                    ctx.contentResolver.openInputStream(uri)?.use { i -> FileOutputStream(tmp).use { o -> i.copyTo(o) } }
                    ApkInstallerManager.launchApkInstall(ctx, tmp)
                }
            }
        }
        val ftakFilePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
            if (uri == null) return@rememberLauncherForActivityResult
            scope.launch {
                val msg = withContext(Dispatchers.IO) { FtakPackageLibraryManager.importFtakFromUri(ctx, uri) }
                pkgEntries = withContext(Dispatchers.IO) { FtakPackageLibraryManager.listAllPackages(ctx) }
                statusMsg = msg; showLibraryDialog = true
            }
        }
        val qrGalleryLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
            if (uri == null) return@rememberLauncherForActivityResult
            isQrBusy = true
            scope.launch {
                val qrText: String? = QrImageDecoder.decodeFromUri(ctx, uri)
                if (!qrText.isNullOrBlank()) {
                    val msg = withContext(Dispatchers.IO) { FtakPackageLibraryManager.processDecodedQrPayload(ctx, qrText) }
                    pkgEntries = withContext(Dispatchers.IO) { FtakPackageLibraryManager.listAllPackages(ctx) }
                    isQrBusy = false; statusMsg = msg; showLibraryDialog = true
                } else {
                    isQrBusy = false
                    Toast.makeText(ctx, "Nie znaleziono kodu QR na obrazie.", Toast.LENGTH_LONG).show()
                }
            }
        }
        fun openApkManagerLocal() {
            scope.launch { appEntries = withContext(Dispatchers.IO) { ApkInstallerManager.scanAllAppsAndPlugins(ctx) }; statusMsg = null; showApkDialog = true }
        }
        fun openConfigurator(hwOverride: String? = null, presetKey: String = "SHORT_FAST_DEFAULT") {
            scope.launch { cfgPlan = withContext(Dispatchers.IO) { AtakMeshtasticConfigManager.buildConfigurationPlan(ctx, hwOverride, presetKey) } }
        }
        fun handleFirmwareAndApplyMesh(p: AtakMeshtasticPlan) {
            if (p.fwMin.isNotBlank()) {
                scope.launch {
                    val meshApp = withContext(Dispatchers.IO) { ApkInstallerManager.getInstalledPkgInfo(ctx, "com.geeksville.mesh")?.versionName ?: "0.0.0" }
                    val isOk = AtakMeshtasticConfigManager.checkFirmwareVersion(meshApp, p.fwMin, p.fwMax)
                    if (!isOk) { fwWarnDialog = p } else { statusMsg = AtakMeshtasticConfigManager.applyMeshtasticRadioConfigViaApp(ctx, p) }
                }
            } else {
                statusMsg = AtakMeshtasticConfigManager.applyMeshtasticRadioConfigViaApp(ctx, p)
            }
        }
        if (showSetup) {
            Surface(modifier = Modifier.fillMaxSize(), color = BgDark) {
                Column(modifier = Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("Field TAK Hub - Konfiguracja", color = TextLight, fontWeight = FontWeight.Bold, fontSize = 22.sp)
                    PermRow("1. Bluetooth / Urzadzenia w poblizu", bleOk, "NADAJ BLUETOOTH") {
                        if (Build.VERSION.SDK_INT >= 31) permLauncher.launch(arrayOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)) else permLauncher.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION))
                    }
                    PermRow("2. Lokalizacja (GPS)", locOk, "NADAJ LOKALIZACJE") { permLauncher.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)) }
                    PermRow("3. Aparat (Skaner QR)", camOk, "NADAJ APARAT") { permLauncher.launch(arrayOf(Manifest.permission.CAMERA)) }
                    PermRow("4. Dostep do plikow", stgOk, "WLACZ DOSTEP") {
                        if (Build.VERSION.SDK_INT >= 30) ctx.startActivity(Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:${ctx.packageName}")).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) })
                    }
                    PermRow("5. Instalacja APK", insOk, "WLACZ INSTALACJE") {
                        if (Build.VERSION.SDK_INT >= 26) ctx.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${ctx.packageName}")).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) })
                    }
                    Button(onClick = { prefs.edit().putBoolean("done", true).apply(); manualOpen = false; showSetup = false }, modifier = Modifier.fillMaxWidth().height(50.dp), colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818))) {
                        Text("PRZEJDZ DO MENU GLOWNEGO", fontWeight = FontWeight.Bold)
                    }
                }
            }
        } else {
            Column(modifier = Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding()) {
                Box(modifier = Modifier.weight(1f).fillMaxWidth()) { content() }
                Surface(color = CardBg, border = BorderStroke(1.dp, BorderTeal), modifier = Modifier.fillMaxWidth()) {
                    Column(modifier = Modifier.fillMaxWidth().padding(8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            Button(onClick = { qrGalleryLauncher.launch("image/*") }, enabled = !isQrBusy, modifier = Modifier.weight(1f).height(38.dp), colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818)), contentPadding = PaddingValues(2.dp)) {
                                Text(if (isQrBusy) "Odczyt QR..." else "QR z galerii", fontWeight = FontWeight.Bold, fontSize = 11.sp)
                            }
                            OutlinedButton(onClick = { showLibraryDialog = true }, modifier = Modifier.weight(1.1f).height(38.dp), border = BorderStroke(1.dp, AccentCyan), contentPadding = PaddingValues(2.dp)) {
                                Text("Pakiety .FTAK (${pkgEntries.size})", color = AccentCyan, fontWeight = FontWeight.Bold, fontSize = 11.sp)
                            }
                            OutlinedButton(onClick = { statusMsg = null; openConfigurator() }, modifier = Modifier.weight(0.95f).height(38.dp), border = BorderStroke(1.dp, AccentCyan), contentPadding = PaddingValues(2.dp)) {
                                Text("Konfiguruj", color = AccentCyan, fontWeight = FontWeight.Bold, fontSize = 11.sp)
                            }
                        }
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            OutlinedButton(onClick = { refresh(); manualOpen = true; showSetup = true }, modifier = Modifier.weight(1f).height(36.dp), border = BorderStroke(1.dp, if (allOk) BorderTeal else StatusRed), contentPadding = PaddingValues(2.dp)) {
                                Text(if (allOk) "Uprawnienia \u2713" else "Uprawnienia !", color = if (allOk) AccentCyan else StatusRed, fontSize = 11.sp)
                            }
                            OutlinedButton(onClick = { openApkManagerLocal() }, modifier = Modifier.weight(1f).height(36.dp), border = BorderStroke(1.dp, BorderTeal), contentPadding = PaddingValues(2.dp)) {
                                Text("APK / Pluginy", color = TextLight, fontSize = 11.sp)
                            }
                            OutlinedButton(onClick = { scope.launch { logRep = withContext(Dispatchers.IO) { ExtendedDiagnosticsManager.generateAndSaveLog(ctx) } } }, modifier = Modifier.weight(0.65f).height(36.dp), border = BorderStroke(1.dp, BorderTeal), contentPadding = PaddingValues(2.dp)) {
                                Text(".LOG", color = TextLight, fontSize = 11.sp)
                            }
                        }
                    }
                }
            }
        }
        if (showLibraryDialog) {
            AlertDialog(onDismissRequest = { showLibraryDialog = false }, containerColor = CardBg, titleContentColor = TextLight, textContentColor = TextLight,
                title = { Text("Biblioteka Pakietow (${pkgEntries.size})", fontWeight = FontWeight.Bold, fontSize = 16.sp) },
                text = {
                    Column(modifier = Modifier.heightIn(max = 420.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            Button(onClick = { qrGalleryLauncher.launch("image/*") }, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818))) { Text("+ QR z galerii", fontWeight = FontWeight.Bold, fontSize = 11.sp) }
                            OutlinedButton(onClick = { ftakFilePicker.launch("*/*") }, modifier = Modifier.weight(1f), border = BorderStroke(1.dp, AccentCyan)) { Text("+ Importuj", color = AccentCyan, fontWeight = FontWeight.Bold, fontSize = 11.sp) }
                        }
                        pkgEntries.forEach { item ->
                            Card(modifier = Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = BgDark), border = BorderStroke(1.dp, if (item.isActive) AccentCyan else BorderTeal)) {
                                Column(modifier = Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                    Text("${item.projectTitle} (${item.sizeKb} KB)", color = TextLight, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                        Button(onClick = { FtakPackageLibraryManager.setActivePackage(ctx, item.file); showLibraryDialog = false; openConfigurator() }, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818))) { Text("Wdroz", fontWeight = FontWeight.Bold, fontSize = 11.sp) }
                                        OutlinedButton(onClick = { FtakPackageLibraryManager.deletePackage(ctx, item); pkgEntries = FtakPackageLibraryManager.listAllPackages(ctx) }, modifier = Modifier.weight(1f), border = BorderStroke(1.dp, StatusRed)) { Text("Usun", color = StatusRed, fontWeight = FontWeight.Bold, fontSize = 11.sp) }
                                    }
                                }
                            }
                        }
                    }
                },
                confirmButton = { TextButton(onClick = { showLibraryDialog = false }) { Text("Zamknij", color = TextLight) } }
            )
        }
        fwWarnDialog?.let { p ->
            AlertDialog(onDismissRequest = { fwWarnDialog = null }, containerColor = CardBg, titleContentColor = TextLight, textContentColor = TextLight,
                title = { Text("Wymagana aktualizacja radia!", color = StatusRed, fontWeight = FontWeight.Bold, fontSize = 16.sp) },
                text = { Text("Wymagane zaktualizowanie radia do min: ${p.fwMin}.", color = TextLight, fontSize = 13.sp) },
                confirmButton = { Button(onClick = { fwWarnDialog = null; statusMsg = AtakMeshtasticConfigManager.applyMeshtasticRadioConfigViaApp(ctx, p) }, colors = ButtonDefaults.buttonColors(containerColor = StatusRed, contentColor = Color.White)) { Text("Zignoruj", fontWeight = FontWeight.Bold) } },
                dismissButton = { TextButton(onClick = { fwWarnDialog = null }) { Text("Anuluj", color = TextLight) } }
            )
        }
        cfgPlan?.let { p ->
            AlertDialog(onDismissRequest = { cfgPlan = null }, containerColor = CardBg, titleContentColor = TextLight, textContentColor = TextLight,
                title = { Text("Konfigurator SHORT_FAST + ATAK", fontWeight = FontWeight.Bold, fontSize = 15.sp) },
                text = {
                    Column(modifier = Modifier.heightIn(max = 440.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                            OutlinedButton(onClick = { openConfigurator(null, "SHORT_FAST_DEFAULT") }, modifier = Modifier.weight(1f), contentPadding = PaddingValues(2.dp)) { Text("Domyslny", color = AccentCyan, fontSize = 10.sp) }
                            OutlinedButton(onClick = { openConfigurator(null, "STRZELANKA") }, modifier = Modifier.weight(1f), contentPadding = PaddingValues(2.dp)) { Text("Strzelanka", color = TextLight, fontSize = 10.sp) }
                            OutlinedButton(onClick = { openConfigurator(null, "CQB") }, modifier = Modifier.weight(0.9f), contentPadding = PaddingValues(2.dp)) { Text("CQB", color = TextLight, fontSize = 10.sp) }
                            OutlinedButton(onClick = { openConfigurator(null, "MILSIM") }, modifier = Modifier.weight(1f), contentPadding = PaddingValues(2.dp)) { Text("Milsim", color = TextLight, fontSize = 10.sp) }
                        }
                        p.diffs.forEach { d -> Text("[${if (d.changed) "ZMIANA" else "OK"}] ${d.parameter}: ${d.targetValue}", fontFamily = FontFamily.Monospace, color = if (d.changed) AccentCyan else TextMuted, fontSize = 11.sp) }
                        statusMsg?.let { Text(it, color = AccentCyan, fontWeight = FontWeight.Bold, fontSize = 12.sp) }
                        Button(onClick = { handleFirmwareAndApplyMesh(p) }, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818))) { Text("1. Wgraj profil (SHORT_FAST) do Meshtastic", fontWeight = FontWeight.Bold, fontSize = 12.sp) }
                        OutlinedButton(onClick = { statusMsg = AtakMeshtasticConfigManager.applyAtakCivConfigPackage(ctx, p) }, modifier = Modifier.fillMaxWidth(), border = BorderStroke(1.dp, AccentCyan)) { Text("2. Wgraj czysty config.pref (PLI) do ATAK", color = AccentCyan, fontWeight = FontWeight.Bold, fontSize = 12.sp) }
                    }
                },
                confirmButton = { TextButton(onClick = { cfgPlan = null }) { Text("Zamknij", color = TextLight) } }
            )
        }
        if (showApkDialog) {
            AlertDialog(onDismissRequest = { showApkDialog = false }, containerColor = CardBg, titleContentColor = TextLight, textContentColor = TextLight,
                title = { Text("Aplikacje i Pluginy", fontWeight = FontWeight.Bold, fontSize = 16.sp) },
                text = {
                    Column(modifier = Modifier.heightIn(max = 420.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        appEntries.forEach { item ->
                            Card(modifier = Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = BgDark), border = BorderStroke(1.dp, BorderTeal)) {
                                Column(modifier = Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                    Text("${item.title}: ${if (item.isInstalled) "OK (${item.installedVersion})" else "BRAK"}", color = if (item.isInstalled) AccentCyan else StatusRed, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                                    if (item.hasLocalApk && item.localApkFile != null) {
                                        Button(onClick = { statusMsg = ApkInstallerManager.launchApkInstall(ctx, item.localApkFile) }, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818))) { Text("Zainstaluj / Aktualizuj", fontWeight = FontWeight.Bold) }
                                    }
                                }
                            }
                        }
                        OutlinedButton(onClick = { apkFilePicker.launch("*/*") }, modifier = Modifier.fillMaxWidth(), border = BorderStroke(1.dp, AccentCyan)) { Text("Wskaz plik .APK z dysku", color = AccentCyan) }
                    }
                },
                confirmButton = { Button(onClick = { openApkManagerLocal() }, colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818))) { Text("Odswiez", fontWeight = FontWeight.Bold) } },
                dismissButton = { TextButton(onClick = { showApkDialog = false }) { Text("Zamknij", color = TextLight) } }
            )
        }
        logRep?.let { r ->
            AlertDialog(onDismissRequest = { logRep = null }, containerColor = CardBg, titleContentColor = TextLight, textContentColor = TextLight,
                title = { Text("Raport .LOG") },
                text = { Column(modifier = Modifier.heightIn(max = 360.dp).verticalScroll(rememberScrollState())) { Text(r.fullText, color = TextLight, fontFamily = FontFamily.Monospace, fontSize = 11.sp) } },
                confirmButton = { Button(onClick = { ExtendedDiagnosticsManager.shareLogReport(ctx, r) }, colors = ButtonDefaults.buttonColors(containerColor = AccentCyan, contentColor = Color(0xFF041818))) { Text("Udostepnij", fontWeight = FontWeight.Bold) } },
                dismissButton = { TextButton(onClick = { logRep = null }) { Text("Zamknij", color = TextLight) } }
            )
        }
    }
}
@Composable
private fun PermRow(title: String, ok: Boolean, btn: String, onClick: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(12.dp), colors = CardDefaults.cardColors(containerColor = CardBg), border = BorderStroke(1.dp, if (ok) BorderTeal else StatusRed)) {
        Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Text(title, color = TextLight, fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                Text(if (ok) "OK" else "WYMAGANE", color = if (ok) AccentCyan else StatusRed, fontWeight = FontWeight.Bold, fontSize = 12.sp)
            }
            if (!ok) OutlinedButton(onClick = onClick, modifier = Modifier.fillMaxWidth(), border = BorderStroke(1.dp, AccentCyan)) { Text(btn, color = AccentCyan, fontWeight = FontWeight.Bold) }
        }
    }
}