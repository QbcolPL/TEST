package com.fieldtakhub.hub.platform

import android.annotation.SuppressLint
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.net.Uri
import android.os.Bundle
import android.os.Looper
import androidx.core.content.FileProvider
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.suspendCancellableCoroutine
import java.io.File
import kotlin.coroutines.resume

/** Intents to other apps. Each returns the intent; the activity starts it. */
object Intents {
    const val MESHTASTIC = "com.geeksville.mesh"

    fun view(uri: String, packageName: String? = null): Intent =
        Intent(Intent.ACTION_VIEW, Uri.parse(uri)).apply {
            if (packageName != null) setPackage(packageName)
            addCategory(Intent.CATEGORY_BROWSABLE)
        }

    fun sms(phone: String, body: String): Intent =
        Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:" + Uri.encode(phone))).putExtra("sms_body", body)

    fun dial(phone: String): Intent = Intent(Intent.ACTION_DIAL, Uri.parse("tel:" + Uri.encode(phone)))

    fun geo(lat: Double, lon: Double, label: String): Intent =
        Intent(Intent.ACTION_VIEW, Uri.parse("geo:$lat,$lon?q=$lat,$lon(" + Uri.encode(label) + ")"))

    fun shareFile(context: Context, file: File, mime: String, title: String): Intent {
        val uri = FileProvider.getUriForFile(context, context.packageName + ".files", file)
        val send = Intent(Intent.ACTION_SEND)
            .setType(mime)
            .putExtra(Intent.EXTRA_STREAM, uri)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        send.clipData = ClipData.newRawUri(title, uri)
        return Intent.createChooser(send, title)
    }

    fun copy(context: Context, label: String, text: String) {
        val cm = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText(label, text))
    }
}

/** GPS position for the SOS panel. Uses the platform LocationManager (no Google Play services needed). */
class LocationTracker(context: Context) {
    private val lm = context.applicationContext.getSystemService(Context.LOCATION_SERVICE) as LocationManager
    private val _location = MutableStateFlow<Location?>(null)
    val location: StateFlow<Location?> = _location
    private var running = false

    private val listener = object : LocationListener {
        override fun onLocationChanged(location: Location) {
            val current = _location.value
            if (current == null || location.accuracy <= current.accuracy + 15 || location.time - current.time > 10_000) {
                _location.value = location
            }
        }

        @Deprecated("Deprecated in Java")
        override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
        override fun onProviderEnabled(provider: String) {}
        override fun onProviderDisabled(provider: String) {}
    }

    val gpsEnabled: Boolean get() = runCatching { lm.isProviderEnabled(LocationManager.GPS_PROVIDER) }.getOrDefault(false)

    @SuppressLint("MissingPermission")
    fun start() {
        if (running) return
        try {
            val last = listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER)
                .mapNotNull { p -> runCatching { lm.getLastKnownLocation(p) }.getOrNull() }
                .maxByOrNull { it.time }
            if (last != null && _location.value == null) _location.value = last
            for (p in listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER)) {
                if (runCatching { lm.isProviderEnabled(p) }.getOrDefault(false)) {
                    lm.requestLocationUpdates(p, 2000L, 0f, listener, Looper.getMainLooper())
                }
            }
            running = true
        } catch (e: SecurityException) {
            running = false
        }
    }

    fun stop() {
        runCatching { lm.removeUpdates(listener) }
        running = false
    }
}

object QrCodes {
    fun encode(text: String, size: Int = 720): Bitmap {
        val hints = mapOf(
            EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M,
            EncodeHintType.MARGIN to 1,
            EncodeHintType.CHARACTER_SET to "UTF-8"
        )
        val matrix = QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, size, size, hints)
        val pixels = IntArray(matrix.width * matrix.height)
        for (y in 0 until matrix.height) {
            val row = y * matrix.width
            for (x in 0 until matrix.width) pixels[row + x] = if (matrix.get(x, y)) 0xFF000000.toInt() else 0xFFFFFFFF.toInt()
        }
        return Bitmap.createBitmap(pixels, matrix.width, matrix.height, Bitmap.Config.ARGB_8888)
    }

    /** Reads a QR code from a picture (e.g. a screenshot sent by the organiser). */
    suspend fun decodeImage(context: Context, uri: Uri): String? {
        val image = try { InputImage.fromFilePath(context, uri) } catch (e: Exception) { return null }
        val scanner = BarcodeScanning.getClient(
            BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build()
        )
        return try {
            suspendCancellableCoroutine { cont ->
                scanner.process(image)
                    .addOnSuccessListener { codes -> if (cont.isActive) cont.resume(codes.firstNotNullOfOrNull { it.rawValue }) }
                    .addOnFailureListener { if (cont.isActive) cont.resume(null) }
            }
        } finally {
            scanner.close()
        }
    }
}
