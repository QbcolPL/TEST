package com.fieldtak.mobile.ui.onboarding

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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.fieldtak.mobile.data.FtakPackageRepository
import com.fieldtak.mobile.ui.components.TacticalIconBadge
import com.fieldtak.mobile.ui.components.TacticalIconType
import com.fieldtak.mobile.ui.theme.*

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
        TacticalPermissionState("GPS", "Lokalizacja GPS", "Wyliczanie kwadratu MGRS i pozycji do meldunku SOS.", TacticalIconType.GPS_SATELLITE, hasGps),
        TacticalPermissionState("CAM", "Dostęp do aparatu (QR)", "Błyskawiczny odczyt kodów QR paczek .ftak z chmury i LAN.", TacticalIconType.CAMERA_QR, hasCamera),
        TacticalPermissionState("BT", "Łączność Bluetooth & LoRa", "Wstrzykiwanie kanałów, PSK oraz ksywy do Meshtastic.", TacticalIconType.BLUETOOTH_LORA, hasBt),
        TacticalPermissionState("FILES", "Dostęp do plików (.ftak)", "Zapisywanie map .mbtiles/.kmz oraz paczek operacji.", TacticalIconType.FILES_CRATE, hasStorage),
        TacticalPermissionState("APK", "Instalacja pakietów (.apk)", "Automatyczna instalacja ATAK-CIV, Meshtastic i pluginów.", TacticalIconType.APK_SHIELD, hasInstallApks)
    )
}

@Composable
fun OnboardingScreen(onContinueToApp: () -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    var permissions by remember { mutableStateOf(evaluatePermissions(context)) }

    val activeFaction = remember {
        val pkgs = FtakPackageRepository.listInstalledPackages(context)
        pkgs.firstOrNull()?.factionName ?: "BLUFOR"
    }

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

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(16.dp)
    ) {
        Column(
            modifier = Modifier.fillMaxSize(),
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
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(top = 4.dp, bottom = 14.dp)
                )

                LazyColumn(
                    verticalArrangement = Arrangement.spacedBy(10.dp),
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
                                    .padding(horizontal = 12.dp, vertical = 11.dp),
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
                                            fontSize = 10.sp
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

            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                if (grantedCount < permissions.size) {
                    OutlinedButton(
                        onClick = {
                            val firstMissing = permissions.firstOrNull { !it.isGranted }
                            if (firstMissing != null) requestSinglePermission(firstMissing.id)
                        },
                        border = BorderStroke(1.2.dp, RadarCyan),
                        shape = RoundedCornerShape(6.dp),
                        modifier = Modifier.fillMaxWidth().height(42.dp)
                    ) {
                        Text(
                            text = "NADAJ BRAKUJĄCE UPRAWNIENIA ($grantedCount/${permissions.size})",
                            color = RadarCyan,
                            fontWeight = FontWeight.Bold,
                            fontSize = 11.sp,
                            fontFamily = FontFamily.Monospace
                        )
                    }
                }

                // Neonowa karta AKTYWNY PROFIL / WEJSCIE DO GRY wzorowana na wizualizacji
                Surface(
                    color = TacticalCard,
                    shape = RoundedCornerShape(8.dp),
                    border = BorderStroke(1.8.dp, RadarCyan),
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable { onContinueToApp() }
                ) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        modifier = Modifier.padding(vertical = 12.dp, horizontal = 16.dp)
                    ) {
                        Text(
                            text = "AKTYWNY PROFIL:",
                            color = TextMuted,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = activeFaction,
                            color = RadarCyan,
                            fontSize = 17.sp,
                            fontWeight = FontWeight.Bold
                        )
                        Text(
                            text = "Kliknij, aby przejść do Skanera QR i Gry ->",
                            color = MilSimGreen,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace,
                            modifier = Modifier.padding(top = 2.dp)
                        )
                    }
                }
            }
        }
    }
}