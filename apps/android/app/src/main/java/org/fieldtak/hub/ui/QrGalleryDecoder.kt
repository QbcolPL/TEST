package org.fieldtak.hub.ui
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.NotFoundException
import com.google.zxing.RGBLuminanceSource
import com.google.zxing.common.GlobalHistogramBinarizer
import com.google.zxing.common.HybridBinarizer
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.fieldtak.hub.util.FtakPackageLibraryManager
import java.util.EnumMap
import kotlin.math.max
object QrImageDecoder {
    suspend fun decodeFromUri(context: Context, uri: Uri): String? = withContext(Dispatchers.Default) {
        try {
            val bmp = loadScaled(context, uri, 1600) ?: return@withContext null
            val w = bmp.width; val h = bmp.height; val px = IntArray(w * h)
            bmp.getPixels(px, 0, w, 0, 0, w, h); bmp.recycle()
            val src = RGBLuminanceSource(w, h, px)
            val hints = EnumMap<DecodeHintType, Any>(DecodeHintType::class.java).apply {
                put(DecodeHintType.POSSIBLE_FORMATS, listOf(BarcodeFormat.QR_CODE))
                put(DecodeHintType.TRY_HARDER, java.lang.Boolean.TRUE)
                put(DecodeHintType.CHARACTER_SET, "UTF-8")
            }
            val r = MultiFormatReader().apply { setHints(hints) }
            try {
                r.decodeWithState(BinaryBitmap(HybridBinarizer(src))).text
            } catch (_: NotFoundException) {
                try {
                    r.decodeWithState(BinaryBitmap(GlobalHistogramBinarizer(src))).text
                } catch (_: NotFoundException) {
                    r.decodeWithState(BinaryBitmap(HybridBinarizer(src.invert()))).text
                }
            } finally {
                r.reset()
            }
        } catch (_: Exception) {
            null
        }
    }
    private fun loadScaled(context: Context, uri: Uri, maxD: Int): Bitmap? {
        val b = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        context.contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, b) }
        if (b.outWidth <= 0) return null
        if (b.outHeight <= 0) return null
        var s = 1; val m = max(b.outWidth, b.outHeight); while (m / s > maxD) s *= 2
        val o = BitmapFactory.Options().apply { inSampleSize = s; inPreferredConfig = Bitmap.Config.ARGB_8888 }
        return context.contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, o) }
    }
}
@Composable
fun PickQrFromGalleryButton(onQrDecoded: (String) -> Unit, modifier: Modifier = Modifier) {
    val ctx = LocalContext.current; val scope = rememberCoroutineScope(); var busy by remember { mutableStateOf(false) }
    DisposableEffect(onQrDecoded) { FtakPackageLibraryManager.latestScannerCallback = onQrDecoded; onDispose {} }
    val l = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { u: Uri? ->
        if (u == null) return@rememberLauncherForActivityResult
        busy = true
        scope.launch {
            val decoded: String? = QrImageDecoder.decodeFromUri(ctx, u)
            busy = false
            if (!decoded.isNullOrBlank()) {
                onQrDecoded(decoded)
            } else {
                Toast.makeText(ctx, "Nie znaleziono kodu QR na obrazie.", Toast.LENGTH_LONG).show()
            }
        }
    }
    Button(onClick = { l.launch("image/*") }, enabled = !busy, shape = RoundedCornerShape(12.dp), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF2CE5D0), contentColor = Color(0xFF041818)), border = BorderStroke(1.dp, Color(0xFF071214)), modifier = modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp).height(48.dp).zIndex(20f)) {
        Text(if (busy) "ODCZYTYWANIE KODU QR..." else "WCZYTAJ KOD QR Z GALERII / PLIKU", fontWeight = FontWeight.Bold, fontSize = 13.sp)
    }
}