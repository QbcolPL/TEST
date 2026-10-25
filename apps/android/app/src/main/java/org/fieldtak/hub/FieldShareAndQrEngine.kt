package org.fieldtak.hub

import android.app.Activity
import android.app.AlertDialog
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.webkit.WebView
import android.widget.*
import com.google.zxing.*
import com.google.zxing.common.GlobalHistogramBinarizer
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.BufferedReader
import java.io.File
import java.io.FileInputStream
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.Inet4Address
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.net.URL
import java.util.Collections
import java.util.EnumMap
import java.util.Locale
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.max
import kotlin.math.roundToInt

object FieldShareAndQrEngine {

    private var activeServerSocket: ServerSocket? = null
    private val serverRunning = AtomicBoolean(false)
    private val downloadCounter = AtomicInteger(0)
    private var activeServerPort: Int = 8080

    fun generateStandardQrBitmap(content: String, sizePx: Int = 520): Bitmap {
        val safeContent = content.ifBlank { "https://fieldtakhub.local" }
        return try {
            val hints = EnumMap<EncodeHintType, Any>(EncodeHintType::class.java).apply {
                put(EncodeHintType.CHARACTER_SET, "UTF-8")
                put(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.M)
                put(EncodeHintType.MARGIN, 2)
            }
            val matrix = QRCodeWriter().encode(safeContent, BarcodeFormat.QR_CODE, sizePx, sizePx, hints)
            val width = matrix.width
            val height = matrix.height
            val pixels = IntArray(width * height)
            for (y in 0 until height) {
                val offset = y * width
                for (x in 0 until width) {
                    pixels[offset + x] = if (matrix.get(x, y)) Color.BLACK else Color.WHITE
                }
            }
            Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).apply {
                setPixels(pixels, 0, width, 0, 0, width, height)
            }
        } catch (_: Exception) {
            Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888).apply {
                eraseColor(Color.WHITE)
            }
        }
    }

    fun decodeQrFromGalleryUri(ctx: Context, imageUri: Uri): String? {
        return try {
            val boundsOpts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            ctx.contentResolver.openInputStream(imageUri)?.use {
                BitmapFactory.decodeStream(it, null, boundsOpts)
            }
            val maxDim = max(boundsOpts.outWidth, boundsOpts.outHeight)
            var sampleSize = 1
            while (maxDim / sampleSize > 1600) {
                sampleSize *= 2
            }
            val decodeOpts = BitmapFactory.Options().apply {
                inSampleSize = sampleSize
                inPreferredConfig = Bitmap.Config.ARGB_8888
            }
            val bitmap = ctx.contentResolver.openInputStream(imageUri)?.use {
                BitmapFactory.decodeStream(it, null, decodeOpts)
            } ?: return null

            val mlKitRes = tryDecodeBitmapWithMlKit(bitmap)
            if (!mlKitRes.isNullOrBlank()) {
                bitmap.recycle()
                return mlKitRes
            }

            val decoded = decodeQrFromBitmap(bitmap)
            bitmap.recycle()
            decoded
        } catch (_: Exception) {
            null
        }
    }

    fun tryDecodeBitmapWithMlKit(bitmap: Bitmap): String? {
        return try {
            val inputImageClass = Class.forName("com.google.mlkit.vision.common.InputImage")
            val fromBitmapMethod = inputImageClass.getMethod("fromBitmap", Bitmap::class.java, Int::class.javaPrimitiveType)
            val inputImage = fromBitmapMethod.invoke(null, bitmap, 0)

            val scanningClass = Class.forName("com.google.mlkit.vision.barcode.BarcodeScanning")
            val scanner = scanningClass.getMethod("getClient").invoke(null)
            val processMethod = scanner.javaClass.getMethod("process", inputImageClass)
            val task = processMethod.invoke(scanner, inputImage)

            val latch = CountDownLatch(1)
            val outRef = AtomicReference<String?>(null)

            val onSuccessClass = Class.forName("com.google.android.gms.tasks.OnSuccessListener")
            val proxy = java.lang.reflect.Proxy.newProxyInstance(
                onSuccessClass.classLoader,
                arrayOf(onSuccessClass)
            ) { _, _, args ->
                val list = args?.firstOrNull() as? List<*>
                if (!list.isNullOrEmpty()) {
                    val firstBarcode = list.first()!!
                    val rawVal = firstBarcode.javaClass.getMethod("getRawValue").invoke(firstBarcode) as? String
                    if (!rawVal.isNullOrBlank()) outRef.set(rawVal)
                }
                latch.countDown()
                null
            }
            val onFailureClass = Class.forName("com.google.android.gms.tasks.OnFailureListener")
            val failProxy = java.lang.reflect.Proxy.newProxyInstance(
                onFailureClass.classLoader,
                arrayOf(onFailureClass)
            ) { _, _, _ ->
                latch.countDown()
                null
            }

            task.javaClass.getMethod("addOnSuccessListener", onSuccessClass).invoke(task, proxy)
            task.javaClass.getMethod("addOnFailureListener", onFailureClass).invoke(task, failProxy)
            latch.await(1500, TimeUnit.MILLISECONDS)
            outRef.get()
        } catch (_: Exception) {
            null
        }
    }

    fun tryDecodeNv21WithMlKit(nv21: ByteArray, width: Int, height: Int, rotationDeg: Int, onFound: (String) -> Unit): Boolean {
        return try {
            val inputImageClass = Class.forName("com.google.mlkit.vision.common.InputImage")
            val fromBytesMethod = inputImageClass.getMethod(
                "fromByteArray",
                ByteArray::class.java,
                Int::class.javaPrimitiveType,
                Int::class.javaPrimitiveType,
                Int::class.javaPrimitiveType,
                Int::class.javaPrimitiveType
            )
            val inputImage = fromBytesMethod.invoke(null, nv21, width, height, rotationDeg, 17)
            val scanningClass = Class.forName("com.google.mlkit.vision.barcode.BarcodeScanning")
            val scanner = scanningClass.getMethod("getClient").invoke(null)
            val processMethod = scanner.javaClass.getMethod("process", inputImageClass)
            val task = processMethod.invoke(scanner, inputImage)

            val onSuccessClass = Class.forName("com.google.android.gms.tasks.OnSuccessListener")
            val proxy = java.lang.reflect.Proxy.newProxyInstance(
                onSuccessClass.classLoader,
                arrayOf(onSuccessClass)
            ) { _, _, args ->
                val list = args?.firstOrNull() as? List<*>
                if (!list.isNullOrEmpty()) {
                    val firstBarcode = list.first()!!
                    val rawVal = firstBarcode.javaClass.getMethod("getRawValue").invoke(firstBarcode) as? String
                    if (!rawVal.isNullOrBlank()) onFound(rawVal)
                }
                null
            }
            task.javaClass.getMethod("addOnSuccessListener", onSuccessClass).invoke(task, proxy)
            true
        } catch (_: Exception) {
            false
        }
    }

    fun decodeQrFromBitmap(bitmap: Bitmap): String? {
        return try {
            val width = bitmap.width
            val height = bitmap.height
            val pixels = IntArray(width * height)
            bitmap.getPixels(pixels, 0, width, 0, 0, width, height)

            val source = RGBLuminanceSource(width, height, pixels)
            decodeLuminanceSourceMultiPass(source)
        } catch (_: Exception) {
            null
        }
    }

    fun decodeYuvCameraFrame(data: ByteArray, width: Int, height: Int): String? {
        try {
            val src1 = PlanarYUVLuminanceSource(data, width, height, 0, 0, width, height, false)
            val res1 = decodeLuminanceSourceMultiPass(src1)
            if (!res1.isNullOrBlank()) return res1
        } catch (_: Exception) { }

        try {
            val rotated = ByteArray(width * height)
            for (y in 0 until height) {
                for (x in 0 until width) {
                    rotated[x * height + height - y - 1] = data[y * width + x]
                }
            }
            val src2 = PlanarYUVLuminanceSource(rotated, height, width, 0, 0, height, width, false)
            val res2 = decodeLuminanceSourceMultiPass(src2)
            if (!res2.isNullOrBlank()) return res2
        } catch (_: Exception) { }

        return null
    }

    private fun decodeLuminanceSourceMultiPass(source: LuminanceSource): String? {
        val hints = EnumMap<DecodeHintType, Any>(DecodeHintType::class.java).apply {
            put(DecodeHintType.TRY_HARDER, java.lang.Boolean.TRUE)
            put(DecodeHintType.CHARACTER_SET, "UTF-8")
            put(DecodeHintType.POSSIBLE_FORMATS, listOf(BarcodeFormat.QR_CODE))
        }
        val reader = MultiFormatReader().apply { setHints(hints) }

        val binarizers = listOf(
            GlobalHistogramBinarizer(source),
            HybridBinarizer(source),
            GlobalHistogramBinarizer(source.invert()),
            HybridBinarizer(source.invert())
        )
        for (bin in binarizers) {
            try {
                val res = reader.decodeWithState(BinaryBitmap(bin))
                if (!res.text.isNullOrBlank()) return res.text
            } catch (_: Exception) {
                reader.reset()
            }
        }
        return null
    }

    // =========================================================================
    // 1. OBSŁUGA KODÓW QR I POBIERANIA .FTAK LUB NIEZALEŻNEGO OPORD Z WI-FI / LAN
    // =========================================================================
    fun handleScannedOrGalleryQrPayload(
        activity: Activity,
        rawPayload: String,
        onImportCompleted: () -> Unit
    ) {
        val trimmed = rawPayload.trim()
        if (trimmed.isEmpty()) {
            Toast.makeText(activity, FtakProvisioningEngine.tr(activity, "⚠️ Pusty kod QR!", "⚠️ Empty QR code!"), Toast.LENGTH_SHORT).show()
            return
        }

        val isHttpDownloadUrl = (trimmed.startsWith("http://", ignoreCase = true) ||
                trimmed.startsWith("https://", ignoreCase = true)) &&
                !trimmed.startsWith("https://meshtastic.org/e/#", ignoreCase = true)

        if (isHttpDownloadUrl) {
            val targetUrl = if (trimmed.endsWith("/")) (trimmed + "deploy.ftak") else trimmed
            downloadFromUrlAndIngestAsync(activity, targetUrl, onImportCompleted)
            return
        }

        if (FtakProvisioningEngine.importFromQrStringOrJson(activity, trimmed)) {
            Toast.makeText(
                activity,
                FtakProvisioningEngine.tr(activity, "✓ Wczytano dane z kodu QR!", "✓ Loaded data from QR code!"),
                Toast.LENGTH_LONG
            ).show()
            onImportCompleted()
        } else {
            Toast.makeText(
                activity,
                FtakProvisioningEngine.tr(activity, "⚠️ Nie rozpoznano formatu QR: ", "⚠️ Unrecognized QR format: ") + trimmed.take(60),
                Toast.LENGTH_LONG
            ).show()
        }
    }

    fun downloadFromUrlAndIngestAsync(
        activity: Activity,
        urlStr: String,
        onImportCompleted: () -> Unit
    ) {
        Toast.makeText(
            activity,
            FtakProvisioningEngine.tr(activity, "📡 Pobieranie z: ", "📡 Downloading from: ") + urlStr,
            Toast.LENGTH_SHORT
        ).show()

        Thread {
            try {
                val bytes = fetchHttpBytes(urlStr)
                val asText = String(bytes, Charsets.UTF_8).trim()

                // 1. Sprawdź czy to samodzielny rozkaz OPORD (HTML lub JSON) pobrany z Buildera LAN / Hotspotu
                if (urlStr.contains("/opord", ignoreCase = true) ||
                    asText.startsWith("<!DOCTYPE html", ignoreCase = true) ||
                    asText.startsWith("<html", ignoreCase = true)) {
                    FtakProvisioningEngine.saveStandaloneOpordContent(activity, asText)
                    // Jeśli to adres .html, spróbuj w tle pobrać również bliźniaczy plik .json dla ustrukturyzowanego widoku
                    if (urlStr.endsWith(".html", ignoreCase = true)) {
                        try {
                            val jsonCompanionUrl = urlStr.substring(0, urlStr.length - 5) + ".json"
                            val jBytes = fetchHttpBytes(jsonCompanionUrl)
                            val jTxt = String(jBytes, Charsets.UTF_8).trim()
                            if (jTxt.startsWith("{")) {
                                FtakProvisioningEngine.saveStandaloneOpordContent(activity, jTxt)
                            }
                        } catch (_: Exception) { }
                    }
                    activity.runOnUiThread {
                        Toast.makeText(
                            activity,
                            FtakProvisioningEngine.tr(activity, "✓ Pobrano rozkaz bojowy OPORD przez Wi-Fi / LAN!", "✓ Downloaded OPORD via Wi-Fi / LAN!"),
                            Toast.LENGTH_LONG
                        ).show()
                        showOpordViewerModal(activity)
                    }
                    return@Thread
                }

                // 2. Sprawdź czy to deskryptor JSON z Buildera wskazujący na paczkę .ftak
                if (asText.startsWith("{")) {
                    try {
                        val root = JSONObject(asText)
                        val nestedPkgUrl = listOf(
                            root.optString("packageUrl"),
                            root.optString("ftakUrl"),
                            root.optString("bundleUrl"),
                            root.optString("downloadUrl"),
                            root.optString("archiveUrl"),
                            root.optString("url")
                        ).firstOrNull { it.isNotBlank() && (it.startsWith("http", true) || it.startsWith("/")) }

                        if (nestedPkgUrl != null) {
                            val resolvedPkgUrl = if (nestedPkgUrl.startsWith("/")) {
                                val base = URL(urlStr)
                                val p = if (base.port != -1) base.port else base.defaultPort
                                base.protocol + "://" + base.host + ":" + p + nestedPkgUrl
                            } else {
                                nestedPkgUrl
                            }
                            try {
                                val pkgBytes = fetchHttpBytes(resolvedPkgUrl)
                                if (FtakProvisioningEngine.importFromFtakStream(activity, pkgBytes.inputStream())) {
                                    FtakProvisioningEngine.importFromQrStringOrJson(activity, asText)
                                    activity.runOnUiThread {
                                        Toast.makeText(
                                            activity,
                                            FtakProvisioningEngine.tr(activity, "✓ Pobrano paczkę .FTAK z Buildera!", "✓ Downloaded .FTAK bundle from Builder!"),
                                            Toast.LENGTH_LONG
                                        ).show()
                                        onImportCompleted()
                                    }
                                    return@Thread
                                }
                            } catch (_: Exception) { }
                        }
                    } catch (_: Exception) { }
                }

                val ok = FtakProvisioningEngine.importFromFtakStream(activity, bytes.inputStream())
                activity.runOnUiThread {
                    if (ok) {
                        Toast.makeText(
                            activity,
                            FtakProvisioningEngine.tr(activity, "✓ Pobrano i wczytano: ", "✓ Downloaded and loaded: ") + FtakProvisioningEngine.getEventName(activity),
                            Toast.LENGTH_LONG
                        ).show()
                        onImportCompleted()
                    } else {
                        Toast.makeText(
                            activity,
                            FtakProvisioningEngine.tr(activity, "⚠️ Pobrany plik nie zawiera prawidłowej konfiguracji!", "⚠️ Downloaded file is not a valid configuration!"),
                            Toast.LENGTH_LONG
                        ).show()
                    }
                }
            } catch (ex: Exception) {
                activity.runOnUiThread {
                    Toast.makeText(
                        activity,
                        FtakProvisioningEngine.tr(activity, "⚠️ Błąd połączenia z ", "⚠️ Connection error to ") + urlStr + " (" + ex.message + ")",
                        Toast.LENGTH_LONG
                    ).show()
                }
            }
        }.start()
    }

    private fun fetchHttpBytes(urlStr: String): ByteArray {
        val url = URL(urlStr)
        val conn = (url.openConnection() as HttpURLConnection).apply {
            connectTimeout = 6000
            readTimeout = 20000
            requestMethod = "GET"
            connect()
        }
        if (conn.responseCode != HttpURLConnection.HTTP_OK) {
            throw Exception("HTTP " + conn.responseCode)
        }
        return conn.inputStream.use { it.readBytes() }
    }

    // =========================================================================
    // 2. WBUDOWANY CZYTNIK OPORD W APLIKACJI ANDROID + IMPORT OPORD Z LAN
    // =========================================================================
    fun showOpordViewerModal(activity: Activity) {
        val opordHtml = FtakProvisioningEngine.getAvailableOpordHtml(activity)
        val opordJson = FtakProvisioningEngine.getAvailableOpordJson(activity)
        val fac = FtakProvisioningEngine.getActiveFaction(activity)
        val accentHex = fac?.colorHex ?: "#1FE0C5"

        val root = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(activity, 14), dp(activity, 14), dp(activity, 14), dp(activity, 12))
        }

        root.addView(TextView(activity).apply {
            text = FtakProvisioningEngine.tr(
                activity,
                "📋 ROZKAZ BOJOWY OPORD (" + (fac?.name ?: "MILSIM") + ")",
                "📋 OPERATIONAL ORDER — OPORD (" + (fac?.name ?: "MILSIM") + ")"
            )
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
        })

        if (opordHtml.isBlank() && opordJson.isBlank()) {
            root.addView(TextView(activity).apply {
                text = FtakProvisioningEngine.tr(
                    activity,
                    "Brak wczytanego rozkazu OPORD. Wczytaj paczkę .ftak zawierającą OPORD lub pobierz sam rozkaz OPORD bezpośrednio z Windows Buildera / Hotspotu po Wi-Fi / LAN poniżej:",
                    "No OPORD loaded yet. Load an .ftak bundle containing an OPORD or pull a standalone OPORD over Wi-Fi / LAN below:"
                )
                setTextColor(Color.parseColor("#B0C6CE"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                setPadding(0, dp(activity, 10), 0, dp(activity, 14))
            })
        } else {
            // Jeśli mamy sformatowany dokument HTML (z tabelami składu oddziału, SARNEG i METOC), wyświetl go w czytelnej kontrolce WebView
            if (opordHtml.isNotBlank()) {
                val webContainer = FrameLayout(activity).apply {
                    background = createRoundedBg("#FFFFFF", accentHex, 2, 10f)
                    setPadding(dp(activity, 4), dp(activity, 4), dp(activity, 4), dp(activity, 4))
                }
                val wv = WebView(activity).apply {
                    settings.javaScriptEnabled = false
                    settings.builtInZoomControls = true
                    settings.displayZoomControls = false
                    loadDataWithBaseURL(null, opordHtml, "text/html", "UTF-8", null)
                }
                webContainer.addView(wv, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(activity, 410)))
                root.addView(webContainer, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    topMargin = dp(activity, 8)
                    bottomMargin = dp(activity, 10)
                })
            } else {
                // Wyświetl ustrukturyzowane karty z opord.json / OpordSummary
                val summaryBox = TextView(activity).apply {
                    text = formatOpordJsonForMobileDisplay(opordJson)
                    setTextColor(Color.WHITE)
                    typeface = Typeface.MONOSPACE
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                    background = createRoundedBg("#0E1D23", accentHex, 1, 10f)
                    setPadding(dp(activity, 12), dp(activity, 10), dp(activity, 12), dp(activity, 10))
                }
                root.addView(summaryBox, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    topMargin = dp(activity, 8)
                    bottomMargin = dp(activity, 10)
                })
            }
        }

        val dlg = AlertDialog.Builder(activity)
            .setView(ScrollView(activity).apply { addView(root) })
            .setPositiveButton(FtakProvisioningEngine.tr(activity, "📡 Importuj OPORD z LAN", "📡 Pull OPORD from LAN")) { _, _ ->
                showImportStandaloneOpordLanDialog(activity)
            }
            .setNeutralButton(FtakProvisioningEngine.tr(activity, "📤 Udostępnij OPORD (Hotspot)", "📤 Share OPORD (Hotspot)")) { _, _ ->
                showWifiHotspotShareDialog(activity, opordOnlyDefault = true)
            }
            .setNegativeButton(FtakProvisioningEngine.tr(activity, "Zamknij", "Close"), null)
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", accentHex, 2, 14f))
        dlg.show()
    }

    fun showImportStandaloneOpordLanDialog(activity: Activity) {
        val box = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12))
        }

        box.addView(TextView(activity).apply {
            text = FtakProvisioningEngine.tr(
                activity,
                "📡 IMPORTUJ SAM ROZKAZ OPORD Z SIECI WI-FI / LAN",
                "📡 IMPORT STANDALONE OPORD OVER WI-FI / LAN"
            )
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
        })

        box.addView(TextView(activity).apply {
            text = FtakProvisioningEngine.tr(
                activity,
                "Podaj adres IP komputera z Windows Builderem (lub telefonu dowódcy) albo pełny link do pliku OPORD (możesz też zeskanować kod QR OPORD z ekranu PC):",
                "Enter the IP address of the Windows Builder PC (or commander's phone) or full OPORD URL (you can also scan the OPORD QR code directly):"
            )
            setTextColor(Color.parseColor("#B0C6CE"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            setPadding(0, dp(activity, 6), 0, dp(activity, 8))
        })

        val facName = FtakProvisioningEngine.getActiveFaction(activity)?.name ?: "BLUFOR"
        val inputUrl = EditText(activity).apply {
            setText("http://192.168.0.43:8766/opord/" + facName + ".html")
            setTextColor(Color.WHITE)
            background = createRoundedBg("#0E1D22", "#1FE0C5", 1, 8f)
            setPadding(dp(activity, 12), dp(activity, 10), dp(activity, 12), dp(activity, 10))
        }
        box.addView(inputUrl)

        val dlg = AlertDialog.Builder(activity)
            .setView(box)
            .setPositiveButton(FtakProvisioningEngine.tr(activity, "📥 Pobierz OPORD", "📥 Download OPORD")) { _, _ ->
                var raw = inputUrl.text.toString().trim()
                if (!raw.startsWith("http://", true) && !raw.startsWith("https://", true)) {
                    raw = "http://" + raw
                }
                if (!raw.contains("/opord", true) && !raw.endsWith(".html", true) && !raw.endsWith(".json", true)) {
                    raw = raw.trimEnd('/') + ":8766/opord/" + facName + ".html"
                }
                downloadFromUrlAndIngestAsync(activity, raw) { }
            }
            .setNegativeButton(FtakProvisioningEngine.tr(activity, "Anuluj", "Cancel"), null)
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", "#1FE0C5", 2, 14f))
        dlg.show()
    }

    private fun formatOpordJsonForMobileDisplay(rawJson: String): String {
        return try {
            val o = JSONObject(rawJson)
            val sb = StringBuilder()
            val opNum = o.optString("OperationNumber", o.optString("operationNumber", "OPORD"))
            val opDate = o.optString("OperationDateIso", o.optString("operationDate", ""))
            val loc = o.optString("LocationName", o.optString("locationName", ""))
            sb.appendLine("📋 " + opNum + "  |  " + opDate + "  |  " + loc)
            sb.appendLine("──────────────────────────────────────")

            val mission = o.optString("MissionFullStatement", o.optString("missionStatement", ""))
            if (mission.isNotBlank()) {
                sb.appendLine("🎯 ZADANIE (MISSION):\n" + mission + "\n")
            }

            val sun = o.optString("MetocSunTimes", o.optString("sunTimes", ""))
            val moon = o.optString("MetocMoonPhase", o.optString("moonPhase", ""))
            val wx = o.optString("MetocCondition", o.optString("weatherCondition", ""))
            if (sun.isNotBlank() || wx.isNotBlank()) {
                sb.appendLine("🌦️ METOC / ŚWIATŁO:")
                if (wx.isNotBlank()) sb.appendLine("• Pogoda: " + wx)
                if (sun.isNotBlank()) sb.appendLine("• Słońce: " + sun)
                if (moon.isNotBlank()) sb.appendLine("• Księżyc: " + moon)
                sb.appendLine()
            }

            val sarneg = o.optString("SarnegKeyWord10", o.optString("sarnegKey", ""))
            if (sarneg.isNotBlank()) {
                sb.appendLine("🔐 SARNEG (0-9): " + sarneg)
            }
            val pass = o.optString("ChallengeAndPassword", o.optString("challengePassword", ""))
            if (pass.isNotBlank()) {
                sb.appendLine("🔑 HASŁA ROZPOZNAWCZE:\n" + pass)
            }
            sb.toString().trim()
        } catch (_: Exception) {
            rawJson
        }
    }

    // =========================================================================
    // 3. LOKALNY SERWER HTTP WI-FI HOTSPOT AD-HOC (SERWUJE .FTAK ORAZ OPORD)
    // =========================================================================
    fun detectLocalHotspotOrWifiIp(): String {
        val candidates = mutableListOf<Pair<Int, String>>()
        try {
            val interfaces = Collections.list(NetworkInterface.getNetworkInterfaces())
            for (nif in interfaces) {
                if (!nif.isUp || nif.isLoopback) continue
                val ifName = nif.name.lowercase(Locale.ROOT)
                for (addr in Collections.list(nif.inetAddresses)) {
                    if (addr is Inet4Address && !addr.isLoopbackAddress) {
                        val ip = addr.hostAddress ?: continue
                        val priority = when {
                            ifName.startsWith("ap") || ifName.startsWith("swlan") || ifName.contains("wlan1") -> 100
                            ip.startsWith("192.168.43.") || ip.startsWith("192.168.137.") -> 95
                            ifName.startsWith("wlan") -> 85
                            ip.startsWith("192.168.") -> 75
                            ip.startsWith("10.") || ip.startsWith("172.") -> 60
                            else -> 20
                        }
                        candidates.add(priority to ip)
                    }
                }
            }
        } catch (_: Exception) { }

        return candidates.maxByOrNull { it.first }?.second ?: "192.168.43.1"
    }

    fun startLocalFtakServer(ctx: Context, opordOnly: Boolean = false, onClientDownloaded: (Int) -> Unit): String {
        stopLocalFtakServer()
        downloadCounter.set(0)

        val ftakFile = FtakProvisioningEngine.getSavedRawFtakArchiveFile(ctx)
        var serverSock: ServerSocket? = null
        for (port in listOf(8080, 8484, 8888, 0)) {
            try {
                serverSock = ServerSocket(port)
                activeServerPort = serverSock.localPort
                break
            } catch (_: Exception) { }
        }

        val boundSocket = serverSock ?: return ""
        activeServerSocket = boundSocket
        serverRunning.set(true)

        Thread {
            while (serverRunning.get()) {
                try {
                    val client = boundSocket.accept()
                    Thread {
                        handleHttpClientSocket(ctx, client, ftakFile, onClientDownloaded)
                    }.start()
                } catch (_: Exception) {
                    if (!serverRunning.get()) break
                }
            }
        }.start()

        val localIp = detectLocalHotspotOrWifiIp()
        val endpoint = if (opordOnly) "/opord.html" else "/deploy.ftak"
        return "http://" + localIp + ":" + activeServerPort + endpoint
    }

    fun stopLocalFtakServer() {
        serverRunning.set(false)
        try {
            activeServerSocket?.close()
        } catch (_: Exception) { }
        activeServerSocket = null
    }

    private fun handleHttpClientSocket(
        ctx: Context,
        socket: Socket,
        ftakFile: File,
        onClientDownloaded: (Int) -> Unit
    ) {
        socket.use { sock ->
            try {
                sock.soTimeout = 10000
                val reader = BufferedReader(InputStreamReader(sock.getInputStream(), Charsets.UTF_8))
                val requestLine = reader.readLine() ?: return
                val parts = requestLine.split(" ")
                val path = if (parts.size >= 2) parts[1] else "/"

                val out = BufferedOutputStream(sock.getOutputStream())
                val evName = FtakProvisioningEngine.getEventName(ctx)
                val safeFileName = evName.replace(Regex("[^a-zA-Z0-9_-]"), "_").ifBlank { "operacja" } + ".ftak"

                if (path.startsWith("/opord", ignoreCase = true)) {
                    val html = FtakProvisioningEngine.getAvailableOpordHtml(ctx).ifBlank {
                        "<html><body style='background:#081316;color:#fff;font-family:monospace;padding:16px;'><pre>" +
                                formatOpordJsonForMobileDisplay(FtakProvisioningEngine.getAvailableOpordJson(ctx)) +
                                "</pre></body></html>"
                    }
                    val htmlBytes = html.toByteArray(Charsets.UTF_8)
                    val headers = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: text/html; charset=utf-8\r\n" +
                            "Content-Length: " + htmlBytes.size + "\r\n" +
                            "Connection: close\r\n\r\n"
                    out.write(headers.toByteArray(Charsets.UTF_8))
                    out.write(htmlBytes)
                    out.flush()
                    val count = downloadCounter.incrementAndGet()
                    onClientDownloaded(count)
                    return
                }

                if (path.startsWith("/deploy.ftak", ignoreCase = true) && ftakFile.exists()) {
                    val fileLen = ftakFile.length()
                    val headers = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/octet-stream\r\n" +
                            "Content-Disposition: attachment; filename=\"" + safeFileName + "\"\r\n" +
                            "Content-Length: " + fileLen + "\r\n" +
                            "Connection: close\r\n\r\n"
                    out.write(headers.toByteArray(Charsets.UTF_8))

                    BufferedInputStream(FileInputStream(ftakFile)).use { fis ->
                        val buf = ByteArray(16384)
                        var read: Int
                        while (fis.read(buf).also { read = it } != -1) {
                            out.write(buf, 0, read)
                        }
                    }
                    out.flush()
                    val count = downloadCounter.incrementAndGet()
                    onClientDownloaded(count)
                } else {
                    val sizeKb = if (ftakFile.exists()) (ftakFile.length() / 1024).coerceAtLeast(1) else 0
                    val html = "<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
                            "<title>Field TAK Hub Hotspot</title></head>" +
                            "<body style='background:#081316;color:#fff;font-family:sans-serif;text-align:center;padding:24px;'>" +
                            "<div style='background:#102229;border:2px solid #1FE0C5;border-radius:14px;padding:20px;max-width:420px;margin:0 auto;'>" +
                            "<h2 style='color:#1FE0C5;margin-top:0;'>📡 FIELD TAK HUB HOTSPOT</h2>" +
                            "<p>Operacja: <b>" + evName + "</b> (" + sizeKb + " KB)</p>" +
                            "<a style='display:block;background:#1FE0C5;color:#051014;font-weight:bold;text-decoration:none;padding:14px;border-radius:10px;margin-top:12px;' href='/deploy.ftak'>📥 POBIERZ PACZKĘ .FTAK</a>" +
                            "<a style='display:block;background:#183440;color:#1FE0C5;font-weight:bold;text-decoration:none;padding:12px;border-radius:10px;margin-top:10px;border:1px solid #1FE0C5;' href='/opord.html'>📋 OTWÓRZ SAM ROZKAZ OPORD</a>" +
                            "</div></body></html>"
                    val htmlBytes = html.toByteArray(Charsets.UTF_8)
                    val headers = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: text/html; charset=utf-8\r\n" +
                            "Content-Length: " + htmlBytes.size + "\r\n" +
                            "Connection: close\r\n\r\n"
                    out.write(headers.toByteArray(Charsets.UTF_8))
                    out.write(htmlBytes)
                    out.flush()
                }
            } catch (_: Exception) { }
        }
    }

    fun showWifiHotspotShareDialog(activity: Activity, opordOnlyDefault: Boolean = false) {
        if (!FtakProvisioningEngine.hasLoadedProfile(activity) && !FtakProvisioningEngine.hasAnyOpordLoaded(activity)) {
            Toast.makeText(
                activity,
                FtakProvisioningEngine.tr(activity, "⚠️ Najpierw wczytaj paczkę .ftak lub rozkaz OPORD!", "⚠️ Load an .ftak package or OPORD first!"),
                Toast.LENGTH_LONG
            ).show()
            return
        }

        val root = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12))
        }

        root.addView(TextView(activity).apply {
            text = FtakProvisioningEngine.tr(activity, "📡 SERWER WI-FI HOTSPOT / LAN (.FTAK & OPORD)", "📡 WI-FI HOTSPOT / LAN SERVER (.FTAK & OPORD)")
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
            gravity = Gravity.CENTER
        })

        val qrImageView = ImageView(activity).apply {
            background = createRoundedBg("#FFFFFF", "#1FE0C5", 3, 12f)
            setPadding(dp(activity, 10), dp(activity, 10), dp(activity, 10), dp(activity, 10))
        }
        root.addView(qrImageView, LinearLayout.LayoutParams(dp(activity, 235), dp(activity, 235)).apply {
            topMargin = dp(activity, 10)
            bottomMargin = dp(activity, 10)
        })

        val urlBox = TextView(activity).apply {
            setTextColor(Color.parseColor("#FAD02C"))
            typeface = Typeface.MONOSPACE
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            gravity = Gravity.CENTER
            background = createRoundedBg("#0E1D22", "#223A42", 1, 8f)
            setPadding(dp(activity, 10), dp(activity, 8), dp(activity, 10), dp(activity, 8))
        }
        root.addView(urlBox, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        val statusCounterView = TextView(activity).apply {
            text = FtakProvisioningEngine.tr(activity, "⏳ Serwer aktywny (Pobrano: 0x)", "⏳ Server active (Downloaded: 0x)")
            setTextColor(Color.parseColor("#2ECC71"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            gravity = Gravity.CENTER
            setPadding(0, dp(activity, 8), 0, dp(activity, 4))
        }
        root.addView(statusCounterView)

        var isOpordMode = opordOnlyDefault
        var currentUrl = ""
        fun refreshHotspotIpAndQr() {
            currentUrl = startLocalFtakServer(activity, isOpordMode) { count ->
                activity.runOnUiThread {
                    statusCounterView.text = FtakProvisioningEngine.tr(
                        activity,
                        "✓ Przesłano do graczy: " + count + "x",
                        "✓ Served to teammates: " + count + "x"
                    )
                }
            }
            urlBox.text = currentUrl
            qrImageView.setImageBitmap(generateStandardQrBitmap(currentUrl, 520))
        }

        refreshHotspotIpAndQr()

        val dlg = AlertDialog.Builder(activity)
            .setView(ScrollView(activity).apply { addView(root) })
            .setPositiveButton(FtakProvisioningEngine.tr(activity, "🔄 Przełącz .FTAK / OPORD", "🔄 Toggle .FTAK / OPORD")) { _, _ -> }
            .setNeutralButton(FtakProvisioningEngine.tr(activity, "📋 Kopiuj link", "📋 Copy URL")) { _, _ -> }
            .setNegativeButton(FtakProvisioningEngine.tr(activity, "⏹ Zatrzymaj", "⏹ Stop")) { _, _ ->
                stopLocalFtakServer()
            }
            .setOnDismissListener {
                stopLocalFtakServer()
            }
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", "#1FE0C5", 2, 14f))
        dlg.show()

        dlg.getButton(AlertDialog.BUTTON_POSITIVE)?.setOnClickListener {
            isOpordMode = !isOpordMode
            refreshHotspotIpAndQr()
        }
        dlg.getButton(AlertDialog.BUTTON_NEUTRAL)?.setOnClickListener {
            val cb = activity.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("Hotspot URL", currentUrl))
            Toast.makeText(activity, "✓ URL: " + currentUrl, Toast.LENGTH_SHORT).show()
        }
    }

    fun showOfflineFactionQrDialog(activity: Activity) {
        val fac = FtakProvisioningEngine.getActiveFaction(activity) ?: return

        val compactFactionJson = JSONObject().apply {
            put("eventName", FtakProvisioningEngine.getEventName(activity))
            put("factions", JSONArray().put(JSONObject().apply {
                put("Name", fac.name)
                put("ColorHex", fac.colorHex)
                put("ChannelName", fac.channelName)
                put("Psk", fac.pskBase64)
                put("ChannelUrl", fac.channelUrl)
                put("LoraRegion", fac.loraRegion)
                put("LoraModemPreset", fac.loraPreset)
                put("HopLimit", fac.hopLimit)
                put("OtsHost", fac.otsHost)
                put("EmergencyPhone", fac.emergencyPhone)
                val vArr = JSONArray()
                for (vc in fac.voiceChannels) {
                    vArr.put(JSONObject().apply {
                        put("roleName", vc.roleName)
                        put("bandType", vc.bandType)
                        put("freqMhz", vc.freqMhz)
                        put("toneOrCode", vc.toneOrCode)
                    })
                }
                put("VoiceChannels", vArr)
            }))
        }.toString()

        val root = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12))
        }

        root.addView(TextView(activity).apply {
            text = "📱 QR OFFLINE: " + fac.name
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
            gravity = Gravity.CENTER
        })

        val qrImg = ImageView(activity).apply {
            setImageBitmap(generateStandardQrBitmap(compactFactionJson, 560))
            background = createRoundedBg("#FFFFFF", fac.colorHex, 3, 12f)
            setPadding(dp(activity, 10), dp(activity, 10), dp(activity, 10), dp(activity, 10))
        }
        root.addView(qrImg, LinearLayout.LayoutParams(dp(activity, 250), dp(activity, 250)).apply {
            topMargin = dp(activity, 10)
        })

        val dlg = AlertDialog.Builder(activity)
            .setView(ScrollView(activity).apply { addView(root) })
            .setPositiveButton(FtakProvisioningEngine.tr(activity, "Zamknij", "Close"), null)
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", fac.colorHex, 2, 14f))
        dlg.show()
    }

    fun showShareFilesPickerDialog(activity: Activity) {
        val fac = FtakProvisioningEngine.getActiveFaction(activity) ?: return

        val items = mutableListOf<Pair<String, File>>()
        val ftakArchive = FtakProvisioningEngine.getSavedRawFtakArchiveFile(activity)
        if (ftakArchive.exists()) {
            val kb = (ftakArchive.length() / 1024.0).roundToInt().coerceAtLeast(1)
            items.add(("📦 .FTAK (" + kb + " KB)") to ftakArchive)
        }

        val factionFiles = FtakProvisioningEngine.getFactionFilteredFiles(activity, fac)
        for (f in factionFiles) {
            val ext = f.extension.uppercase(Locale.ROOT)
            val kb = (f.length() / 1024.0).roundToInt().coerceAtLeast(1)
            items.add(("[" + ext + "] " + f.name + " (" + kb + " KB)") to f)
        }

        if (items.isEmpty()) return

        val labels = items.map { it.first }.toTypedArray()
        AlertDialog.Builder(activity)
            .setTitle(FtakProvisioningEngine.tr(activity, "📤 Wyślij plik (Quick Share / Garmin)", "📤 Share File (Quick Share / Garmin)"))
            .setItems(labels) { _, which ->
                shareSingleFileViaProvider(activity, items[which].second)
            }
            .setNegativeButton(FtakProvisioningEngine.tr(activity, "Anuluj", "Cancel"), null)
            .show()
    }

    fun shareSingleFileViaProvider(activity: Activity, file: File) {
        try {
            val uri = FtakShareContentProvider.buildUriForFile(activity, file)
            val mime = when (file.extension.lowercase(Locale.ROOT)) {
                "gpx" -> "application/gpx+xml"
                "kmz" -> "application/vnd.google-earth.kmz"
                "zip" -> "application/zip"
                "apk" -> "application/vnd.android.package-archive"
                else -> "application/octet-stream"
            }
            val sendIntent = Intent(Intent.ACTION_SEND).apply {
                type = mime
                putExtra(Intent.EXTRA_STREAM, uri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            activity.startActivity(Intent.createChooser(sendIntent, file.name))
        } catch (ex: Exception) {
            Toast.makeText(activity, "⚠️ Share Error: " + ex.message, Toast.LENGTH_LONG).show()
        }
    }

    private fun dp(ctx: Context, value: Int): Int =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value.toFloat(), ctx.resources.displayMetrics).toInt()

    private fun createRoundedBg(fillHex: String, strokeHex: String, strokeWidthDp: Int, radiusDp: Float): GradientDrawable {
        return GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusDp * 2.5f
            setColor(try { Color.parseColor(fillHex) } catch (_: Exception) { Color.parseColor("#081316") })
            setStroke(strokeWidthDp * 2, try { Color.parseColor(strokeHex) } catch (_: Exception) { Color.parseColor("#1FE0C5") })
        }
    }
}