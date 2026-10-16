package org.fieldtak.hub.ui

import android.content.ContentResolver
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.RGBLuminanceSource
import com.google.zxing.common.GlobalHistogramBinarizer
import com.google.zxing.common.HybridBinarizer
import java.util.EnumMap

object QrImageDecoder {
    fun decodeUri(context: Context, uri: Uri): String? = decodeFromUri(context, uri)
    fun decodeFromUri(context: Context, uri: Uri): String? = decodeFromUri(context.contentResolver, uri)

    fun decodeFromUri(resolver: ContentResolver, uri: Uri): String? {
        var sampledBitmap: Bitmap? = null
        return try {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

            var sampleSize = 1
            while ((bounds.outHeight / 2 / sampleSize) >= 1600 && (bounds.outWidth / 2 / sampleSize) >= 1600) sampleSize *= 2

            val opts = BitmapFactory.Options().apply { inSampleSize = sampleSize; inPreferredConfig = Bitmap.Config.ARGB_8888 }
            sampledBitmap = resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, opts) } ?: return null

            for (maxDim in listOf(1400, 960, 640)) {
                var candidate: Bitmap? = null
                try {
                    val w = sampledBitmap.width; val h = sampledBitmap.height
                    candidate = if (w <= maxDim && h <= maxDim) sampledBitmap else {
                        val r = w.toFloat() / h.toFloat()
                        val nw = if (r > 1f) maxDim else (maxDim * r).toInt().coerceAtLeast(1)
                        val nh = if (r > 1f) (maxDim / r).toInt().coerceAtLeast(1) else maxDim
                        Bitmap.createScaledBitmap(sampledBitmap, nw, nh, true)
                    }
                    val px = IntArray(candidate.width * candidate.height)
                    candidate.getPixels(px, 0, candidate.width, 0, 0, candidate.width, candidate.height)
                    val src = RGBLuminanceSource(candidate.width, candidate.height, px)
                    val hints = EnumMap<DecodeHintType, Any>(DecodeHintType::class.java).apply {
                        put(DecodeHintType.TRY_HARDER, true)
                        put(DecodeHintType.POSSIBLE_FORMATS, listOf(BarcodeFormat.QR_CODE))
                        put(DecodeHintType.CHARACTER_SET, "UTF-8")
                    }
                    val reader = MultiFormatReader().apply { setHints(hints) }
                    try { return reader.decodeWithState(BinaryBitmap(HybridBinarizer(src))).text } catch (_: Exception) { reader.reset() }
                    try { return reader.decodeWithState(BinaryBitmap(GlobalHistogramBinarizer(src))).text } catch (_: Exception) { reader.reset() }
                    try { return reader.decodeWithState(BinaryBitmap(HybridBinarizer(src.invert()))).text } catch (_: Exception) { reader.reset() }
                } finally {
                    if (candidate != null && candidate !== sampledBitmap) candidate.recycle()
                }
            }
            null
        } catch (_: Exception) { null } finally { sampledBitmap?.recycle() }
    }
}
