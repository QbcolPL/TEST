package com.fieldtakhub.hub.ui.screens

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Bluetooth
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.Satellite
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.fieldtakhub.hub.HubState
import com.fieldtakhub.hub.HubViewModel
import com.fieldtakhub.hub.core.Atak
import com.fieldtakhub.hub.platform.AppInspector
import com.fieldtakhub.hub.ui.components.HubHeader
import com.fieldtakhub.hub.ui.components.PrimaryButton
import com.fieldtakhub.hub.ui.components.SecondaryButton
import com.fieldtakhub.hub.ui.components.SectionLabel
import com.fieldtakhub.hub.ui.components.hubBackground
import com.fieldtakhub.hub.ui.theme.Hub

object HubPermissions {
    val location = arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
    val camera = arrayOf(Manifest.permission.CAMERA)

    /** Bluetooth for the Meshtastic radio; on Android 13+ also "Nearby devices" for the Wi-Fi hotspot. */
    val radio: Array<String>
        get() = buildList {
            if (Build.VERSION.SDK_INT >= 31) {
                add(Manifest.permission.BLUETOOTH_SCAN)
                add(Manifest.permission.BLUETOOTH_CONNECT)
            }
            if (Build.VERSION.SDK_INT >= 33) add(Manifest.permission.NEARBY_WIFI_DEVICES)
        }.toTypedArray()

    fun granted(context: Context, perms: Array<String>): Boolean =
        perms.all { ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED }

    /** Permissions needed to start a local-only hotspot on this Android version. */
    val hotspot: Array<String>
        get() = if (Build.VERSION.SDK_INT >= 33) arrayOf(Manifest.permission.NEARBY_WIFI_DEVICES) else location
}

@Composable
fun OnboardingScreen(state: HubState, vm: HubViewModel, firstRun: Boolean) {
    val context = LocalContext.current
    var tick by remember { mutableIntStateOf(0) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { tick++ }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { tick++ }

    val locationOk = remember(tick) { HubPermissions.granted(context, HubPermissions.location) }
    val cameraOk = remember(tick) { HubPermissions.granted(context, HubPermissions.camera) }
    val radioOk = remember(tick) { HubPermissions.granted(context, HubPermissions.radio) }
    val installOk = remember(tick) { AppInspector.canInstallPackages(context) }

    Column(
        Modifier
            .fillMaxSize()
            .hubBackground()
            .systemBarsPadding()
            .verticalScroll(rememberScrollState())
            .padding(16.dp)
    ) {
        HubHeader(onBack = if (firstRun) null else ({ vm.showPermissions(false) }))
        SectionLabel("Moduł: uprawnienia & paczki", Modifier.padding(top = 8.dp))
        Text("Onboarding & Permissions", color = Hub.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold)
        Text(
            "Każde uprawnienie służy jednej funkcji. Możesz je nadać teraz albo później — aplikacja zapyta ponownie, gdy będzie potrzebne.",
            color = Hub.TextDim, fontSize = 13.sp, modifier = Modifier.padding(top = 4.dp, bottom = 10.dp)
        )

        PermissionItem(
            Icons.Filled.Satellite, "Lokalizacja GPS", locationOk,
            "Pozycja do alarmu SOS z siatką MGRS (SMS do organizatora). Na Androidzie 8–12 także do uruchomienia hotspotu Wi-Fi przy udostępnianiu paczki. Lokalizacja nie jest śledzona w tle ani nigdzie wysyłana."
        ) { launcher.launch(HubPermissions.location) }

        PermissionItem(
            Icons.Filled.PhotoCamera, "Dostęp do aparatu (QR)", cameraOk,
            "Skanowanie kodów QR z Buildera, plakatów i telefonów innych graczy. Obraz z aparatu nie jest zapisywany."
        ) { launcher.launch(HubPermissions.camera) }

        PermissionItem(
            Icons.Filled.Bluetooth, "Łączność Bluetooth & LoRa", radioOk,
            if (Build.VERSION.SDK_INT >= 33)
                "Połączenie z radiem Meshtastic (LoRa) przez Bluetooth oraz „Urządzenia w pobliżu”, bez których Android nie uruchomi hotspotu Wi-Fi do dzielenia się paczką."
            else
                "Połączenie z radiem Meshtastic (LoRa) przez Bluetooth. Na tej wersji Androida nadawane automatycznie przy instalacji."
        ) { launcher.launch(HubPermissions.radio) }

        PermissionItem(
            Icons.Filled.FolderOpen, "Dostęp do plików (.ftak)", true,
            "Paczki wskazujesz w systemowym oknie wyboru plików albo otwierasz z komunikatora — aplikacja widzi tylko pliki, które sam wybierzesz. Nie potrzebuje dostępu do całej pamięci."
        ) { }

        PermissionItem(
            Icons.Filled.Shield, "Instalacja pakietów (.apk)", installOk,
            "Instalacja i aktualizacja ATAK, wtyczek i Meshtastic zapisanych w paczce organizatora. Każdą instalację i tak potwierdzasz w oknie systemu."
        ) { vm.startIntent(AppInspector.installPermissionIntent(context)) }

        Spacer(Modifier.height(6.dp))
        SecondaryButton(
            "Nadaj wszystkie naraz",
            modifier = Modifier.fillMaxWidth(),
            enabled = !(locationOk && cameraOk && radioOk)
        ) {
            launcher.launch(HubPermissions.location + HubPermissions.camera + HubPermissions.radio)
        }

        Spacer(Modifier.height(18.dp))
        ProfileCard(state)
        Spacer(Modifier.height(18.dp))
        PrimaryButton(if (firstRun) "PRZEJDŹ DO SKANERA" else "GOTOWE") {
            if (firstRun) vm.completeOnboarding() else vm.showPermissions(false)
        }
        Spacer(Modifier.height(24.dp))
    }
}

@Composable
private fun PermissionItem(icon: ImageVector, title: String, granted: Boolean, explanation: String, onGrant: () -> Unit) {
    val shape = RoundedCornerShape(10.dp)
    Column(
        Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp)
            .clip(shape)
            .background(Hub.Panel)
            .border(1.dp, if (granted) Hub.Border else Hub.BorderStrong, shape)
            .clickable(enabled = !granted, onClick = onGrant)
            .padding(horizontal = 14.dp, vertical = 12.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(icon, contentDescription = null, tint = Hub.Cyan, modifier = Modifier.size(28.dp))
            Spacer(Modifier.width(14.dp))
            Text(title, color = Hub.Text, fontSize = 17.sp, fontWeight = FontWeight.Medium, modifier = Modifier.weight(1f))
            if (granted) {
                Icon(Icons.Filled.Check, contentDescription = "Nadane", tint = Hub.Green, modifier = Modifier.size(30.dp))
            } else {
                Text(
                    "NADAJ",
                    color = Hub.Background,
                    fontWeight = FontWeight.Bold,
                    fontSize = 12.sp,
                    modifier = Modifier
                        .clip(RoundedCornerShape(6.dp))
                        .background(Hub.Cyan)
                        .padding(horizontal = 10.dp, vertical = 6.dp)
                )
            }
        }
        Text(explanation, color = Hub.TextDim, fontSize = 13.sp, lineHeight = 18.sp, modifier = Modifier.padding(start = 42.dp, top = 6.dp))
    }
}

/** "AKTYWNY PROFIL: BLUFOR" card. */
@Composable
fun ProfileCard(state: HubState, onClick: (() -> Unit)? = null) {
    val active = state.active?.pkg
    val color = Atak.parseHexColor(active?.factionColor)?.let { Color(0xFF000000 or it.toLong()) } ?: Hub.Cyan
    val shape = RoundedCornerShape(16.dp)
    Box(
        Modifier
            .fillMaxWidth()
            .shadow(16.dp, shape, ambientColor = Hub.Cyan, spotColor = Hub.Cyan)
            .clip(shape)
            .background(Hub.Panel)
            .border(2.dp, Hub.Cyan, shape)
            .then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier)
            .padding(vertical = 18.dp, horizontal = 14.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
            Text("AKTYWNY PROFIL:", color = Hub.TextDim, fontSize = 15.sp, letterSpacing = 1.sp)
            Text(
                active?.faction?.ifBlank { null }?.uppercase() ?: active?.title?.uppercase() ?: "BRAK PACZKI",
                color = if (active != null) color else Hub.Muted,
                fontSize = 30.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.5.sp
            )
            if (active != null && active.eventName.isNotBlank() && active.faction.isNotBlank()) {
                Text(active.eventName, color = Hub.TextDim, fontSize = 13.sp)
            }
        }
    }
}
