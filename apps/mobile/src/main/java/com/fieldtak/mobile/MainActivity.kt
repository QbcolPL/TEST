package com.fieldtak.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import com.fieldtak.mobile.ui.onboarding.OnboardingScreen
import com.fieldtak.mobile.ui.packages.PackageManagerScreen
import com.fieldtak.mobile.ui.theme.FieldTakHubTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FieldTakHubTheme {
                var showOnboarding by remember { mutableStateOf(true) }
                if (showOnboarding) {
                    OnboardingScreen(onAllGranted = { showOnboarding = false })
                } else {
                    PackageManagerScreen()
                }
            }
        }
    }
}
