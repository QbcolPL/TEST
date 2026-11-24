package com.fieldtakhub.hub.ui.screens

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.WifiTethering
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtakhub.hub.HubState
import com.fieldtakhub.hub.HubViewModel
import com.fieldtakhub.hub.Tab
import com.fieldtakhub.hub.core.Atak
import com.fieldtakhub.hub.core.PackageKind
import com.fieldtakhub.hub.core.SignatureState
import com.fieldtakhub.hub.core.humanBytes
import com.fieldtakhub.hub.data.StoredPackage
import com.fieldtakhub.hub.ui.components.Badge
import com.fieldtakhub.hub.ui.components.HubHeader
import com.fieldtakhub.hub.ui.components.HubPanel
import com.fieldtakhub.hub.ui.components.SecondaryButton
import com.fieldtakhub.hub.ui.components.SectionLabel
import com.fieldtakhub.hub.ui.theme.Hub
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

fun factionColor(hex: String?): Color =
    Atak.parseHexColor(hex)?.let { Color(0xFF000000 or it.toLong()) } ?: Hub.Cyan

fun formatDate(millis: Long): String = SimpleDateFormat("dd.MM.yyyy HH:mm", Locale.ROOT).format(Date(millis))

@Composable
fun PackagesScreen(state: HubState, vm: HubViewModel) {
    val context = LocalContext.current
    val fileLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) vm.importFromUri(uri)
    }
    var pendingShare by remember { mutableStateOf<String?>(null) }
    val hotspotPermissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        val id = pendingShare
        pendingShare = null
        // Without the permission the hotspot cannot start; share over the current Wi-Fi instead.
        if (id != null) vm.startShare(id, viaHotspot = result.values.all { it })
    }
    val share: (String) -> Unit = { id ->
        if (HubPermissions.granted(context, HubPermissions.hotspot)) {
            vm.startShare(id)
        } else {
            pendingShare = id
            hotspotPermissions.launch(HubPermissions.hotspot)
        }
    }
    LazyColumn(
        Modifier.fillMaxSize(),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        item { HubHeader() }
        item {
            SectionLabel("Moduł: menedżer paczek .ftak")
            ProfileCard(state) { state.active?.let { vm.showDetails(it.id) } }
        }
        item {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                SecondaryButton("Importuj plik", Icons.Filled.FolderOpen, Modifier.weight(1f)) { fileLauncher.launch(arrayOf("*/*")) }
                SecondaryButton("Skanuj / odbierz", Icons.Filled.QrCodeScanner, Modifier.weight(1f)) { vm.selectTab(Tab.SCANNER) }
            }
            val total = state.packages.sumOf { it.sizeBytes }
            Text(
                if (state.packages.isEmpty()) "Brak zapisanych paczek." else "Zapisane paczki: ${state.packages.size} · ${humanBytes(total)}",
                color = Hub.TextDim, fontSize = 12.sp, modifier = Modifier.padding(top = 8.dp)
            )
        }
        if (state.packages.isEmpty()) {
            item {
                HubPanel {
                    Text("Jak dodać paczkę?", color = Hub.Text, fontWeight = FontWeight.Bold)
                    Text(
                        "• Zeskanuj kod QR z Buildera (chmura, sieć Wi-Fi organizatora lub kod offline).\n" +
                            "• Otwórz plik .ftak z komunikatora albo menedżera plików.\n" +
                            "• Odbierz paczkę od innego gracza — on wybiera „Udostępnij przez hotspot”, Ty skanujesz jego kod.",
                        color = Hub.TextDim, fontSize = 13.sp, lineHeight = 19.sp, modifier = Modifier.padding(top = 6.dp)
                    )
                }
            }
        }
        items(state.packages, key = { it.id }) { sp -> PackageCard(sp, state, vm, share) }
        item { Spacer(Modifier.height(12.dp)) }
    }
}

@Composable
private fun PackageCard(sp: StoredPackage, state: HubState, vm: HubViewModel, share: (String) -> Unit) {
    val pkg = sp.pkg
    val isActive = state.activeId == sp.id && pkg.hasDeployment
    val isOpord = state.opordId == sp.id && pkg.hasOpord
    HubPanel(border = if (isActive) Hub.Cyan else Hub.Border, padding = androidx.compose.foundation.layout.PaddingValues(0.dp)) {
        Row(Modifier.height(IntrinsicSize.Min)) {
            Box(Modifier.width(6.dp).fillMaxHeight().background(factionColor(pkg.factionColor)))
            Column(Modifier.weight(1f).padding(12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(pkg.title, color = Hub.Text, fontSize = 16.sp, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                    if (isActive) Badge("AKTYWNA", Hub.Cyan, filled = true)
                }
                Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    Badge(shortKind(pkg.kind), Hub.TextDim)
                    when (pkg.signature.state) {
                        SignatureState.VERIFIED -> Badge("PODPISANA", Hub.Green)
                        SignatureState.UNSIGNED -> if (pkg.kind != PackageKind.OPORD_ONLY) Badge("BEZ PODPISU", Hub.Amber)
                        SignatureState.INVALID -> Badge("PODPIS BŁĘDNY", Hub.Red)
                    }
                    if (pkg.hasOpord) Badge(if (isOpord) "OPORD ✓" else "OPORD", if (isOpord) Hub.Cyan else Hub.TextDim)
                    if (pkg.isExpired()) Badge("WYGASŁA", Hub.Red)
                }
                val expiry = pkg.expiresAtMillis()?.let { "ważna do ${formatDate(it)}" } ?: "bezterminowa"
                Text(
                    "${humanBytes(sp.sizeBytes)} · import ${formatDate(sp.importedAt)} · $expiry",
                    color = Hub.TextDim, fontSize = 12.sp, modifier = Modifier.padding(top = 6.dp)
                )
                Text(sp.source, color = Hub.Muted, fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    if (!isActive || (pkg.hasOpord && !isOpord)) {
                        SecondaryButton("Aktywuj", Icons.Filled.CheckCircle, Modifier.weight(1f)) { vm.activate(sp.id) }
                    }
                    SecondaryButton("Udostępnij", Icons.Filled.WifiTethering, Modifier.weight(1f)) { share(sp.id) }
                }
                Row(Modifier.fillMaxWidth().padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    SecondaryButton("Szczegóły", Icons.Filled.Info, Modifier.weight(1f), color = Hub.TextDim) { vm.showDetails(sp.id) }
                    SecondaryButton("Plik", Icons.Filled.Share, Modifier.weight(1f), color = Hub.TextDim) { vm.shareViaApps(sp.id) }
                    SecondaryButton("Usuń", Icons.Filled.Delete, Modifier.weight(1f), color = Hub.Red) { vm.askDelete(sp.id) }
                }
            }
        }
    }
}

fun shortKind(kind: PackageKind): String = when (kind) {
    PackageKind.FTAK_V2 -> "FTAK v2"
    PackageKind.FTAK_V24 -> "FTAK v2.4"
    PackageKind.FTAK_V30 -> "FTAK v3.0"
    PackageKind.OFFLINE_QR -> "QR OFFLINE"
    PackageKind.OPORD_ONLY -> "OPORD"
}
