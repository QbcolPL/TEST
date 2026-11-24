package com.fieldtakhub.hub.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fieldtakhub.hub.HubViewModel
import com.fieldtakhub.hub.Tab
import com.fieldtakhub.hub.ui.components.HubBottomBar
import com.fieldtakhub.hub.ui.components.hubBackground
import com.fieldtakhub.hub.ui.screens.CheatSheetScreen
import com.fieldtakhub.hub.ui.screens.HubDialogs
import com.fieldtakhub.hub.ui.screens.OnboardingScreen
import com.fieldtakhub.hub.ui.screens.OpordScreen
import com.fieldtakhub.hub.ui.screens.PackagesScreen
import com.fieldtakhub.hub.ui.screens.ScannerScreen
import com.fieldtakhub.hub.ui.theme.HubTheme

@Composable
fun HubRoot(vm: HubViewModel) {
    val state by vm.state.collectAsStateWithLifecycle()
    HubTheme {
        Box(Modifier.fillMaxSize().hubBackground()) {
            when {
                !state.loaded -> Unit
                !state.onboardingDone -> OnboardingScreen(state, vm, firstRun = true)
                state.showPermissions -> {
                    BackHandler { vm.showPermissions(false) }
                    OnboardingScreen(state, vm, firstRun = false)
                }
                else -> {
                    BackHandler(enabled = state.tab != Tab.SCANNER) { vm.selectTab(Tab.SCANNER) }
                    Scaffold(
                        containerColor = Color.Transparent,
                        bottomBar = { HubBottomBar(state.tab, state.opordSource != null) { vm.selectTab(it) } }
                    ) { padding ->
                        Box(Modifier.fillMaxSize().padding(padding)) {
                            when (state.tab) {
                                Tab.SCANNER -> ScannerScreen(state, vm)
                                Tab.PACKAGES -> PackagesScreen(state, vm)
                                Tab.OPORD -> OpordScreen(state, vm)
                                Tab.CHEATSHEET -> CheatSheetScreen(state, vm)
                            }
                        }
                    }
                }
            }
            HubDialogs(state, vm)
        }
    }
}
