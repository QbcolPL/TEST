package com.fieldtak.mobile.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.fieldtak.mobile.ui.theme.*

enum class TacticalIconType {
    GPS_SATELLITE,
    CAMERA_QR,
    BLUETOOTH_LORA,
    FILES_CRATE,
    APK_SHIELD,
    GALLERY_QR,
    FOLDER_OPEN,
    WIFI_HOTSPOT,
    INJECT_ARROWS
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
                // Korpus satelity i panele sloneczne (styl taktyczny)
                drawLine(tint, Offset(w * 0.15f, h * 0.15f), Offset(w * 0.85f, h * 0.85f), s)
                drawRect(
                    color = tint,
                    topLeft = Offset(w * 0.36f, h * 0.36f),
                    size = Size(w * 0.28f, h * 0.28f),
                    style = Stroke(s)
                )
                drawLine(tint, Offset(w * 0.12f, h * 0.38f), Offset(w * 0.38f, h * 0.12f), s)
                drawLine(tint, Offset(w * 0.62f, h * 0.88f), Offset(w * 0.88f, h * 0.62f), s)
                // Fale namierzania MGRS w lewym dolnym rogu
                drawArc(
                    color = MilSimGreen,
                    startAngle = 100f,
                    sweepAngle = 70f,
                    useCenter = false,
                    topLeft = Offset(w * 0.08f, h * 0.45f),
                    size = Size(w * 0.45f, h * 0.45f),
                    style = Stroke(s * 0.85f)
                )
            }

            TacticalIconType.CAMERA_QR -> {
                // Obudowa kamery z celownikiem optycznym
                drawRoundRect(
                    color = tint,
                    topLeft = Offset(w * 0.08f, h * 0.24f),
                    size = Size(w * 0.84f, h * 0.62f),
                    cornerRadius = CornerRadius(3.dp.toPx()),
                    style = Stroke(s)
                )
                drawLine(tint, Offset(w * 0.32f, h * 0.14f), Offset(w * 0.68f, h * 0.14f), s * 1.2f)
                drawCircle(
                    color = tint,
                    radius = w * 0.18f,
                    center = Offset(w * 0.5f, h * 0.55f),
                    style = Stroke(s)
                )
                drawCircle(color = MilSimGreen, radius = w * 0.05f, center = Offset(w * 0.5f, h * 0.55f))
            }

            TacticalIconType.BLUETOOTH_LORA -> {
                // Runa Bluetooth po lewej + fale radiowe LoRa po prawej
                val btPath = Path().apply {
                    moveTo(w * 0.22f, h * 0.28f)
                    lineTo(w * 0.52f, h * 0.72f)
                    lineTo(w * 0.36f, h * 0.88f)
                    lineTo(w * 0.36f, h * 0.12f)
                    lineTo(w * 0.52f, h * 0.28f)
                    lineTo(w * 0.22f, h * 0.72f)
                }
                drawPath(btPath, color = tint, style = Stroke(s))
                drawArc(
                    color = MilSimGreen,
                    startAngle = -45f,
                    sweepAngle = 90f,
                    useCenter = false,
                    topLeft = Offset(w * 0.42f, h * 0.26f),
                    size = Size(w * 0.32f, h * 0.48f),
                    style = Stroke(s * 0.9f)
                )
                drawArc(
                    color = tint,
                    startAngle = -45f,
                    sweepAngle = 90f,
                    useCenter = false,
                    topLeft = Offset(w * 0.54f, h * 0.14f),
                    size = Size(w * 0.40f, h * 0.72f),
                    style = Stroke(s * 0.9f)
                )
            }

            TacticalIconType.FILES_CRATE -> {
                // Skrzynia taktyczna .ftak
                drawRoundRect(
                    color = tint,
                    topLeft = Offset(w * 0.10f, h * 0.22f),
                    size = Size(w * 0.80f, h * 0.64f),
                    cornerRadius = CornerRadius(2.dp.toPx()),
                    style = Stroke(s)
                )
                drawLine(tint, Offset(w * 0.10f, h * 0.42f), Offset(w * 0.90f, h * 0.42f), s)
                drawRect(
                    color = MilSimGreen,
                    topLeft = Offset(w * 0.40f, h * 0.36f),
                    size = Size(w * 0.20f, h * 0.14f)
                )
            }

            TacticalIconType.APK_SHIELD -> {
                // Tarcza instalatora pakietow APK
                val shield = Path().apply {
                    moveTo(w * 0.50f, h * 0.08f)
                    lineTo(w * 0.88f, h * 0.24f)
                    lineTo(w * 0.82f, h * 0.64f)
                    lineTo(w * 0.50f, h * 0.92f)
                    lineTo(w * 0.18f, h * 0.64f)
                    lineTo(w * 0.12f, h * 0.24f)
                    close()
                }
                drawPath(shield, color = tint, style = Stroke(s))
                drawLine(MilSimGreen, Offset(w * 0.50f, h * 0.30f), Offset(w * 0.50f, h * 0.66f), s)
                drawLine(MilSimGreen, Offset(w * 0.35f, h * 0.52f), Offset(w * 0.50f, h * 0.68f), s)
                drawLine(MilSimGreen, Offset(w * 0.65f, h * 0.52f), Offset(w * 0.50f, h * 0.68f), s)
            }

            TacticalIconType.GALLERY_QR -> {
                // Ramka obrazu z znacznikami QR w naroznikach
                drawRoundRect(
                    color = tint,
                    topLeft = Offset(w * 0.10f, h * 0.14f),
                    size = Size(w * 0.80f, h * 0.72f),
                    cornerRadius = CornerRadius(2.dp.toPx()),
                    style = Stroke(s)
                )
                drawRect(color = tint, topLeft = Offset(w * 0.22f, h * 0.26f), size = Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(color = tint, topLeft = Offset(w * 0.60f, h * 0.26f), size = Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(color = MilSimGreen, topLeft = Offset(w * 0.22f, h * 0.54f), size = Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawCircle(color = MilSimGreen, radius = w * 0.08f, center = Offset(w * 0.69f, h * 0.63f))
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

            TacticalIconType.WIFI_HOTSPOT -> {
                drawCircle(color = tint, radius = w * 0.10f, center = Offset(w * 0.50f, h * 0.58f))
                drawLine(tint, Offset(w * 0.50f, h * 0.64f), Offset(w * 0.50f, h * 0.90f), s)
                drawArc(
                    color = tint,
                    startAngle = 210f,
                    sweepAngle = 120f,
                    useCenter = false,
                    topLeft = Offset(w * 0.22f, h * 0.26f),
                    size = Size(w * 0.56f, h * 0.56f),
                    style = Stroke(s)
                )
                drawArc(
                    color = tint,
                    startAngle = 205f,
                    sweepAngle = 130f,
                    useCenter = false,
                    topLeft = Offset(w * 0.08f, h * 0.10f),
                    size = Size(w * 0.84f, h * 0.80f),
                    style = Stroke(s * 0.85f)
                )
            }

            TacticalIconType.INJECT_ARROWS -> {
                // Strzalka w dol (po lewej) i w gore (po prawej) - ↓↑
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

/**
 * Taktyczne narozniki celownika QR + pozioma linia lasera jak na wizualizacji
 */
@Composable
fun TacticalQrReticleOverlay(modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val w = size.width
        val h = size.height
        val padX = w * 0.12f
        val padY = h * 0.12f
        val left = padX
        val right = w - padX
        val top = padY
        val bottom = h - padY
        val cornerLen = minOf(w, h) * 0.16f
        val stroke = 3.2.dp.toPx()

        // 4 zielono-cyjanowe narozniki celownika
        // Lewy-Gorny
        drawLine(RadarCyan, Offset(left, top), Offset(left + cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(left, top), Offset(left, top + cornerLen), stroke)
        // Prawy-Gorny
        drawLine(RadarCyan, Offset(right, top), Offset(right - cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(right, top), Offset(right, top + cornerLen), stroke)
        // Lewy-Dolny
        drawLine(RadarCyan, Offset(left, bottom), Offset(left + cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(left, bottom), Offset(left, bottom - cornerLen), stroke)
        // Prawy-Dolny
        drawLine(RadarCyan, Offset(right, bottom), Offset(right - cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(right, bottom), Offset(right, bottom - cornerLen), stroke)

        // Pozioma linia lasera skanujacego w polowie wysokosci
        drawLine(
            color = RadarCyan,
            start = Offset(left * 0.75f, h * 0.5f),
            end = Offset(right + left * 0.25f, h * 0.5f),
            strokeWidth = 2.2.dp.toPx()
        )
    }
}