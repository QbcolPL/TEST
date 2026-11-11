package com.fieldtakhub.mobile.ui.screens

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.net.Uri
import android.os.Bundle
import android.webkit.WebView
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.fieldtakhub.mobile.data.*
import com.fieldtakhub.mobile.ui.theme.*
import java.io.File

@SuppressLint("MissingPermission")
@Composable
fun OpordAndCheatSheetScreen(
    activePackage: FtakPackageSummary?,
    initialFocusOnCheatSheet: Boolean
) {
    val context = LocalContext.current
    val scrollState = rememberScrollState()

    var currentMgrs by remember { mutableStateOf("34U CA 48120 21050") }
    var currentLatLon by remember { mutableStateOf("49.84944, 18.81267") }

    DisposableEffect(context) {
        val locMgr = context.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
        val hasPerm = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED

        val listener = object : LocationListener {
            override fun onLocationChanged(loc: Location) {
                currentMgrs = LatLonToMgrs.convert(loc.latitude, loc.longitude)
                currentLatLon = String.format("%.5f, %.5f", loc.latitude, loc.longitude)
            }
            @Deprecated("Deprecated in Java")
            override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
            override fun onProviderEnabled(provider: String) {}
            override fun onProviderDisabled(provider: String) {}
        }

        if (hasPerm && locMgr != null) {
            runCatching {
                val lastGps = locMgr.getLastKnownLocation(LocationManager.GPS_PROVIDER)
                    ?: locMgr.getLastKnownLocation(LocationManager.NETWORK_PROVIDER)
                if (lastGps != null) {
                    currentMgrs = LatLonToMgrs.convert(lastGps.latitude, lastGps.longitude)
                    currentLatLon = String.format("%.5f, %.5f", lastGps.latitude, lastGps.longitude)
                }
                locMgr.requestLocationUpdates(LocationManager.GPS_PROVIDER, 3000L, 2f, listener)
                locMgr.requestLocationUpdates(LocationManager.NETWORK_PROVIDER, 5000L, 5f, listener)
            }
        }
        onDispose {
            runCatching { locMgr?.removeUpdates(listener) }
        }
    }

    val days = activePackage?.opordDays ?: listOf(
        OpordDaySheet(
            dayTitle = "DZIEŃ 1",
            situation = "Siły przeciwnika utrzymują kluczowe skrzyżowania w sektorze.",
            metoc = "Warunki umiarkowane, wiatr zachodni 3 m/s.",
            mission5W = "Opanować i utrzymać obiekt ALFA do godz. 14:00.",
            executionTask = "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.",
            sarnegWord = "BIEGUSZOWICE",
            challengePassword = "GRZMOT / BŁYSK",
            logistics = "FOB / Respawn w kwadracie startowym."
        ),
        OpordDaySheet(
            dayTitle = "DZIEŃ 2",
            situation = "Przegrupowanie pododdziałów i obrona okrężna.",
            metoc = "Poranne zamglenie w dolinach.",
            mission5W = "Zabezpieczyć strefę lądowania LZ o godzinie H.",
            executionTask = "Rotacja czujek co 45 minut, cisza radiowa na kanale głównym.",
            sarnegWord = "STRUMIEŃ",
            challengePassword = "ZAMEK / WIEŻA",
            logistics = "Ewakuacja MEDEVAC drogą główną."
        )
    )

    var selectedDayIndex by remember { mutableStateOf(0) }
    var showMapTab by remember { mutableStateOf(false) }
    var showFullHtmlWebView by remember { mutableStateOf(false) }

    val voiceChannels = activePackage?.voiceChannels ?: listOf(
        VoiceChannelItem("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0"),
        VoiceChannelItem("Taktyczny Alfa", "VHF", "145.500 MHz", "CTCSS 88.5")
    )
    val loraLine = if (activePackage != null) {
        "LoRa: ${activePackage.loraRegion} · ${activePackage.loraPreset} · ${activePackage.loraChannelName}"
    } else {
        "LoRa: EU_868 · SHORT_FAST · MESH"
    }
    val emergencyPhone = activePackage?.emergencyPhone ?: "+48 600 000 000"
    val (operatorFull, operatorShort) = remember { FtakPackageRepository.loadOperatorCallsign(context) }

    val mgrsParts = currentMgrs.split(" ")
    val mgrsLine1 = if (mgrsParts.size >= 2) "MGRS: ${mgrsParts[0]}" else "MGRS: $currentMgrs"
    val mgrsLine2 = if (mgrsParts.size >= 4) "${mgrsParts[1]} ${mgrsParts[2]} ${mgrsParts[3]}" else currentLatLon

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .verticalScroll(scrollState)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        // 1. CZERWONY PANEL SOS MGRS ALARM (1:1 z prawym ekranem wizualizacji)
        Surface(
            color = SosCardBg,
            shape = RoundedCornerShape(8.dp),
            border = BorderStroke(1.6.dp, DangerCrimson),
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(modifier = Modifier.padding(12.dp)) {
                Text(
                    text = "SOS MGRS ALARM",
                    color = DangerCrimson,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold
                )
                Spacer(modifier = Modifier.height(8.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = mgrsLine1,
                            color = TextWhite,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = mgrsLine2,
                            color = TextWhite,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = "ORGA: $emergencyPhone",
                            color = TextMuted,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace,
                            modifier = Modifier.padding(top = 2.dp)
                        )
                    }

                    Button(
                        onClick = {
                            val smsBody = "🆘 SOS MEDEVAC / ALARM!\nOperator: $operatorFull [$operatorShort]\nStrona: ${activePackage?.factionName ?: "BLUFOR"}\nPozycja MGRS: $currentMgrs\nGPS: $currentLatLon"
                            val smsIntent = Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:${Uri.encode(emergencyPhone)}")).apply {
                                putExtra("sms_body", smsBody)
                                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                            }
                            runCatching { context.startActivity(smsIntent) }
                        },
                        colors = ButtonDefaults.buttonColors(containerColor = SosButtonBg),
                        shape = RoundedCornerShape(6.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp)
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            TacticalVectorIcon(type = TacticalIconType.SOS_BEACON, tint = TextWhite, size = 18.dp)
                            Spacer(modifier = Modifier.width(8.dp))
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Text("WYŚLIJ", color = TextWhite, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                                Text("SMS SOS", color = TextWhite, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }
            }
        }

        // 2. NAGLOWEK ROZKAZ BOJOWY (OPORD) & LACZNOSC + PRZYCISKI [ DZIEN 1 ] [ DZIEN 2 ] [ MAPA ]
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "ROZKAZ BOJOWY (OPORD) & ŁĄCZNOŚĆ",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold
                )
                if (activePackage?.opordHtmlPath != null) {
                    Text(
                        text = if (showFullHtmlWebView) "[Widok Kart]" else "[Pełny Arkusz A4]",
                        color = RadarCyan,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.clickable { showFullHtmlWebView = !showFullHtmlWebView }
                    )
                }
            }

            Text(
                text = "OPORD",
                color = TextMuted,
                fontSize = 11.sp,
                fontFamily = FontFamily.Monospace
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                days.forEachIndexed { idx, daySheet ->
                    val isSelected = !showMapTab && (selectedDayIndex == idx)
                    Surface(
                        color = if (isSelected) Color(0xFF0E2A31) else TacticalCard,
                        shape = RoundedCornerShape(4.dp),
                        border = BorderStroke(1.2.dp, if (isSelected) RadarCyan else MutedGrid),
                        modifier = Modifier
                            .weight(1f)
                            .clickable {
                                showMapTab = false
                                selectedDayIndex = idx
                            }
                    ) {
                        Box(
                            contentAlignment = Alignment.Center,
                            modifier = Modifier.padding(vertical = 8.dp, horizontal = 4.dp)
                        ) {
                            Text(
                                text = "[ ${daySheet.dayTitle} ]",
                                color = if (isSelected) RadarCyan else TextMuted,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                fontFamily = FontFamily.Monospace
                            )
                        }
                    }
                }

                Surface(
                    color = if (showMapTab) Color(0xFF0E2A31) else TacticalCard,
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, if (showMapTab) RadarCyan else MutedGrid),
                    modifier = Modifier
                        .weight(1f)
                        .clickable { showMapTab = true }
                ) {
                    Box(
                        contentAlignment = Alignment.Center,
                        modifier = Modifier.padding(vertical = 8.dp, horizontal = 4.dp)
                    ) {
                        Text(
                            text = "[ MAPA ]",
                            color = if (showMapTab) RadarCyan else TextMuted,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                    }
                }
            }
        }

        // 3. TABELA CZESTOTLIWOSCI (Radio) — 1:1 z wizualizacja
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = "TABELA CZĘSTOTLIWOŚCI (Radio)",
                    color = TextMuted,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )

                voiceChannels.forEach { ch ->
                    Text(
                        text = "${ch.band}: ${ch.freqOrChannel} · ${ch.ctcssOrTg}",
                        color = TextWhite,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace
                    )
                    Divider(color = MutedGrid, thickness = 0.8.dp)
                }

                Text(
                    text = loraLine,
                    color = TextWhite,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )
            }
        }

        // 4. ZAWARTOSC WYBRANEGO DNIA OPORD LUB ZALACZNIKA NR 3 (MAPA)
        if (showMapTab) {
            Annex3ZoomableMapCard(activePackage = activePackage)
        } else if (showFullHtmlWebView && activePackage?.opordHtmlPath != null) {
            Card(
                colors = CardDefaults.cardColors(containerColor = TacticalCard),
                shape = RoundedCornerShape(6.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(420.dp)
                    .border(1.2.dp, RadarCyan, RoundedCornerShape(6.dp))
            ) {
                AndroidView(
                    modifier = Modifier.fillMaxSize(),
                    factory = { ctx ->
                        WebView(ctx).apply {
                            settings.javaScriptEnabled = false
                            settings.builtInZoomControls = true
                            settings.displayZoomControls = false
                            loadUrl("file://${activePackage.opordHtmlPath}")
                        }
                    }
                )
            }
        } else {
            val activeDay = days.getOrElse(selectedDayIndex) { days.first() }
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    text = "Zadanie:",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = activeDay.executionTask,
                    color = TextWhite,
                    fontSize = 12.sp,
                    lineHeight = 17.sp
                )
                Divider(color = MutedGrid, thickness = 1.dp, modifier = Modifier.padding(vertical = 2.dp))

                Card(
                    colors = CardDefaults.cardColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .border(1.dp, MutedGrid, RoundedCornerShape(6.dp))
                ) {
                    Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(
                            text = "I. SYTUACJA & METOC (${activeDay.dayTitle})",
                            color = RadarCyan,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(text = activeDay.situation, color = TextWhite, fontSize = 11.sp)
                        Text(text = "METOC: ${activeDay.metoc}", color = TextMuted, fontSize = 10.sp)

                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "II. MISJA (5W)",
                            color = RadarCyan,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(text = activeDay.mission5W, color = TextWhite, fontSize = 11.sp)

                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "III.E SŁOWO-KLUCZ SARNEG & HASŁA",
                            color = RadarCyan,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = "SARNEG: ${activeDay.sarnegWord}  |  Hasło: ${activeDay.challengePassword}",
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

@Composable
private fun Annex3ZoomableMapCard(activePackage: FtakPackageSummary?) {
    val mapPath = activePackage?.annex3MapImagePath
    val bitmap = remember(mapPath) {
        if (!mapPath.isNullOrBlank() && File(mapPath).exists()) {
            BitmapFactory.decodeFile(mapPath)
        } else null
    }

    var scale by remember { mutableStateOf(1f) }
    var offsetX by remember { mutableStateOf(0f) }
    var offsetY by remember { mutableStateOf(0f) }

    Card(
        colors = CardDefaults.cardColors(containerColor = TacticalCard),
        shape = RoundedCornerShape(6.dp),
        modifier = Modifier
            .fillMaxWidth()
            .border(1.2.dp, RadarCyan, RoundedCornerShape(6.dp))
    ) {
        Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "ZAŁĄCZNIK NR 3 — MAPA TAKTYCZNA OPERACJI",
                    color = RadarCyan,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )
                if (bitmap != null) {
                    Text(
                        text = "[Reset Zoom]",
                        color = MilSimGreen,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.clickable {
                            scale = 1f
                            offsetX = 0f
                            offsetY = 0f
                        }
                    )
                }
            }

            if (bitmap != null) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(300.dp)
                        .background(InputTrench, RoundedCornerShape(4.dp))
                        .pointerInput(Unit) {
                            detectTransformGestures { _, pan, zoom, _ ->
                                scale = (scale * zoom).coerceIn(1f, 5f)
                                offsetX += pan.x
                                offsetY += pan.y
                            }
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Image(
                        bitmap = bitmap.asImageBitmap(),
                        contentDescription = "Załącznik nr 3 Mapa Taktyczna",
                        contentScale = ContentScale.Fit,
                        modifier = Modifier
                            .fillMaxSize()
                            .graphicsLayer(
                                scaleX = scale,
                                scaleY = scale,
                                translationX = offsetX,
                                translationY = offsetY
                            )
                    )
                }
                Text(
                    text = "Użyj dwóch palców, aby powiększyć i przesuwać mapę z Załącznika nr 3.",
                    color = TextMuted,
                    fontSize = 10.sp
                )
            } else {
                Text(
                    text = "Aktualna paczka .ftak nie zawiera załączonego obrazu mapy w Załączniku nr 3 (lub żadna paczka nie została jeszcze wczytana).",
                    color = TextMuted,
                    fontSize = 11.sp
                )
            }
        }
    }
}