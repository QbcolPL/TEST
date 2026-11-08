package com.fieldtak.mobile.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.data.FtakPackageSummary
import com.fieldtak.mobile.ui.theme.*

enum class TacticalTab(val label: String) {
    SCANNER("SKANER"),
    PACKAGES("PACZKI"),
    OPORD("OPORD"),
    CHEATSHEET("ŚCIĄGA")
}

@Composable
fun TacticalTopBar(activePackage: FtakPackageSummary?, onOpenPermissions: () -> Unit) {
    Surface(
        color = TacticalCard,
        shape = RoundedCornerShape(8.dp),
        border = BorderStroke(1.2.dp, RadarCyan),
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
                Canvas(modifier = Modifier.size(28.dp)) {
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

            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Surface(
                    color = InputTrench,
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.dp, if (activePackage != null) MilSimGreen else MutedGrid),
                    modifier = Modifier.clickable { onOpenPermissions() }
                ) {
                    Text(
                        text = activePackage?.factionName ?: "BRAK PACZKI",
                        color = if (activePackage != null) MilSimGreen else TextMuted,
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
                    )
                }
            }
        }
    }
}

@Composable
fun TacticalBottomBar(currentTab: TacticalTab, onSelectTab: (TacticalTab) -> Unit) {
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
                val selected = tab == currentTab
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
                drawLine(color, Offset(w * 0.15f, h / 2f), Offset(w * 0.85f, h / 2f), s)
            }
            TacticalTab.PACKAGES -> {
                drawRect(color = color, topLeft = Offset(w * 0.1f, h * 0.2f), size = Size(w * 0.8f, h * 0.65f), style = Stroke(s))
                drawLine(color, Offset(w * 0.1f, h * 0.42f), Offset(w * 0.9f, h * 0.42f), s)
                drawLine(color, Offset(w * 0.35f, h * 0.08f), Offset(w * 0.65f, h * 0.08f), s)
            }
            TacticalTab.OPORD -> {
                drawRect(color = color, topLeft = Offset(w * 0.15f, h * 0.1f), size = Size(w * 0.7f, h * 0.8f), style = Stroke(s))
                drawLine(color, Offset(w * 0.3f, h * 0.35f), Offset(w * 0.7f, h * 0.35f), s)
                drawLine(color, Offset(w * 0.3f, h * 0.55f), Offset(w * 0.7f, h * 0.55f), s)
                drawLine(color, Offset(w * 0.3f, h * 0.75f), Offset(w * 0.55f, h * 0.75f), s)
            }
            TacticalTab.CHEATSHEET -> {
                drawCircle(color = color, radius = w * 0.38f, style = Stroke(s))
                drawCircle(color = color, radius = w * 0.12f)
                drawLine(color, Offset(w / 2f, 0f), Offset(w / 2f, h * 0.22f), s)
                drawLine(color, Offset(w / 2f, h * 0.78f), Offset(w / 2f, h), s)
                drawLine(color, Offset(0f, h / 2f), Offset(w * 0.22f, h / 2f), s)
                drawLine(color, Offset(w * 0.78f, h / 2f), Offset(w, h / 2f), s)
            }
        }
    }
}