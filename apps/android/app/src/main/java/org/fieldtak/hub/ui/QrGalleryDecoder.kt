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
            // 1. Odczyt samych wymiarów bez alokacji pikseli w pamięci RAM (Ochrona przed OutOfMemoryError)
            val boundsOptions = BitmapFactory.Options().apply {
                inJustDecodeBounds = true
            }
            resolver.openInputStream(uri)?.use { stream ->
                BitmapFactory.decodeStream(stream, null, boundsOptions)
            }

            if (boundsOptions.outWidth <= 0 || boundsOptions.outHeight <= 0) return null

            // 2. Wyliczenie współczynnika redukcji inSampleSize przed wczytaniem bitmapy do RAM
            val decodeOptions = BitmapFactory.Options().apply {
                inSampleSize = calculateInSampleSize(boundsOptions.outWidth, boundsOptions.outHeight, 1600)
                inPreferredConfig = Bitmap.Config.ARGB_8888
            }

            sampledBitmap = resolver.openInputStream(uri)?.use { stream ->
                BitmapFactory.decodeStream(stream, null, decodeOptions)
            } ?: return null

            decodeBitmapMultiPass(sampledBitmap)
        } catch (e: Exception) {
            e.printStackTrace()
            null
        } finally {
            sampledBitmap?.recycle()
        }
    }

    fun decodeBitmapMultiPass(bitmap: Bitmap): String? {
        val targetSizes = listOf(1400, 960, 640)
        for (maxDim in targetSizes) {
            var candidate: Bitmap? = null
            try {
                candidate = scaleBitmapDown(bitmap, maxDim)
                val decoded = decodeSingleBitmap(candidate)
                if (decoded != null) return decoded
            } finally {
                if (candidate != null && candidate !== bitmap) {
                    candidate.recycle()
                }
            }
        }
        return null
    }

    private fun decodeSingleBitmap(bitmap: Bitmap): String? {
        val width = bitmap.width
        val height = bitmap.height
        if (width <= 0 || height <= 0) return null

        val pixels = IntArray(width * height)
        bitmap.getPixels(pixels, 0, width, 0, 0, width, height)

        val source = RGBLuminanceSource(width, height, pixels)
        val hints = EnumMap<DecodeHintType, Any>(DecodeHintType::class.java).apply {
            put(DecodeHintType.TRY_HARDER, true)
            put(DecodeHintType.POSSIBLE_FORMATS, listOf(BarcodeFormat.QR_CODE))
            put(DecodeHintType.CHARACTER_SET, "UTF-8")
        }
        val reader = MultiFormatReader().apply { setHints(hints) }

        try {
            return reader.decodeWithState(BinaryBitmap(HybridBinarizer(source))).text
        } catch (_: Exception) {
            reader.reset()
        }

        try {
            return reader.decodeWithState(BinaryBitmap(GlobalHistogramBinarizer(source))).text
        } catch (_: Exception) {
            reader.reset()
        }

        try {
            val inverted = source.invert()
            return reader.decodeWithState(BinaryBitmap(HybridBinarizer(inverted))).text
        } catch (_: Exception) {
            reader.reset()
        }

        return null
    }

    private fun calculateInSampleSize(width: Int, height: Int, maxDim: Int): Int {
        var inSampleSize = 1
        if (height > maxDim || width > maxDim) {
            val halfHeight = height / 2
            val halfWidth = width / 2
            while ((halfHeight / inSampleSize) >= maxDim && (halfWidth / inSampleSize) >= maxDim) {
                inSampleSize *= 2
            }
        }
        return inSampleSize
    }

    private fun scaleBitmapDown(bitmap: Bitmap, maxDimension: Int): Bitmap {
        val width = bitmap.width
        val height = bitmap.height
        if (width <= maxDimension && height <= maxDimension) {
            return bitmap
        }
        val ratio = width.toFloat() / height.toFloat()
        val newWidth: Int
        val newHeight: Int
        if (ratio > 1f) {
            newWidth = maxDimension
            newHeight = (maxDimension / ratio).toInt().coerceAtLeast(1)
        } else {
            newHeight = maxDimension
            newWidth = (maxDimension * ratio).toInt().coerceAtLeast(1)
        }
        return Bitmap.createScaledBitmap(bitmap, newWidth, newHeight, true)
    }
}