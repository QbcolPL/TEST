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

    fun decodeFromUri(context: Context, uri: Uri): String? {
        return decodeFromUri(context.contentResolver, uri)
    }

    fun decodeFromUri(resolver: ContentResolver, uri: Uri): String? {
        var sampledBitmap: Bitmap? = null
        return try {
            val boundsOptions = BitmapFactory.Options().apply {
                inJustDecodeBounds = true
            }
            resolver.openInputStream(uri)?.use { stream ->
                BitmapFactory.decodeStream(stream, null, boundsOptions)
            }

            if (boundsOptions.outWidth <= 0 || boundsOptions.outHeight <= 0) return null

            val decodeOptions = BitmapFactory.Options().apply {
                var inSampleSize = 1
                if (boundsOptions.outHeight > 1600 || boundsOptions.outWidth > 1600) {
                    val halfHeight = boundsOptions.outHeight / 2
                    val halfWidth = boundsOptions.outWidth / 2
                    while ((halfHeight / inSampleSize) >= 1600 && (halfWidth / inSampleSize) >= 1600) {
                        inSampleSize *= 2
                    }
                }
                this.inSampleSize = inSampleSize
                inPreferredConfig = Bitmap.Config.ARGB_8888
            }

            sampledBitmap = resolver.openInputStream(uri)?.use { stream ->
                BitmapFactory.decodeStream(stream, null, decodeOptions)
            } ?: return null

            val targetSizes = listOf(1400, 960, 640)
            for (maxDim in targetSizes) {
                var candidate: Bitmap? = null
                try {
                    val width = sampledBitmap.width
                    val height = sampledBitmap.height
                    if (width <= maxDim && height <= maxDim) {
                        candidate = sampledBitmap
                    } else {
                        val ratio = width.toFloat() / height.toFloat()
                        val newWidth = if (ratio > 1f) maxDim else (maxDim * ratio).toInt().coerceAtLeast(1)
                        val newHeight = if (ratio > 1f) (maxDim / ratio).toInt().coerceAtLeast(1) else maxDim
                        candidate = Bitmap.createScaledBitmap(sampledBitmap, newWidth, newHeight, true)
                    }

                    val pixels = IntArray(candidate.width * candidate.height)
                    candidate.getPixels(pixels, 0, candidate.width, 0, 0, candidate.width, candidate.height)

                    val source = RGBLuminanceSource(candidate.width, candidate.height, pixels)
                    val hints = EnumMap<DecodeHintType, Any>(DecodeHintType::class.java).apply {
                        put(DecodeHintType.TRY_HARDER, true)
                        put(DecodeHintType.POSSIBLE_FORMATS, listOf(BarcodeFormat.QR_CODE))
                        put(DecodeHintType.CHARACTER_SET, "UTF-8")
                    }
                    val reader = MultiFormatReader().apply { setHints(hints) }

                    try { return reader.decodeWithState(BinaryBitmap(HybridBinarizer(source))).text } catch (_: Exception) { reader.reset() }
                    try { return reader.decodeWithState(BinaryBitmap(GlobalHistogramBinarizer(source))).text } catch (_: Exception) { reader.reset() }
                    try { return reader.decodeWithState(BinaryBitmap(HybridBinarizer(source.invert()))).text } catch (_: Exception) { reader.reset() }
                } finally {
                    if (candidate != null && candidate !== sampledBitmap) candidate.recycle()
                }
            }
            null
        } catch (e: Exception) {
            null
        } finally {
            sampledBitmap?.recycle()
        }
    }
}