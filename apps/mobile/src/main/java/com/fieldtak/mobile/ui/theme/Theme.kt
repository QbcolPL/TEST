package com.fieldtak.mobile.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val AbyssalSlate = Color(0xFF060E12)
val TacticalCard = Color(0xFF0A181E)
val InputTrench = Color(0xFF071115)
val RadarCyan = Color(0xFF1FE0C5)
val MutedGrid = Color(0xFF163844)
val MilSimGreen = Color(0xFF2ECC71)
val CautionAmber = Color(0xFFFAD02C)
val DangerCrimson = Color(0xFFEB4034)
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
