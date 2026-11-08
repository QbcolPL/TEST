package com.fieldtak.mobile.ui.packages

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.data.FtakPackageRepository
import com.fieldtak.mobile.data.FtakPackageSummary
import com.fieldtak.mobile.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun PackageManagerScreen(
    activePackage: FtakPackageSummary?,
    onActivePackageChanged: (FtakPackageSummary?) -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var packages by remember { mutableStateOf(FtakPackageRepository.listInstalledPackages(context)) }

    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val imported = FtakPackageRepository.importFromUri(context, uri)
                packages = FtakPackageRepository.listInstalledPackages(context)
                if (imported != null) onActivePackageChanged(imported)
            }
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(14.dp)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Text(
                text = "MENEDŻER PACZEK .FTAK (${packages.size})",
                color = RadarCyan,
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(bottom = 10.dp)
            )

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(
                    onClick = { filePicker.launch("*/*") },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, RadarCyan),
                    modifier = Modifier.weight(1f).height(38.dp)
                ) {
                    Text(text = "📁 WCZYTAJ Z PLIKU", color = RadarCyan, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
                Button(
                    onClick = { },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, MilSimGreen),
                    modifier = Modifier.weight(1f).height(38.dp)
                ) {
                    Text(text = "📡 HOTSPOT WI-FI", color = MilSimGreen, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }

            if (packages.isEmpty()) {
                Card(
                    colors = CardDefaults.cardColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .border(1.dp, MutedGrid, RoundedCornerShape(6.dp))
                ) {
                    Text(
                        text = "Brak zapisanych paczek .ftak w pamięci telefonu. Zeskanuj kod QR w zakładce SKANER lub kliknij 'Wczytaj z pliku'.",
                        color = TextMuted,
                        fontSize = 12.sp,
                        modifier = Modifier.padding(16.dp)
                    )
                }
            } else {
                LazyColumn(
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.weight(1f)
                ) {
                    items(packages.size) { index ->
                        val pkg = packages[index]
                        val isCurrent = activePackage?.fileName == pkg.fileName
                        Card(
                            colors = CardDefaults.cardColors(containerColor = TacticalCard),
                            shape = RoundedCornerShape(6.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .border(1.3.dp, if (isCurrent) RadarCyan else MutedGrid, RoundedCornerShape(6.dp))
                                .clickable {
                                    FtakPackageRepository.setActivePackageFileName(context, pkg.fileName)
                                    onActivePackageChanged(pkg)
                                }
                        ) {
                            Column(modifier = Modifier.padding(12.dp)) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Column(modifier = Modifier.weight(1f)) {
                                        Text(
                                            text = "${pkg.eventName} — ${pkg.factionName}",
                                            color = TextWhite,
                                            fontSize = 13.sp,
                                            fontWeight = FontWeight.Bold
                                        )
                                        Text(
                                            text = "Plik: ${pkg.fileName} (${pkg.formattedSize})",
                                            color = TextMuted,
                                            fontSize = 10.sp,
                                            fontFamily = FontFamily.Monospace
                                        )
                                    }

                                    Row(
                                        verticalAlignment = Alignment.CenterVertically,
                                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                                    ) {
                                        if (isCurrent) {
                                            Surface(
                                                color = InputTrench,
                                                shape = RoundedCornerShape(4.dp),
                                                border = BorderStroke(1.dp, MilSimGreen)
                                            ) {
                                                Text(
                                                    text = "AKTYWNA",
                                                    color = MilSimGreen,
                                                    fontSize = 9.sp,
                                                    fontWeight = FontWeight.Bold,
                                                    modifier = Modifier.padding(horizontal = 6.dp, vertical = 3.dp)
                                                )
                                            }
                                        }

                                        Surface(
                                            color = InputTrench,
                                            shape = RoundedCornerShape(4.dp),
                                            border = BorderStroke(1.dp, DangerCrimson),
                                            modifier = Modifier
                                                .size(28.dp)
                                                .clickable {
                                                    FtakPackageRepository.deletePackage(context, pkg.fileName)
                                                    packages = FtakPackageRepository.listInstalledPackages(context)
                                                    onActivePackageChanged(packages.firstOrNull())
                                                }
                                        ) {
                                            Box(contentAlignment = Alignment.Center) {
                                                Text(
                                                    text = "−",
                                                    color = DangerCrimson,
                                                    fontSize = 15.sp,
                                                    fontWeight = FontWeight.Bold
                                                )
                                            }
                                        }
                                    }
                                }

                                Spacer(modifier = Modifier.height(4.dp))
                                Text(
                                    text = "OTS: ${pkg.otsHost} | LoRa: ${pkg.loraRegion} (${pkg.loraPreset})",
                                    color = RadarCyan,
                                    fontSize = 10.sp,
                                    fontFamily = FontFamily.Monospace
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}