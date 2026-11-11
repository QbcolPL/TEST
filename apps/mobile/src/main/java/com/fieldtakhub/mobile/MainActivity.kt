package com.fieldtakhub.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import com.fieldtakhub.mobile.data.FtakPackageRepository
import com.fieldtakhub.mobile.ui.screens.OnboardingScreen
import com.fieldtakhub.mobile.ui.screens.OpordAndCheatSheetScreen
import com.fieldtakhub.mobile.ui.screens.PackageManagerScreen
import com.fieldtakhub.mobile.ui.screens.ScannerScreen
import com.fieldtakhub.mobile.ui.screens.evaluatePermissions
import com.fieldtakhub.mobile.ui.theme.AbyssalSlate
import com.fieldtakhub.mobile.ui.theme.FieldTakHubTheme
import com.fieldtakhub.mobile.ui.theme.TacticalBottomBar
import com.fieldtakhub.mobile.ui.theme.TacticalTab
import com.fieldtakhub.mobile.ui.theme.TacticalTopBar

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FieldTakHubTheme {
                val context = LocalContext.current

                // Wymuszone otwarcie ekranu powitalnego (Onboarding & Permissions) przy pierwszym uruchomieniu
                // lub gdy brakuje uprawnien bojowych
                val hasCompletedFirstRun = remember { FtakPackageRepository.isOnboardingCompleted(context) }
                val initialPerms = remember { evaluatePermissions(context) }
                var isOnboardingOpen by remember {
                    mutableStateOf(!hasCompletedFirstRun || initialPerms.any { !it.isGranted })
                }

                var currentTab by remember { mutableStateOf(TacticalTab.SCANNER) }
                var activePackage by remember {
                    val all = FtakPackageRepository.listInstalledPackages(context)
                    val savedName = FtakPackageRepository.getActivePackageFileName(context)
                    mutableStateOf(all.firstOrNull { it.fileName == savedName } ?: all.firstOrNull())
                }

                Scaffold(
                    containerColor = AbyssalSlate,
                    topBar = {
                        TacticalTopBar(
                            isOnboardingOpen = isOnboardingOpen,
                            onToggleOnboarding = { isOnboardingOpen = !isOnboardingOpen }
                        )
                    },
                    bottomBar = {
                        TacticalBottomBar(
                            currentTab = currentTab,
                            isOnboardingOpen = isOnboardingOpen,
                            onSelectTab = { selectedTab ->
                                FtakPackageRepository.setOnboardingCompleted(context, true)
                                isOnboardingOpen = false
                                currentTab = selectedTab
                            }
                        )
                    }
                ) { innerPadding ->
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .background(AbyssalSlate)
                            .padding(innerPadding)
                    ) {
                        if (isOnboardingOpen) {
                            OnboardingScreen(
                                activePackage = activePackage,
                                onFinishOnboarding = {
                                    FtakPackageRepository.setOnboardingCompleted(context, true)
                                    isOnboardingOpen = false
                                }
                            )
                        } else {
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
                                        onActivePackageChanged = { updated -> activePackage = updated },
                                        onOpenPermissions = { isOnboardingOpen = true }
                                    )
                                }
                                TacticalTab.OPORD -> {
                                    OpordAndCheatSheetScreen(
                                        activePackage = activePackage,
                                        initialFocusOnCheatSheet = false
                                    )
                                }
                                TacticalTab.CHEATSHEET -> {
                                    OpordAndCheatSheetScreen(
                                        activePackage = activePackage,
                                        initialFocusOnCheatSheet = true
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}