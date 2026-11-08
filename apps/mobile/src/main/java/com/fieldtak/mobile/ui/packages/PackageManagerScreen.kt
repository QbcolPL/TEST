package com.fieldtak.mobile.ui.packages

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.ui.theme.*

data class FtakPackage(val name: String, val faction: String, val size: String, val active: Boolean)

@Composable
fun PackageManagerScreen() {
    val packages = remember {
        mutableStateListOf(
            FtakPackage("OPERACJA AEGIS_BLUFOR.ftak", "BLUFOR", "48.2 MB", true),
            FtakPackage("TRENING SOBOTNI_ORGA.ftak", "ORGA", "12.4 MB", false)
        )
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(16.dp)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Text(
                text = "MENEDŻER PACZEK .FTAK",
                color = RadarCyan,
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(bottom = 12.dp)
            )

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(
                    onClick = { },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, RadarCyan),
                    modifier = Modifier.weight(1f).height(36.dp)
                ) {
                    Text(text = "📁 WCZYTAJ Z PLIKU", color = RadarCyan, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
                Button(
                    onClick = { },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, MilSimGreen),
                    modifier = Modifier.weight(1f).height(36.dp)
                ) {
                    Text(text = "📡 HOTSPOT WI-FI", color = MilSimGreen, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }

            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.weight(1f)
            ) {
                items(packages.size) { index ->
                    val pkg = packages[index]
                    Card(
                        colors = CardDefaults.cardColors(containerColor = TacticalCard),
                        shape = RoundedCornerShape(6.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .border(1.2.dp, if (pkg.active) RadarCyan else MutedGrid, RoundedCornerShape(6.dp))
                    ) {
                        Column(modifier = Modifier.padding(12.dp)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(text = pkg.name, color = TextWhite, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                if (pkg.active) {
                                    Surface(
                                        color = InputTrench,
                                        shape = RoundedCornerShape(4.dp),
                                        border = BorderStroke(1.dp, RadarCyan)
                                    ) {
                                        Text(
                                            text = "AKTYWNA",
                                            color = RadarCyan,
                                            fontSize = 9.sp,
                                            fontWeight = FontWeight.Bold,
                                            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                                        )
                                    }
                                }
                            }
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(text = "Strona: ${pkg.faction} | Rozmiar: ${pkg.size}", color = TextMuted, fontSize = 11.sp)
                        }
                    }
                }
            }
        }
    }
}
