package com.fieldtakhub.hub.ui.screens

import android.graphics.BitmapFactory
import android.location.Location
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.ExpandLess
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.Map
import androidx.compose.material.icons.filled.SettingsInputAntenna
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fieldtakhub.hub.HubState
import com.fieldtakhub.hub.HubViewModel
import com.fieldtakhub.hub.core.Mgrs
import com.fieldtakhub.hub.core.MeshConfig
import com.fieldtakhub.hub.core.OpordDay
import com.fieldtakhub.hub.core.OpordParser
import com.fieldtakhub.hub.data.StoredPackage
import com.fieldtakhub.hub.platform.Intents
import com.fieldtakhub.hub.ui.components.BracketTab
import com.fieldtakhub.hub.ui.components.FrameLine
import com.fieldtakhub.hub.ui.components.HubHeader
import com.fieldtakhub.hub.ui.components.HubPanel
import com.fieldtakhub.hub.ui.components.SecondaryButton
import com.fieldtakhub.hub.ui.components.SectionLabel
import com.fieldtakhub.hub.ui.theme.Hub
import com.fieldtakhub.hub.ui.theme.Mono
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
fun OpordScreen(state: HubState, vm: HubViewModel) {
    val source = state.opordSource
    val opord = source?.pkg?.opord
    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 12.dp)
    ) {
        HubHeader()
        Spacer(Modifier.height(14.dp))
        SosPanel(state, vm)
        if (source == null || opord == null || opord.days.isEmpty()) {
            Spacer(Modifier.height(14.dp))
            HubPanel {
                Text("Brak rozkazu OPORD", color = Hub.Text, fontWeight = FontWeight.Bold)
                Text("Zaimportuj paczkę z rozkazem albo kod „OPORD (LAN)” z Buildera.", color = Hub.TextDim, fontSize = 13.sp)
            }
            return@Column
        }
        SectionLabel("Rozkaz bojowy (OPORD) & łączność", color = Hub.Text)
        Text(
            listOf("OPORD", opord.operationNumber, opord.faction).filter { it.isNotBlank() }.joinToString(" · "),
            color = Hub.TextDim, fontSize = 13.sp
        )
        var selected by rememberSaveable(source.id) { mutableIntStateOf(0) }
        val mapIndex = opord.days.size
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(vertical = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            opord.days.forEachIndexed { i, d -> BracketTab(d.title, selected == i) { selected = i } }
            BracketTab("Mapa", selected == mapIndex) { selected = mapIndex }
        }
        if (selected < opord.days.size) {
            DayView(opord.days[selected], source, state, vm)
        } else {
            MapView(source, opord.days, vm)
        }
        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun SosPanel(state: HubState, vm: HubViewModel) {
    val context = LocalContext.current
    var tick by remember { mutableIntStateOf(0) }
    val granted = remember(tick) { HubPermissions.granted(context, HubPermissions.location) }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { tick++ }
    DisposableEffect(granted) {
        if (granted) vm.location.start()
        onDispose { vm.location.stop() }
    }
    val location by vm.location.location.collectAsStateWithLifecycle()
    val phone = state.emergencyPhone
    val shape = RoundedCornerShape(12.dp)
    Column(
        Modifier
            .fillMaxWidth()
            .clip(shape)
            .background(Hub.RedPanel)
            .border(2.dp, Hub.Red, shape)
            .padding(14.dp)
    ) {
        Text("SOS MGRS ALARM", color = Hub.Red, fontSize = 26.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
        Spacer(Modifier.height(8.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                val loc: Location? = location
                when {
                    !granted -> {
                        Text("Brak dostępu do lokalizacji", color = Hub.Text, fontSize = 14.sp)
                        Text(
                            "Nadaj", color = Hub.Cyan, fontSize = 14.sp, fontWeight = FontWeight.Bold,
                            modifier = Modifier.padding(top = 4.dp).clickable { launcher.launch(HubPermissions.location) }
                        )
                    }
                    loc == null -> Text(
                        if (vm.location.gpsEnabled) "Szukam pozycji GPS…" else "GPS wyłączony — włącz lokalizację",
                        color = Hub.Text, fontSize = 15.sp
                    )
                    !Mgrs.isSupported(loc.latitude, loc.longitude) -> Text(Mgrs.formatLatLon(loc.latitude, loc.longitude), color = Hub.Text, fontFamily = Mono)
                    else -> {
                        val mgrs = Mgrs.format(loc.latitude, loc.longitude)
                        Text("MGRS: " + mgrs.substringBefore(' '), color = Hub.Text, fontSize = 18.sp, fontFamily = Mono)
                        Text(mgrs.substringAfter(' '), color = Hub.Text, fontSize = 18.sp, fontFamily = Mono)
                        Text("±${loc.accuracy.toInt()} m", color = Hub.TextDim, fontSize = 12.sp)
                    }
                }
            }
            val enabled = !phone.isNullOrBlank()
            Row(
                Modifier
                    .clip(RoundedCornerShape(10.dp))
                    .background(if (enabled) Hub.Red else Hub.RedDeep)
                    .clickable(enabled = enabled) { vm.sendSos(location) }
                    .padding(horizontal = 12.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Filled.SettingsInputAntenna, contentDescription = null, tint = Color.White, modifier = Modifier.size(26.dp))
                Spacer(Modifier.width(8.dp))
                Text("WYŚLIJ\nSMS SOS", color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp, lineHeight = 17.sp)
            }
        }
        Text(
            if (phone.isNullOrBlank()) "Paczka nie zawiera numeru organizatora." else "SMS do organizatora: $phone (wysyłasz go sam w aplikacji SMS).",
            color = Hub.TextDim, fontSize = 11.sp, modifier = Modifier.padding(top = 8.dp)
        )
    }
}

@Composable
private fun DayView(day: OpordDay, source: StoredPackage, state: HubState, vm: HubViewModel) {
    val mesh: MeshConfig? = source.pkg.mesh ?: state.active?.pkg?.mesh
    val voices = (source.pkg.voiceChannels.ifEmpty { state.active?.pkg?.voiceChannels.orEmpty() })
    HubPanel {
        Text("TABELA CZĘSTOTLIWOŚCI (Radio)", color = Hub.TextDim, fontSize = 13.sp, letterSpacing = 1.sp, modifier = Modifier.padding(bottom = 4.dp))
        for (v in voices) {
            val head = listOf(v.band, v.role).filter { it.isNotBlank() }.joinToString(" ")
            FrameLine(listOf(head.ifBlank { "Radio" } + ": " + v.frequency, v.tone).filter { it.isNotBlank() }.joinToString(" · "))
        }
        if (day.primaryFreq.isNotBlank()) FrameLine("Główna: ${day.primaryFreq}" + day.primaryChannel.takeIf { it.isNotBlank() }?.let { " · $it" }.orEmpty())
        if (day.backupFreq.isNotBlank()) FrameLine("Zapasowa: ${day.backupFreq}" + day.alternateChannel.takeIf { it.isNotBlank() }?.let { " · $it" }.orEmpty())
        if (day.hqFreq.isNotBlank()) FrameLine("HQ: ${day.hqFreq}" + day.hqChannel.takeIf { it.isNotBlank() }?.let { " · $it" }.orEmpty())
        if (mesh != null) {
            FrameLine(listOfNotNull("LoRa: " + (mesh.region ?: "?"), mesh.modemPreset, "MESH", mesh.channelName.ifBlank { null }).joinToString(" · "))
        }
        if (day.customChannelsTable.isNotBlank()) Text(day.customChannelsTable, color = Hub.Text, fontFamily = Mono, fontSize = 13.sp, modifier = Modifier.padding(top = 6.dp))
        if (day.commsSummary.isNotBlank()) Text(day.commsSummary, color = Hub.TextDim, fontSize = 12.sp, modifier = Modifier.padding(top = 6.dp))
        if (voices.isEmpty() && mesh == null && day.primaryFreq.isBlank()) Text("Brak częstotliwości w rozkazie.", color = Hub.Muted, fontSize = 13.sp)
    }
    Spacer(Modifier.height(12.dp))
    val header = listOf(day.dateIso, day.locationName).filter { it.isNotBlank() }.joinToString(" · ")
    if (header.isNotBlank()) Text(header, color = Hub.TextDim, fontSize = 13.sp)
    if (day.mission.isNotBlank()) {
        Text("Zadanie:", color = Hub.Text, fontSize = 18.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp))
        Text(day.mission, color = Hub.Text, fontSize = 16.sp, lineHeight = 22.sp)
    }
    if (day.schedule.isNotBlank()) {
        Text("Harmonogram:", color = Hub.Text, fontSize = 15.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 10.dp))
        Text(day.schedule, color = Hub.Text, fontSize = 14.sp, lineHeight = 20.sp)
    }
    if (day.metoc.isNotEmpty()) {
        Text(day.metoc.joinToString("  ·  ") { "${it.first}: ${it.second}" }, color = Hub.TextDim, fontSize = 12.sp, modifier = Modifier.padding(top = 10.dp))
    }
    Spacer(Modifier.height(10.dp))
    for (section in day.sections) {
        var open by remember(section.title) { mutableStateOf(false) }
        HubPanel(modifier = Modifier.padding(vertical = 4.dp), onClick = { open = !open }) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(section.title, color = Hub.Text, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                Icon(if (open) Icons.Filled.ExpandLess else Icons.Filled.ExpandMore, contentDescription = null, tint = Hub.TextDim)
            }
            if (open) {
                for ((label, value) in section.items) {
                    Text(label, color = Hub.Cyan, fontSize = 12.sp, modifier = Modifier.padding(top = 8.dp))
                    Text(value, color = Hub.Text, fontSize = 14.sp, lineHeight = 20.sp)
                }
            }
        }
    }
    if (day.codewords.isNotEmpty() || day.challenge.isNotBlank() || day.sarneg.isNotBlank()) {
        HubPanel(modifier = Modifier.padding(vertical = 4.dp)) {
            Text("Hasła i kody", color = Hub.Text, fontWeight = FontWeight.SemiBold)
            for (c in day.codewords) {
                Text(
                    listOf(c.word, c.letter, c.digit).filter { it.isNotBlank() }.joinToString(" · "),
                    color = Hub.Text, fontFamily = Mono, fontSize = 14.sp, modifier = Modifier.padding(top = 6.dp)
                )
                if (c.duress.isNotBlank()) Text("Słowo przymusu: ${c.duress}", color = Hub.Red, fontSize = 13.sp)
            }
            if (day.challenge.isNotBlank()) {
                Text("Hasło / odzew", color = Hub.Cyan, fontSize = 12.sp, modifier = Modifier.padding(top = 8.dp))
                Text(day.challenge, color = Hub.Text, fontSize = 14.sp)
            }
            if (day.sarneg.isNotBlank()) {
                Text("Klucz SARNEG", color = Hub.Cyan, fontSize = 12.sp, modifier = Modifier.padding(top = 8.dp))
                Text(day.sarneg, color = Hub.Text, fontFamily = Mono, fontSize = 14.sp)
            }
        }
    }
    if (day.roster.isNotEmpty()) {
        HubPanel(modifier = Modifier.padding(vertical = 4.dp)) {
            Text("Skład drużyny", color = Hub.Text, fontWeight = FontWeight.SemiBold)
            for (r in day.roster) {
                Text(r.callsign, color = Hub.Cyan, fontSize = 14.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp))
                Text(listOf(r.role, r.gear).filter { it.isNotBlank() }.joinToString(" · "), color = Hub.TextDim, fontSize = 13.sp)
            }
        }
    }
    if (source.pkg.opordHtmlEntry != null) {
        Spacer(Modifier.height(8.dp))
        SecondaryButton("Pełny rozkaz (dokument)", Icons.Filled.Description, Modifier.fillMaxWidth()) { vm.showDocument(true) }
    }
}

@Composable
private fun MapView(source: StoredPackage, days: List<OpordDay>, vm: HubViewModel) {
    var images by remember(source.id) { mutableStateOf<List<ImageBitmap>?>(null) }
    LaunchedEffect(source.id) {
        images = withContext(Dispatchers.IO) {
            val html = vm.readOpordHtml() ?: return@withContext emptyList()
            OpordParser.extractImages(html).mapNotNull { decodeScaled(it, 2048) }
        }
    }
    val list = images
    when {
        list == null -> Text("Wczytywanie mapy…", color = Hub.TextDim)
        list.isEmpty() -> HubPanel {
            Text("Brak mapy w rozkazie", color = Hub.Text, fontWeight = FontWeight.SemiBold)
            Text("Mapa pojawi się, gdy organizator dołączy Załącznik 3 w Builderze. Warstwy KMZ/KML z paczki możesz wysłać do ATAK poniżej.", color = Hub.TextDim, fontSize = 13.sp)
        }
        else -> for (img in list) ZoomableImage(img)
    }
    val day = days.firstOrNull { it.lat != null && it.lon != null }
    if (day != null) {
        val lat = day.lat ?: 0.0
        val lon = day.lon ?: 0.0
        Spacer(Modifier.height(10.dp))
        HubPanel {
            Text("Rejon działań", color = Hub.TextDim, fontSize = 12.sp)
            Text(day.locationName.ifBlank { "Punkt zbiórki" }, color = Hub.Text, fontWeight = FontWeight.SemiBold)
            if (Mgrs.isSupported(lat, lon)) Text(Mgrs.format(lat, lon), color = Hub.Cyan, fontFamily = Mono)
            Text(Mgrs.formatLatLon(lat, lon), color = Hub.TextDim, fontFamily = Mono, fontSize = 12.sp)
            Spacer(Modifier.height(6.dp))
            SecondaryButton("Pokaż w mapach", Icons.Filled.Map) { vm.startIntent(Intents.geo(lat, lon, day.locationName.ifBlank { "Rejon" })) }
        }
    }
    val overlays = source.pkg.overlays
    if (overlays.isNotEmpty()) {
        Spacer(Modifier.height(10.dp))
        HubPanel {
            Text("Warstwy mapy w paczce", color = Hub.Text, fontWeight = FontWeight.SemiBold)
            for (o in overlays) {
                Row(Modifier.fillMaxWidth().padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(o.substringAfterLast('/'), color = Hub.Text, fontSize = 14.sp, modifier = Modifier.weight(1f))
                    SecondaryButton("Do ATAK", Icons.Filled.CloudUpload) { vm.injectAtakFile(o, source.id) }
                }
            }
        }
    }
}

@Composable
private fun ZoomableImage(image: ImageBitmap) {
    var scale by remember { mutableFloatStateOf(1f) }
    var offset by remember { mutableStateOf(Offset.Zero) }
    Image(
        bitmap = image,
        contentDescription = "Mapa",
        contentScale = ContentScale.FillWidth,
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(max = 520.dp)
            .padding(vertical = 4.dp)
            .clip(RoundedCornerShape(8.dp))
            .border(1.dp, Hub.BorderStrong, RoundedCornerShape(8.dp))
            .clipToBounds()
            .pointerInput(Unit) {
                detectTransformGestures { _, pan, zoom, _ ->
                    scale = (scale * zoom).coerceIn(1f, 6f)
                    offset = if (scale == 1f) Offset.Zero else offset + pan
                }
            }
            .graphicsLayer {
                scaleX = scale
                scaleY = scale
                translationX = offset.x
                translationY = offset.y
            }
    )
}

private fun decodeScaled(bytes: ByteArray, maxSide: Int): ImageBitmap? {
    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
    if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
    var sample = 1
    while (maxOf(bounds.outWidth, bounds.outHeight) / sample > maxSide) sample *= 2
    val opts = BitmapFactory.Options().apply { inSampleSize = sample }
    return BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)?.asImageBitmap()
}
