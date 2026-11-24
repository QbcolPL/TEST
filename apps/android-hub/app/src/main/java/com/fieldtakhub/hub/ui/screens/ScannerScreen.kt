package com.fieldtakhub.hub.ui.screens

import android.os.Handler
import android.os.Looper
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ContentPaste
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.SwapVert
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.fieldtakhub.hub.AppCheck
import com.fieldtakhub.hub.CheckState
import com.fieldtakhub.hub.HubState
import com.fieldtakhub.hub.HubViewModel
import com.fieldtakhub.hub.ServerState
import com.fieldtakhub.hub.core.AppRole
import com.fieldtakhub.hub.core.Callsign
import com.fieldtakhub.hub.platform.QrCodes
import com.fieldtakhub.hub.ui.components.HubField
import com.fieldtakhub.hub.ui.components.HubHeader
import com.fieldtakhub.hub.ui.components.HubPanel
import com.fieldtakhub.hub.ui.components.PrimaryButton
import com.fieldtakhub.hub.ui.components.SecondaryButton
import com.fieldtakhub.hub.ui.components.SmallTextButton
import com.fieldtakhub.hub.ui.components.StatusLine
import com.fieldtakhub.hub.ui.components.hubFieldColors
import com.fieldtakhub.hub.ui.theme.Hub
import com.fieldtakhub.hub.ui.theme.Mono
import com.google.mlkit.vision.barcode.BarcodeScanner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import kotlinx.coroutines.launch
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

@Composable
fun ScannerScreen(state: HubState, vm: HubViewModel) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var tick by remember { mutableIntStateOf(0) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { tick++ }
    val cameraOk = remember(tick) { HubPermissions.granted(context, HubPermissions.camera) }
    val cameraLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { tick++ }
    val fileLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) vm.importFromUri(uri)
    }
    val imageLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) scope.launch {
            val text = QrCodes.decodeImage(context, uri)
            if (text != null) vm.handleLink(text) else vm.notify("Brak kodu QR", "Na wybranym obrazie nie znaleziono kodu QR.", error = true)
        }
    }
    var pasteOpen by remember { mutableStateOf(false) }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 12.dp)
    ) {
        HubHeader()
        Spacer(Modifier.height(16.dp))

        Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            Box(
                Modifier
                    .size(250.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(Color(0xFF0E1E21)),
                contentAlignment = Alignment.Center
            ) {
                if (cameraOk) {
                    QrCamera(active = !state.overlayOpen, onCode = { vm.onQrScanned(it) }, modifier = Modifier.fillMaxSize())
                } else {
                    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(16.dp)) {
                        Icon(Icons.Filled.QrCodeScanner, contentDescription = null, tint = Hub.Muted, modifier = Modifier.size(64.dp))
                        Spacer(Modifier.height(8.dp))
                        Text("Aparat jest potrzebny do skanowania kodów QR.", color = Hub.TextDim, fontSize = 13.sp, textAlign = TextAlign.Center)
                        Spacer(Modifier.height(8.dp))
                        SecondaryButton("Włącz aparat", Icons.Filled.PhotoCamera) {
                            cameraLauncher.launch(android.Manifest.permission.CAMERA)
                        }
                    }
                }
                ScanOverlay(Modifier.fillMaxSize(), animate = cameraOk && !state.overlayOpen)
            }
        }
        Text(
            "ZESKANUJ QR / .FTAK",
            color = Hub.Text,
            fontSize = 15.sp,
            letterSpacing = 1.sp,
            textAlign = TextAlign.Center,
            modifier = Modifier.fillMaxWidth().padding(top = 8.dp)
        )
        Row(
            Modifier.fillMaxWidth().padding(top = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            SecondaryButton("Plik .ftak", Icons.Filled.FolderOpen, Modifier.weight(1f)) { fileLauncher.launch(arrayOf("*/*")) }
            SecondaryButton("Obraz QR", Icons.Filled.Image, Modifier.weight(1f)) { imageLauncher.launch("image/*") }
            SecondaryButton("Link", Icons.Filled.ContentPaste, Modifier.weight(1f)) { pasteOpen = true }
        }

        Spacer(Modifier.height(14.dp))
        IntegrationStatus(state, onRefresh = { vm.refreshStatus() }, onOpen = { vm.openInject() })

        Spacer(Modifier.height(14.dp))
        CallsignEditor(state, vm)

        Spacer(Modifier.height(16.dp))
        PrimaryButton(
            "WSTRZYKNIJ DO\nATAK + MESHTASTIC",
            icon = Icons.Filled.SwapVert,
            enabled = state.active != null && state.callsign.isNotBlank()
        ) { vm.openInject() }
        if (state.active == null) {
            Text(
                "Najpierw zeskanuj kod paczki od organizatora.",
                color = Hub.TextDim, fontSize = 12.sp, textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth().padding(top = 6.dp)
            )
        }
        Spacer(Modifier.height(16.dp))
    }

    if (pasteOpen) PasteLinkDialog(onDismiss = { pasteOpen = false }) { text ->
        pasteOpen = false
        vm.handleLink(text)
    }
}

@Composable
private fun IntegrationStatus(state: HubState, onRefresh: () -> Unit, onOpen: () -> Unit) {
    HubPanel(onClick = onOpen) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Status integracji", color = Hub.Text, fontSize = 18.sp, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            Icon(
                Icons.Filled.Refresh, contentDescription = "Odśwież", tint = Hub.TextDim,
                modifier = Modifier.size(22.dp).clickable(onClick = onRefresh)
            )
        }
        val active = state.active
        Text(
            active?.pkg?.title?.let { "Paczka: $it" } ?: "Brak aktywnej paczki",
            color = Hub.TextDim, fontSize = 12.sp, modifier = Modifier.padding(bottom = 6.dp)
        )
        val main = state.apps.filter { it.requirement.role != AppRole.PLUGIN }
        val plugins = state.apps.filter { it.requirement.role == AppRole.PLUGIN }
        for (check in main) AppStatusLine(check)
        if (plugins.size in 1..3) {
            for (check in plugins) AppStatusLine(check)
        } else if (plugins.size > 3) {
            val ok = plugins.count { it.state == CheckState.OK }
            StatusLine("Wtyczki ATAK", "$ok/${plugins.size}", if (ok == plugins.size) Hub.Green else Hub.Amber)
        }
        val server = active?.pkg?.server
        when (state.server) {
            ServerState.REACHABLE -> StatusLine("Server OTS", "Osiągalny", Hub.Green, server?.endpoint)
            ServerState.UNREACHABLE -> StatusLine("Server OTS", "Brak połączenia", Hub.Amber, server?.endpoint?.let { "$it — sprawdź sieć / VPN" })
            ServerState.CHECKING -> StatusLine("Server OTS", "Sprawdzanie…", Hub.Muted, server?.endpoint)
            ServerState.NONE -> StatusLine("Server OTS", if (active != null) "Brak w paczce" else "—", Hub.Muted, if (active != null) "Tryb bez serwera (mesh)" else null)
        }
    }
}

@Composable
private fun AppStatusLine(check: AppCheck) {
    val (label, color) = when (check.state) {
        CheckState.OK -> "OK" to Hub.Green
        CheckState.MISSING -> "BRAK" to Hub.Red
        CheckState.OUTDATED -> "AKTUALIZACJA" to Hub.Amber
    }
    StatusLine(check.name, label, color, check.detail)
}

@Composable
private fun CallsignEditor(state: HubState, vm: HubViewModel) {
    Text("ZNAK WYWOŁAWCZY (Callsign)", color = Hub.TextDim, fontSize = 13.sp, letterSpacing = 1.sp, modifier = Modifier.padding(bottom = 4.dp))
    HubField(value = state.callsign, onChange = { vm.setCallsign(it) }, placeholder = "np. Wilk-1")
    Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text("Skrót Meshtastic (max 4 znaki)", color = Hub.Cyan, fontSize = 13.sp)
            Text(
                if (state.shortManual) "ustawiony ręcznie" else "propozycja automatyczna",
                color = Hub.TextDim, fontSize = 11.sp
            )
        }
        Text("[", color = Hub.Cyan, fontSize = 22.sp, fontFamily = Mono)
        OutlinedTextField(
            value = state.shortName,
            onValueChange = { vm.setShortName(it) },
            singleLine = true,
            modifier = Modifier.width(104.dp),
            textStyle = androidx.compose.ui.text.TextStyle(fontSize = 20.sp, fontFamily = Mono, color = Hub.Cyan, textAlign = TextAlign.Center, fontWeight = FontWeight.Bold),
            colors = hubFieldColors(),
            shape = RoundedCornerShape(8.dp)
        )
        Text("]", color = Hub.Cyan, fontSize = 22.sp, fontFamily = Mono)
    }
    val suggestions = remember(state.callsign) { Callsign.suggestions(state.callsign) }
    if (suggestions.isNotEmpty()) {
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(top = 6.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text("Propozycje:", color = Hub.TextDim, fontSize = 12.sp, modifier = Modifier.padding(end = 6.dp))
            for (s in suggestions) {
                val selected = s == state.shortName
                val shape = RoundedCornerShape(6.dp)
                Text(
                    s,
                    modifier = Modifier
                        .padding(end = 6.dp)
                        .clip(shape)
                        .background(if (selected) Hub.CyanSoft else Color.Transparent)
                        .border(1.dp, if (selected) Hub.Cyan else Hub.BorderStrong, shape)
                        .clickable { vm.setShortName(s) }
                        .padding(horizontal = 10.dp, vertical = 6.dp),
                    color = if (selected) Hub.Cyan else Hub.Text,
                    fontFamily = Mono,
                    fontSize = 14.sp
                )
            }
            if (state.shortManual) SmallTextButton("auto") { vm.resetShortName() }
        }
    }
}

@Composable
private fun PasteLinkDialog(onDismiss: () -> Unit, onSubmit: (String) -> Unit) {
    var text by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Hub.Panel,
        title = { Text("Wklej link paczki", color = Hub.Text) },
        text = {
            Column {
                Text("Link fieldtak://…, adres http(s) pliku .ftak albo link z Dysku Google.", color = Hub.TextDim, fontSize = 13.sp)
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = text, onValueChange = { text = it },
                    modifier = Modifier.fillMaxWidth(),
                    colors = hubFieldColors(),
                    placeholder = { Text("https://…", color = Hub.Muted) }
                )
            }
        },
        confirmButton = { TextButton(onClick = { if (text.isNotBlank()) onSubmit(text.trim()) }) { Text("Importuj", color = Hub.Cyan) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Anuluj", color = Hub.TextDim) } }
    )
}

/** Corner brackets and the moving "laser" line from the mockup. */
@Composable
private fun ScanOverlay(modifier: Modifier, animate: Boolean) {
    val transition = rememberInfiniteTransition(label = "laser")
    val position by transition.animateFloat(
        initialValue = 0.08f,
        targetValue = 0.92f,
        animationSpec = infiniteRepeatable(tween(1800, easing = LinearEasing), RepeatMode.Reverse),
        label = "laserPosition"
    )
    Canvas(modifier) {
        val w = size.width
        val h = size.height
        val len = w * 0.17f
        val sw = 5.dp.toPx()
        val c = Hub.Cyan
        drawLine(c, Offset(0f, 0f), Offset(len, 0f), sw, StrokeCap.Round)
        drawLine(c, Offset(0f, 0f), Offset(0f, len), sw, StrokeCap.Round)
        drawLine(c, Offset(w, 0f), Offset(w - len, 0f), sw, StrokeCap.Round)
        drawLine(c, Offset(w, 0f), Offset(w, len), sw, StrokeCap.Round)
        drawLine(c, Offset(0f, h), Offset(len, h), sw, StrokeCap.Round)
        drawLine(c, Offset(0f, h), Offset(0f, h - len), sw, StrokeCap.Round)
        drawLine(c, Offset(w, h), Offset(w - len, h), sw, StrokeCap.Round)
        drawLine(c, Offset(w, h), Offset(w, h - len), sw, StrokeCap.Round)
        if (animate) {
            val y = h * position
            drawLine(
                Brush.horizontalGradient(listOf(Color.Transparent, c, Color.Transparent)),
                Offset(0f, y), Offset(w, y), 3.dp.toPx()
            )
        }
    }
}

/** CameraX preview + ML Kit QR detection. Bound to the screen's lifecycle; paused while dialogs are open. */
@Composable
private fun QrCamera(active: Boolean, onCode: (String) -> Unit, modifier: Modifier) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember {
        PreviewView(context).apply {
            scaleType = PreviewView.ScaleType.FILL_CENTER
            implementationMode = PreviewView.ImplementationMode.COMPATIBLE
        }
    }
    val latestOnCode by rememberUpdatedState(onCode)

    DisposableEffect(active, lifecycleOwner) {
        if (!active) return@DisposableEffect onDispose { }
        val executor = Executors.newSingleThreadExecutor()
        val scanner = BarcodeScanning.getClient(BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build())
        val main = Handler(Looper.getMainLooper())
        val processing = AtomicBoolean(false)
        val future = ProcessCameraProvider.getInstance(context)
        var provider: ProcessCameraProvider? = null
        var disposed = false
        future.addListener({
            if (disposed) return@addListener
            val p = try { future.get() } catch (e: Exception) { return@addListener }
            provider = p
            val preview = Preview.Builder().build()
            preview.setSurfaceProvider(previewView.surfaceProvider)
            val analysis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
            analysis.setAnalyzer(executor) { proxy -> analyze(proxy, scanner, processing) { code -> main.post { latestOnCode(code) } } }
            try {
                p.unbindAll()
                p.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
            } catch (e: Exception) {
                // No back camera or camera in use by another app; the file/link import still works.
            }
        }, ContextCompat.getMainExecutor(context))
        onDispose {
            disposed = true
            runCatching { provider?.unbindAll() }
            scanner.close()
            executor.shutdown()
        }
    }
    AndroidView(factory = { previewView }, modifier = modifier)
}

private fun analyze(proxy: ImageProxy, scanner: BarcodeScanner, processing: AtomicBoolean, onCode: (String) -> Unit) {
    if (!processing.compareAndSet(false, true)) {
        proxy.close()
        return
    }
    val image = try {
        val rotation = proxy.imageInfo.rotationDegrees
        val bitmap = proxy.toBitmap()
        InputImage.fromBitmap(bitmap, rotation)
    } catch (e: Exception) {
        null
    } finally {
        proxy.close()
    }
    if (image == null) {
        processing.set(false)
        return
    }
    scanner.process(image)
        .addOnSuccessListener { codes -> codes.firstNotNullOfOrNull { it.rawValue }?.let(onCode) }
        .addOnCompleteListener { processing.set(false) }
}
