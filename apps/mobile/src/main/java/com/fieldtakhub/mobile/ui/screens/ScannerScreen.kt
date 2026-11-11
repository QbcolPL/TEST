package com.fieldtakhub.mobile.ui.screens

import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import android.util.Size
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
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
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.fieldtakhub.mobile.data.*
import com.fieldtakhub.mobile.ui.theme.*
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.common.InputImage
import kotlinx.coroutines.launch
import java.util.concurrent.Executors

@Composable
fun ScannerScreen(
    activePackage: FtakPackageSummary?,
    onPackageLoaded: (FtakPackageSummary) -> Unit
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val scope = rememberCoroutineScope()
    val scrollState = rememberScrollState()

    var hasCameraPerm by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
        )
    }
    val camPermLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        hasCameraPerm = it
    }

    var isDownloading by remember { mutableStateOf(false) }
    var progressPct by remember { mutableStateOf(0) }
    var statusMessage by remember { mutableStateOf("ZESKANUJ QR / .FTAK") }
    var provisionReport by remember { mutableStateOf<ProvisioningReport?>(null) }

    val savedCallsigns = remember { FtakPackageRepository.loadOperatorCallsign(context) }
    var fullCallsign by remember { mutableStateOf(savedCallsigns.first) }
    var shortCallsign by remember { mutableStateOf(savedCallsigns.second) }
    var showRosterDropdown by remember { mutableStateOf(false) }

    var apkStatuses by remember(activePackage) {
        mutableStateOf(
            if (activePackage != null) ApkCompatibilityEngine.checkCompatibility(context, activePackage)
            else emptyList()
        )
    }

    DisposableEffect(lifecycleOwner, activePackage) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                hasCameraPerm = ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
                if (activePackage != null) {
                    apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, activePackage)
                }
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    fun onQrDetected(rawValue: String) {
        if (isDownloading) return
        isDownloading = true
        scope.launch {
            runCatching {
                FtakPackageRepository.handleScannedQrPayload(context, rawValue) { pct, msg ->
                    progressPct = pct
                    statusMessage = msg
                }
            }.onSuccess { summary ->
                isDownloading = false
                statusMessage = "ZAŁADOWANO: ${summary.eventName} (${summary.factionName})"
                onPackageLoaded(summary)
                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
            }.onFailure { err ->
                isDownloading = false
                statusMessage = "Błąd QR: ${err.message}"
            }
        }
    }

    val galleryQrLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
        if (uri != null) {
            statusMessage = "Odczytywanie kodu QR z obrazu z galerii..."
            runCatching {
                val inputImage = InputImage.fromFilePath(context, uri)
                BarcodeScanning.getClient().process(inputImage)
                    .addOnSuccessListener { barcodes ->
                        val raw = barcodes.firstOrNull { !it.rawValue.isNullOrBlank() }?.rawValue
                        if (!raw.isNullOrBlank()) {
                            onQrDetected(raw)
                        } else {
                            statusMessage = "Błąd: Na wybranym zdjęciu z galerii nie znaleziono kodu QR."
                        }
                    }
                    .addOnFailureListener { e ->
                        statusMessage = "Błąd skanowania obrazu: ${e.message}"
                    }
            }.onFailure { e ->
                statusMessage = "Błąd otwarcia obrazu: ${e.message}"
            }
        }
    }

    val filePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val summary = FtakPackageRepository.importFromUri(context, uri)
                if (summary != null) {
                    onPackageLoaded(summary)
                    apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
                    statusMessage = "Wczytano paczkę: ${summary.fileName}"
                }
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .verticalScroll(scrollState)
            .padding(horizontal = 12.dp, vertical = 4.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        // 1. CELOWNIK SKANERA QR + PRZYCISKI GALERII I PLIKU .FTAK
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(8.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(8.dp))
        ) {
            Column(
                modifier = Modifier.padding(10.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(185.dp)
                        .background(InputTrench, RoundedCornerShape(6.dp)),
                    contentAlignment = Alignment.Center
                ) {
                    if (hasCameraPerm) {
                        CameraQrPreview(onQrScanned = { onQrDetected(it) })
                    } else {
                        Button(
                            onClick = { camPermLauncher.launch(Manifest.permission.CAMERA) },
                            colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                            shape = RoundedCornerShape(4.dp)
                        ) {
                            Text("ODBLOKUJ APARAT SKANERA QR", color = AbyssalSlate, fontWeight = FontWeight.Bold, fontSize = 11.sp)
                        }
                    }

                    TacticalQrReticleOverlay(modifier = Modifier.fillMaxSize())
                }

                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = statusMessage,
                    color = if (statusMessage.startsWith("Błąd")) DangerCrimson else TextMuted,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )

                if (isDownloading) {
                    LinearProgressIndicator(
                        progress = progressPct / 100f,
                        color = RadarCyan,
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 6.dp)
                    )
                }

                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = { galleryQrLauncher.launch("image/*") },
                        border = BorderStroke(1.2.dp, RadarCyan),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(horizontal = 6.dp)
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.GALLERY_QR, tint = RadarCyan, size = 14.dp)
                        Spacer(modifier = Modifier.width(6.dp))
                        Text("QR Z GALERII", color = RadarCyan, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }

                    OutlinedButton(
                        onClick = { filePickerLauncher.launch("*/*") },
                        border = BorderStroke(1.2.dp, MutedGrid),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(horizontal = 6.dp)
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.FOLDER_OPEN, tint = TextWhite, size = 14.dp)
                        Spacer(modifier = Modifier.width(6.dp))
                        Text("PLIK .FTAK", color = TextWhite, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        // 2. KARTA STATUS INTEGRACJI (1:1 z wizualizacja srodkowego ekranu)
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(5.dp)) {
                Text(
                    text = "Status integracji",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold
                )

                if (activePackage == null) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = MilSimGreen, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("ATAK-CIV ", color = TextWhite, fontSize = 12.sp)
                        Text("(OK)", color = MilSimGreen, fontSize = 12.sp)
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = MilSimGreen, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("Meshtastic ", color = TextWhite, fontSize = 12.sp)
                        Text("(OK)", color = MilSimGreen, fontSize = 12.sp)
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = RadarCyan, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("Server OTS ", color = TextWhite, fontSize = 12.sp)
                        Text("(Oczekuje na paczkę .ftak)", color = TextMuted, fontSize = 12.sp)
                    }
                } else {
                    apkStatuses.forEach { st ->
                        val dotColor = when {
                            st.isUpToDate -> MilSimGreen
                            st.isInstalled -> CautionAmber
                            else -> DangerCrimson
                        }
                        val statusText = when {
                            st.isUpToDate -> "(OK)"
                            st.isInstalled -> "(Wymaga v${st.item.requiredVersion})"
                            else -> "(Brak -> Zainstaluj)"
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                                Text("● ", color = dotColor, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                Text("${st.item.role} ", color = TextWhite, fontSize = 12.sp)
                                Text(statusText, color = dotColor, fontSize = 12.sp)
                            }

                            if (!st.isUpToDate) {
                                Surface(
                                    color = CautionAmber,
                                    shape = RoundedCornerShape(4.dp),
                                    modifier = Modifier.clickable {
                                        scope.launch {
                                            val res = ApkCompatibilityEngine.extractAndInstallBundledApk(context, activePackage, st.item)
                                            res.onFailure { err -> statusMessage = err.message ?: "Błąd instalacji APK" }
                                        }
                                    }
                                ) {
                                    Text(
                                        text = if (st.isInstalled) "AKTUALIZUJ" else "ZAINSTALUJ",
                                        color = AbyssalSlate,
                                        fontSize = 9.sp,
                                        fontWeight = FontWeight.Bold,
                                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp)
                                    )
                                }
                            }
                        }
                    }

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = MilSimGreen, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("Server OTS ", color = TextWhite, fontSize = 12.sp)
                        Text("(Połączono: ${activePackage.otsHost})", color = MilSimGreen, fontSize = 12.sp)
                    }
                }
            }
        }

        // 3. ZNAK WYWOLAWCZY (Callsign) + Automated preview: [WLK1] + Przycisk WSTRZYKNIJ
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "ZNAK WYWOŁAWCZY (Callsign)",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace
                    )

                    if (activePackage != null && activePackage.squadCallsigns.isNotEmpty()) {
                        Box {
                            Text(
                                text = "[Wybierz z OPORD ▼]",
                                color = RadarCyan,
                                fontSize = 10.sp,
                                fontFamily = FontFamily.Monospace,
                                modifier = Modifier.clickable { showRosterDropdown = true }
                            )
                            DropdownMenu(
                                expanded = showRosterDropdown,
                                onDismissRequest = { showRosterDropdown = false }
                            ) {
                                activePackage.squadCallsigns.forEach { cs ->
                                    DropdownMenuItem(
                                        text = { Text(cs) },
                                        onClick = {
                                            fullCallsign = cs
                                            shortCallsign = FtakPackageRepository.generateMeshtasticShortName(cs)
                                            FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                                            showRosterDropdown = false
                                        }
                                    )
                                }
                            }
                        }
                    }
                }

                OutlinedTextField(
                    value = fullCallsign,
                    onValueChange = { newVal ->
                        fullCallsign = newVal
                        shortCallsign = FtakPackageRepository.generateMeshtasticShortName(newVal)
                        FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                    },
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = RadarCyan,
                        unfocusedBorderColor = RadarCyan,
                        focusedContainerColor = InputTrench,
                        unfocusedContainerColor = InputTrench,
                        focusedTextColor = TextWhite,
                        unfocusedTextColor = TextWhite
                    ),
                    modifier = Modifier.fillMaxWidth()
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Automated preview:",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = "[$shortCallsign]",
                            color = TextWhite,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace,
                            modifier = Modifier.padding(end = 8.dp)
                        )
                        OutlinedTextField(
                            value = shortCallsign,
                            onValueChange = { newShort ->
                                shortCallsign = newShort.filter { it.isLetterOrDigit() }.take(4).uppercase()
                                FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                            },
                            singleLine = true,
                            colors = OutlinedTextFieldDefaults.colors(
                                focusedBorderColor = RadarCyan,
                                unfocusedBorderColor = MutedGrid,
                                focusedContainerColor = InputTrench,
                                unfocusedContainerColor = InputTrench,
                                focusedTextColor = RadarCyan,
                                unfocusedTextColor = RadarCyan
                            ),
                            modifier = Modifier.width(84.dp)
                        )
                    }
                }

                Button(
                    onClick = {
                        if (activePackage == null) {
                            statusMessage = "Błąd: Najpierw zeskanuj kod QR lub wczytaj plik .ftak!"
                            return@Button
                        }
                        scope.launch {
                            statusMessage = "Wstrzykiwanie do ATAK-CIV i Meshtastic..."
                            val report = AtakMeshtasticProvisioner.provisionAll(
                                context = context,
                                pkg = activePackage,
                                fullCallsign = fullCallsign,
                                shortCallsign = shortCallsign
                            )
                            provisionReport = report
                            statusMessage = report.summaryTitle
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(50.dp)
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.Center
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.INJECT_ARROWS, tint = AbyssalSlate, size = 20.dp)
                        Spacer(modifier = Modifier.width(10.dp))
                        Text(
                            text = "WSTRZYKNIJ DO ATAK + MESHTASTIC",
                            color = AbyssalSlate,
                            fontWeight = FontWeight.Bold,
                            fontSize = 12.sp
                        )
                    }
                }

                if (provisionReport != null) {
                    Surface(
                        color = InputTrench,
                        shape = RoundedCornerShape(4.dp),
                        border = BorderStroke(1.dp, MilSimGreen),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Column(modifier = Modifier.padding(8.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                            Text(
                                text = provisionReport!!.summaryTitle,
                                color = MilSimGreen,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                fontFamily = FontFamily.Monospace
                            )
                            provisionReport!!.logLines.forEach { line ->
                                Text(
                                    text = line,
                                    color = TextWhite,
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
}

@Composable
@androidx.annotation.OptIn(androidx.camera.core.ExperimentalGetImage::class)
private fun CameraQrPreview(onQrScanned: (String) -> Unit) {
    val lifecycleOwner = LocalLifecycleOwner.current
    val executor = remember { Executors.newSingleThreadExecutor() }
    var lastScanned by remember { mutableStateOf("") }

    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            val previewView = PreviewView(ctx).apply {
                implementationMode = PreviewView.ImplementationMode.COMPATIBLE
            }
            val cameraProviderFuture = ProcessCameraProvider.getInstance(ctx)
            cameraProviderFuture.addListener({
                val cameraProvider = cameraProviderFuture.get()
                val preview = Preview.Builder().build().also {
                    it.setSurfaceProvider(previewView.surfaceProvider)
                }

                val scanner = BarcodeScanning.getClient()
                val analysis = ImageAnalysis.Builder()
                    .setTargetResolution(Size(1280, 720))
                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                    .build()

                analysis.setAnalyzer(executor) { imageProxy ->
                    val mediaImage = imageProxy.image
                    if (mediaImage != null) {
                        val image = InputImage.fromMediaImage(mediaImage, imageProxy.imageInfo.rotationDegrees)
                        scanner.process(image)
                            .addOnSuccessListener { barcodes ->
                                val raw = barcodes.firstOrNull { !it.rawValue.isNullOrBlank() }?.rawValue
                                if (!raw.isNullOrBlank() && raw != lastScanned) {
                                    lastScanned = raw
                                    onQrScanned(raw)
                                }
                            }
                            .addOnCompleteListener {
                                imageProxy.close()
                            }
                    } else {
                        imageProxy.close()
                    }
                }

                runCatching {
                    cameraProvider.unbindAll()
                    cameraProvider.bindToLifecycle(
                        lifecycleOwner,
                        CameraSelector.DEFAULT_BACK_CAMERA,
                        preview,
                        analysis
                    )
                }
            }, ContextCompat.getMainExecutor(ctx))
            previewView
        }
    )
}