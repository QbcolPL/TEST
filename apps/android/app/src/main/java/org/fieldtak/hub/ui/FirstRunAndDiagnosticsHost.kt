package org.fieldtak.hub.ui
import android.Manifest
import android.content.Context
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
import androidx.compose.ui.Alignment
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
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.fieldtak.hub.diagnostics.ExtendedDiagnosticsManager
import org.fieldtak.hub.diagnostics.GeneratedLogReport
import org.fieldtak.hub.util.ApkInstallerManager
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
fun FirstRunAndDiagnosticsHost(
    content: @Composable () -> Unit
) {
    if (LocalGateActive.current) {
        content()
        return
    }
    CompositionLocalProvider(LocalGateActive provides true) {
        val ctx = LocalContext.current
        val scope = rememberCoroutineScope()
        val prefs = remember {
            ctx.getSharedPreferences("fth_setup_24", 0)
        }
        var camOk by remember {
            mutableStateOf(FirstRunPermissionHelper.isCameraReady(ctx))
        }
        var locOk by remember {
            mutableStateOf(FirstRunPermissionHelper.isLocationReady(ctx))
        }
        var bleOk by remember {
            mutableStateOf(
                FirstRunPermissionHelper.isNearbyBluetoothReady(ctx)
            )
        }
        var stgOk by remember {
            mutableStateOf(
                FirstRunPermissionHelper.isStorageAccessReady(ctx)
            )
        }
        var insOk by remember {
            mutableStateOf(
                FirstRunPermissionHelper.canInstallUnknownApps(ctx)
            )
        }
        val allOk = camOk && locOk && bleOk && stgOk && insOk
        var showSetup by remember {
            mutableStateOf(!allOk && !prefs.getBoolean("done", false))
        }
        var manualOpen by remember { mutableStateOf(false) }
        var logRep by remember {
            mutableStateOf<GeneratedLogReport?>(null)
        }
        var showApkDialog by remember { mutableStateOf(false) }
        var appEntries by remember {
            mutableStateOf<List<DetectedAppEntry>>(emptyList())
        }
        var showLibraryDialog by remember { mutableStateOf(false) }
        var pkgEntries by remember {
            mutableStateOf<List<FtakLibraryEntry>>(emptyList())
        }
        var pkgToDelete by remember {
            mutableStateOf<FtakLibraryEntry?>(null)
        }
        var confirmClearAll by remember { mutableStateOf(false) }
        var isQrBusy by remember { mutableStateOf(false) }
        var statusMsg by remember { mutableStateOf<String?>(null) }
        fun reloadPackages() {
            scope.launch {
                pkgEntries = withContext(Dispatchers.IO) {
                    FtakPackageLibraryManager.listAllPackages(ctx)
                }
            }
        }
        fun refresh() {
            camOk = FirstRunPermissionHelper.isCameraReady(ctx)
            locOk = FirstRunPermissionHelper.isLocationReady(ctx)
            bleOk =
                FirstRunPermissionHelper.isNearbyBluetoothReady(ctx)
            stgOk =
                FirstRunPermissionHelper.isStorageAccessReady(ctx)
            insOk =
                FirstRunPermissionHelper.canInstallUnknownApps(ctx)
            if (camOk && locOk && bleOk && stgOk && insOk) {
                prefs.edit().putBoolean("done", true).apply()
                if (!manualOpen) showSetup = false
            }
            reloadPackages()
            if (showApkDialog) {
                scope.launch {
                    appEntries = withContext(Dispatchers.IO) {
                        ApkInstallerManager.scanAllAppsAndPlugins(ctx)
                    }
                }
            }
        }
        val lo = LocalLifecycleOwner.current
        DisposableEffect(lo) {
            val obs = LifecycleEventObserver { _, ev ->
                if (ev == Lifecycle.Event.ON_RESUME) refresh()
            }
            lo.lifecycle.addObserver(obs)
            onDispose { lo.lifecycle.removeObserver(obs) }
        }
        LaunchedEffect(showSetup) {
            while (showSetup) {
                refresh()
                delay(800L)
            }
        }
        val permLauncher = rememberLauncherForActivityResult(
            ActivityResultContracts.RequestMultiplePermissions()
        ) { refresh() }
        val apkFilePicker = rememberLauncherForActivityResult(
            ActivityResultContracts.GetContent()
        ) { uri: Uri? ->
            if (uri == null) return@rememberLauncherForActivityResult
            scope.launch {
                val msg = withContext(Dispatchers.IO) {
                    try {
                        val tmp = File(ctx.cacheDir, "picked_install.apk")
                        ctx.contentResolver.openInputStream(uri)?.use { i ->
                            FileOutputStream(tmp).use { o -> i.copyTo(o) }
                        }
                        ApkInstallerManager.launchApkInstall(ctx, tmp)
                    } catch (e: Exception) {
                        "B\u0142\u0105d odczytu APK: ${e.message}"
                    }
                }
                statusMsg = msg
            }
        }
        val ftakFilePicker = rememberLauncherForActivityResult(
            ActivityResultContracts.GetContent()
        ) { uri: Uri? ->
            if (uri == null) return@rememberLauncherForActivityResult
            scope.launch {
                val msg = withContext(Dispatchers.IO) {
                    FtakPackageLibraryManager.importFtakFromUri(ctx, uri)
                }
                pkgEntries = withContext(Dispatchers.IO) {
                    FtakPackageLibraryManager.listAllPackages(ctx)
                }
                statusMsg = msg
                showLibraryDialog = true
                Toast.makeText(ctx, msg, Toast.LENGTH_LONG).show()
            }
        }
        val qrGalleryLauncher = rememberLauncherForActivityResult(
            ActivityResultContracts.GetContent()
        ) { uri: Uri? ->
            if (uri == null) return@rememberLauncherForActivityResult
            isQrBusy = true
            statusMsg = "Dekodowanie kodu QR z obrazu..."
            scope.launch {
                val decodedRes = QrImageDecoder.decodeFromUri(ctx, uri)
                decodedRes.fold(
                    onSuccess = { qrText ->
                        val msg = withContext(Dispatchers.IO) {
                            FtakPackageLibraryManager.processDecodedQrPayload(
                                ctx, qrText
                            ) { prog ->
                                statusMsg = prog
                            }
                        }
                        pkgEntries = withContext(Dispatchers.IO) {
                            FtakPackageLibraryManager.listAllPackages(ctx)
                        }
                        isQrBusy = false
                        statusMsg = msg
                        showLibraryDialog = true
                        Toast.makeText(ctx, msg, Toast.LENGTH_LONG).show()
                    },
                    onFailure = {
                        isQrBusy = false
                        val err = "Nie znaleziono kodu QR na wybranym zdj\u0119ciu."
                        statusMsg = err
                        Toast.makeText(ctx, err, Toast.LENGTH_LONG).show()
                    }
                )
            }
        }
        fun makeLog() {
            scope.launch {
                val r = withContext(Dispatchers.IO) {
                    ExtendedDiagnosticsManager.generateAndSaveLog(ctx)
                }
                logRep = r
                Toast.makeText(
                    ctx,
                    "Zapisano: ${r.savedLocationDescription}",
                    Toast.LENGTH_LONG
                ).show()
            }
        }
        fun openApkManager() {
            scope.launch {
                appEntries = withContext(Dispatchers.IO) {
                    ApkInstallerManager.scanAllAppsAndPlugins(ctx)
                }
                statusMsg = null
                showApkDialog = true
            }
        }
        fun openPackageLibrary() {
            scope.launch {
                pkgEntries = withContext(Dispatchers.IO) {
                    FtakPackageLibraryManager.listAllPackages(ctx)
                }
                statusMsg = null
                showLibraryDialog = true
            }
        }
        if (showSetup) {
            Surface(
                modifier = Modifier.fillMaxSize(),
                color = BgDark
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .statusBarsPadding()
                        .navigationBarsPadding()
                        .verticalScroll(rememberScrollState())
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        "Field TAK Hub",
                        color = TextLight,
                        fontWeight = FontWeight.Bold,
                        fontSize = 24.sp
                    )
                    Card(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp),
                        colors = CardDefaults.cardColors(containerColor = CardBg),
                        border = BorderStroke(1.dp, BorderTeal)
                    ) {
                        Column(modifier = Modifier.padding(14.dp)) {
                            Text(
                                "PIERWSZA KONFIGURACJA UPRAWNIE\u0143",
                                color = AccentCyan,
                                fontWeight = FontWeight.Bold,
                                fontSize = 14.sp
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                "Nadaj wymagane uprawnienia systemowe, aby " +
                                "odblokowa\u0107 skaner QR, \u0142\u0105czno\u015B\u0107 Bluetooth z radiem " +
                                "Meshtastic oraz instalator pakiet\u00F3w APK.",
                                color = TextMuted,
                                fontSize = 13.sp
                            )
                        }
                    }
                    if (!bleOk || !locOk || !camOk) {
                        Button(
                            onClick = {
                                val list = mutableListOf(
                                    Manifest.permission.CAMERA,
                                    Manifest.permission.ACCESS_FINE_LOCATION,
                                    Manifest.permission.ACCESS_COARSE_LOCATION
                                )
                                if (Build.VERSION.SDK_INT >= 31) {
                                    list.add(Manifest.permission.BLUETOOTH_SCAN)
                                    list.add(Manifest.permission.BLUETOOTH_CONNECT)
                                }
                                if (Build.VERSION.SDK_INT >= 33) {
                                    list.add(Manifest.permission.POST_NOTIFICATIONS)
                                }
                                permLauncher.launch(list.toTypedArray())
                            },
                            modifier = Modifier.fillMaxWidth().height(48.dp),
                            shape = RoundedCornerShape(12.dp),
                            colors = ButtonDefaults.buttonColors(
                                containerColor = AccentCyan,
                                contentColor = Color(0xFF041818)
                            )
                        ) {
                            Text(
                                "NADAJ PAKIET UPRAWNIE\u0143 (BLE + GPS + APARAT)",
                                fontWeight = FontWeight.Bold,
                                fontSize = 13.sp
                            )
                        }
                    }
                    PermRow(
                        "1. Bluetooth / Urz\u0105dzenia w pobli\u017Cu",
                        bleOk,
                        "NADAJ BLUETOOTH"
                    ) {
                        if (Build.VERSION.SDK_INT >= 31) {
                            permLauncher.launch(arrayOf(
                                Manifest.permission.BLUETOOTH_SCAN,
                                Manifest.permission.BLUETOOTH_CONNECT
                            ))
                        } else {
                            permLauncher.launch(arrayOf(
                                Manifest.permission.ACCESS_FINE_LOCATION
                            ))
                        }
                    }
                    PermRow(
                        "2. Lokalizacja (GPS)",
                        locOk,
                        "NADAJ LOKALIZACJ\u0118"
                    ) {
                        permLauncher.launch(arrayOf(
                            Manifest.permission.ACCESS_FINE_LOCATION,
                            Manifest.permission.ACCESS_COARSE_LOCATION
                        ))
                    }
                    PermRow(
                        "3. Aparat (Skaner QR)",
                        camOk,
                        "NADAJ APARAT"
                    ) {
                        permLauncher.launch(arrayOf(
                            Manifest.permission.CAMERA
                        ))
                    }
                    PermRow(
                        "4. Dost\u0119p do wszystkich plik\u00F3w",
                        stgOk,
                        "W\u0141\u0104CZ DOST\u0118P DO PLIK\u00D3W"
                    ) {
                        if (Build.VERSION.SDK_INT >= 30) {
                            try {
                                val i = Intent(
                                    Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                                    Uri.parse("package:${ctx.packageName}")
                                )
                                i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                                ctx.startActivity(i)
                            } catch (_: Exception) {
                                val i = Intent(
                                    Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION
                                )
                                i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                                ctx.startActivity(i)
                            }
                        } else {
                            permLauncher.launch(arrayOf(
                                Manifest.permission.READ_EXTERNAL_STORAGE,
                                Manifest.permission.WRITE_EXTERNAL_STORAGE
                            ))
                        }
                    }
                    PermRow(
                        "5. Instalowanie nieznanych APK",
                        insOk,
                        "W\u0141\u0104CZ INSTALOWANIE APK"
                    ) {
                        if (Build.VERSION.SDK_INT >= 26) {
                            val i = Intent(
                                Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                                Uri.parse("package:${ctx.packageName}")
                            )
                            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                            ctx.startActivity(i)
                        }
                    }
                    Spacer(modifier = Modifier.height(4.dp))
                    Button(
                        onClick = {
                            prefs.edit().putBoolean("done", true).apply()
                            manualOpen = false
                            showSetup = false
                        },
                        modifier = Modifier.fillMaxWidth().height(52.dp),
                        shape = RoundedCornerShape(12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = AccentCyan,
                            contentColor = Color(0xFF041818)
                        )
                    ) {
                        Text(
                            "PRZEJD\u0179 DO MENU G\u0141\u00D3WNEGO",
                            fontWeight = FontWeight.Bold,
                            fontSize = 14.sp
                        )
                    }
                }
            }
        } else {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .statusBarsPadding()
                    .navigationBarsPadding()
            ) {
                Box(modifier = Modifier.weight(1f).fillMaxWidth()) {
                    content()
                }
                Surface(
                    color = CardBg,
                    border = BorderStroke(1.dp, BorderTeal),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 8.dp, vertical = 6.dp),
                        verticalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(6.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Button(
                                onClick = { qrGalleryLauncher.launch("image/*") },
                                enabled = !isQrBusy,
                                modifier = Modifier.weight(1f).height(40.dp),
                                shape = RoundedCornerShape(10.dp),
                                colors = ButtonDefaults.buttonColors(
                                    containerColor = AccentCyan,
                                    contentColor = Color(0xFF041818)
                                ),
                                contentPadding = PaddingValues(4.dp)
                            ) {
                                Text(
                                    if (isQrBusy) "Odczyt QR..." else "QR z galerii",
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 11.sp
                                )
                            }
                            OutlinedButton(
                                onClick = { openPackageLibrary() },
                                modifier = Modifier.weight(1.15f).height(40.dp),
                                shape = RoundedCornerShape(10.dp),
                                border = BorderStroke(1.dp, AccentCyan),
                                contentPadding = PaddingValues(4.dp)
                            ) {
                                Text(
                                    "Pakiety .FTAK (${pkgEntries.size})",
                                    color = AccentCyan,
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 11.sp
                                )
                            }
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(6.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            OutlinedButton(
                                onClick = {
                                    refresh()
                                    manualOpen = true
                                    showSetup = true
                                },
                                modifier = Modifier.weight(1f).height(36.dp),
                                shape = RoundedCornerShape(10.dp),
                                border = BorderStroke(
                                    1.dp,
                                    if (allOk) BorderTeal else StatusRed
                                ),
                                contentPadding = PaddingValues(2.dp)
                            ) {
                                Text(
                                    if (allOk) "Uprawnienia \u2713" else "Uprawnienia !",
                                    color = if (allOk) AccentCyan else StatusRed,
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 11.sp
                                )
                            }
                            OutlinedButton(
                                onClick = { openApkManager() },
                                modifier = Modifier.weight(1f).height(36.dp),
                                shape = RoundedCornerShape(10.dp),
                                border = BorderStroke(1.dp, BorderTeal),
                                contentPadding = PaddingValues(2.dp)
                            ) {
                                Text(
                                    "APK / Pluginy",
                                    color = TextLight,
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 11.sp
                                )
                            }
                            OutlinedButton(
                                onClick = { makeLog() },
                                modifier = Modifier.weight(0.65f).height(36.dp),
                                shape = RoundedCornerShape(10.dp),
                                border = BorderStroke(1.dp, BorderTeal),
                                contentPadding = PaddingValues(2.dp)
                            ) {
                                Text(
                                    ".LOG",
                                    color = TextLight,
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 11.sp
                                )
                            }
                        }
                    }
                }
            }
        }
        if (showLibraryDialog) {
            AlertDialog(
                onDismissRequest = { showLibraryDialog = false },
                containerColor = CardBg,
                titleContentColor = TextLight,
                textContentColor = TextLight,
                title = {
                    Text(
                        "Biblioteka Pakiet\u00F3w .FTAK (${pkgEntries.size})",
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp
                    )
                },
                text = {
                    Column(
                        modifier = Modifier
                            .heightIn(max = 440.dp)
                            .verticalScroll(rememberScrollState()),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            Button(
                                onClick = { qrGalleryLauncher.launch("image/*") },
                                modifier = Modifier.weight(1f),
                                colors = ButtonDefaults.buttonColors(
                                    containerColor = AccentCyan,
                                    contentColor = Color(0xFF041818)
                                )
                            ) {
                                Text(
                                    "+ QR z galerii",
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 11.sp
                                )
                            }
                            OutlinedButton(
                                onClick = { ftakFilePicker.launch("*/*") },
                                modifier = Modifier.weight(1f),
                                border = BorderStroke(1.dp, AccentCyan)
                            ) {
                                Text(
                                    "+ Importuj .FTAK",
                                    color = AccentCyan,
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 11.sp
                                )
                            }
                        }
                        if (pkgEntries.isEmpty()) {
                            Card(
                                modifier = Modifier.fillMaxWidth(),
                                colors = CardDefaults.cardColors(containerColor = BgDark),
                                border = BorderStroke(1.dp, BorderTeal)
                            ) {
                                Column(modifier = Modifier.padding(12.dp)) {
                                    Text(
                                        "Brak zapisanych pakiet\u00F3w .FTAK w bibliotece.",
                                        color = TextLight,
                                        fontWeight = FontWeight.SemiBold,
                                        fontSize = 13.sp
                                    )
                                    Spacer(modifier = Modifier.height(4.dp))
                                    Text(
                                        "U\u017Cyj przycisku [+ QR z galerii] lub [+ Importuj .FTAK], " +
                                        "aby doda\u0107 pakiet konfiguracyjny.",
                                        color = TextMuted,
                                        fontSize = 12.sp
                                    )
                                }
                            }
                        } else {
                            pkgEntries.forEach { item ->
                                Card(
                                    modifier = Modifier.fillMaxWidth(),
                                    colors = CardDefaults.cardColors(containerColor = BgDark),
                                    border = BorderStroke(
                                        1.dp,
                                        if (item.isActive) AccentCyan else BorderTeal
                                    )
                                ) {
                                    Column(
                                        modifier = Modifier.padding(10.dp),
                                        verticalArrangement = Arrangement.spacedBy(6.dp)
                                    ) {
                                        Row(
                                            modifier = Modifier.fillMaxWidth(),
                                            horizontalArrangement = Arrangement.SpaceBetween,
                                            verticalAlignment = Alignment.CenterVertically
                                        ) {
                                            Text(
                                                item.projectTitle,
                                                color = TextLight,
                                                fontWeight = FontWeight.Bold,
                                                fontSize = 13.sp,
                                                modifier = Modifier.weight(1f)
                                            )
                                            Text(
                                                if (item.isPartBuffer) "BUFOR .PART"
                                                else if (item.isActive) "AKTYWNY \u2713"
                                                else "v${item.projectVersion}",
                                                color = if (item.isPartBuffer) StatusRed
                                                    else AccentCyan,
                                                fontWeight = FontWeight.Bold,
                                                fontSize = 11.sp
                                            )
                                        }
                                        Text(
                                            "Plik: ${item.fileName} (${item.sizeKb} KB) | ${item.modifiedDate}",
                                            color = TextMuted,
                                            fontSize = 11.sp
                                        )
                                        if (!item.isPartBuffer) {
                                            Text(
                                                "Zawarto\u015B\u0107: APK=${item.apkCount}, Mapy=${item.mapCount}, Nak\u0142adki=${item.overlayCount}",
                                                color = TextLight,
                                                fontSize = 11.sp
                                            )
                                        }
                                        Row(
                                            modifier = Modifier.fillMaxWidth(),
                                            horizontalArrangement = Arrangement.spacedBy(6.dp)
                                        ) {
                                            if (!item.isPartBuffer) {
                                                Button(
                                                    onClick = {
                                                        statusMsg = FtakPackageLibraryManager
                                                            .setActivePackage(ctx, item.file)
                                                        pkgEntries = FtakPackageLibraryManager
                                                            .listAllPackages(ctx)
                                                        showLibraryDialog = false
                                                        openApkManager()
                                                    },
                                                    modifier = Modifier.weight(1f),
                                                    colors = ButtonDefaults.buttonColors(
                                                        containerColor = AccentCyan,
                                                        contentColor = Color(0xFF041818)
                                                    )
                                                ) {
                                                    Text(
                                                        "Wdr\u00F3\u017C / APK",
                                                        fontWeight = FontWeight.Bold,
                                                        fontSize = 11.sp
                                                    )
                                                }
                                            }
                                            OutlinedButton(
                                                onClick = { pkgToDelete = item },
                                                modifier = Modifier.weight(1f),
                                                border = BorderStroke(1.dp, StatusRed)
                                            ) {
                                                Text(
                                                    "Usu\u0144 pakiet",
                                                    color = StatusRed,
                                                    fontWeight = FontWeight.Bold,
                                                    fontSize = 11.sp
                                                )
                                            }
                                        }
                                    }
                                }
                            }
                            OutlinedButton(
                                onClick = { confirmClearAll = true },
                                modifier = Modifier.fillMaxWidth(),
                                border = BorderStroke(1.dp, StatusRed)
                            ) {
                                Text(
                                    "Usu\u0144 wszystkie pakiety z biblioteki (${pkgEntries.size})",
                                    color = StatusRed,
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 12.sp
                                )
                            }
                        }
                        statusMsg?.let { msg ->
                            Text(
                                msg,
                                color = AccentCyan,
                                fontWeight = FontWeight.Bold,
                                fontSize = 12.sp
                            )
                        }
                    }
                },
                confirmButton = {
                    Button(
                        onClick = { openPackageLibrary() },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = AccentCyan,
                            contentColor = Color(0xFF041818)
                        )
                    ) {
                        Text("Od\u015Bwie\u017C", fontWeight = FontWeight.Bold)
                    }
                },
                dismissButton = {
                    TextButton(onClick = { showLibraryDialog = false }) {
                        Text("Zamknij", color = TextLight)
                    }
                }
            )
        }
        pkgToDelete?.let { target ->
            AlertDialog(
                onDismissRequest = { pkgToDelete = null },
                containerColor = CardBg,
                titleContentColor = TextLight,
                textContentColor = TextLight,
                title = {
                    Text("Usun\u0105\u0107 pakiet .FTAK?", fontWeight = FontWeight.Bold)
                },
                text = {
                    Text(
                        "Czy na pewno chcesz usun\u0105\u0107 pakiet \"${target.projectTitle}\" " +
                        "(${target.fileName}) z pami\u0119ci telefonu?",
                        color = TextLight
                    )
                },
                confirmButton = {
                    Button(
                        onClick = {
                            val msg = FtakPackageLibraryManager.deletePackage(ctx, target)
                            pkgToDelete = null
                            pkgEntries = FtakPackageLibraryManager.listAllPackages(ctx)
                            statusMsg = msg
                            Toast.makeText(ctx, msg, Toast.LENGTH_SHORT).show()
                        },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = StatusRed,
                            contentColor = Color.White
                        )
                    ) {
                        Text("Tak, usu\u0144", fontWeight = FontWeight.Bold)
                    }
                },
                dismissButton = {
                    TextButton(onClick = { pkgToDelete = null }) {
                        Text("Anuluj", color = TextLight)
                    }
                }
            )
        }
        if (confirmClearAll) {
            AlertDialog(
                onDismissRequest = { confirmClearAll = false },
                containerColor = CardBg,
                titleContentColor = TextLight,
                textContentColor = TextLight,
                title = {
                    Text("Wyczy\u015Bci\u0107 ca\u0142\u0105 bibliotek\u0119?", fontWeight = FontWeight.Bold)
                },
                text = {
                    Text(
                        "Wszystkie zapisane pakiety .FTAK oraz niedoko\u0144czone bufory .part " +
                        "zostan\u0105 trwale usuni\u0119te z aplikacji.",
                        color = TextLight
                    )
                },
                confirmButton = {
                    Button(
                        onClick = {
                            val msg = FtakPackageLibraryManager.deleteAllPackages(ctx)
                            confirmClearAll = false
                            pkgEntries = FtakPackageLibraryManager.listAllPackages(ctx)
                            statusMsg = msg
                            Toast.makeText(ctx, msg, Toast.LENGTH_SHORT).show()
                        },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = StatusRed,
                            contentColor = Color.White
                        )
                    ) {
                        Text("Usu\u0144 wszystkie", fontWeight = FontWeight.Bold)
                    }
                },
                dismissButton = {
                    TextButton(onClick = { confirmClearAll = false }) {
                        Text("Anuluj", color = TextLight)
                    }
                }
            )
        }
        if (showApkDialog) {
            AlertDialog(
                onDismissRequest = { showApkDialog = false },
                containerColor = CardBg,
                titleContentColor = TextLight,
                textContentColor = TextLight,
                title = {
                    Text(
                        "Aplikacje i Pluginy (ATAK / Meshtastic)",
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp
                    )
                },
                text = {
                    Column(
                        modifier = Modifier
                            .heightIn(max = 430.dp)
                            .verticalScroll(rememberScrollState()),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        appEntries.forEach { item ->
                            Card(
                                modifier = Modifier.fillMaxWidth(),
                                colors = CardDefaults.cardColors(
                                    containerColor = BgDark
                                ),
                                border = BorderStroke(1.dp, BorderTeal)
                            ) {
                                Column(
                                    modifier = Modifier.padding(10.dp),
                                    verticalArrangement =
                                        Arrangement.spacedBy(6.dp)
                                ) {
                                    Row(
                                        modifier = Modifier.fillMaxWidth(),
                                        horizontalArrangement =
                                            Arrangement.SpaceBetween
                                    ) {
                                        Text(
                                            item.title,
                                            color = TextLight,
                                            fontWeight = FontWeight.Bold,
                                            fontSize = 13.sp
                                        )
                                        Text(
                                            if (item.isInstalled)
                                                "OK (${item.installedVersion})"
                                            else "BRAK",
                                            color = if (item.isInstalled)
                                                AccentCyan else StatusRed,
                                            fontWeight = FontWeight.Bold,
                                            fontSize = 11.sp
                                        )
                                    }
                                    if (item.hasLocalApk &&
                                        item.localApkFile != null) {
                                        Button(
                                            onClick = {
                                                statusMsg = ApkInstallerManager
                                                    .launchApkInstall(
                                                        ctx, item.localApkFile
                                                    )
                                            },
                                            modifier = Modifier.fillMaxWidth(),
                                            colors = ButtonDefaults.buttonColors(
                                                containerColor = AccentCyan,
                                                contentColor = Color(0xFF041818)
                                            )
                                        ) {
                                            Text(
                                                if (item.isInstalled)
                                                    "Aktualizuj z APK"
                                                else "Zainstaluj z paczki APK",
                                                fontWeight = FontWeight.Bold
                                            )
                                        }
                                    } else if (!item.isInstalled) {
                                        Row(
                                            horizontalArrangement =
                                                Arrangement.spacedBy(6.dp)
                                        ) {
                                            OutlinedButton(
                                                onClick = {
                                                    ApkInstallerManager
                                                        .launchStoreOrGithubFallback(
                                                            ctx,
                                                            item.packageName
                                                        )
                                                },
                                                modifier = Modifier.weight(1f),
                                                border = BorderStroke(1.dp, AccentCyan)
                                            ) {
                                                Text(
                                                    "Sklep Play",
                                                    color = AccentCyan,
                                                    fontSize = 11.sp
                                                )
                                            }
                                            OutlinedButton(
                                                onClick = {
                                                    apkFilePicker.launch("*/*")
                                                },
                                                modifier = Modifier.weight(1f),
                                                border = BorderStroke(1.dp, AccentCyan)
                                            ) {
                                                Text(
                                                    "Wska\u017C .APK",
                                                    color = AccentCyan,
                                                    fontSize = 11.sp
                                                )
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        OutlinedButton(
                            onClick = { apkFilePicker.launch("*/*") },
                            modifier = Modifier.fillMaxWidth(),
                            border = BorderStroke(1.dp, AccentCyan)
                        ) {
                            Text(
                                "Zainstaluj plik .APK / Plugin z dysku",
                                color = AccentCyan
                            )
                        }
                        statusMsg?.let { msg ->
                            Text(
                                msg,
                                color = AccentCyan,
                                fontWeight = FontWeight.Bold,
                                fontSize = 12.sp
                            )
                        }
                    }
                },
                confirmButton = {
                    Button(
                        onClick = { openApkManager() },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = AccentCyan,
                            contentColor = Color(0xFF041818)
                        )
                    ) {
                        Text("Od\u015Bwie\u017C", fontWeight = FontWeight.Bold)
                    }
                },
                dismissButton = {
                    TextButton(onClick = { showApkDialog = false }) {
                        Text("Zamknij", color = TextLight)
                    }
                }
            )
        }
        logRep?.let { r ->
            AlertDialog(
                onDismissRequest = { logRep = null },
                containerColor = CardBg,
                titleContentColor = TextLight,
                textContentColor = TextLight,
                title = { Text("Raport .LOG (${r.issuesCount} uwag)") },
                text = {
                    Column(
                        modifier = Modifier
                            .heightIn(max = 380.dp)
                            .verticalScroll(rememberScrollState())
                    ) {
                        Text(
                            "Plik: ${r.savedLocationDescription}",
                            color = AccentCyan,
                            fontWeight = FontWeight.Bold,
                            fontSize = 12.sp
                        )
                        Spacer(modifier = Modifier.height(6.dp))
                        Text(
                            r.fullText,
                            color = TextLight,
                            fontFamily = FontFamily.Monospace,
                            fontSize = 11.sp
                        )
                    }
                },
                confirmButton = {
                    Button(
                        onClick = {
                            ExtendedDiagnosticsManager.shareLogReport(ctx, r)
                        },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = AccentCyan,
                            contentColor = Color(0xFF041818)
                        )
                    ) {
                        Text("Udost\u0119pnij .LOG", fontWeight = FontWeight.Bold)
                    }
                },
                dismissButton = {
                    TextButton(onClick = { logRep = null }) {
                        Text("Zamknij", color = TextLight)
                    }
                }
            )
        }
    }
}
@Composable
private fun PermRow(
    title: String,
    ok: Boolean,
    btn: String,
    onClick: () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = CardBg),
        border = BorderStroke(
            1.dp,
            if (ok) BorderTeal else Color(0xFF6E2A33)
        )
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    title,
                    color = TextLight,
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 15.sp
                )
                Text(
                    if (ok) "GOTOWE \u2713" else "WYMAGANE",
                    color = if (ok) AccentCyan else StatusRed,
                    fontWeight = FontWeight.Bold,
                    fontSize = 12.sp
                )
            }
            if (!ok) {
                OutlinedButton(
                    onClick = onClick,
                    modifier = Modifier.fillMaxWidth().height(44.dp),
                    shape = RoundedCornerShape(10.dp),
                    border = BorderStroke(1.dp, AccentCyan)
                ) {
                    Text(
                        btn,
                        color = AccentCyan,
                        fontWeight = FontWeight.Bold,
                        fontSize = 13.sp
                    )
                }
            }
        }
    }
}