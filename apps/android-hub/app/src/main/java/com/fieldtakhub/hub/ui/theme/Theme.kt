package com.fieldtakhub.hub.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/** Colours taken from the Field TAK Hub mockup: near-black teal background, cyan accent, green status. */
object Hub {
    val Background = Color(0xFF061012)
    val BackgroundTop = Color(0xFF0A1A1D)
    val Grid = Color(0x0F2EE6D6)
    val Panel = Color(0xFF0B191C)
    val PanelHigh = Color(0xFF102428)
    val Border = Color(0xFF1D3B3F)
    val BorderStrong = Color(0xFF2B5A5E)
    val Cyan = Color(0xFF2EE6D6)
    val CyanSoft = Color(0x332EE6D6)
    val CyanDeep = Color(0xFF13A89C)
    val Text = Color(0xFFE4F2F1)
    val TextDim = Color(0xFF93B2AF)
    val Muted = Color(0xFF5D7A78)
    val Green = Color(0xFF46D97A)
    val Amber = Color(0xFFFFB238)
    val Red = Color(0xFFFF3B3B)
    val RedDeep = Color(0xFF3A0A0D)
    val RedPanel = Color(0xFF240709)
}

private val scheme = darkColorScheme(
    primary = Hub.Cyan,
    onPrimary = Hub.Background,
    secondary = Hub.CyanDeep,
    onSecondary = Hub.Text,
    background = Hub.Background,
    onBackground = Hub.Text,
    surface = Hub.Panel,
    onSurface = Hub.Text,
    surfaceVariant = Hub.PanelHigh,
    onSurfaceVariant = Hub.TextDim,
    outline = Hub.Border,
    error = Hub.Red,
    onError = Color.White
)

private val typography = Typography(
    headlineSmall = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.Bold, fontSize = 22.sp, letterSpacing = 0.5.sp),
    titleLarge = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.Bold, fontSize = 20.sp, letterSpacing = 1.sp),
    titleMedium = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.SemiBold, fontSize = 17.sp),
    bodyLarge = TextStyle(fontFamily = FontFamily.SansSerif, fontSize = 16.sp, lineHeight = 22.sp),
    bodyMedium = TextStyle(fontFamily = FontFamily.SansSerif, fontSize = 14.sp, lineHeight = 20.sp),
    bodySmall = TextStyle(fontFamily = FontFamily.SansSerif, fontSize = 12.sp, lineHeight = 16.sp),
    labelLarge = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.Bold, fontSize = 15.sp, letterSpacing = 1.sp),
    labelMedium = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.Medium, fontSize = 12.sp, letterSpacing = 1.2.sp),
    labelSmall = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.Medium, fontSize = 11.sp, letterSpacing = 0.8.sp)
)

val Mono = FontFamily.Monospace

@Composable
fun HubTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = scheme, typography = typography, content = content)
}
