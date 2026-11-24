package com.fieldtakhub.hub.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Assignment
import androidx.compose.material.icons.filled.ChevronLeft
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.ButtonDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtakhub.hub.Tab
import com.fieldtakhub.hub.ui.theme.Hub
import com.fieldtakhub.hub.ui.theme.Mono

/** Dark background with the faint tactical grid of the mockup. */
fun Modifier.hubBackground(): Modifier = this
    .background(Brush.verticalGradient(listOf(Hub.BackgroundTop, Hub.Background)))
    .drawBehind {
        val step = 28.dp.toPx()
        var x = 0f
        while (x < size.width) {
            drawLine(Hub.Grid, Offset(x, 0f), Offset(x, size.height), 1f)
            x += step
        }
        var y = 0f
        while (y < size.height) {
            drawLine(Hub.Grid, Offset(0f, y), Offset(size.width, y), 1f)
            y += step
        }
    }

/** Crosshair logo from the mockup header. */
@Composable
fun TargetIcon(size: Dp = 34.dp, color: Color = Hub.Cyan) {
    Canvas(Modifier.size(size)) {
        val r = this.size.minDimension / 2f
        val stroke = r * 0.12f
        drawCircle(color, radius = r * 0.78f, style = Stroke(stroke))
        drawCircle(color, radius = r * 0.42f, style = Stroke(stroke * 0.8f))
        drawCircle(color, radius = r * 0.14f)
        val c = center
        drawLine(color, Offset(c.x, c.y - r), Offset(c.x, c.y - r * 0.6f), stroke)
        drawLine(color, Offset(c.x, c.y + r * 0.6f), Offset(c.x, c.y + r), stroke)
        drawLine(color, Offset(c.x - r, c.y), Offset(c.x - r * 0.6f, c.y), stroke)
        drawLine(color, Offset(c.x + r * 0.6f, c.y), Offset(c.x + r, c.y), stroke)
    }
}

@Composable
fun HubHeader(onBack: (() -> Unit)? = null, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(14.dp)
    Row(
        modifier
            .fillMaxWidth()
            .shadow(10.dp, shape, ambientColor = Hub.Cyan, spotColor = Hub.Cyan)
            .clip(shape)
            .background(Hub.Panel)
            .border(1.5.dp, Hub.Cyan, shape)
            .padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        TargetIcon(34.dp)
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text("FIELD TAK HUB", color = Hub.Text, fontWeight = FontWeight.Bold, fontSize = 20.sp, letterSpacing = 1.sp)
            Text("connect · deploy · operate", color = Hub.Cyan, fontSize = 13.sp)
        }
        if (onBack != null) {
            Box(
                Modifier
                    .size(40.dp)
                    .clip(RoundedCornerShape(8.dp))
                    .border(1.dp, Hub.Cyan, RoundedCornerShape(8.dp))
                    .clickable(onClick = onBack),
                contentAlignment = Alignment.Center
            ) {
                Icon(Icons.Filled.ChevronLeft, contentDescription = "Wstecz", tint = Hub.Cyan)
            }
        }
    }
}

@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier, color: Color = Hub.TextDim) {
    Text(
        text.uppercase(),
        modifier = modifier.padding(top = 14.dp, bottom = 6.dp),
        color = color,
        fontSize = 12.sp,
        letterSpacing = 1.2.sp,
        fontWeight = FontWeight.Medium
    )
}

@Composable
fun HubPanel(
    modifier: Modifier = Modifier,
    border: Color = Hub.Border,
    background: Color = Hub.Panel,
    padding: PaddingValues = PaddingValues(14.dp),
    onClick: (() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit
) {
    val shape = RoundedCornerShape(10.dp)
    var m = modifier
        .fillMaxWidth()
        .clip(shape)
        .background(background)
        .border(1.dp, border, shape)
    if (onClick != null) m = m.clickable(onClick = onClick)
    Column(m.padding(padding), content = content)
}

/** Big cyan action button ("WSTRZYKNIJ DO ATAK + MESHTASTIC"). */
@Composable
fun PrimaryButton(
    text: String,
    icon: ImageVector? = null,
    enabled: Boolean = true,
    modifier: Modifier = Modifier,
    onClick: () -> Unit
) {
    val shape = RoundedCornerShape(14.dp)
    val bg = if (enabled) Brush.horizontalGradient(listOf(Color(0xFF34F0DF), Color(0xFF1FD3C2))) else Brush.horizontalGradient(listOf(Hub.PanelHigh, Hub.PanelHigh))
    Row(
        modifier
            .fillMaxWidth()
            .then(if (enabled) Modifier.shadow(14.dp, shape, ambientColor = Hub.Cyan, spotColor = Hub.Cyan) else Modifier)
            .clip(shape)
            .background(bg)
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 18.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.Center
    ) {
        val fg = if (enabled) Hub.Background else Hub.Muted
        if (icon != null) {
            Icon(icon, contentDescription = null, tint = fg, modifier = Modifier.size(28.dp))
            Spacer(Modifier.width(14.dp))
        }
        Text(text, color = fg, fontWeight = FontWeight.Bold, fontSize = 17.sp, letterSpacing = 0.8.sp, textAlign = TextAlign.Center)
    }
}

@Composable
fun SecondaryButton(
    text: String,
    icon: ImageVector? = null,
    modifier: Modifier = Modifier,
    color: Color = Hub.Cyan,
    enabled: Boolean = true,
    onClick: () -> Unit
) {
    OutlinedButton(
        onClick = onClick,
        enabled = enabled,
        modifier = modifier,
        shape = RoundedCornerShape(10.dp),
        border = BorderStroke(1.dp, if (enabled) color else Hub.Border),
        colors = ButtonDefaults.outlinedButtonColors(contentColor = color, disabledContentColor = Hub.Muted),
        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp)
    ) {
        if (icon != null) {
            Icon(icon, contentDescription = null, modifier = Modifier.size(18.dp))
            Spacer(Modifier.width(6.dp))
        }
        Text(text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
fun SmallTextButton(text: String, color: Color = Hub.Cyan, onClick: () -> Unit) {
    TextButton(onClick = onClick, contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp)) {
        Text(text, color = color, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
fun StatusDot(color: Color, size: Dp = 10.dp) {
    Box(Modifier.size(size).clip(CircleShape).background(color))
}

/** "● ATAK-CIV (OK)" */
@Composable
fun StatusLine(name: String, status: String, color: Color, detail: String? = null) {
    Row(Modifier.fillMaxWidth().padding(vertical = 3.dp), verticalAlignment = Alignment.CenterVertically) {
        StatusDot(color)
        Spacer(Modifier.width(10.dp))
        Column(Modifier.weight(1f)) {
            Row {
                Text(name, color = Hub.Text, fontSize = 16.sp)
                Text(" ($status)", color = color, fontSize = 16.sp)
            }
            if (!detail.isNullOrBlank()) Text(detail, color = Hub.TextDim, fontSize = 12.sp)
        }
    }
}

@Composable
fun Badge(text: String, color: Color, filled: Boolean = false) {
    val shape = RoundedCornerShape(6.dp)
    Text(
        text,
        modifier = Modifier
            .clip(shape)
            .background(if (filled) color else Color.Transparent)
            .border(1.dp, color, shape)
            .padding(horizontal = 6.dp, vertical = 2.dp),
        color = if (filled) Hub.Background else color,
        fontSize = 11.sp,
        fontWeight = FontWeight.Bold,
        letterSpacing = 0.6.sp,
        maxLines = 1
    )
}

/** "[ DZIEŃ 1 ]" tab from the OPORD screen. */
@Composable
fun BracketTab(text: String, selected: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(6.dp)
    Text(
        "[ ${text.uppercase()} ]",
        modifier = Modifier
            .clip(shape)
            .background(if (selected) Hub.CyanSoft else Color.Transparent)
            .border(1.dp, if (selected) Hub.Cyan else Hub.BorderStrong, shape)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 8.dp),
        color = if (selected) Hub.Cyan else Hub.TextDim,
        fontSize = 14.sp,
        fontWeight = if (selected) FontWeight.Bold else FontWeight.Medium,
        maxLines = 1
    )
}

/** Monospace line in a thin frame, e.g. "PMR: CH 1 (446.00625) · CTCSS 67.0". */
@Composable
fun FrameLine(text: String, color: Color = Hub.Text) {
    val shape = RoundedCornerShape(6.dp)
    Text(
        text,
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 3.dp)
            .clip(shape)
            .background(Hub.Background)
            .border(1.dp, Hub.Border, shape)
            .padding(horizontal = 10.dp, vertical = 8.dp),
        color = color,
        fontFamily = Mono,
        fontSize = 14.sp
    )
}

@Composable
fun KeyValue(label: String, value: String, valueColor: Color = Hub.Text, mono: Boolean = false) {
    Column(Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
        Text(label, color = Hub.TextDim, fontSize = 12.sp)
        Text(value, color = valueColor, fontSize = 15.sp, fontFamily = if (mono) Mono else null)
    }
}

@Composable
fun hubFieldColors() = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = Hub.Cyan,
    unfocusedBorderColor = Hub.BorderStrong,
    focusedTextColor = Hub.Text,
    unfocusedTextColor = Hub.Text,
    cursorColor = Hub.Cyan,
    focusedContainerColor = Hub.Background,
    unfocusedContainerColor = Hub.Background,
    focusedLabelColor = Hub.Cyan,
    unfocusedLabelColor = Hub.TextDim
)

@Composable
fun HubField(
    value: String,
    onChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    placeholder: String = "",
    singleLine: Boolean = true,
    mono: Boolean = false
) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        modifier = modifier.fillMaxWidth(),
        singleLine = singleLine,
        placeholder = { Text(placeholder, color = Hub.Muted) },
        textStyle = androidx.compose.ui.text.TextStyle(fontSize = 18.sp, fontFamily = if (mono) Mono else null, color = Hub.Text),
        colors = hubFieldColors(),
        shape = RoundedCornerShape(8.dp)
    )
}

@Composable
fun HubBottomBar(tab: Tab, opordEnabled: Boolean, onSelect: (Tab) -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .background(Hub.Panel)
            .drawBehind { drawLine(Hub.Border, Offset(0f, 0f), Offset(size.width, 0f), 2f) }
            .navigationBarsPadding()
    ) {
        Row(Modifier.fillMaxWidth().height(64.dp)) {
            NavItem(Tab.SCANNER, "SKANER", Icons.Filled.QrCodeScanner, tab, true, onSelect)
            NavItem(Tab.PACKAGES, "PACZKI", Icons.Filled.Inventory2, tab, true, onSelect)
            NavItem(Tab.OPORD, "OPORD", if (opordEnabled) Icons.AutoMirrored.Filled.Assignment else Icons.Filled.Lock, tab, opordEnabled, onSelect)
            NavItem(Tab.CHEATSHEET, "ŚCIĄGA", Icons.Filled.Settings, tab, true, onSelect)
        }
    }
}

@Composable
private fun RowScope.NavItem(
    target: Tab,
    label: String,
    icon: ImageVector,
    current: Tab,
    enabled: Boolean,
    onSelect: (Tab) -> Unit
) {
    val selected = current == target
    val color = when {
        selected -> Hub.Cyan
        enabled -> Hub.TextDim
        else -> Hub.Muted
    }
    Column(
        Modifier
            .weight(1f)
            .fillMaxWidth()
            .clickable { onSelect(target) }
            .drawBehind {
                if (selected) drawLine(Hub.Cyan, Offset(size.width * 0.15f, 1f), Offset(size.width * 0.85f, 1f), 6f)
            }
            .padding(top = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Icon(icon, contentDescription = label, tint = color, modifier = Modifier.size(26.dp))
        Spacer(Modifier.height(2.dp))
        Text(label, color = color, fontSize = 12.sp, fontWeight = if (selected) FontWeight.Bold else FontWeight.Medium, letterSpacing = 0.6.sp)
    }
}
