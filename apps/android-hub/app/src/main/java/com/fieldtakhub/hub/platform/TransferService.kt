package com.fieldtakhub.hub.platform

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.fieldtakhub.hub.MainActivity

/**
 * Foreground service that keeps the app process (and [LocalServer]) running while ATAK or
 * another phone downloads a file. Without it Android may freeze the app as soon as ATAK
 * comes to the front. Stops itself when nothing is shared any more.
 */
class TransferService : Service() {
    private val handler = Handler(Looper.getMainLooper())
    private val check = object : Runnable {
        override fun run() {
            LocalServer.stopIfIdle()
            if (!LocalServer.hasShares) {
                stopSelf()
            } else {
                handler.postDelayed(this, 15_000)
            }
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL, "Udostępnianie paczek", NotificationManager.IMPORTANCE_LOW)
        )
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val text = intent?.getStringExtra(EXTRA_TEXT) ?: "Field TAK Hub udostępnia pliki"
        val notification = NotificationCompat.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle("Field TAK Hub")
            .setContentText(text)
            .setOngoing(true)
            .setContentIntent(open)
            .build()
        ServiceCompat.startForeground(this, NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        handler.removeCallbacks(check)
        handler.postDelayed(check, 15_000)
        return START_NOT_STICKY
    }

    override fun onTimeout(startId: Int, fgsType: Int) {
        LocalServer.unpublishAll { true }
        stopSelf()
    }

    override fun onDestroy() {
        handler.removeCallbacks(check)
        super.onDestroy()
    }

    companion object {
        private const val CHANNEL = "transfers"
        private const val NOTIFICATION_ID = 41
        private const val EXTRA_TEXT = "text"

        fun start(context: Context, text: String) {
            val intent = Intent(context, TransferService::class.java).putExtra(EXTRA_TEXT, text)
            try {
                ContextCompat.startForegroundService(context, intent)
            } catch (e: Exception) {
                // Starting from the background is not allowed; the share still works while the app is visible.
            }
        }
    }
}
