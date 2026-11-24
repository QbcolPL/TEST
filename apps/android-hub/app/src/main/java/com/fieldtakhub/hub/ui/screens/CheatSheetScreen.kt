package com.fieldtakhub.hub.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material.icons.filled.Sms
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtakhub.hub.BuildConfig
import com.fieldtakhub.hub.HubState
import com.fieldtakhub.hub.HubViewModel
import com.fieldtakhub.hub.core.Atak
import com.fieldtakhub.hub.core.Callsign
import com.fieldtakhub.hub.core.MeshChannel
import com.fieldtakhub.hub.core.Codec
import com.fieldtakhub.hub.platform.Intents
import com.fieldtakhub.hub.ui.components.FrameLine
import com.fieldtakhub.hub.ui.components.HubHeader
import com.fieldtakhub.hub.ui.components.HubPanel
import com.fieldtakhub.hub.ui.components.KeyValue
import com.fieldtakhub.hub.ui.components.SecondaryButton
import com.fieldtakhub.hub.ui.components.SectionLabel
import com.fieldtakhub.hub.ui.theme.Hub

@Composable
fun CheatSheetScreen(state: HubState, vm: HubViewModel) {
    val active = state.active?.pkg
    val opord = state.opordSource?.pkg?.opord
    val day = opord?.days?.firstOrNull()
    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 12.dp)
    ) {
        HubHeader()
        SectionLabel("Ściąga taktyczna", Modifier.padding(top = 10.dp), color = Hub.Text)

        HubPanel {
            Text("Kontakt z organizatorem", color = Hub.Text, fontWeight = FontWeight.SemiBold)
            val phone = state.emergencyPhone
            if (phone.isNullOrBlank()) {
                Text("Paczka nie zawiera numeru organizatora.", color = Hub.Muted, fontSize = 13.sp)
            } else {
                Text(phone, color = Hub.Cyan, fontSize = 24.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(vertical = 4.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    SecondaryButton("Zadzwoń", Icons.Filled.Phone, Modifier.weight(1f)) { vm.startIntent(Intents.dial(phone)) }
                    SecondaryButton("SMS", Icons.Filled.Sms, Modifier.weight(1f)) { vm.startIntent(Intents.sms(phone, "")) }
                }
            }
        }

        Spacer(Modifier.height(10.dp))
        HubPanel {
            Text("Częstotliwości", color = Hub.Text, fontWeight = FontWeight.SemiBold)
            val voices = active?.voiceChannels.orEmpty()
            for (v in voices) {
                FrameLine(listOf(listOf(v.band, v.role).filter { it.isNotBlank() }.joinToString(" ") + ": " + v.frequency, v.tone).filter { it.isNotBlank() }.joinToString(" · "))
            }
            day?.let { d ->
                if (d.primaryFreq.isNotBlank()) FrameLine("Główna: ${d.primaryFreq}")
                if (d.backupFreq.isNotBlank()) FrameLine("Zapasowa: ${d.backupFreq}")
                if (d.hqFreq.isNotBlank()) FrameLine("HQ: ${d.hqFreq}")
            }
            active?.mesh?.let { m ->
                FrameLine(listOfNotNull("LoRa: " + (m.region ?: "?"), m.modemPreset, "MESH", m.channelName.ifBlank { null }).joinToString(" · "))
                val psk = Codec.base64Decode(m.pskBase64) ?: m.channelUrl?.let { MeshChannel.decodeUrl(it)?.primary?.psk }
                if (psk != null) Text("Klucz kanału: " + MeshChannel.describePsk(psk), color = Hub.TextDim, fontSize = 12.sp)
            }
            if (voices.isEmpty() && active?.mesh == null && day == null) Text("Brak danych — zaimportuj paczkę.", color = Hub.Muted, fontSize = 13.sp)
        }

        if (opord != null) {
            Spacer(Modifier.height(10.dp))
            HubPanel {
                Text("Hasła i kryptonimy", color = Hub.Text, fontWeight = FontWeight.SemiBold)
                for (c in opord.codewords) {
                    Text(listOf(c.word, c.letter, c.digit).filter { it.isNotBlank() }.joinToString(" · "), color = Hub.Text, fontSize = 15.sp, modifier = Modifier.padding(top = 6.dp))
                    if (c.duress.isNotBlank()) Text("Przymus: ${c.duress}", color = Hub.Red, fontSize = 13.sp)
                }
                day?.let { d ->
                    if (d.challenge.isNotBlank()) KeyValue("Hasło / odzew", d.challenge)
                    if (d.sarneg.isNotBlank()) KeyValue("Klucz SARNEG", d.sarneg, mono = true)
                    if (d.targetCodeNames.isNotBlank()) KeyValue("Kryptonimy obiektów", d.targetCodeNames)
                }
            }
        }

        Spacer(Modifier.height(10.dp))
        HubPanel {
            Text("Moje ustawienia", color = Hub.Text, fontWeight = FontWeight.SemiBold)
            KeyValue("Callsign (ATAK)", state.callsign.ifBlank { "—" })
            KeyValue("Meshtastic", if (state.callsign.isBlank()) "—" else "${Callsign.longName(state.callsign)} [${state.shortName}]", mono = true)
            active?.let { p ->
                KeyValue("Strona", listOf(p.faction, Atak.nearestTeam(p.factionColor)?.let { "zespół ATAK: $it" }).filterNotNull().filter { it.isNotBlank() }.joinToString(" · ").ifBlank { "—" })
                p.server?.let { KeyValue("Serwer TAK", it.endpoint, mono = true) }
            }
        }

        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            SecondaryButton("Uprawnienia", Icons.Filled.Shield, Modifier.weight(1f)) { vm.showPermissions(true) }
            SecondaryButton("Odśwież status", Icons.Filled.Refresh, Modifier.weight(1f)) { vm.refreshStatus() }
        }
        Text(
            "Field TAK Hub ${BuildConfig.VERSION_NAME} · obsługuje paczki Buildera: v2 (podpisane), v2.4, v3.0, QR offline i OPORD.",
            color = Hub.Muted, fontSize = 11.sp, modifier = Modifier.padding(top = 12.dp)
        )
        Spacer(Modifier.height(16.dp))
    }
}
