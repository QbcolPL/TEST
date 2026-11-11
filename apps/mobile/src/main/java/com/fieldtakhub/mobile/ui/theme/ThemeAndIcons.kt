package com.fieldtakhub.mobile.ui.theme

import android.graphics.Bitmap
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.google.zxing.BarcodeFormat
import com.google.zxing.qrcode.QRCodeWriter

val AbyssalSlate = Color(0xFF060E12)
val TacticalCard = Color(0xFF0A181E)
val InputTrench = Color(0xFF071115)
val RadarCyan = Color(0xFF1FE0C5)
val MutedGrid = Color(0xFF163844)
val MilSimGreen = Color(0xFF2ECC71)
val CautionAmber = Color(0xFFFAD02C)
val DangerCrimson = Color(0xFFEB4034)
val SosCardBg = Color(0xFF2A0D12)
val SosButtonBg = Color(0xFFC92A2A)
val TextWhite = Color(0xFFFFFFFF)
val TextMuted = Color(0xFF8FA8B2)

private val DarkTacticalColorScheme = darkColorScheme(
    primary = RadarCyan,
    onPrimary = AbyssalSlate,
    secondary = MilSimGreen,
    background = AbyssalSlate,
    surface = TacticalCard,
    onSurface = TextWhite,
    onBackground = TextWhite,
    error = DangerCrimson
)

@Composable
fun FieldTakHubTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = DarkTacticalColorScheme,
        content = content
    )
}

enum class TacticalTab(val label: String) {
    SCANNER("SKANER"),
    PACKAGES("PACZKI"),
    OPORD("OPORD"),
    CHEATSHEET("ŚCIĄGA")
}

enum class TacticalIconType {
    GPS_SATELLITE,
    CAMERA_QR,
    BLUETOOTH_LORA,
    FILES_CRATE,
    APK_SHIELD,
    GALLERY_QR,
    FOLDER_OPEN,
    WIFI_HOTSPOT,
    INJECT_ARROWS,
    SOS_BEACON
}

@Composable
fun TacticalTopBar(
    isOnboardingOpen: Boolean,
    onToggleOnboarding: () -> Unit
) {
    Surface(
        color = TacticalCard,
        shape = RoundedCornerShape(10.dp),
        border = BorderStroke(1.5.dp, RadarCyan),
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 10.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Canvas(modifier = Modifier.size(30.dp)) {
                    val stroke = 2.dp.toPx()
                    drawCircle(color = RadarCyan, radius = size.minDimension / 2.2f, style = Stroke(stroke))
                    drawCircle(color = RadarCyan, radius = size.minDimension / 4.2f, style = Stroke(stroke * 0.8f))
                    drawLine(RadarCyan, Offset(size.width / 2, 0f), Offset(size.width / 2, size.height), stroke * 0.7f)
                    drawLine(RadarCyan, Offset(0f, size.height / 2), Offset(size.width, size.height / 2), stroke * 0.7f)
                }
                Spacer(modifier = Modifier.width(10.dp))
                Column {
                    Text(
                        text = "FIELD TAK HUB",
                        color = TextWhite,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Text(
                        text = "connect · deploy · operate",
                        color = RadarCyan,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }

            // Przycisk [<] identyczny jak na wizualizacji i w Builderze Windows
            Surface(
                color = if (isOnboardingOpen) RadarCyan else InputTrench,
                shape = RoundedCornerShape(4.dp),
                border = BorderStroke(1.dp, RadarCyan),
                modifier = Modifier
                    .size(30.dp)
                    .clickable { onToggleOnboarding() }
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(
                        text = "<",
                        color = if (isOnboardingOpen) AbyssalSlate else RadarCyan,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }
        }
    }
}

@Composable
fun TacticalBottomBar(
    currentTab: TacticalTab,
    isOnboardingOpen: Boolean,
    onSelectTab: (TacticalTab) -> Unit
) {
    Surface(
        color = TacticalCard,
        border = BorderStroke(1.dp, MutedGrid),
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 8.dp, horizontal = 6.dp),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            TacticalTab.values().forEach { tab ->
                val selected = !isOnboardingOpen && (tab == currentTab)
                val tint = if (selected) RadarCyan else TextMuted
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    modifier = Modifier
                        .weight(1f)
                        .clickable { onSelectTab(tab) }
                        .padding(vertical = 4.dp)
                ) {
                    TacticalTabVectorIcon(tab = tab, color = tint)
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = tab.label,
                        color = tint,
                        fontSize = 10.sp,
                        fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }
        }
    }
}

@Composable
private fun TacticalTabVectorIcon(tab: TacticalTab, color: Color) {
    Canvas(modifier = Modifier.size(20.dp)) {
        val s = 1.8.dp.toPx()
        val w = size.width
        val h = size.height
        when (tab) {
            TacticalTab.SCANNER -> {
                val len = w * 0.28f
                drawLine(color, Offset(0f, 0f), Offset(len, 0f), s)
                drawLine(color, Offset(0f, 0f), Offset(0f, len), s)
                drawLine(color, Offset(w, 0f), Offset(w - len, 0f), s)
                drawLine(color, Offset(w, 0f), Offset(w, len), s)
                drawLine(color, Offset(0f, h), Offset(len, h), s)
                drawLine(color, Offset(0f, h), Offset(0f, h - len), s)
                drawLine(color, Offset(w, h), Offset(w - len, h), s)
                drawLine(color, Offset(w, h), Offset(w, h - len), s)
                drawRect(color, Offset(w * 0.26f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.8f))
                drawRect(color, Offset(w * 0.56f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.8f))
                drawRect(color, Offset(w * 0.26f, h * 0.56f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.8f))
            }
            TacticalTab.PACKAGES -> {
                val p = Path().apply {
                    moveTo(w * 0.08f, h * 0.24f)
                    lineTo(w * 0.38f, h * 0.24f)
                    lineTo(w * 0.48f, h * 0.35f)
                    lineTo(w * 0.92f, h * 0.35f)
                    lineTo(w * 0.92f, h * 0.84f)
                    lineTo(w * 0.08f, h * 0.84f)
                    close()
                }
                drawPath(p, color = color, style = Stroke(s))
            }
            TacticalTab.OPORD -> {
                drawRect(color = color, topLeft = Offset(w * 0.15f, h * 0.10f), size = Size(w * 0.70f, h * 0.80f), style = Stroke(s))
                drawLine(color, Offset(w * 0.30f, h * 0.35f), Offset(w * 0.70f, h * 0.35f), s)
                drawLine(color, Offset(w * 0.30f, h * 0.55f), Offset(w * 0.70f, h * 0.55f), s)
                drawLine(color, Offset(w * 0.30f, h * 0.75f), Offset(w * 0.55f, h * 0.75f), s)
            }
            TacticalTab.CHEATSHEET -> {
                drawCircle(color = color, radius = w * 0.32f, style = Stroke(s))
                drawCircle(color = color, radius = w * 0.12f)
                drawLine(color, Offset(w / 2f, 0f), Offset(w / 2f, h * 0.20f), s)
                drawLine(color, Offset(w / 2f, h * 0.80f), Offset(w / 2f, h), s)
                drawLine(color, Offset(0f, h / 2f), Offset(w * 0.20f, h / 2f), s)
                drawLine(color, Offset(w * 0.80f, h / 2f), Offset(w, h / 2f), s)
            }
        }
    }
}

@Composable
fun TacticalIconBadge(
    type: TacticalIconType,
    tint: Color = RadarCyan,
    boxSize: Dp = 36.dp,
    iconSize: Dp = 20.dp
) {
    Surface(
        color = InputTrench,
        shape = RoundedCornerShape(6.dp),
        border = BorderStroke(1.dp, MutedGrid),
        modifier = Modifier.size(boxSize)
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            TacticalVectorIcon(type = type, tint = tint, size = iconSize)
        }
    }
}

@Composable
fun TacticalVectorIcon(
    type: TacticalIconType,
    tint: Color = RadarCyan,
    size: Dp = 20.dp
) {
    Canvas(modifier = Modifier.size(size)) {
        val w = this.size.width
        val h = this.size.height
        val s = 1.8.dp.toPx()

        when (type) {
            TacticalIconType.GPS_SATELLITE -> {
                drawLine(tint, Offset(w * 0.15f, h * 0.15f), Offset(w * 0.85f, h * 0.85f), s)
                drawRect(tint, Offset(w * 0.36f, h * 0.36f), Size(w * 0.28f, h * 0.28f), style = Stroke(s))
                drawLine(tint, Offset(w * 0.12f, h * 0.38f), Offset(w * 0.38f, h * 0.12f), s)
                drawLine(tint, Offset(w * 0.62f, h * 0.88f), Offset(w * 0.88f, h * 0.62f), s)
                drawArc(MilSimGreen, 100f, 70f, false, Offset(w * 0.08f, h * 0.45f), Size(w * 0.45f, h * 0.45f), style = Stroke(s * 0.85f))
            }
            TacticalIconType.CAMERA_QR -> {
                drawRoundRect(tint, Offset(w * 0.08f, h * 0.24f), Size(w * 0.84f, h * 0.62f), CornerRadius(3.dp.toPx()), style = Stroke(s))
                drawLine(tint, Offset(w * 0.32f, h * 0.14f), Offset(w * 0.68f, h * 0.14f), s * 1.2f)
                drawCircle(tint, w * 0.18f, Offset(w * 0.5f, h * 0.55f), style = Stroke(s))
                drawCircle(MilSimGreen, w * 0.05f, Offset(w * 0.5f, h * 0.55f))
            }
            TacticalIconType.BLUETOOTH_LORA -> {
                val btPath = Path().apply {
                    moveTo(w * 0.22f, h * 0.28f)
                    lineTo(w * 0.52f, h * 0.72f)
                    lineTo(w * 0.36f, h * 0.88f)
                    lineTo(w * 0.36f, h * 0.12f)
                    lineTo(w * 0.52f, h * 0.28f)
                    lineTo(w * 0.22f, h * 0.72f)
                }
                drawPath(btPath, color = tint, style = Stroke(s))
                drawArc(MilSimGreen, -45f, 90f, false, Offset(w * 0.42f, h * 0.26f), Size(w * 0.32f, h * 0.48f), style = Stroke(s * 0.9f))
                drawArc(tint, -45f, 90f, false, Offset(w * 0.54f, h * 0.14f), Size(w * 0.40f, h * 0.72f), style = Stroke(s * 0.9f))
            }
            TacticalIconType.FILES_CRATE -> {
                drawRoundRect(tint, Offset(w * 0.10f, h * 0.22f), Size(w * 0.80f, h * 0.64f), CornerRadius(2.dp.toPx()), style = Stroke(s))
                drawLine(tint, Offset(w * 0.10f, h * 0.42f), Offset(w * 0.90f, h * 0.42f), s)
                drawRect(MilSimGreen, Offset(w * 0.40f, h * 0.36f), Size(w * 0.20f, h * 0.14f))
            }
            TacticalIconType.APK_SHIELD -> {
                drawCircle(tint, w * 0.42f, Offset(w * 0.50f, h * 0.50f), style = Stroke(s))
                drawLine(MilSimGreen, Offset(w * 0.50f, h * 0.26f), Offset(w * 0.50f, h * 0.70f), s)
                drawLine(MilSimGreen, Offset(w * 0.34f, h * 0.54f), Offset(w * 0.50f, h * 0.70f), s)
                drawLine(MilSimGreen, Offset(w * 0.66f, h * 0.54f), Offset(w * 0.50f, h * 0.70f), s)
            }
            TacticalIconType.GALLERY_QR -> {
                drawRoundRect(tint, Offset(w * 0.10f, h * 0.14f), Size(w * 0.80f, h * 0.72f), CornerRadius(2.dp.toPx()), style = Stroke(s))
                drawRect(tint, Offset(w * 0.22f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(tint, Offset(w * 0.60f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(MilSimGreen, Offset(w * 0.22f, h * 0.54f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawCircle(MilSimGreen, w * 0.08f, Offset(w * 0.69f, h * 0.63f))
            }
            TacticalIconType.FOLDER_OPEN -> {
                val p = Path().apply {
                    moveTo(w * 0.08f, h * 0.24f)
                    lineTo(w * 0.38f, h * 0.24f)
                    lineTo(w * 0.48f, h * 0.34f)
                    lineTo(w * 0.92f, h * 0.34f)
                    lineTo(w * 0.92f, h * 0.82f)
                    lineTo(w * 0.08f, h * 0.82f)
                    close()
                }
                drawPath(p, color = tint, style = Stroke(s))
            }
            TacticalIconType.WIFI_HOTSPOT, TacticalIconType.SOS_BEACON -> {
                drawCircle(tint, w * 0.10f, Offset(w * 0.50f, h * 0.56f))
                drawLine(tint, Offset(w * 0.50f, h * 0.62f), Offset(w * 0.50f, h * 0.90f), s)
                drawArc(tint, 210f, 120f, false, Offset(w * 0.22f, h * 0.24f), Size(w * 0.56f, h * 0.56f), style = Stroke(s))
                drawArc(tint, 205f, 130f, false, Offset(w * 0.08f, h * 0.10f), Size(w * 0.84f, h * 0.80f), style = Stroke(s * 0.85f))
            }
            TacticalIconType.INJECT_ARROWS -> {
                drawLine(tint, Offset(w * 0.30f, h * 0.15f), Offset(w * 0.30f, h * 0.85f), s * 1.1f)
                drawLine(tint, Offset(w * 0.14f, h * 0.65f), Offset(w * 0.30f, h * 0.85f), s * 1.1f)
                drawLine(tint, Offset(w * 0.46f, h * 0.65f), Offset(w * 0.30f, h * 0.85f), s * 1.1f)

                drawLine(tint, Offset(w * 0.70f, h * 0.85f), Offset(w * 0.70f, h * 0.15f), s * 1.1f)
                drawLine(tint, Offset(w * 0.54f, h * 0.35f), Offset(w * 0.70f, h * 0.15f), s * 1.1f)
                drawLine(tint, Offset(w * 0.86f, h * 0.35f), Offset(w * 0.70f, h * 0.15f), s * 1.1f)
            }
        }
    }
}

@Composable
fun TacticalQrReticleOverlay(modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val w = size.width
        val h = size.height
        val padX = w * 0.10f
        val padY = h * 0.10f
        val left = padX
        val right = w - padX
        val top = padY
        val bottom = h - padY
        val cornerLen = minOf(w, h) * 0.18f
        val stroke = 3.5.dp.toPx()

        drawLine(RadarCyan, Offset(left, top), Offset(left + cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(left, top), Offset(left, top + cornerLen), stroke)
        drawLine(RadarCyan, Offset(right, top), Offset(right - cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(right, top), Offset(right, top + cornerLen), stroke)
        drawLine(RadarCyan, Offset(left, bottom), Offset(left + cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(left, bottom), Offset(left, bottom - cornerLen), stroke)
        drawLine(RadarCyan, Offset(right, bottom), Offset(right - cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(right, bottom), Offset(right, bottom - cornerLen), stroke)

        drawLine(
            color = RadarCyan,
            start = Offset(left * 0.7f, h * 0.5f),
            end = Offset(right + left * 0.3f, h * 0.5f),
            strokeWidth = 2.2.dp.toPx()
        )
    }
}

fun generateQrBitmap(content: String, sizePx: Int = 512): Bitmap? {
    return runCatching {
        val matrix = QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, sizePx, sizePx)
        val bmp = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888)
        for (x in 0 until sizePx) {
            for (y in 0 until sizePx) {
                bmp.setPixel(x, y, if (matrix.get(x, y)) android.graphics.Color.BLACK else android.graphics.Color.WHITE)
            }
        }
        bmp
    }.getOrNull()
}