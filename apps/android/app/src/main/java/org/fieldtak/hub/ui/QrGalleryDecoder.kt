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
        return try {
            val options = BitmapFactory.Options().apply {
                inPreferredConfig = Bitmap.Config.ARGB_8888
            }
            val bitmap = resolver.openInputStream(uri)?.use { inputStream ->
                BitmapFactory.decodeStream(inputStream, null, options)
            } ?: return null

            decodeBitmapMultiPass(bitmap)
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }

    fun decodeBitmapMultiPass(bitmap: Bitmap): String? {
        // Testujemy kilka rozdzielczości (duże zrzuty ekranu vs zdjęcia z aparatu)
        val targetSizes = listOf(1600, 1024, 720)
        for (maxDim in targetSizes) {
            val candidate = scaleBitmapDown(bitmap, maxDim)
            decodeSingleBitmap(candidate)?.let { return it }
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

        // 1. Standardowy HybridBinarizer
        try {
            return reader.decodeWithState(BinaryBitmap(HybridBinarizer(source))).text
        } catch (_: Exception) {
            reader.reset()
        }

        // 2. GlobalHistogramBinarizer (lepszy dla zrzutów ekranu z galerii)
        try {
            return reader.decodeWithState(BinaryBitmap(GlobalHistogramBinarizer(source))).text
        } catch (_: Exception) {
            reader.reset()
        }

        // 3. Odwrócone kolory (dla kodów QR na ciemnym tle)
        try {
            val inverted = source.invert()
            return reader.decodeWithState(BinaryBitmap(HybridBinarizer(inverted))).text
        } catch (_: Exception) {
            reader.reset()
        }

        return null
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