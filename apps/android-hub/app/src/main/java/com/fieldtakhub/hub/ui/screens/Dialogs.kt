package com.fieldtakhub.hub.ui.screens

import android.content.Intent
import android.provider.Settings
import android.webkit.WebView
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudDownload
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Radio
import androidx.compose.material.icons.filled.Router
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material.icons.filled.WifiTethering
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.fieldtakhub.hub.AppCheck
import com.fieldtakhub.hub.CheckState
import com.fieldtakhub.hub.HubState
import com.fieldtakhub.hub.HubViewModel
import com.fieldtakhub.hub.SharePhase
import com.fieldtakhub.hub.core.Callsign
import com.fieldtakhub.hub.core.Codec
import com.fieldtakhub.hub.core.MeshChannel
import com.fieldtakhub.hub.core.SignatureState
import com.fieldtakhub.hub.core.humanBytes
import com.fieldtakhub.hub.data.StoredPackage
import com.fieldtakhub.hub.platform.AppInspector
import com.fieldtakhub.hub.platform.Intents
import com.fieldtakhub.hub.platform.QrCodes
import com.fieldtakhub.hub.ui.components.Badge
import com.fieldtakhub.hub.ui.components.HubHeader
import com.fieldtakhub.hub.ui.components.HubPanel
import com.fieldtakhub.hub.ui.components.KeyValue
import com.fieldtakhub.hub.ui.components.PrimaryButton
import com.fieldtakhub.hub.ui.components.SecondaryButton
import com.fieldtakhub.hub.ui.components.hubBackground
import com.fieldtakhub.hub.ui.theme.Hub
import com.fieldtakhub.hub.ui.theme.Mono

@Composable
fun HubDialogs(state: HubState, vm: HubViewModel) {
    if (state.injectOpen) InjectDialog(state, vm)
    state.details?.let { DetailsDialog(it, state, vm) }
    if (state.documentOpen) DocumentDialog(vm)
    if (state.share != null) ShareDialog(state, vm)
    if (state.wifiPrompt != null) WifiPromptDialog(state, vm)
    state.busy?.let { busy ->
        Dialog(onDismissRequest = {}, properties = DialogProperties(dismissOnBackPress = false, dismissOnClickOutside = false)) {
            HubPanel(border = Hub.Cyan) {
                Text(busy.title, color = Hub.Text, fontSize = 17.sp, fontWeight = FontWeight.Bold)
                if (busy.detail.isNotBlank()) Text(busy.detail, color = Hub.TextDim, fontSize = 13.sp, modifier = Modifier.padding(top = 4.dp))
                Spacer(Modifier.height(12.dp))
                val p = busy.progress
                if (p != null) {
                    LinearProgressIndicator(progress = { p }, modifier = Modifier.fillMaxWidth(), color = Hub.Cyan, trackColor = Hub.Border)
                } else {
                    LinearProgressIndicator(modifier = Modifier.fillMaxWidth(), color = Hub.Cyan, trackColor = Hub.Border)
                }
                if (busy.cancellable) {
                    Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.CenterEnd) {
                        TextButton(onClick = { vm.cancelImport() }) { Text("Anuluj", color = Hub.TextDim) }
                    }
                }
            }
        }
    }
    state.notice?.let { n ->
        AlertDialog(
            onDismissRequest = { vm.dismissNotice() },
            containerColor = Hub.Panel,
            title = { Text(n.title, color = if (n.error) Hub.Amber else Hub.Text, fontWeight = FontWeight.Bold) },
            text = {
                Text(n.text, color = Hub.Text, fontSize = 14.sp, lineHeight = 20.sp, modifier = Modifier.verticalScroll(rememberScrollState()))
            },
            confirmButton = {
                val primary = n.actions.lastOrNull { it.primary } ?: n.actions.lastOrNull()
                if (primary == null) {
                    TextButton(onClick = { vm.dismissNotice() }) { Text("OK", color = Hub.Cyan) }
                } else {
                    TextButton(onClick = primary.run) { Text(primary.label, color = Hub.Cyan, fontWeight = FontWeight.Bold) }
                }
            },
            dismissButton = {
                val primary = n.actions.lastOrNull { it.primary } ?: n.actions.lastOrNull()
                val others = n.actions.filter { it !== primary }
                if (others.isNotEmpty()) {
                    Row {
                        for (a in others) TextButton(onClick = a.run) { Text(a.label, color = Hub.TextDim) }
                    }
                }
            }
        )
    }
}

@Composable
private fun FullScreen(onDismiss: () -> Unit, content: @Composable () -> Unit) {
    Dialog(onDismissRequest = onDismiss, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Box(Modifier.fillMaxSize().hubBackground()) { content() }
    }
}

@Composable
private fun StepPanel(number: Int, title: String, done: Boolean, content: @Composable () -> Unit) {
    HubPanel(border = if (done) Hub.Green else Hub.Border, modifier = Modifier.padding(vertical = 5.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                Modifier.size(26.dp).clip(RoundedCornerShape(13.dp)).background(if (done) Hub.Green else Hub.CyanSoft),
                contentAlignment = Alignment.Center
            ) {
                Text("$number", color = if (done) Hub.Background else Hub.Cyan, fontWeight = FontWeight.Bold, fontSize = 14.sp)
            }
            Spacer(Modifier.width(10.dp))
            Text(title, color = Hub.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            if (done) Icon(Icons.Filled.CheckCircle, contentDescription = "Wykonano", tint = Hub.Green)
        }
        Spacer(Modifier.height(6.dp))
        content()
    }
}

@Composable
private fun InjectDialog(state: HubState, vm: HubViewModel) {
    val active = state.active ?: return
    val pkg = active.pkg
    val context = LocalContext.current
    FullScreen(onDismiss = { vm.closeInject() }) {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
            HubHeader(onBack = { vm.closeInject() })
            Text("Wstrzyknięcie ustawień", color = Hub.Text, fontSize = 20.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 12.dp))
            Text(pkg.title, color = Hub.TextDim, fontSize = 13.sp)
            Text(
                "Callsign: ${state.callsign.ifBlank { "—" }}   Meshtastic: [${state.shortName.ifBlank { "—" }}]",
                color = Hub.Cyan, fontFamily = Mono, fontSize = 14.sp, modifier = Modifier.padding(top = 4.dp, bottom = 6.dp)
            )

            // 1. Apps
            val pending = state.apps.filter { it.state != CheckState.OK }
            StepPanel(1, "Aplikacje (ATAK, wtyczki, Meshtastic)", pending.isEmpty()) {
                for (check in state.apps) AppRow(check, vm)
                if (!AppInspector.canInstallPackages(context) && state.apps.any { it.state != CheckState.OK && it.requirement.bundledEntry != null }) {
                    Text("Instalacja z paczki wymaga zgody „Instaluj nieznane aplikacje”.", color = Hub.Amber, fontSize = 12.sp)
                }
            }

            // 2. ATAK identity and reporting
            val settings = vm.atakSettingsPreview()
            StepPanel(2, "ATAK: callsign, zespół, raportowanie", "atak_identity" in state.injectDone) {
                for (s in settings) {
                    Row(Modifier.fillMaxWidth().padding(vertical = 1.dp)) {
                        Text(s.label, color = Hub.TextDim, fontSize = 13.sp, modifier = Modifier.weight(1f))
                        Text(s.value, color = Hub.Text, fontFamily = Mono, fontSize = 13.sp)
                    }
                }
                Spacer(Modifier.height(6.dp))
                SecondaryButton("Wyślij do ATAK", Icons.Filled.Radio, Modifier.fillMaxWidth(), enabled = state.callsign.isNotBlank()) { vm.injectAtakSettings() }
                Text("ATAK pokaże okno z listą zmian — zatwierdź je.", color = Hub.TextDim, fontSize = 12.sp)
            }

            // 3. Server and data packages
            val server = pkg.server
            val hasServerPref = pkg.atakImports.any { it.endsWith(".pref", true) } || pkg.atakImports.any { it.contains("mission", true) }
            val serverDone = "atak_enroll" in state.injectDone || "import:server" in state.injectDone ||
                pkg.atakImports.isNotEmpty() && pkg.atakImports.all { "import:$it" in state.injectDone }
            StepPanel(3, "ATAK: serwer OpenTAK i paczki danych", serverDone) {
                if (server != null) Text("Serwer: ${server.name} — ${server.endpoint}", color = Hub.Text, fontSize = 14.sp)
                val enroll = pkg.enrollmentUri
                if (enroll != null) {
                    ImportRow("Rejestracja użytkownika na serwerze", "atak_enroll" in state.injectDone) { vm.injectEnrollment() }
                }
                if (server != null && enroll == null && !hasServerPref) {
                    ImportRow("Połączenie z serwerem (utworzone z paczki)", "import:server" in state.injectDone) { vm.injectAtakFile(null) }
                }
                for (entry in pkg.atakImports) {
                    ImportRow(entry.substringAfterLast('/'), "import:$entry" in state.injectDone) { vm.injectAtakFile(entry) }
                }
                for (entry in pkg.certificates) {
                    ImportRow("Certyfikat: " + entry.substringAfterLast('/'), "import:$entry" in state.injectDone) { vm.injectAtakFile(entry) }
                }
                for (entry in pkg.overlays + pkg.mapLayers) {
                    ImportRow("Mapa: " + entry.substringAfterLast('/'), "import:$entry" in state.injectDone) { vm.injectAtakFile(entry) }
                }
                if (server == null && pkg.atakImports.isEmpty() && pkg.overlays.isEmpty()) {
                    Text("Paczka nie zawiera serwera ani paczek danych (gra tylko przez mesh).", color = Hub.TextDim, fontSize = 13.sp)
                }
            }

            // 4. Meshtastic channel
            val mesh = pkg.mesh
            StepPanel(4, "Meshtastic: kanał, radio i klucz PSK", "mesh_channel" in state.injectDone) {
                if (mesh == null) {
                    Text("Paczka nie zawiera ustawień Meshtastic.", color = Hub.TextDim, fontSize = 13.sp)
                } else {
                    val decoded = mesh.effectiveUrl()?.let { MeshChannel.decodeUrl(it) }
                    val ch = decoded?.primary
                    val lora = decoded?.lora
                    KeyValue("Kanał", ch?.name?.ifBlank { null } ?: mesh.channelName.ifBlank { "—" })
                    if (lora != null) KeyValue("Radio", "${MeshChannel.REGIONS[lora.region] ?: lora.region} · ${MeshChannel.PRESETS[lora.modemPreset] ?: lora.modemPreset} · hop ${lora.hopLimit}", mono = true)
                    val psk = ch?.psk ?: Codec.base64Decode(mesh.pskBase64)
                    if (psk != null) KeyValue("Klucz", MeshChannel.describePsk(psk), if (MeshChannel.isPrivatePsk(psk)) Hub.Green else Hub.Amber)
                    if (ch?.uplink == true) Text("Kanał ma włączone wysyłanie do MQTT (uplink) — działa tylko, jeśli radio ma skonfigurowany MQTT.", color = Hub.Amber, fontSize = 12.sp)
                    Spacer(Modifier.height(6.dp))
                    SecondaryButton("Otwórz w Meshtastic", Icons.Filled.Router, Modifier.fillMaxWidth()) { vm.injectMeshChannel() }
                    Text("Meshtastic zapyta, czy zastąpić kanały — potwierdź. Radio musi być połączone z aplikacją.", color = Hub.TextDim, fontSize = 12.sp)
                }
            }

            // 5. Meshtastic node name
            StepPanel(5, "Meshtastic: nazwa radia", "mesh_owner" in state.injectDone) {
                KeyValue("Długa nazwa", Callsign.longName(state.callsign).ifBlank { "—" })
                KeyValue("Krótka nazwa", state.shortName.ifBlank { "—" }, mono = true)
                SecondaryButton("Ustaw w Meshtastic", Icons.Filled.ContentCopy, Modifier.fillMaxWidth(), enabled = state.callsign.isNotBlank()) { vm.injectMeshOwner() }
            }

            Text(
                "Każdy krok potwierdzasz w ATAK lub Meshtastic — Android nie pozwala zmieniać ustawień innych aplikacji bez Twojej zgody. Po powrocie tutaj przejdź do następnego kroku.",
                color = Hub.TextDim, fontSize = 12.sp, modifier = Modifier.padding(top = 10.dp)
            )
            Spacer(Modifier.height(12.dp))
            PrimaryButton("GOTOWE") { vm.closeInject() }
            Spacer(Modifier.height(20.dp))
        }
    }
}

@Composable
private fun AppRow(check: AppCheck, vm: HubViewModel) {
    val color = when (check.state) {
        CheckState.OK -> Hub.Green
        CheckState.MISSING -> Hub.Red
        CheckState.OUTDATED -> Hub.Amber
    }
    Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(check.name, color = Hub.Text, fontSize = 14.sp, fontWeight = FontWeight.Medium)
            Text(check.detail, color = color, fontSize = 12.sp)
        }
        if (check.state != CheckState.OK) {
            val bundled = check.requirement.bundledEntry != null
            when {
                bundled -> SecondaryButton(if (check.state == CheckState.MISSING) "Zainstaluj" else "Aktualizuj", Icons.Filled.CloudDownload) { vm.installApp(check) }
                check.packageName != null && check.requirement.packageNameReliable -> SecondaryButton("Sklep", Icons.Filled.CloudDownload, color = Hub.TextDim) { vm.installApp(check) }
                else -> Unit
            }
        }
    }
}

@Composable
private fun ImportRow(label: String, done: Boolean, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 3.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, color = Hub.Text, fontSize = 13.sp, modifier = Modifier.weight(1f))
        if (done) Icon(Icons.Filled.CheckCircle, contentDescription = null, tint = Hub.Green, modifier = Modifier.size(20.dp).padding(end = 4.dp))
        SecondaryButton(if (done) "Ponów" else "Do ATAK", Icons.Filled.Radio, color = if (done) Hub.TextDim else Hub.Cyan, onClick = onClick)
    }
}

@Composable
private fun ShareDialog(state: HubState, vm: HubViewModel) {
    val share = state.share ?: return
    FullScreen(onDismiss = { vm.stopShare() }) {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            HubHeader(onBack = { vm.stopShare() })
            Text("Udostępnianie paczki", color = Hub.Text, fontSize = 20.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 12.dp))
            Text(share.title, color = Hub.TextDim, fontSize = 13.sp, textAlign = TextAlign.Center)
            Spacer(Modifier.height(14.dp))
            when (share.phase) {
                SharePhase.STARTING -> {
                    CircularProgressIndicator(color = Hub.Cyan)
                    Text(if (share.viaHotspot) "Uruchamiam hotspot Wi-Fi…" else "Przygotowuję udostępnianie…", color = Hub.TextDim, modifier = Modifier.padding(top = 10.dp))
                }
                SharePhase.READY -> {
                    val qr = share.qrText.orEmpty()
                    val bitmap = remember(qr) { QrCodes.encode(qr).asImageBitmap() }
                    Box(Modifier.clip(RoundedCornerShape(12.dp)).background(Color.White).padding(10.dp)) {
                        Image(bitmap, contentDescription = "Kod QR paczki", modifier = Modifier.size(280.dp))
                    }
                    Text(
                        "Drugi telefon: Field TAK Hub → SKANER → zeskanuj ten kod." +
                            if (share.viaHotspot) " Połączy się z hotspotem sam (Android 10+)." else " Oba telefony muszą być w tej samej sieci Wi-Fi.",
                        color = Hub.Text, fontSize = 14.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 12.dp)
                    )
                    if (share.ssid != null) {
                        HubPanel(modifier = Modifier.padding(top = 10.dp)) {
                            KeyValue("Sieć Wi-Fi (ręcznie na starszych telefonach)", share.ssid, mono = true)
                            share.password?.let { KeyValue("Hasło", it, mono = true) }
                        }
                    }
                    Text("Pobrania: ${share.downloads}", color = Hub.Cyan, fontSize = 16.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 10.dp))
                    Text("Nie zamykaj tego ekranu, dopóki inni nie pobiorą paczki.", color = Hub.TextDim, fontSize = 12.sp)
                }
                SharePhase.FAILED -> {
                    Text(share.error ?: "Nie udało się uruchomić udostępniania.", color = Hub.Amber, fontSize = 14.sp, textAlign = TextAlign.Center)
                    Spacer(Modifier.height(12.dp))
                    SecondaryButton("Spróbuj hotspot ponownie", Icons.Filled.WifiTethering, Modifier.fillMaxWidth()) { vm.startShare(share.entryId, true) }
                    SecondaryButton("Przez tę samą sieć Wi-Fi", Icons.Filled.Wifi, Modifier.fillMaxWidth()) { vm.startShare(share.entryId, false) }
                    SecondaryButton("Wyślij plik (Quick Share, Bluetooth…)", Icons.Filled.Share, Modifier.fillMaxWidth()) {
                        vm.stopShare()
                        vm.shareViaApps(share.entryId)
                    }
                }
            }
            Spacer(Modifier.height(16.dp))
            PrimaryButton("ZAKOŃCZ UDOSTĘPNIANIE") { vm.stopShare() }
            Spacer(Modifier.height(20.dp))
        }
    }
}

@Composable
private fun WifiPromptDialog(state: HubState, vm: HubViewModel) {
    val prompt = state.wifiPrompt ?: return
    val context = LocalContext.current
    AlertDialog(
        onDismissRequest = { vm.dismissWifiPrompt() },
        containerColor = Hub.Panel,
        title = { Text("Połącz się z hotspotem", color = Hub.Text) },
        text = {
            Column {
                Text("Ten Android nie łączy się automatycznie. Połącz się ręcznie z siecią:", color = Hub.TextDim, fontSize = 13.sp)
                KeyValue("Sieć", prompt.credentials.ssid, mono = true)
                prompt.credentials.password?.let { KeyValue("Hasło", it, mono = true) }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    SecondaryButton("Kopiuj hasło", Icons.Filled.ContentCopy) { Intents.copy(context, "Hasło Wi-Fi", prompt.credentials.password.orEmpty()) }
                    SecondaryButton("Ustawienia Wi-Fi", Icons.Filled.Wifi) { vm.startIntent(Intent(Settings.ACTION_WIFI_SETTINGS)) }
                }
                Text("Potem wróć tutaj i naciśnij „Pobierz”.", color = Hub.TextDim, fontSize = 13.sp, modifier = Modifier.padding(top = 6.dp))
            }
        },
        confirmButton = { TextButton(onClick = { vm.wifiManualDone() }) { Text("Pobierz", color = Hub.Cyan, fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { vm.dismissWifiPrompt() }) { Text("Anuluj", color = Hub.TextDim) } }
    )
}

@Composable
private fun DetailsDialog(sp: StoredPackage, state: HubState, vm: HubViewModel) {
    val pkg = sp.pkg
    FullScreen(onDismiss = { vm.showDetails(null) }) {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
            HubHeader(onBack = { vm.showDetails(null) })
            Text(pkg.title, color = Hub.Text, fontSize = 20.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 12.dp))
            Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Badge(pkg.kind.label, Hub.TextDim)
                if (pkg.isExpired()) Badge("WYGASŁA", Hub.Red)
            }
            HubPanel(modifier = Modifier.padding(top = 10.dp)) {
                when (pkg.signature.state) {
                    SignatureState.VERIFIED -> KeyValue("Podpis", "Zgodny · klucz ${pkg.signature.shortFingerprint}", Hub.Green, mono = true)
                    SignatureState.UNSIGNED -> KeyValue("Podpis", pkg.signature.detail, Hub.Amber)
                    SignatureState.INVALID -> KeyValue("Podpis", pkg.signature.detail, Hub.Red)
                }
                KeyValue("SHA-256 pliku", sp.sha256.take(32) + "…", mono = true)
                KeyValue("Rozmiar", humanBytes(sp.sizeBytes))
                KeyValue("Źródło", sp.source)
                KeyValue("Ważność", pkg.expiresAtMillis()?.let { formatDate(it) } ?: "bezterminowa")
            }
            HubPanel(modifier = Modifier.padding(top = 10.dp)) {
                Text("Konfiguracja", color = Hub.Text, fontWeight = FontWeight.SemiBold)
                if (pkg.faction.isNotBlank()) KeyValue("Strona", pkg.faction)
                pkg.server?.let { KeyValue("Serwer TAK", "${it.name} · ${it.endpoint}", mono = true) }
                pkg.enrollmentUri?.let { KeyValue("Rejestracja", "link do serwera ${com.fieldtakhub.hub.core.Atak.enrollHost(it).orEmpty()}") }
                pkg.mesh?.let { m -> KeyValue("Meshtastic", listOfNotNull(m.channelName.ifBlank { null }, m.region, m.modemPreset, m.hopLimit?.let { "hop $it" }).joinToString(" · ").ifBlank { "kanał z linku" }) }
                pkg.emergencyPhone?.let { KeyValue("Telefon organizatora", it) }
                for (v in pkg.voiceChannels) KeyValue("Radio: ${v.role.ifBlank { v.band }}", listOf(v.band, v.frequency, v.tone).filter { it.isNotBlank() }.joinToString(" · "), mono = true)
                pkg.atakTarget?.let { KeyValue("Wymagany ATAK", it.describe()) }
                for (s in pkg.atakSettings) KeyValue("ATAK: ${s.label}", s.value, mono = true)
            }
            HubPanel(modifier = Modifier.padding(top = 10.dp)) {
                Text("Zawartość", color = Hub.Text, fontWeight = FontWeight.SemiBold)
                for (r in pkg.requirements) {
                    val apk = sp.apkFor(r.bundledEntry)
                    KeyValue(
                        "${r.role.label}: ${apk?.label ?: r.displayName}",
                        if (apk != null) "${apk.packageName} ${apk.versionName} (w paczce)"
                        else (r.requirement?.describe()?.let { "wymagana $it" } ?: "bez wersji") + if (r.bundledEntry == null) " · nie ma w paczce" else ""
                    )
                }
                for (e in pkg.atakImports) KeyValue("Dla ATAK", e)
                for (e in pkg.overlays) KeyValue("Warstwa mapy", e)
                for (e in pkg.mapLayers) KeyValue("Mapa offline", e)
                for (e in pkg.certificates) KeyValue("Certyfikat", e)
                if (pkg.hasOpord) KeyValue("Rozkaz OPORD", "${pkg.opord?.days?.size ?: 0} dni" + if (pkg.opordHtmlEntry != null) " + dokument" else "")
            }
            if (pkg.notes.isNotEmpty()) {
                HubPanel(modifier = Modifier.padding(top = 10.dp), border = Hub.Amber) {
                    for (n in pkg.notes) Text("• $n", color = Hub.Amber, fontSize = 13.sp)
                }
            }
            Spacer(Modifier.height(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (state.activeId != sp.id) SecondaryButton("Aktywuj", Icons.Filled.CheckCircle, Modifier.weight(1f)) { vm.activate(sp.id) }
                SecondaryButton("Udostępnij", Icons.Filled.WifiTethering, Modifier.weight(1f)) { vm.showDetails(null); vm.startShare(sp.id) }
            }
            Spacer(Modifier.height(20.dp))
        }
    }
}

@Composable
private fun DocumentDialog(vm: HubViewModel) {
    val html = remember { vm.readOpordHtml() }
    FullScreen(onDismiss = { vm.showDocument(false) }) {
        Column(Modifier.fillMaxSize().padding(12.dp)) {
            HubHeader(onBack = { vm.showDocument(false) })
            Spacer(Modifier.height(8.dp))
            if (html == null) {
                Text("Brak dokumentu rozkazu w paczce.", color = Hub.TextDim)
            } else {
                AndroidView(
                    factory = { ctx ->
                        WebView(ctx).apply {
                            settings.javaScriptEnabled = false
                            settings.builtInZoomControls = true
                            settings.displayZoomControls = false
                            loadDataWithBaseURL(null, html, "text/html", "utf-8", null)
                        }
                    },
                    modifier = Modifier.fillMaxSize().clip(RoundedCornerShape(8.dp))
                )
            }
        }
    }
}
