package com.fieldtak.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.data.FtakPackageRepository
import com.fieldtak.mobile.ui.components.TacticalBottomBar
import com.fieldtak.mobile.ui.components.TacticalTab
import com.fieldtak.mobile.ui.components.TacticalTopBar
import com.fieldtak.mobile.ui.onboarding.OnboardingScreen
import com.fieldtak.mobile.ui.onboarding.evaluatePermissions
import com.fieldtak.mobile.ui.packages.PackageManagerScreen
import com.fieldtak.mobile.ui.scanner.ScannerScreen
import com.fieldtak.mobile.ui.theme.AbyssalSlate
import com.fieldtak.mobile.ui.theme.FieldTakHubTheme
import com.fieldtak.mobile.ui.theme.RadarCyan

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FieldTakHubTheme {
                val context = LocalContext.current
                val initialPerms = remember { evaluatePermissions(context) }
                var showOnboarding by remember {
                    mutableStateOf(initialPerms.any { !it.isGranted })
                }

                var currentTab by remember { mutableStateOf(TacticalTab.SCANNER) }
                var activePackage by remember {
                    val all = FtakPackageRepository.listInstalledPackages(context)
                    val savedName = FtakPackageRepository.getActivePackageFileName(context)
                    mutableStateOf(all.firstOrNull { it.fileName == savedName } ?: all.firstOrNull())
                }

                if (showOnboarding) {
                    OnboardingScreen(onContinueToApp = { showOnboarding = false })
                } else {
                    Scaffold(
                        containerColor = AbyssalSlate,
                        topBar = {
                            TacticalTopBar(
                                activePackage = activePackage,
                                onOpenPermissions = { showOnboarding = true }
                            )
                        },
                        bottomBar = {
                            TacticalBottomBar(
                                currentTab = currentTab,
                                onSelectTab = { currentTab = it }
                            )
                        }
                    ) { innerPadding ->
                        Box(
                            modifier = Modifier
                                .fillMaxSize()
                                .background(AbyssalSlate)
                                .padding(innerPadding)
                        ) {
                            when (currentTab) {
                                TacticalTab.SCANNER -> {
                                    ScannerScreen(
                                        activePackage = activePackage,
                                        onPackageLoaded = { loaded -> activePackage = loaded }
                                    )
                                }
                                TacticalTab.PACKAGES -> {
                                    PackageManagerScreen(
                                        activePackage = activePackage,
                                        onActivePackageChanged = { updated -> activePackage = updated }
                                    )
                                }
                                TacticalTab.OPORD -> {
                                    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
                                        Text("Moduł Czytnika OPORD (Dni + Mapa Załącznik 3) — Etap 4", color = RadarCyan, fontSize = 13.sp)
                                    }
                                }
                                TacticalTab.CHEATSHEET -> {
                                    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
                                        Text("Moduł Ściągi Taktycznej & Alarmu SOS MGRS — Etap 4", color = RadarCyan, fontSize = 13.sp)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}