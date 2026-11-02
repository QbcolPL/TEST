package org.fieldtak.hub.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import org.fieldtak.hub.R

val FieldBg = Color(0xFF0B1114)
val FieldPanel = Color(0xFF102127)
val FieldCyan = Color(0xFF28E0D7)
val FieldRed = Color(0xFFFF3B4E)
val FieldGood = Color(0xFF59D17D)
val FieldMuted = Color(0xFFB7C5C9)

private val FieldTakDarkColors = darkColorScheme(
    primary = FieldCyan,
    onPrimary = FieldBg,
    primaryContainer = Color(0xFF12373B),
    onPrimaryContainer = Color.White,
    secondary = FieldGood,
    onSecondary = FieldBg,
    secondaryContainer = Color(0xFF173526),
    onSecondaryContainer = Color.White,
    tertiary = Color(0xFF92E6E1),
    background = FieldBg,
    onBackground = Color.White,
    surface = FieldPanel,
    onSurface = Color.White,
    surfaceVariant = Color(0xFF183038),
    onSurfaceVariant = Color(0xFFCAD4D7),
    outline = Color(0xFF58757B),
    error = FieldRed,
    onError = Color.White,
    errorContainer = Color(0xFF3A1F24),
    onErrorContainer = Color(0xFFFFB7C0),
    scrim = Color.Black
)

@Composable
fun FieldTakHubTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = FieldTakDarkColors, content = { FirstRunAndDiagnosticsHost { content() } })
}

@Composable
fun FieldBrandHeader(versionLabel: String, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier.fillMaxWidth(),
        color = FieldPanel,
        shape = RoundedCornerShape(10.dp),
        border = BorderStroke(1.dp, FieldCyan.copy(alpha = 0.55f))
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Surface(
                modifier = Modifier.size(58.dp),
                color = FieldBg,
                shape = RoundedCornerShape(10.dp),
                border = BorderStroke(1.dp, FieldCyan.copy(alpha = 0.65f))
            ) {
                Image(
                    painter = painterResource(R.drawable.ic_launcher_foreground),
                    contentDescription = null,
                    modifier = Modifier.padding(2.dp)
                )
            }
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text("FIELD TAK HUB", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Black)
                Text("PROVISION • DEPLOY • READY", color = FieldCyan, style = MaterialTheme.typography.labelMedium)
                Text(versionLabel, color = FieldMuted, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

@Composable
fun FieldPrimaryButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit
) = Button(
    onClick = onClick,
    modifier = modifier,
    enabled = enabled,
    shape = RoundedCornerShape(8.dp),
    colors = ButtonDefaults.buttonColors(
        containerColor = FieldCyan,
        contentColor = FieldBg,
        disabledContainerColor = FieldCyan.copy(alpha = 0.28f),
        disabledContentColor = FieldMuted
    ),
    content = { FirstRunAndDiagnosticsHost { content() } }
)

@Composable
fun FieldOutlineButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit
) = OutlinedButton(
    onClick = onClick,
    modifier = modifier,
    enabled = enabled,
    shape = RoundedCornerShape(8.dp),
    border = BorderStroke(1.dp, if (enabled) FieldCyan else FieldMuted.copy(alpha = 0.35f)),
    colors = ButtonDefaults.outlinedButtonColors(contentColor = FieldCyan, disabledContentColor = FieldMuted),
    content = { FirstRunAndDiagnosticsHost { content() } }
)

@Composable
fun FieldTonalButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit
) = FilledTonalButton(
    onClick = onClick,
    modifier = modifier,
    enabled = enabled,
    shape = RoundedCornerShape(8.dp),
    colors = ButtonDefaults.filledTonalButtonColors(
        containerColor = Color(0xFF16343A),
        contentColor = FieldCyan,
        disabledContainerColor = FieldPanel,
        disabledContentColor = FieldMuted
    ),
    content = { FirstRunAndDiagnosticsHost { content() } }
)
