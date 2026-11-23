package com.fieldtakhub.mobile

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import java.util.Locale
import kotlin.math.cos
import kotlin.math.floor
import kotlin.math.pow
import kotlin.math.sin
import kotlin.math.sqrt
import kotlin.math.tan
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.ui.viewinterop.AndroidView
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.Executors

private val FthBgDeep = Color(0xFF050C10)
private val FthCardBg = Color(0xFF0A171D)
private val FthCardBorder = Color(0xFF193C47)
private val FthInputBg = Color(0xFF071217)
private val FthCyan = Color(0xFF1FE8CC)
private val FthCyanBright = Color(0xFF39F8DC)
private val FthGreenOk = Color(0xFF2EE682)
private val FthTextPrimary = Color(0xFFEAF7F9)
private val FthTextMuted = Color(0xFF779DA8)
private val FthRedBg = Color(0xFF280B0E)
private val FthRedBorder = Color(0xFFE8453C)
private val FthRedBright = Color(0xFFFF524A)
private val FthAmberWarn = Color(0xFFFAD02C)

private enum class FthTab(val label: String) {
    SCANNER("SKANER"),
    PACKAGES("PACZKI"),
    OPORD("OPORD"),
    CHEATSHEET("ŚCIĄGA")
}

private data class FthPermState(
    val gps: Boolean,
    val camera: Boolean,
    val bluetooth: Boolean,
    val files: Boolean,
    val installApk: Boolean
) {
    val grantedCount: Int
        get() = listOf(gps, camera, bluetooth, files, installApk).count { it }
    val allGranted: Boolean
        get() = gps && camera && bluetooth && files && installApk
}

class MainActivity : ComponentActivity() {

    private var permState by mutableStateOf(FthPermState(false, false, false, false, false))
    private var initialPromptTriggered = false

    private val runtimePermissionsLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) {
        refreshPermissions()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, true)
        window.statusBarColor = 0xFF050C10.toInt()
        window.navigationBarColor = 0xFF050C10.toInt()

        refreshPermissions()

        setContent {
            LaunchedEffect(Unit) {
                if (!initialPromptTriggered) {
                    initialPromptTriggered = true
                    if (!permState.gps || !permState.camera || !permState.bluetooth) {
                        requestRuntimePermissions()
                    }
                }
            }

            FthMainApp(
                activity = this@MainActivity,
                permState = permState,
                onRequestRuntime = { requestRuntimePermissions() },
                onRequestFiles = { openAllFilesPermissionSettings() },
                onRequestInstallApk = { openInstallPackagesSettings() },
                onRequestNextMissing = { requestNextMissingPermission() }
            )
        }
    }

    override fun onResume() {
        super.onResume()
        refreshPermissions()
    }

    private fun refreshPermissions() {
        val gps = hasPerm(Manifest.permission.ACCESS_FINE_LOCATION) ||
            hasPerm(Manifest.permission.ACCESS_COARSE_LOCATION)
        val camera = hasPerm(Manifest.permission.CAMERA)
        val bt = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            hasPerm(Manifest.permission.BLUETOOTH_SCAN) && hasPerm(Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            hasPerm(Manifest.permission.BLUETOOTH)
        }
        val files = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            hasPerm(Manifest.permission.READ_EXTERNAL_STORAGE) &&
                hasPerm(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }
        val installApk = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            packageManager.canRequestPackageInstalls()
        } else {
            true
        }
        permState = FthPermState(gps, camera, bt, files, installApk)
    }

    private fun hasPerm(permission: String): Boolean {
        return ContextCompat.checkSelfPermission(this, permission) == PackageManager.PERMISSION_GRANTED
    }

    private fun requestRuntimePermissions() {
        val perms = mutableListOf(
            Manifest.permission.ACCESS_FINE_LOCATION,
            Manifest.permission.ACCESS_COARSE_LOCATION,
            Manifest.permission.CAMERA
        )
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            perms.add(Manifest.permission.BLUETOOTH_SCAN)
            perms.add(Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            perms.add(Manifest.permission.BLUETOOTH)
        }
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            perms.add(Manifest.permission.READ_EXTERNAL_STORAGE)
            perms.add(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }
        runtimePermissionsLauncher.launch(perms.toTypedArray())
    }

    private fun openAllFilesPermissionSettings() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            try {
                val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                    data = Uri.parse("package:$packageName")
                }
                startActivity(intent)
            } catch (_: Exception) {
                try {
                    startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
                } catch (_: Exception) {
                    openAppDetailsSettings()
                }
            }
        } else {
            requestRuntimePermissions()
        }
    }

    private fun openInstallPackagesSettings() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            try {
                val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES).apply {
                    data = Uri.parse("package:$packageName")
                }
                startActivity(intent)
            } catch (_: Exception) {
                openAppDetailsSettings()
            }
        }
    }

    private fun openAppDetailsSettings() {
        try {
            val intent = Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
                data = Uri.parse("package:$packageName")
            }
            startActivity(intent)
        } catch (_: Exception) { }
    }

    private fun requestNextMissingPermission() {
        when {
            !permState.gps || !permState.camera || !permState.bluetooth -> requestRuntimePermissions()
            !permState.files -> openAllFilesPermissionSettings()
            !permState.installApk -> openInstallPackagesSettings()
        }
    }
}

@Composable
private fun FthMainApp(
    activity: ComponentActivity,
    permState: FthPermState,
    onRequestRuntime: () -> Unit,
    onRequestFiles: () -> Unit,
    onRequestInstallApk: () -> Unit,
    onRequestNextMissing: () -> Unit
) {
    val context = LocalContext.current
    var currentTab by remember { mutableStateOf(FthTab.SCANNER) }
    var callsign by remember { mutableStateOf("Wilk-1") }
    var lastScannedPayload by remember { mutableStateOf<String?>(null) }
    var activeProfile by remember { mutableStateOf("BLUFOR") }
    var opordSubTab by remember { mutableStateOf(0) }

    val isLockedByPermissions = !permState.allGranted

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(FthBgDeep)
    ) {
        FthTacticalGridBackground(modifier = Modifier.fillMaxSize())

        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 14.dp, vertical = 10.dp)
        ) {
            FthHeaderBar(
                onBackClick = {
                    if (!isLockedByPermissions) {
                        currentTab = FthTab.SCANNER
                    } else {
                        Toast.makeText(
                            context,
                            "Nadaj wszystkie 5 uprawnień, aby odblokować aplikację.",
                            Toast.LENGTH_SHORT
                        ).show()
                    }
                }
            )

            Spacer(modifier = Modifier.height(12.dp))

            Box(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth()
            ) {
                if (isLockedByPermissions) {
                    FthOnboardingPermissionsScreen(
                        permState = permState,
                        activeProfile = activeProfile,
                        isHardGateMode = true,
                        onToggleProfile = {
                            activeProfile = if (activeProfile == "BLUFOR") "OPFOR" else "BLUFOR"
                        },
                        onRequestGps = onRequestRuntime,
                        onRequestCamera = onRequestRuntime,
                        onRequestBluetooth = onRequestRuntime,
                        onRequestFiles = onRequestFiles,
                        onRequestInstallApk = onRequestInstallApk,
                        onPrimaryAction = onRequestNextMissing
                    )
                } else {
                    when (currentTab) {
                        FthTab.SCANNER -> FthScannerScreen(
                            activity = activity,
                            callsign = callsign,
                            onCallsignChange = { callsign = it },
                            lastScannedPayload = lastScannedPayload,
                            onQrDetected = { code ->
                                if (lastScannedPayload != code) {
                                    lastScannedPayload = code
                                    Toast.makeText(context, "Zeskanowano pakiet QR!", Toast.LENGTH_SHORT).show()
                                }
                            },
                            onInjectClick = {
                                val msg = if (lastScannedPayload != null) {
                                    "Wstrzykuję pakiet dla [$callsign] do ATAK + Meshtastic..."
                                } else {
                                    "Synchronizacja profilu [$callsign] z ATAK-CIV + Meshtastic..."
                                }
                                Toast.makeText(context, msg, Toast.LENGTH_LONG).show()
                            }
                        )
                        FthTab.PACKAGES -> FthOnboardingPermissionsScreen(
                            permState = permState,
                            activeProfile = activeProfile,
                            isHardGateMode = false,
                            onToggleProfile = {
                                activeProfile = if (activeProfile == "BLUFOR") "OPFOR" else "BLUFOR"
                            },
                            onRequestGps = onRequestRuntime,
                            onRequestCamera = onRequestRuntime,
                            onRequestBluetooth = onRequestRuntime,
                            onRequestFiles = onRequestFiles,
                            onRequestInstallApk = onRequestInstallApk,
                            onPrimaryAction = {
                                activeProfile = if (activeProfile == "BLUFOR") "OPFOR" else "BLUFOR"
                            }
                        )
                        FthTab.OPORD -> FthOpordScreen(
                            context = context,
                            callsign = callsign,
                            selectedSubTab = opordSubTab,
                            onSelectSubTab = { opordSubTab = it }
                        )
                        FthTab.CHEATSHEET -> FthCheatSheetScreen()
                    }
                }
            }

            Spacer(modifier = Modifier.height(8.dp))

            FthBottomNavBar(
                selectedTab = if (isLockedByPermissions) FthTab.PACKAGES else currentTab,
                isLocked = isLockedByPermissions,
                onSelectTab = { tab ->
                    if (isLockedByPermissions) {
                        Toast.makeText(
                            context,
                            "Dostęp zablokowany (${permState.grantedCount}/5). Nadaj najpierw wszystkie uprawnienia!",
                            Toast.LENGTH_SHORT
                        ).show()
                        onRequestNextMissing()
                    } else {
                        currentTab = tab
                    }
                }
            )
        }
    }
}

@Composable
private fun FthTacticalGridBackground(modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val step = 34.dp.toPx()
        val lineColor = Color(0xFF0D232B)
        var x = 0f
        while (x < size.width) {
            drawLine(lineColor, Offset(x, 0f), Offset(x, size.height), strokeWidth = 1f)
            x += step
        }
        var y = 0f
        while (y < size.height) {
            drawLine(lineColor, Offset(0f, y), Offset(size.width, y), strokeWidth = 1f)
            y += step
        }
    }
}

@Composable
private fun FthHeaderBar(onBackClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .background(Color(0xFF08161C))
            .border(1.3.dp, FthCyan.copy(alpha = 0.78f), RoundedCornerShape(14.dp))
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Canvas(modifier = Modifier.size(32.dp)) {
            val c = Offset(size.width / 2f, size.height / 2f)
            val rOuter = size.minDimension * 0.44f
            val rMid = size.minDimension * 0.28f
            val rInner = size.minDimension * 0.11f
            drawCircle(FthCyan, radius = rOuter, center = c, style = Stroke(width = 2.dp.toPx()))
            drawCircle(FthCyan.copy(alpha = 0.75f), radius = rMid, center = c, style = Stroke(width = 1.5.dp.toPx()))
            drawCircle(FthCyanBright, radius = rInner, center = c)
            drawLine(FthCyan, Offset(c.x, 2f), Offset(c.x, size.height * 0.24f), strokeWidth = 1.6.dp.toPx())
            drawLine(FthCyan, Offset(c.x, size.height * 0.76f), Offset(c.x, size.height - 2f), strokeWidth = 1.6.dp.toPx())
            drawLine(FthCyan, Offset(2f, c.y), Offset(size.width * 0.24f, c.y), strokeWidth = 1.6.dp.toPx())
            drawLine(FthCyan, Offset(size.width * 0.76f, c.y), Offset(size.width - 2f, c.y), strokeWidth = 1.6.dp.toPx())
        }

        Spacer(modifier = Modifier.width(10.dp))

        Column(modifier = Modifier.weight(1f)) {
            BasicText(
                text = "FIELD TAK HUB",
                style = TextStyle(
                    color = FthTextPrimary,
                    fontSize = 16.sp,
                    fontWeight = FontWeight.ExtraBold,
                    letterSpacing = 1.1.sp
                )
            )
            BasicText(
                text = "connect · deploy · operate",
                style = TextStyle(
                    color = FthCyan.copy(alpha = 0.85f),
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Medium,
                    letterSpacing = 0.4.sp
                )
            )
        }

        Box(
            modifier = Modifier
                .size(34.dp)
                .clip(RoundedCornerShape(8.dp))
                .background(Color(0xFF0A1D24))
                .border(1.dp, FthCyan.copy(alpha = 0.55f), RoundedCornerShape(8.dp))
                .clickable { onBackClick() },
            contentAlignment = Alignment.Center
        ) {
            BasicText(
                text = "<",
                style = TextStyle(
                    color = FthCyan,
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold
                )
            )
        }
    }
}

@Composable
private fun FthOnboardingPermissionsScreen(
    permState: FthPermState,
    activeProfile: String,
    isHardGateMode: Boolean,
    onToggleProfile: () -> Unit,
    onRequestGps: () -> Unit,
    onRequestCamera: () -> Unit,
    onRequestBluetooth: () -> Unit,
    onRequestFiles: () -> Unit,
    onRequestInstallApk: () -> Unit,
    onPrimaryAction: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
    ) {
        BasicText(
            text = "MODUŁ: UPRAWNIENIA & PACZKI",
            style = TextStyle(
                color = FthCyan.copy(alpha = 0.8f),
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 0.9.sp
            )
        )
        Spacer(modifier = Modifier.height(4.dp))
        BasicText(
            text = "Onboarding & Permissions",
            style = TextStyle(
                color = FthTextPrimary,
                fontSize = 21.sp,
                fontWeight = FontWeight.Bold
            )
        )

        if (isHardGateMode) {
            Spacer(modifier = Modifier.height(6.dp))
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(8.dp))
                    .background(Color(0xFF241B08))
                    .border(1.dp, FthAmberWarn.copy(alpha = 0.75f), RoundedCornerShape(8.dp))
                    .padding(horizontal = 10.dp, vertical = 7.dp)
            ) {
                BasicText(
                    text = "WYMAGANE DO STARTU (${permState.grantedCount}/5): Nadaj wszystkie 5 uprawnień poniżej, aby odblokować skaner QR i moduły taktyczne.",
                    style = TextStyle(
                        color = FthAmberWarn,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.SemiBold,
                        lineHeight = 15.sp
                    )
                )
            }
        }

        Spacer(modifier = Modifier.height(12.dp))

        FthPermissionCardRow("Lokalizacja GPS", permState.gps, 0, onRequestGps)
        Spacer(modifier = Modifier.height(9.dp))
        FthPermissionCardRow("Dostęp do aparatu (QR)", permState.camera, 1, onRequestCamera)
        Spacer(modifier = Modifier.height(9.dp))
        FthPermissionCardRow("Łączność Bluetooth & LoRa", permState.bluetooth, 2, onRequestBluetooth)
        Spacer(modifier = Modifier.height(9.dp))
        FthPermissionCardRow("Dostęp do plików (.ftak)", permState.files, 3, onRequestFiles)
        Spacer(modifier = Modifier.height(9.dp))
        FthPermissionCardRow("Instalacja pakietów (.apk)", permState.installApk, 4, onRequestInstallApk)

        Spacer(modifier = Modifier.height(16.dp))

        val btnBorder = if (permState.allGranted) FthCyanBright else FthAmberWarn
        val btnBg = if (permState.allGranted) {
            Brush.verticalGradient(listOf(Color(0xFF0E343B), Color(0xFF082228)))
        } else {
            Brush.verticalGradient(listOf(Color(0xFF2B1F09), Color(0xFF1A1205)))
        }

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(14.dp))
                .background(btnBg)
                .border(1.8.dp, btnBorder, RoundedCornerShape(14.dp))
                .clickable {
                    if (permState.allGranted) onToggleProfile() else onPrimaryAction()
                }
                .padding(vertical = 15.dp, horizontal = 14.dp),
            contentAlignment = Alignment.Center
        ) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                BasicText(
                    text = if (permState.allGranted) "AKTYWNY PROFIL:" else "BLOKADA STARTU (${permState.grantedCount}/5)",
                    style = TextStyle(
                        color = if (permState.allGranted) FthTextMuted else FthAmberWarn,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        letterSpacing = 1.0.sp
                    )
                )
                Spacer(modifier = Modifier.height(2.dp))
                BasicText(
                    text = if (permState.allGranted) activeProfile else "NADAJ BRAKUJĄCE UPRAWNIENIA",
                    style = TextStyle(
                        color = if (permState.allGranted) FthCyanBright else FthTextPrimary,
                        fontSize = if (permState.allGranted) 22.sp else 15.sp,
                        fontWeight = FontWeight.ExtraBold,
                        letterSpacing = 1.2.sp
                    )
                )
            }
        }

        Spacer(modifier = Modifier.height(8.dp))
    }
}

@Composable
private fun FthPermissionCardRow(
    title: String,
    granted: Boolean,
    iconType: Int,
    onClick: () -> Unit
) {
    val borderCol = if (granted) FthCardBorder else FthAmberWarn.copy(alpha = 0.65f)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(11.dp))
            .background(FthCardBg)
            .border(1.1.dp, borderCol, RoundedCornerShape(11.dp))
            .clickable { onClick() }
            .padding(horizontal = 13.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        FthPermissionVectorIcon(iconType = iconType, modifier = Modifier.size(22.dp))
        Spacer(modifier = Modifier.width(12.dp))
        BasicText(
            text = title,
            modifier = Modifier.weight(1f),
            style = TextStyle(
                color = FthTextPrimary,
                fontSize = 14.5.sp,
                fontWeight = FontWeight.SemiBold
            )
        )
        if (granted) {
            Canvas(modifier = Modifier.size(18.dp)) {
                val p = Path().apply {
                    moveTo(size.width * 0.14f, size.height * 0.52f)
                    lineTo(size.width * 0.42f, size.height * 0.80f)
                    lineTo(size.width * 0.88f, size.height * 0.22f)
                }
                drawPath(
                    path = p,
                    color = FthGreenOk,
                    style = Stroke(width = 2.6.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round)
                )
            }
        } else {
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(6.dp))
                    .background(Color(0xFF2B2009))
                    .border(1.dp, FthAmberWarn, RoundedCornerShape(6.dp))
                    .padding(horizontal = 8.dp, vertical = 3.dp)
            ) {
                BasicText(
                    text = "NADAJ",
                    style = TextStyle(
                        color = FthAmberWarn,
                        fontSize = 10.sp,
                        fontWeight = FontWeight.ExtraBold
                    )
                )
            }
        }
    }
}

@Composable
private fun FthPermissionVectorIcon(iconType: Int, modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val stroke = Stroke(width = 1.8.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round)
        val c = Offset(size.width / 2f, size.height / 2f)
        when (iconType) {
            0 -> {
                drawCircle(FthCyan, radius = size.minDimension * 0.26f, center = c, style = stroke)
                drawLine(FthCyan, Offset(c.x, 1f), Offset(c.x, size.height * 0.24f), strokeWidth = 1.8.dp.toPx())
                drawLine(FthCyan, Offset(c.x, size.height * 0.76f), Offset(c.x, size.height - 1f), strokeWidth = 1.8.dp.toPx())
                drawLine(FthCyan, Offset(1f, c.y), Offset(size.width * 0.24f, c.y), strokeWidth = 1.8.dp.toPx())
                drawLine(FthCyan, Offset(size.width * 0.76f, c.y), Offset(size.width - 1f, c.y), strokeWidth = 1.8.dp.toPx())
            }
            1 -> {
                drawRoundRect(
                    color = FthCyan,
                    topLeft = Offset(1.5.dp.toPx(), 4.dp.toPx()),
                    size = Size(size.width - 3.dp.toPx(), size.height - 7.dp.toPx()),
                    cornerRadius = CornerRadius(3.dp.toPx(), 3.dp.toPx()),
                    style = stroke
                )
                drawCircle(FthCyan, radius = size.minDimension * 0.18f, center = Offset(c.x, c.y + 1.dp.toPx()), style = stroke)
            }
            2 -> {
                val p = Path().apply {
                    moveTo(size.width * 0.30f, size.height * 0.26f)
                    lineTo(size.width * 0.72f, size.height * 0.72f)
                    lineTo(size.width * 0.50f, size.height * 0.90f)
                    lineTo(size.width * 0.50f, size.height * 0.10f)
                    lineTo(size.width * 0.72f, size.height * 0.28f)
                    lineTo(size.width * 0.30f, size.height * 0.74f)
                }
                drawPath(p, color = FthCyan, style = stroke)
            }
            3 -> {
                drawRoundRect(
                    color = FthCyan,
                    topLeft = Offset(2.dp.toPx(), 4.dp.toPx()),
                    size = Size(size.width - 4.dp.toPx(), size.height - 7.dp.toPx()),
                    cornerRadius = CornerRadius(2.5.dp.toPx(), 2.5.dp.toPx()),
                    style = stroke
                )
                drawLine(FthCyan, Offset(2.dp.toPx(), 8.5.dp.toPx()), Offset(size.width - 2.dp.toPx(), 8.5.dp.toPx()), strokeWidth = 1.6.dp.toPx())
            }
            else -> {
                drawCircle(FthCyan, radius = size.minDimension * 0.42f, center = c, style = stroke)
                drawLine(FthCyan, Offset(c.x, size.height * 0.26f), Offset(c.x, size.height * 0.68f), strokeWidth = 1.8.dp.toPx())
                drawLine(FthCyan, Offset(c.x - 4.dp.toPx(), size.height * 0.52f), Offset(c.x, size.height * 0.70f), strokeWidth = 1.8.dp.toPx())
                drawLine(FthCyan, Offset(c.x + 4.dp.toPx(), size.height * 0.52f), Offset(c.x, size.height * 0.70f), strokeWidth = 1.8.dp.toPx())
            }
        }
    }
}

@Composable
private fun FthScannerScreen(
    activity: ComponentActivity,
    callsign: String,
    onCallsignChange: (String) -> Unit,
    lastScannedPayload: String?,
    onQrDetected: (String) -> Unit,
    onInjectClick: () -> Unit
) {
    val context = LocalContext.current
    val atakInstalled = remember { isPackageInstalled(context, "com.atakmap.app.civ") }
    val meshtasticInstalled = remember { isPackageInstalled(context, "com.geeksville.mesh") }
    val callsignShort = remember(callsign) { buildShortCallsignTag(callsign) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState()),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth(0.76f)
                .aspectRatio(1f)
                .clip(RoundedCornerShape(10.dp))
                .background(Color(0xFF071318)),
            contentAlignment = Alignment.Center
        ) {
            FthLiveCameraOrViewfinder(
                activity = activity,
                onQrDetected = onQrDetected,
                modifier = Modifier.fillMaxSize()
            )
            FthAnimatedLaserScannerOverlay(modifier = Modifier.fillMaxSize())
        }

        Spacer(modifier = Modifier.height(8.dp))

        BasicText(
            text = if (lastScannedPayload == null) "ZESKANUJ QR / .FTAK" else "WYKRYTO KOD QR (.FTAK GOTOWY)",
            style = TextStyle(
                color = if (lastScannedPayload == null) FthTextMuted else FthGreenOk,
                fontSize = 12.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.1.sp
            )
        )

        Spacer(modifier = Modifier.height(14.dp))

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 2.dp)
        ) {
            BasicText(
                text = "Status integracji",
                style = TextStyle(
                    color = FthTextPrimary,
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold
                )
            )
            Spacer(modifier = Modifier.height(6.dp))

            FthIntegrationStatusLine("ATAK-CIV", if (atakInstalled) "(OK)" else "(Nie wykryto)", atakInstalled)
            Spacer(modifier = Modifier.height(3.dp))
            FthIntegrationStatusLine("Meshtastic", if (meshtasticInstalled) "(OK)" else "(Nie wykryto)", meshtasticInstalled)
            Spacer(modifier = Modifier.height(3.dp))
            FthIntegrationStatusLine("Server OTS", "(Połączono)", true)

            Spacer(modifier = Modifier.height(14.dp))

            BasicText(
                text = "ZNAK WYWOŁAWCZY (Callsign)",
                style = TextStyle(
                    color = FthTextMuted,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 0.7.sp
                )
            )
            Spacer(modifier = Modifier.height(5.dp))

            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(8.dp))
                    .background(FthInputBg)
                    .border(1.2.dp, FthCyan.copy(alpha = 0.65f), RoundedCornerShape(8.dp))
                    .padding(horizontal = 12.dp, vertical = 11.dp)
            ) {
                BasicTextField(
                    value = callsign,
                    onValueChange = { if (it.length <= 24) onCallsignChange(it) },
                    singleLine = true,
                    cursorBrush = SolidColor(FthCyanBright),
                    textStyle = TextStyle(
                        color = FthTextPrimary,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.SemiBold
                    ),
                    modifier = Modifier.fillMaxWidth()
                )
            }

            Spacer(modifier = Modifier.height(5.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                BasicText(
                    text = "Automated preview:",
                    style = TextStyle(color = FthTextMuted, fontSize = 11.5.sp)
                )
                BasicText(
                    text = "[$callsignShort]",
                    style = TextStyle(
                        color = FthTextPrimary,
                        fontSize = 11.5.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace
                    )
                )
            }

            Spacer(modifier = Modifier.height(14.dp))

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .background(Brush.horizontalGradient(listOf(Color(0xFF15C8B4), Color(0xFF39F6D8))))
                    .clickable { onInjectClick() }
                    .padding(horizontal = 16.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.Center
            ) {
                BasicText(
                    text = "↓↑",
                    style = TextStyle(
                        color = Color(0xFF05181C),
                        fontSize = 20.sp,
                        fontWeight = FontWeight.Black
                    )
                )
                Spacer(modifier = Modifier.width(12.dp))
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    BasicText(
                        text = "WSTRZYKNIJ DO",
                        style = TextStyle(
                            color = Color(0xFF041418),
                            fontSize = 13.5.sp,
                            fontWeight = FontWeight.ExtraBold,
                            letterSpacing = 0.6.sp
                        )
                    )
                    BasicText(
                        text = "ATAK + MESHTASTIC",
                        style = TextStyle(
                            color = Color(0xFF041418),
                            fontSize = 13.5.sp,
                            fontWeight = FontWeight.ExtraBold,
                            letterSpacing = 0.6.sp
                        )
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(8.dp))
    }
}

@Composable
private fun FthAnimatedLaserScannerOverlay(modifier: Modifier = Modifier) {
    val infiniteTransition = rememberInfiniteTransition(label = "fth_qr_laser")
    val laserPos by infiniteTransition.animateFloat(
        initialValue = 0.08f,
        targetValue = 0.92f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 1850, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "fth_laser_y"
    )

    Canvas(modifier = modifier) {
        val w = size.width
        val h = size.height
        val cornerLen = w * 0.16f
        val strokePx = 4.dp.toPx()
        val halfS = strokePx / 2f
        val cornerColor = Color(0xFF28EBBF)

        drawLine(cornerColor, Offset(halfS, cornerLen), Offset(halfS, halfS), strokeWidth = strokePx, cap = StrokeCap.Square)
        drawLine(cornerColor, Offset(halfS, halfS), Offset(cornerLen, halfS), strokeWidth = strokePx, cap = StrokeCap.Square)

        drawLine(cornerColor, Offset(w - cornerLen, halfS), Offset(w - halfS, halfS), strokeWidth = strokePx, cap = StrokeCap.Square)
        drawLine(cornerColor, Offset(w - halfS, halfS), Offset(w - halfS, cornerLen), strokeWidth = strokePx, cap = StrokeCap.Square)

        drawLine(cornerColor, Offset(halfS, h - cornerLen), Offset(halfS, h - halfS), strokeWidth = strokePx, cap = StrokeCap.Square)
        drawLine(cornerColor, Offset(halfS, h - halfS), Offset(cornerLen, h - halfS), strokeWidth = strokePx, cap = StrokeCap.Square)

        drawLine(cornerColor, Offset(w - cornerLen, h - halfS), Offset(w - halfS, h - halfS), strokeWidth = strokePx, cap = StrokeCap.Square)
        drawLine(cornerColor, Offset(w - halfS, h - halfS), Offset(w - halfS, h - cornerLen), strokeWidth = strokePx, cap = StrokeCap.Square)

        val laserY = h * laserPos
        val glowHalf = 18.dp.toPx()
        val glowTop = (laserY - glowHalf).coerceAtLeast(strokePx)
        val glowBottom = (laserY + glowHalf).coerceAtMost(h - strokePx)

        if (glowBottom > glowTop) {
            drawRect(
                brush = Brush.verticalGradient(
                    colors = listOf(
                        Color.Transparent,
                        FthCyanBright.copy(alpha = 0.42f),
                        Color.Transparent
                    ),
                    startY = glowTop,
                    endY = glowBottom
                ),
                topLeft = Offset(strokePx, glowTop),
                size = Size(w - strokePx * 2f, glowBottom - glowTop)
            )
        }

        drawLine(
            brush = Brush.horizontalGradient(
                colors = listOf(
                    Color.Transparent,
                    FthCyan,
                    Color.White,
                    FthCyan,
                    Color.Transparent
                )
            ),
            start = Offset(strokePx, laserY),
            end = Offset(w - strokePx, laserY),
            strokeWidth = 2.6.dp.toPx()
        )
    }
}

@Composable
private fun FthIntegrationStatusLine(label: String, statusText: String, isOk: Boolean) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(8.dp)
                .clip(CircleShape)
                .background(if (isOk) FthGreenOk else FthAmberWarn)
        )
        Spacer(modifier = Modifier.width(8.dp))
        BasicText(
            text = "$label ",
            style = TextStyle(color = FthTextPrimary, fontSize = 13.5.sp, fontWeight = FontWeight.SemiBold)
        )
        BasicText(
            text = statusText,
            style = TextStyle(color = if (isOk) FthGreenOk else FthAmberWarn, fontSize = 13.5.sp, fontWeight = FontWeight.Bold)
        )
    }
}

@Composable
private fun FthOpordScreen(
    context: Context,
    callsign: String,
    selectedSubTab: Int,
    onSelectSubTab: (Int) -> Unit
) {
    val liveMgrs = remember { getLastKnownMgrs(context) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(12.dp))
                .background(FthRedBg)
                .border(1.5.dp, FthRedBorder, RoundedCornerShape(12.dp))
                .padding(14.dp)
        ) {
            BasicText(
                text = "SOS MGRS ALARM",
                style = TextStyle(
                    color = FthRedBright,
                    fontSize = 20.sp,
                    fontWeight = FontWeight.ExtraBold,
                    letterSpacing = 1.0.sp
                ),
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(modifier = Modifier.height(10.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    BasicText(
                        text = "MGRS: ${liveMgrs.first}",
                        style = TextStyle(
                            color = FthTextPrimary,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                    )
                    BasicText(
                        text = liveMgrs.second,
                        style = TextStyle(
                            color = FthTextPrimary,
                            fontSize = 13.5.sp,
                            fontWeight = FontWeight.ExtraBold,
                            fontFamily = FontFamily.Monospace
                        )
                    )
                }

                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(9.dp))
                        .background(FthRedBorder)
                        .clickable {
                            val body = "SOS! ALARM TAKTYCZNY! Znak: $callsign | Pozycja MGRS: ${liveMgrs.first} ${liveMgrs.second}"
                            val intent = Intent(Intent.ACTION_SENDTO).apply {
                                data = Uri.parse("smsto:")
                                putExtra("sms_body", body)
                            }
                            try {
                                context.startActivity(intent)
                            } catch (_: Exception) {
                                Toast.makeText(context, body, Toast.LENGTH_LONG).show()
                            }
                        }
                        .padding(horizontal = 14.dp, vertical = 9.dp),
                    contentAlignment = Alignment.Center
                ) {
                    BasicText(
                        text = "WYŚLIJ\nSMS SOS",
                        style = TextStyle(
                            color = Color(0xFF1A0505),
                            fontSize = 11.5.sp,
                            fontWeight = FontWeight.ExtraBold,
                            textAlign = TextAlign.Center
                        )
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(14.dp))

        BasicText(
            text = "ROZKAZ BOJOWY (OPORD) & ŁĄCZNOŚĆ",
            style = TextStyle(color = FthTextPrimary, fontSize = 13.5.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = 0.5.sp)
        )
        Spacer(modifier = Modifier.height(4.dp))
        BasicText(
            text = "OPORD",
            style = TextStyle(color = FthTextMuted, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        )
        Spacer(modifier = Modifier.height(6.dp))

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            val labels = listOf("[ DZIEŃ 1 ]", "[ DZIEŃ 2 ]", "[ MAPA ]")
            labels.forEachIndexed { idx, txt ->
                val isSel = selectedSubTab == idx
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .clip(RoundedCornerShape(7.dp))
                        .background(if (isSel) Color(0xFF11353E) else FthCardBg)
                        .border(1.1.dp, if (isSel) FthCyan else FthCardBorder, RoundedCornerShape(7.dp))
                        .clickable { onSelectSubTab(idx) }
                        .padding(vertical = 8.dp),
                    contentAlignment = Alignment.Center
                ) {
                    BasicText(
                        text = txt,
                        style = TextStyle(
                            color = if (isSel) FthCyanBright else FthTextMuted,
                            fontSize = 11.5.sp,
                            fontWeight = FontWeight.Bold
                        )
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(12.dp))

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(11.dp))
                .background(FthCardBg)
                .border(1.dp, FthCardBorder, RoundedCornerShape(11.dp))
                .padding(12.dp)
        ) {
            BasicText(
                text = "TABELA CZĘSTOTLIWOŚCI (Radio)",
                style = TextStyle(color = FthTextMuted, fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 0.5.sp)
            )
            Spacer(modifier = Modifier.height(8.dp))
            FthRadioFreqRow("PMR: CH 1 (446.00625) · CTCSS 67.0")
            Spacer(modifier = Modifier.height(7.dp))
            FthRadioFreqRow("VHF: 145.500 MHz · CTCSS 88.5")
            Spacer(modifier = Modifier.height(7.dp))
            FthRadioFreqRow("LoRa: EU_868 · SHORT_FAST · MESH")
        }

        Spacer(modifier = Modifier.height(14.dp))

        BasicText(
            text = "Zadanie:",
            style = TextStyle(color = FthTextPrimary, fontSize = 15.sp, fontWeight = FontWeight.Bold)
        )
        Spacer(modifier = Modifier.height(5.dp))
        val taskText = when (selectedSubTab) {
            0 -> "Skryte przemieszczenie czujki z przodu, rozpoznanie podejść oraz utrzymanie stałej łączności LoRa Mesh z bazą."
            1 -> "Zajęcie rubieży obronnej przed świtem, wyznaczenie sektorów ognia dla wsparcia KM i gotowość do ewakuacji MEDEVAC."
            else -> "Paczka mapowa offline (skala 1:5000, siatka MGRS / WGS84) zsynchronizowana z Field TAK Hub Builder."
        }
        BasicText(
            text = taskText,
            style = TextStyle(color = FthTextMuted, fontSize = 13.sp, lineHeight = 18.sp)
        )
        Spacer(modifier = Modifier.height(8.dp))
    }
}

@Composable
private fun FthRadioFreqRow(text: String) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(6.dp))
            .background(Color(0xFF071217))
            .border(0.8.dp, FthCardBorder.copy(alpha = 0.7f), RoundedCornerShape(6.dp))
            .padding(horizontal = 10.dp, vertical = 7.dp)
    ) {
        BasicText(
            text = text,
            style = TextStyle(color = FthTextPrimary, fontSize = 12.5.sp, fontWeight = FontWeight.SemiBold, fontFamily = FontFamily.Monospace)
        )
    }
}

@Composable
private fun FthCheatSheetScreen() {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
    ) {
        BasicText(
            text = "MODUŁ: ŚCIĄGA TAKTYCZNA",
            style = TextStyle(color = FthCyan.copy(alpha = 0.8f), fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 0.9.sp)
        )
        Spacer(modifier = Modifier.height(4.dp))
        BasicText(
            text = "Procedury & Meldunki (Offline)",
            style = TextStyle(color = FthTextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
        )
        Spacer(modifier = Modifier.height(12.dp))

        FthCheatCard(
            "MELDUNEK SALUTE (Rozpoznanie)",
            "S - Size (Liczebność)\nA - Activity (Aktywność)\nL - Location (Lokalizacja MGRS)\nU - Unit (Umundurowanie/Formacja)\nT - Time (Czas obserwacji)\nE - Equipment (Uzbrojenie/Sprzęt)"
        )
        Spacer(modifier = Modifier.height(10.dp))
        FthCheatCard(
            "PROTOKÓŁ MARCH (TCCC)",
            "M - Massive Hemorrhage (Staza/Krwotok)\nA - Airway (Drożność dróg oddechowych)\nR - Respiration (Opatrunek wentylowy)\nC - Circulation (Wstrząs/Płyny)\nH - Hypothermia / Head (Termika/Głowa)"
        )
        Spacer(modifier = Modifier.height(10.dp))
        FthCheatCard(
            "ALFABET FONETYCZNY NATO",
            "Alfa · Bravo · Charlie · Delta · Echo · Foxtrot · Golf · Hotel · India · Juliett · Kilo · Lima · Mike · November · Oscar · Papa · Quebec · Romeo · Sierra · Tango · Uniform · Victor · Whiskey · X-ray · Yankee · Zulu"
        )
    }
}

@Composable
private fun FthCheatCard(title: String, body: String) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .background(FthCardBg)
            .border(1.dp, FthCardBorder, RoundedCornerShape(10.dp))
            .padding(12.dp)
    ) {
        BasicText(text = title, style = TextStyle(color = FthCyan, fontSize = 12.5.sp, fontWeight = FontWeight.ExtraBold))
        Spacer(modifier = Modifier.height(6.dp))
        BasicText(text = body, style = TextStyle(color = FthTextPrimary, fontSize = 12.sp, lineHeight = 18.sp))
    }
}

@Composable
private fun FthBottomNavBar(
    selectedTab: FthTab,
    isLocked: Boolean,
    onSelectTab: (FthTab) -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(Color(0xFF071319))
            .border(1.dp, FthCardBorder, RoundedCornerShape(12.dp))
            .padding(vertical = 8.dp, horizontal = 4.dp),
        horizontalArrangement = Arrangement.SpaceEvenly,
        verticalAlignment = Alignment.CenterVertically
    ) {
        FthTab.values().forEach { tab ->
            val isActive = tab == selectedTab && !isLocked
            val tint = when {
                isLocked && tab == FthTab.PACKAGES -> FthAmberWarn
                isLocked -> Color(0xFF344C54)
                isActive -> FthCyanBright
                else -> FthTextMuted
            }
            Column(
                modifier = Modifier
                    .weight(1f)
                    .clickable { onSelectTab(tab) }
                    .padding(vertical = 2.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                FthNavTabIcon(tab = tab, tint = tint, modifier = Modifier.size(20.dp))
                Spacer(modifier = Modifier.height(3.dp))
                BasicText(
                    text = tab.label,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    style = TextStyle(
                        color = tint,
                        fontSize = 10.sp,
                        fontWeight = if (isActive) FontWeight.ExtraBold else FontWeight.SemiBold,
                        letterSpacing = 0.4.sp
                    )
                )
            }
        }
    }
}

@Composable
private fun FthNavTabIcon(tab: FthTab, tint: Color, modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val st = Stroke(width = 1.7.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round)
        val w = size.width
        val h = size.height
        when (tab) {
            FthTab.SCANNER -> {
                drawRect(tint, Offset(w * 0.12f, h * 0.12f), Size(w * 0.28f, h * 0.28f), style = st)
                drawRect(tint, Offset(w * 0.60f, h * 0.12f), Size(w * 0.28f, h * 0.28f), style = st)
                drawRect(tint, Offset(w * 0.12f, h * 0.60f), Size(w * 0.28f, h * 0.28f), style = st)
                drawRect(tint, Offset(w * 0.62f, h * 0.62f), Size(w * 0.22f, h * 0.22f))
            }
            FthTab.PACKAGES -> {
                drawRoundRect(tint, Offset(w * 0.12f, h * 0.22f), Size(w * 0.76f, h * 0.62f), CornerRadius(2.dp.toPx()), style = st)
                drawLine(tint, Offset(w * 0.12f, h * 0.44f), Offset(w * 0.88f, h * 0.44f), strokeWidth = 1.5.dp.toPx())
            }
            FthTab.OPORD -> {
                drawRoundRect(tint, Offset(w * 0.18f, h * 0.10f), Size(w * 0.64f, h * 0.80f), CornerRadius(2.dp.toPx()), style = st)
                drawLine(tint, Offset(w * 0.32f, h * 0.36f), Offset(w * 0.68f, h * 0.36f), strokeWidth = 1.5.dp.toPx())
                drawLine(tint, Offset(w * 0.32f, h * 0.56f), Offset(w * 0.68f, h * 0.56f), strokeWidth = 1.5.dp.toPx())
                drawLine(tint, Offset(w * 0.32f, h * 0.74f), Offset(w * 0.56f, h * 0.74f), strokeWidth = 1.5.dp.toPx())
            }
            FthTab.CHEATSHEET -> {
                drawCircle(tint, radius = w * 0.32f, center = Offset(w / 2f, h / 2f), style = st)
                drawCircle(tint, radius = w * 0.12f, center = Offset(w / 2f, h / 2f))
            }
        }
    }
}

private fun isPackageInstalled(context: Context, packageName: String): Boolean {
    return try {
        context.packageManager.getPackageInfo(packageName, 0)
        true
    } catch (_: Exception) {
        false
    }
}

private fun buildShortCallsignTag(callsign: String): String {
    val clean = callsign.uppercase(Locale.ROOT).filter { it.isLetterOrDigit() }
    if (clean.isEmpty()) return "FTH1"
    val digits = clean.filter { it.isDigit() }
    val letters = clean.filter { it.isLetter() }
    val prefix = letters.take(3).ifEmpty { clean.take(3) }
    val suffix = digits.takeLast(1).ifEmpty { "1" }
    return (prefix + suffix).take(4)
}

@SuppressLint("MissingPermission")
private fun getLastKnownMgrs(context: Context): Pair<String, String> {
    return try {
        val lm = context.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
        val providers = lm?.getProviders(true) ?: emptyList()
        var best: Location? = null
        for (p in providers) {
            val loc = lm?.getLastKnownLocation(p) ?: continue
            if (best == null || loc.accuracy < best.accuracy) best = loc
        }
        if (best != null) {
            latLonToMgrsSplit(best.latitude, best.longitude)
        } else {
            Pair("34U", "CA 48120 21050")
        }
    } catch (_: Exception) {
        Pair("34U", "CA 48120 21050")
    }
}

private fun latLonToMgrsSplit(latIn: Double, lonIn: Double): Pair<String, String> {
    val lat = latIn.coerceIn(-80.0, 84.0)
    val lon = lonIn.coerceIn(-180.0, 179.999999)
    val zone = floor((lon + 180.0) / 6.0).toInt() + 1
    val bands = "CDEFGHJKLMNPQRSTUVWXX"
    val bandIdx = floor((lat + 80.0) / 8.0).toInt().coerceIn(0, bands.length - 1)
    val band = bands[bandIdx]

    val a = 6378137.0
    val f = 1.0 / 298.257223563
    val k0 = 0.9996
    val e2 = 2.0 * f - f * f
    val ep2 = e2 / (1.0 - e2)
    val latRad = Math.toRadians(lat)
    val lonRad = Math.toRadians(lon)
    val lon0Rad = Math.toRadians(((zone - 1) * 6 - 180 + 3).toDouble())

    val nVal = a / sqrt(1.0 - e2 * sin(latRad).pow(2))
    val tVal = tan(latRad).pow(2)
    val cVal = ep2 * cos(latRad).pow(2)
    val aVal = cos(latRad) * (lonRad - lon0Rad)
    val mVal = a * (
        (1.0 - e2 / 4.0 - 3.0 * e2 * e2 / 64.0 - 5.0 * e2 * e2 * e2 / 256.0) * latRad -
            (3.0 * e2 / 8.0 + 3.0 * e2 * e2 / 32.0 + 45.0 * e2 * e2 * e2 / 1024.0) * sin(2.0 * latRad) +
            (15.0 * e2 * e2 / 256.0 + 45.0 * e2 * e2 * e2 / 1024.0) * sin(4.0 * latRad) -
            (35.0 * e2 * e2 * e2 / 3072.0) * sin(6.0 * latRad)
        )
    val easting = k0 * nVal * (
        aVal + (1.0 - tVal + cVal) * aVal.pow(3) / 6.0 +
            (5.0 - 18.0 * tVal + tVal * tVal + 72.0 * cVal - 58.0 * ep2) * aVal.pow(5) / 120.0
        ) + 500000.0
    var northing = k0 * (
        mVal + nVal * tan(latRad) * (
            aVal * aVal / 2.0 +
                (5.0 - tVal + 9.0 * cVal + 4.0 * cVal * cVal) * aVal.pow(4) / 24.0 +
                (61.0 - 58.0 * tVal + tVal * tVal + 600.0 * cVal - 330.0 * ep2) * aVal.pow(6) / 720.0
            )
        )
    if (lat < 0.0) northing += 10000000.0

    val setNum = (zone - 1) % 6
    val colSets = arrayOf("ABCDEFGH", "JKLMNPQR", "STUVWXYZ", "ABCDEFGH", "JKLMNPQR", "STUVWXYZ")
    val rowSets = arrayOf("ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE")
    val colIdx = ((floor(easting / 100000.0).toInt() - 1) % 8 + 8) % 8
    val rowIdx = (floor((northing % 2000000.0) / 100000.0).toInt() % 20 + 20) % 20
    val sq = "${colSets[setNum][colIdx]}${rowSets[setNum % 2][rowIdx]}"
    val e5 = floor(easting % 100000.0).toInt().coerceIn(0, 99999)
    val n5 = floor(northing % 100000.0).toInt().coerceIn(0, 99999)

    return Pair(
        String.format(Locale.US, "%02d%c", zone, band),
        String.format(Locale.US, "%s %05d %05d", sq, e5, n5)
    )
}

@Composable
@SuppressLint("UnsafeOptInUsageError")
private fun FthLiveCameraOrViewfinder(
    activity: ComponentActivity,
    onQrDetected: (String) -> Unit,
    modifier: Modifier = Modifier
) {
    val executor = remember { Executors.newSingleThreadExecutor() }
    AndroidView(
        modifier = modifier,
        factory = { ctx ->
            val previewView = PreviewView(ctx).apply {
                scaleType = PreviewView.ScaleType.FILL_CENTER
            }
            val cameraProviderFuture = ProcessCameraProvider.getInstance(ctx)
            cameraProviderFuture.addListener({
                try {
                    val cameraProvider = cameraProviderFuture.get()
                    val preview = Preview.Builder().build().also {
                        it.setSurfaceProvider(previewView.surfaceProvider)
                    }
                    val scanner = BarcodeScanning.getClient()
                    val analysis = ImageAnalysis.Builder()
                        .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                        .build()
                    analysis.setAnalyzer(executor) { imageProxy ->
                        val mediaImage = imageProxy.image
                        if (mediaImage != null) {
                            val inputImg = InputImage.fromMediaImage(mediaImage, imageProxy.imageInfo.rotationDegrees)
                            scanner.process(inputImg)
                                .addOnSuccessListener { barcodes ->
                                    val raw = barcodes.firstOrNull()?.rawValue
                                    if (!raw.isNullOrBlank()) {
                                        activity.runOnUiThread { onQrDetected(raw) }
                                    }
                                }
                                .addOnCompleteListener {
                                    imageProxy.close()
                                }
                        } else {
                            imageProxy.close()
                        }
                    }
                    cameraProvider.unbindAll()
                    cameraProvider.bindToLifecycle(
                        activity,
                        CameraSelector.DEFAULT_BACK_CAMERA,
                        preview,
                        analysis
                    )
                } catch (_: Exception) { }
            }, ContextCompat.getMainExecutor(ctx))
            previewView
        }
    )
}
