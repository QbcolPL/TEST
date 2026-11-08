package com.fieldtak.mobile.ui.scanner

import android.Manifest
import android.content.pm.PackageManager
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
import com.fieldtak.mobile.data.*
import com.fieldtak.mobile.ui.theme.*
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

    val filePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val summary = FtakPackageRepository.importFromUri(context, uri)
                if (summary != null) onPackageLoaded(summary)
            }
        }
    }

    var isDownloading by remember { mutableStateOf(false) }
    var progressPct by remember { mutableStateOf(0) }
    var statusMessage by remember { mutableStateOf("Skieruj celownik na kod QR paczki .ftak lub wybierz plik z pamięci.") }

    val savedCallsigns = remember { FtakPackageRepository.loadOperatorCallsign(context) }
    var fullCallsign by remember { mutableStateOf(savedCallsigns.first) }
    var shortCallsign by remember { mutableStateOf(savedCallsigns.second) }

    var apkStatuses by remember(activePackage) {
        mutableStateOf(
            if (activePackage != null) ApkCompatibilityEngine.checkCompatibility(context, activePackage)
            else emptyList()
        )
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
                statusMessage = "Pobrano i zweryfikowano paczkę: ${summary.fileName}"
                onPackageLoaded(summary)
                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
            }.onFailure { err ->
                isDownloading = false
                statusMessage = "Błąd QR: ${err.message}"
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .verticalScroll(scrollState)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(8.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.5.dp, RadarCyan, RoundedCornerShape(8.dp))
        ) {
            Column(
                modifier = Modifier.padding(10.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(195.dp)
                        .background(InputTrench, RoundedCornerShape(6.dp))
                        .border(1.dp, MutedGrid, RoundedCornerShape(6.dp)),
                    contentAlignment = Alignment.Center
                ) {
                    if (hasCameraPerm) {
                        CameraQrPreview(onQrScanned = { onQrDetected(it) })
                    } else {
                        Button(
                            onClick = { camPermLauncher.launch(Manifest.permission.CAMERA) },
                            colors = ButtonDefaults.buttonColors(containerColor = RadarCyan)
                        ) {
                            Text("WŁĄCZ APARAT SKANERA QR", color = AbyssalSlate, fontWeight = FontWeight.Bold)
                        }
                    }

                    Box(
                        modifier = Modifier
                            .fillMaxWidth(0.78f)
                            .height(2.dp)
                            .background(RadarCyan)
                    )
                }

                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = statusMessage,
                    color = if (statusMessage.startsWith("Błąd")) DangerCrimson else RadarCyan,
                    fontSize = 11.sp,
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
                    modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = { filePickerLauncher.launch("*/*") },
                        border = BorderStroke(1.dp, RadarCyan),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(4.dp)
                    ) {
                        Text("📂 WCZYTAJ PLIK .FTAK", color = RadarCyan, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                    OutlinedButton(
                        onClick = {
                            if (activePackage != null) {
                                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, activePackage)
                                statusMessage = "Odświeżono wersje pakietów APK."
                            }
                        },
                        border = BorderStroke(1.dp, MutedGrid),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(4.dp)
                    ) {
                        Text("🔄 SPRAWDŹ WERSJE APK", color = TextWhite, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = "Status integracji (Weryfikacja wersji z paczki .ftak)",
                    color = TextWhite,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Bold
                )

                if (activePackage == null) {
                    Text(
                        text = "Zeskanuj kod QR lub wybierz paczkę .ftak, aby porównać wersje ATAK-CIV, Meshtastic oraz pluginów.",
                        color = TextMuted,
                        fontSize = 11.sp
                    )
                } else {
                    apkStatuses.forEach { st ->
                        val statusColor = when {
                            st.isUpToDate -> MilSimGreen
                            st.isInstalled -> CautionAmber
                            else -> DangerCrimson
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = "● ${st.item.displayName}",
                                    color = statusColor,
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Bold
                                )
                                Text(
                                    text = if (st.isInstalled)
                                        "Zainstalowano: v${st.installedVersion} (Wymagana: v${st.item.requiredVersion})"
                                    else
                                        "Brak w telefonie (Wymagana: v${st.item.requiredVersion})",
                                    color = TextMuted,
                                    fontSize = 10.sp,
                                    fontFamily = FontFamily.Monospace
                                )
                            }

                            if (!st.isUpToDate) {
                                Button(
                                    onClick = {
                                        scope.launch {
                                            val res = ApkCompatibilityEngine.extractAndInstallBundledApk(context, activePackage, st.item)
                                            res.onFailure { err -> statusMessage = err.message ?: "Błąd instalacji APK" }
                                        }
                                    },
                                    colors = ButtonDefaults.buttonColors(containerColor = CautionAmber),
                                    shape = RoundedCornerShape(4.dp),
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp),
                                    modifier = Modifier.height(28.dp)
                                ) {
                                    Text(
                                        text = if (st.isInstalled) "AKTUALIZUJ" else "ZAINSTALUJ",
                                        color = AbyssalSlate,
                                        fontSize = 10.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                }
                            } else {
                                Text(
                                    text = "(OK)",
                                    color = MilSimGreen,
                                    fontSize = 11.sp,
                                    fontWeight = FontWeight.Bold,
                                    fontFamily = FontFamily.Monospace
                                )
                            }
                        }
                    }
                }
            }
        }

        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    text = "ZNAK WYWOŁAWCZY (Callsign ATAK & Skrót Meshtastic)",
                    color = RadarCyan,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedTextField(
                        value = fullCallsign,
                        onValueChange = { newVal ->
                            fullCallsign = newVal
                            shortCallsign = FtakPackageRepository.generateMeshtasticShortName(newVal)
                            FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                        },
                        label = { Text("Callsign ATAK", fontSize = 10.sp) },
                        singleLine = true,
                        modifier = Modifier.weight(1.8f)
                    )

                    OutlinedTextField(
                        value = shortCallsign,
                        onValueChange = { newShort ->
                            shortCallsign = newShort.take(4).uppercase()
                            FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                        },
                        label = { Text("Skrót LoRa (4 zn.)", fontSize = 10.sp) },
                        singleLine = true,
                        modifier = Modifier.weight(1f)
                    )
                }

                Text(
                    text = "Automated preview: [$fullCallsign] -> Meshtastic ShortName: [$shortCallsign]",
                    color = TextMuted,
                    fontSize = 10.sp,
                    fontFamily = FontFamily.Monospace
                )

                Button(
                    onClick = {
                        FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                        statusMessage = "Gotowe do wstrzyknięcia: $fullCallsign ($shortCallsign) [Etap 3]"
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(48.dp)
                ) {
                    Text(
                        text = "↓↑ WSTRZYKNIJ DO ATAK + MESHTASTIC",
                        color = AbyssalSlate,
                        fontWeight = FontWeight.Bold,
                        fontSize = 13.sp
                    )
                }
            }
        }
    }
}

@Composable
@androidx.annotation.OptIn(androidx.camera.core.ExperimentalGetImage::class)
private fun CameraQrPreview(onQrScanned: (String) -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val executor = remember { Executors.newSingleThreadExecutor() }
    var lastScanned by remember { mutableStateOf("") }

    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            val previewView = PreviewView(ctx)
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