package org.fieldtak.hub

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.view.Window

class MainActivity : Activity() {

    companion object {
        const val REQ_QR_SCAN = 9001
        const val REQ_PICK_FILE = 9002
    }

    private var dashboardController: FthTacticalDashboardController? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        requestWindowFeature(Window.FEATURE_NO_TITLE)

        val controller = FthTacticalDashboardController(this)
        dashboardController = controller
        setContentView(controller.createRootView())

        handleIncomingIntent(intent)
    }

    override fun onNewIntent(intent: Intent?) {
        super.onNewIntent(intent)
        handleIncomingIntent(intent)
    }

    private fun handleIncomingIntent(intent: Intent?) {
        val dataUri = intent?.data ?: return
        val scheme = dataUri.scheme?.lowercase() ?: ""
        if (scheme == "http" || scheme == "https" || scheme == "meshtastic") {
            dashboardController?.handleIncomingQrOrFilePayload(dataUri.toString())
        } else {
            dashboardController?.handlePickedFileUri(dataUri)
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != RESULT_OK || data == null) return

        when (requestCode) {
            REQ_QR_SCAN -> {
                val contents = data.getStringExtra("SCAN_RESULT") ?: return
                dashboardController?.handleIncomingQrOrFilePayload(contents)
            }
            REQ_PICK_FILE -> {
                val uri = data.data ?: return
                dashboardController?.handlePickedFileUri(uri)
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        FthHotspotP2pServer.stopServer()
    }
}
