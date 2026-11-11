package com.fieldtakhub.mobile.ui.screens

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.fieldtakhub.mobile.data.FtakPackageRepository
import com.fieldtakhub.mobile.data.FtakPackageSummary
import com.fieldtakhub.mobile.data.LocalFtakHotspotServer
import com.fieldtakhub.mobile.ui.theme.*
import kotlinx.coroutines.launch
import java.io.File

data class TacticalPermissionState(
    val id: String,
    val title: String,
    val desc: String,
    val iconType: TacticalIconType,
    val isGranted: Boolean
)

fun evaluatePermissions(context: Context): List<TacticalPermissionState> {
    val hasGps = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
    val hasCamera = ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
    val hasBt = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED
    } else true

    val hasStorage = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
        Environment.isExternalStorageManager()
    } else {
        ContextCompat.checkSelfPermission(context, Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED
    }

    val hasInstallApks = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
        context.packageManager.canRequestPackageInstalls()
    } else true

    return listOf(
        TacticalPermissionState(
            "GPS",
            "Lokalizacja GPS",
            "Wyliczanie dokładnego kwadratu MGRS do meldunku alarmowego SOS oraz pozycjonowania na mapie.",
            TacticalIconType.GPS_SATELLITE,
            hasGps
        ),
        TacticalPermissionState(
            "CAM",
            "Dostęp do aparatu (QR)",
            "Błyskawiczny odczyt kodów QR paczek .ftak z plakatów w Safezone, laptopa lub ekranu innego gracza.",
            TacticalIconType.CAMERA_QR,
            hasCamera
        ),
        TacticalPermissionState(
            "BT",
            "Łączność Bluetooth & LoRa",
            "Komunikacja z radiem Meshtastic w celu automatycznego wgrania kanałów LoRa, klucza PSK i Callsignu.",
            TacticalIconType.BLUETOOTH_LORA,
            hasBt
        ),
        TacticalPermissionState(
            "FILES",
            "Dostęp do plików (.ftak)",
            "Przechowywanie biblioteki paczek .ftak w pamięci telefonu oraz wgrywanie map (.mbtiles, .kmz) do ATAK.",
            TacticalIconType.FILES_CRATE,
            hasStorage
        ),
        TacticalPermissionState(
            "APK",
            "Instalacja pakietów (.apk)",
            "Automatyczna instalacja lub aktualizacja aplikacji ATAK-CIV, Meshtastic oraz pluginów prosto z paczki.",
            TacticalIconType.APK_SHIELD,
            hasInstallApks
        )
    )
}

@Composable
fun OnboardingScreen(
    activePackage: FtakPackageSummary?,
    onFinishOnboarding: () -> Unit
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    var permissions by remember { mutableStateOf(evaluatePermissions(context)) }

    val runtimeLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestMultiplePermissions()
    ) {
        permissions = evaluatePermissions(context)
    }

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                permissions = evaluatePermissions(context)
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    fun requestSinglePermission(id: String) {
        when (id) {
            "GPS" -> runtimeLauncher.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
            "CAM" -> runtimeLauncher.launch(arrayOf(Manifest.permission.CAMERA))
            "BT" -> {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    runtimeLauncher.launch(arrayOf(Manifest.permission.BLUETOOTH_CONNECT, Manifest.permission.BLUETOOTH_SCAN))
                }
            }
            "FILES" -> {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                        data = Uri.parse("package:${context.packageName}")
                    }
                    runCatching { context.startActivity(intent) }.onFailure {
                        context.startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
                    }
                } else {
                    runtimeLauncher.launch(arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE, Manifest.permission.WRITE_EXTERNAL_STORAGE))
                }
            }
            "APK" -> {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES).apply {
                        data = Uri.parse("package:${context.packageName}")
                    }
                    context.startActivity(intent)
                }
            }
        }
    }

    val grantedCount = permissions.count { it.isGranted }
    val activeFaction = activePackage?.factionName ?: "BLUFOR"

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(horizontal = 14.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.SpaceBetween
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = "MODUŁ: UPRAWNIENIA & PACZKI",
                color = TextMuted,
                fontSize = 11.sp,
                fontFamily = FontFamily.Monospace
            )
            Text(
                text = "Onboarding & Permissions",
                color = TextWhite,
                fontSize = 17.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(top = 2.dp, bottom = 10.dp)
            )

            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                items(permissions.size) { index ->
                    val perm = permissions[index]
                    Card(
                        colors = CardDefaults.cardColors(containerColor = TacticalCard),
                        shape = RoundedCornerShape(6.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .border(1.2.dp, if (perm.isGranted) MutedGrid else RadarCyan, RoundedCornerShape(6.dp))
                            .clickable { requestSinglePermission(perm.id) }
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 12.dp, vertical = 10.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                modifier = Modifier.weight(1f).padding(end = 8.dp)
                            ) {
                                TacticalIconBadge(
                                    type = perm.iconType,
                                    tint = RadarCyan,
                                    boxSize = 36.dp,
                                    iconSize = 20.dp
                                )
                                Spacer(modifier = Modifier.width(12.dp))
                                Column {
                                    Text(
                                        text = perm.title,
                                        color = TextWhite,
                                        fontSize = 13.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                    Text(
                                        text = perm.desc,
                                        color = TextMuted,
                                        fontSize = 10.sp,
                                        lineHeight = 13.sp
                                    )
                                }
                            }

                            if (perm.isGranted) {
                                Text(
                                    text = "✓",
                                    color = MilSimGreen,
                                    fontSize = 18.sp,
                                    fontWeight = FontWeight.Bold,
                                    modifier = Modifier.padding(horizontal = 8.dp)
                                )
                            } else {
                                Surface(
                                    color = InputTrench,
                                    shape = RoundedCornerShape(4.dp),
                                    border = BorderStroke(1.dp, RadarCyan),
                                    modifier = Modifier.clickable { requestSinglePermission(perm.id) }
                                ) {
                                    Text(
                                        text = "NADAJ",
                                        color = RadarCyan,
                                        fontSize = 10.sp,
                                        fontWeight = FontWeight.Bold,
                                        fontFamily = FontFamily.Monospace,
                                        modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp)
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        Column(
            verticalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.padding(top = 8.dp, bottom = 4.dp)
        ) {
            if (grantedCount < permissions.size) {
                OutlinedButton(
                    onClick = {
                        val firstMissing = permissions.firstOrNull { !it.isGranted }
                        if (firstMissing != null) requestSinglePermission(firstMissing.id)
                    },
                    border = BorderStroke(1.2.dp, RadarCyan),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier.fillMaxWidth().height(40.dp)
                ) {
                    Text(
                        text = "NADAJ KOLEJNE UPRAWNIENIE ($grantedCount/${permissions.size})",
                        color = RadarCyan,
                        fontWeight = FontWeight.Bold,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }

            // Dolny swiecacy modul AKTYWNY PROFIL: BLUFOR 1:1 jak na wizualizacji
            Surface(
                color = TacticalCard,
                shape = RoundedCornerShape(8.dp),
                border = BorderStroke(1.8.dp, RadarCyan),
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable {
                        FtakPackageRepository.setOnboardingCompleted(context, true)
                        onFinishOnboarding()
                    }
            ) {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    modifier = Modifier.padding(vertical = 12.dp, horizontal = 16.dp)
                ) {
                    Text(
                        text = "AKTYWNY PROFIL:",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )
                    Text(
                        text = activeFaction,
                        color = RadarCyan,
                        fontSize = 18.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }
        }
    }
}

@Composable
fun PackageManagerScreen(
    activePackage: FtakPackageSummary?,
    onActivePackageChanged: (FtakPackageSummary?) -> Unit,
    onOpenPermissions: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var packages by remember { mutableStateOf(FtakPackageRepository.listInstalledPackages(context)) }
    var hotspotShareUrl by remember { mutableStateOf<String?>(null) }
    var statusNote by remember { mutableStateOf("") }

    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val imported = FtakPackageRepository.importFromUri(context, uri)
                packages = FtakPackageRepository.listInstalledPackages(context)
                if (imported != null) {
                    onActivePackageChanged(imported)
                    statusNote = "Zaimportowano paczkę: ${imported.fileName}"
                }
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column {
                Text(
                    text = "BIBLIOTEKA PACZEK .FTAK (${packages.size})",
                    color = RadarCyan,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = "Zarządzaj pobranymi operacjami i udostępniaj przez Hotspot Wi-Fi",
                    color = TextMuted,
                    fontSize = 10.sp
                )
            }
            Text(
                text = "[Uprawnienia]",
                color = RadarCyan,
                fontSize = 10.sp,
                fontFamily = FontFamily.Monospace,
                modifier = Modifier.clickable { onOpenPermissions() }
            )
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            OutlinedButton(
                onClick = { filePicker.launch("*/*") },
                border = BorderStroke(1.2.dp, RadarCyan),
                shape = RoundedCornerShape(4.dp),
                modifier = Modifier.weight(1f).height(38.dp),
                contentPadding = PaddingValues(horizontal = 6.dp)
            ) {
                TacticalVectorIcon(type = TacticalIconType.FOLDER_OPEN, tint = RadarCyan, size = 15.dp)
                Spacer(modifier = Modifier.width(6.dp))
                Text("WCZYTAJ .FTAK", color = RadarCyan, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            }

            OutlinedButton(
                onClick = {
                    val target = activePackage ?: packages.firstOrNull()
                    if (target == null) {
                        statusNote = "Brak paczki .ftak do udostępnienia przez Hotspot!"
                    } else {
                        val file = File(target.localFilePath)
                        if (file.exists()) {
                            hotspotShareUrl = LocalFtakHotspotServer.startSharingPackage(file)
                        }
                    }
                },
                border = BorderStroke(1.2.dp, MilSimGreen),
                shape = RoundedCornerShape(4.dp),
                modifier = Modifier.weight(1f).height(38.dp),
                contentPadding = PaddingValues(horizontal = 6.dp)
            ) {
                TacticalVectorIcon(type = TacticalIconType.WIFI_HOTSPOT, tint = MilSimGreen, size = 15.dp)
                Spacer(modifier = Modifier.width(6.dp))
                Text("HOTSPOT WI-FI QR", color = MilSimGreen, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            }
        }

        if (statusNote.isNotBlank()) {
            Text(
                text = statusNote,
                color = MilSimGreen,
                fontSize = 11.sp,
                fontFamily = FontFamily.Monospace
            )
        }

        // Modal z kodem QR lokalnego serwera Hotspot Wi-Fi dla zapominalskich
        if (hotspotShareUrl != null) {
            val qrBmp = remember(hotspotShareUrl) { generateQrBitmap(hotspotShareUrl!!, 460) }
            Card(
                colors = CardDefaults.cardColors(containerColor = TacticalCard),
                shape = RoundedCornerShape(8.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .border(1.5.dp, MilSimGreen, RoundedCornerShape(8.dp))
            ) {
                Column(
                    modifier = Modifier.padding(12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text(
                        text = "📡 DYSTRYBUCJA POLOWA HOTSPOT WI-FI",
                        color = MilSimGreen,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Text(
                        text = "Włącz Hotspot Wi-Fi w telefonie. Drugi gracz po podłączeniu skanuje poniższy kod QR:",
                        color = TextMuted,
                        fontSize = 10.sp,
                        modifier = Modifier.padding(top = 2.dp, bottom = 8.dp)
                    )
                    if (qrBmp != null) {
                        Image(
                            bitmap = qrBmp.asImageBitmap(),
                            contentDescription = "Hotspot QR",
                            modifier = Modifier
                                .size(170.dp)
                                .border(2.dp, RadarCyan, RoundedCornerShape(4.dp))
                        )
                    }
                    Text(
                        text = hotspotShareUrl!!,
                        color = RadarCyan,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.padding(top = 6.dp, bottom = 6.dp)
                    )
                    OutlinedButton(
                        onClick = {
                            LocalFtakHotspotServer.stop()
                            hotspotShareUrl = null
                        },
                        border = BorderStroke(1.dp, DangerCrimson),
                        modifier = Modifier.height(32.dp)
                    ) {
                        Text("ZATRZYMAJ SERWER HOTSPOT", color = DangerCrimson, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        if (packages.isEmpty()) {
            Card(
                colors = CardDefaults.cardColors(containerColor = TacticalCard),
                shape = RoundedCornerShape(6.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .border(1.dp, MutedGrid, RoundedCornerShape(6.dp))
            ) {
                Text(
                    text = "Brak zapisanych paczek .ftak w bibliotece telefonu.\nPrzejdź do zakładki SKANER (aby zeskanować kod QR z kamery lub wczytać go z galerii) albo użyj przycisku 'WCZYTAJ .FTAK' powyżej.",
                    color = TextMuted,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(16.dp)
                )
            }
        } else {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.weight(1f)
            ) {
                items(packages.size) { index ->
                    val pkg = packages[index]
                    val isCurrent = activePackage?.fileName == pkg.fileName
                    Card(
                        colors = CardDefaults.cardColors(containerColor = TacticalCard),
                        shape = RoundedCornerShape(6.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .border(1.4.dp, if (isCurrent) RadarCyan else MutedGrid, RoundedCornerShape(6.dp))
                            .clickable {
                                FtakPackageRepository.setActivePackageFileName(context, pkg.fileName)
                                onActivePackageChanged(pkg)
                            }
                    ) {
                        Column(modifier = Modifier.padding(12.dp)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column(modifier = Modifier.weight(1f)) {
                                    Text(
                                        text = "${pkg.eventName} — ${pkg.factionName}",
                                        color = TextWhite,
                                        fontSize = 13.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                    Text(
                                        text = "Plik: ${pkg.fileName} (${pkg.formattedSize})",
                                        color = TextMuted,
                                        fontSize = 10.sp,
                                        fontFamily = FontFamily.Monospace
                                    )
                                }

                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                                ) {
                                    if (isCurrent) {
                                        Surface(
                                            color = InputTrench,
                                            shape = RoundedCornerShape(4.dp),
                                            border = BorderStroke(1.dp, MilSimGreen)
                                        ) {
                                            Text(
                                                text = "AKTYWNA",
                                                color = MilSimGreen,
                                                fontSize = 9.sp,
                                                fontWeight = FontWeight.Bold,
                                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 3.dp)
                                            )
                                        }
                                    }

                                    Surface(
                                        color = InputTrench,
                                        shape = RoundedCornerShape(4.dp),
                                        border = BorderStroke(1.dp, DangerCrimson),
                                        modifier = Modifier
                                            .size(28.dp)
                                            .clickable {
                                                FtakPackageRepository.deletePackage(context, pkg.fileName)
                                                packages = FtakPackageRepository.listInstalledPackages(context)
                                                onActivePackageChanged(packages.firstOrNull())
                                            }
                                    ) {
                                        Box(contentAlignment = Alignment.Center) {
                                            Text(
                                                text = "−",
                                                color = DangerCrimson,
                                                fontSize = 15.sp,
                                                fontWeight = FontWeight.Bold
                                            )
                                        }
                                    }
                                }
                            }

                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = "OTS: ${pkg.otsHost} | LoRa: ${pkg.loraRegion} · ${pkg.loraPreset}",
                                color = RadarCyan,
                                fontSize = 10.sp,
                                fontFamily = FontFamily.Monospace
                            )
                        }
                    }
                }
            }
        }
    }
}