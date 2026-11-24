package com.fieldtakhub.hub

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.core.content.IntentCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import com.fieldtakhub.hub.ui.HubRoot
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    private val vm: HubViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { HubRoot(vm) }
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                vm.commands.collect { execute(it) }
            }
        }
        if (savedInstanceState == null) handleIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        vm.refreshStatus()
    }

    private fun execute(command: UiCommand) {
        when (command) {
            is UiCommand.Launch -> {
                try {
                    startActivity(command.intent)
                } catch (e: ActivityNotFoundException) {
                    val fallback = command.fallback
                    val opened = fallback != null && try {
                        startActivity(fallback)
                        true
                    } catch (e2: ActivityNotFoundException) {
                        false
                    }
                    if (!opened) command.failMessage?.let { vm.notify("Nie można otworzyć", it, error = true) }
                } catch (e: SecurityException) {
                    command.failMessage?.let { vm.notify("Nie można otworzyć", it, error = true) }
                }
            }
        }
    }

    /** fieldtak:// links, .ftak files opened from other apps and files shared to the hub. */
    private fun handleIntent(intent: Intent?) {
        if (intent == null) return
        when (intent.action) {
            Intent.ACTION_VIEW -> {
                val data = intent.data ?: return
                when (data.scheme?.lowercase()) {
                    "fieldtak", "ftak" -> vm.handleLink(data.toString())
                    "content", "file" -> vm.importFromUri(data)
                }
            }
            Intent.ACTION_SEND -> {
                val stream = IntentCompat.getParcelableExtra(intent, Intent.EXTRA_STREAM, Uri::class.java)
                if (stream != null) {
                    vm.importFromUri(stream)
                } else {
                    intent.getStringExtra(Intent.EXTRA_TEXT)?.let { vm.handleLink(it) }
                }
            }
        }
    }
}
