package com.fieldtak.mobile.ui.onboarding

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

data class PermissionItem(val title: String, val desc: String, val granted: Boolean)

@Composable
fun OnboardingScreen(onAllGranted: () -> Unit) {
    val permissions = remember {
        mutableStateListOf(
            PermissionItem("LOKALIZACJA GPS / MGRS", "Wyliczanie kwadratu MGRS i pozycji do meldunku SOS.", true),
            PermissionItem("APARAT (SKANER QR)", "Błyskawiczny odczyt kodów QR paczek ftak.", true),
            PermissionItem("BLUETOOTH & LORA", "Wstrzykiwanie kanałów i ksywy do Meshtastic.", true),
            PermissionItem("PAMIĘĆ I PLIKI (.FTAK)", "Zapisywanie map .mbtiles/.kmz oraz paczek operacji.", false),
            PermissionItem("INSTALACJA PAKIETÓW (.APK)", "Automatyczna instalacja ATAK-CIV i pluginów.", false)
        )
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(16.dp)
    ) {
        Column(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.SpaceBetween
        ) {
            Column {
                Text(
                    text = "FIELD TAK HUB MOBILE",
                    color = RadarCyan,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = "connect · deploy · operate",
                    color = TextMuted,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(bottom = 16.dp)
                )

                Text(
                    text = "WYMAGANE UPRAWNIENIA OPERACYJNE",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(bottom = 8.dp)
                )

                LazyColumn(
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.weight(1f)
                ) {
                    items(permissions.size) { index ->
                        val perm = permissions[index]
                        Card(
                            colors = CardDefaults.cardColors(containerColor = TacticalCard),
                            shape = RoundedCornerShape(6.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
                        ) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(12.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column(modifier = Modifier.weight(1f)) {
                                    Text(text = perm.title, color = RadarCyan, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                    Text(text = perm.desc, color = TextMuted, fontSize = 11.sp)
                                }
                                Button(
                                    onClick = { },
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = if (perm.granted) MilSimGreen else InputTrench
                                    ),
                                    shape = RoundedCornerShape(4.dp),
                                    modifier = Modifier.height(32.dp)
                                ) {
                                    Text(
                                        text = if (perm.granted) "NADANO ✓" else "NADAJ",
                                        color = if (perm.granted) AbyssalSlate else RadarCyan,
                                        fontSize = 11.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                }
                            }
                        }
                    }
                }
            }

            Button(
                onClick = onAllGranted,
                colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                shape = RoundedCornerShape(6.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(48.dp)
            ) {
                Text(
                    text = "WEJDŹ DO GRY (START OPERACJI)",
                    color = AbyssalSlate,
                    fontWeight = FontWeight.Bold,
                    fontSize = 13.sp
                )
            }
        }
    }
}
