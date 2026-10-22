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
import android.view.ViewGroup
import android.widget.*
import com.google.zxing.*
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
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import kotlin.math.max
import kotlin.math.roundToInt

object FieldShareAndQrEngine {

    private var activeServerSocket: ServerSocket? = null
    private val serverRunning = AtomicBoolean(false)
    private val downloadCounter = AtomicInteger(0)
    private var activeServerPort: Int = 8080

    // =========================================================================
    // 1. GENERATOR PRAWDZIWYCH KODÓW QR (STANDARD ISO ZXING)
    // =========================================================================
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

    // =========================================================================
    // 2. DEKODER KODÓW QR Z GALERII ZDJĘĆ (ZRZUTY EKRANU Z DISCORDA / APARATU)
    // =========================================================================
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

            val decoded = decodeQrFromBitmap(bitmap)
            bitmap.recycle()
            decoded
        } catch (_: Exception) {
            null
        }
    }

    fun decodeQrFromBitmap(bitmap: Bitmap): String? {
        return try {
            val width = bitmap.width
            val height = bitmap.height
            val pixels = IntArray(width * height)
            bitmap.getPixels(pixels, 0, width, 0, 0, width, height)

            val source = RGBLuminanceSource(width, height, pixels)
            val hints = EnumMap<DecodeHintType, Any>(DecodeHintType::class.java).apply {
                put(DecodeHintType.TRY_HARDER, java.lang.Boolean.TRUE)
                put(DecodeHintType.CHARACTER_SET, "UTF-8")
                put(DecodeHintType.POSSIBLE_FORMATS, listOf(BarcodeFormat.QR_CODE))
            }
            val reader = MultiFormatReader().apply { setHints(hints) }

            try {
                val binaryBitmap = BinaryBitmap(HybridBinarizer(source))
                reader.decodeWithState(binaryBitmap).text
            } catch (_: Exception) {
                // Drugi przebieg dla kodów QR w negatywie (jasny kod na ciemnym tle)
                val invertedBitmap = BinaryBitmap(HybridBinarizer(source.invert()))
                reader.decodeWithState(invertedBitmap).text
            }
        } catch (_: Exception) {
            null
        }
    }

    /**
     * Przetwarza dowolny zeskanowany lub wczytany z Galerii ładunek QR:
     * - Jeśli to link do lokalnego serwera Wi-Fi Hotspot (http://192.168...:8080/deploy.ftak),
     *   automatycznie pobiera całą paczkę .ftak przez Wi-Fi i rozpakowuje mapy!
     * - Jeśli to JSON lub link Meshtastic, importuje profil bezpośrednio.
     */
    fun handleScannedOrGalleryQrPayload(
        activity: Activity,
        rawPayload: String,
        onImportCompleted: () -> Unit
    ) {
        val trimmed = rawPayload.trim()
        if (trimmed.isEmpty()) {
            Toast.makeText(activity, "⚠️ Pusty kod QR!", Toast.LENGTH_SHORT).show()
            return
        }

        val isHttpFtakUrl = (trimmed.startsWith("http://", ignoreCase = true) ||
                trimmed.startsWith("https://", ignoreCase = true)) &&
                (trimmed.endsWith(".ftak", ignoreCase = true) ||
                        trimmed.contains("/deploy.ftak", ignoreCase = true) ||
                        trimmed.contains(":8080/", ignoreCase = true) ||
                        trimmed.contains(":8484/", ignoreCase = true))

        if (isHttpFtakUrl) {
            val targetUrl = if (trimmed.endsWith("/")) "${trimmed}deploy.ftak" else trimmed
            downloadFtakFromHotspotAsync(activity, targetUrl, onImportCompleted)
            return
        }

        if (FtakProvisioningEngine.importFromQrStringOrJson(activity, trimmed)) {
            Toast.makeText(
                activity,
                "✓ Wczytano konfigurację z kodu QR: ${FtakProvisioningEngine.getEventName(activity)}",
                Toast.LENGTH_LONG
            ).show()
            onImportCompleted()
        } else {
            Toast.makeText(
                activity,
                "⚠️ Nie rozpoznano formatu kodu QR (oczekiwano profilu .ftak, linku Wi-Fi Hotspot lub Meshtastic).",
                Toast.LENGTH_LONG
            ).show()
        }
    }

    private fun downloadFtakFromHotspotAsync(
        activity: Activity,
        urlStr: String,
        onImportCompleted: () -> Unit
    ) {
        Toast.makeText(activity, "📡 Pobieranie paczki .ftak przez Wi-Fi z: $urlStr ...", Toast.LENGTH_SHORT).show()
        Thread {
            try {
                val url = URL(urlStr)
                val conn = (url.openConnection() as HttpURLConnection).apply {
                    connectTimeout = 6000
                    readTimeout = 20000
                    requestMethod = "GET"
                    connect()
                }
                if (conn.responseCode != HttpURLConnection.HTTP_OK) {
                    throw Exception("Serwer zwrócił kod HTTP ${conn.responseCode}")
                }
                val ok = conn.inputStream.use { stream ->
                    FtakProvisioningEngine.importFromFtakStream(activity, stream)
                }
                activity.runOnUiThread {
                    if (ok) {
                        Toast.makeText(
                            activity,
                            "✓ Pobrano i rozpakowano paczkę .ftak przez Wi-Fi Hotspot!",
                            Toast.LENGTH_LONG
                        ).show()
                        onImportCompleted()
                    } else {
                        Toast.makeText(activity, "⚠️ Pobrany plik nie jest prawidłową paczką .ftak!", Toast.LENGTH_LONG).show()
                    }
                }
            } catch (ex: Exception) {
                activity.runOnUiThread {
                    Toast.makeText(
                        activity,
                        "⚠️ Błąd pobierania z Wi-Fi Hotspot (${ex.message}). Upewnij się, że jesteś połączony z Wi-Fi kolegi!",
                        Toast.LENGTH_LONG
                    ).show()
                }
            }
        }.start()
    }

    // =========================================================================
    // 3. LOKALNY SERWER HTTP WI-FI HOTSPOT AD-HOC (DZIELENIE SIĘ .FTAK W LESIE)
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
                            // Najwyższy priorytet dla interfejsów Hotspotu Androida (SoftAP)
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

    fun startLocalFtakServer(ctx: Context, onClientDownloaded: (Int) -> Unit): String {
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
        return "http://$localIp:$activeServerPort/deploy.ftak"
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

                if (path.startsWith("/deploy.ftak", ignoreCase = true) && ftakFile.exists()) {
                    val fileLen = ftakFile.length()
                    val headers = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/octet-stream\r\n" +
                            "Content-Disposition: attachment; filename=\"$safeFileName\"\r\n" +
                            "Content-Length: $fileLen\r\n" +
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
                    val html = """
                        <!DOCTYPE html>
                        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                        <title>Field TAK Hub — Hotspot</title>
                        <style>
                          body { background:#081316; color:#fff; font-family:sans-serif; text-align:center; padding:24px; }
                          .card { background:#102229; border:2px solid #1FE0C5; border-radius:14px; padding:20px; max-width:420px; margin:0 auto; }
                          h2 { color:#1FE0C5; margin-top:0; }
                          .btn { display:block; background:#1FE0C5; color:#051014; font-weight:bold; text-decoration:none; padding:16px; border-radius:10px; font-size:18px; margin-top:18px; }
                        </style></head>
                        <body><div class="card">
                          <h2>📡 FIELD TAK HUB HOTSPOT</h2>
                          <p>Operacja: <b>$evName</b></p>
                          <p>Rozmiar paczki: <b>$sizeKb KB</b></p>
                          <a class="btn" href="/deploy.ftak">📥 POBIERZ PACZKĘ .FTAK</a>
                        </div></body></html>
                    """.trimIndent()
                    val htmlBytes = html.toByteArray(Charsets.UTF_8)
                    val headers = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: text/html; charset=utf-8\r\n" +
                            "Content-Length: ${htmlBytes.size}\r\n" +
                            "Connection: close\r\n\r\n"
                    out.write(headers.toByteArray(Charsets.UTF_8))
                    out.write(htmlBytes)
                    out.flush()
                }
            } catch (_: Exception) { }
        }
    }

    // =========================================================================
    // 4. OKNA DIALOGOWE UDOSTĘPNIANIA (WI-FI HOTSPOT + QR NA EKRANIE + PLIKI)
    // =========================================================================
    fun showWifiHotspotShareDialog(activity: Activity) {
        if (!FtakProvisioningEngine.hasLoadedProfile(activity)) {
            Toast.makeText(activity, "⚠️ Najpierw wczytaj paczkę .ftak lub zeskanuj kod QR!", Toast.LENGTH_LONG).show()
            return
        }

        val root = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12))
        }

        val titleView = TextView(activity).apply {
            text = "📡 SERWER WI-FI HOTSPOT AD-HOC"
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
            gravity = Gravity.CENTER
        }
        root.addView(titleView)

        val instrView = TextView(activity).apply {
            text = "1. Włącz Hotspot Wi-Fi w swoim telefonie (lub połączcie się z tym samym Wi-Fi).\n" +
                    "2. Koledzy z drużyny łączą się z Twoim Wi-Fi i skanują poniższy kod QR w aplikacji Field TAK Hub (lub zwykłym aparatem):"
            setTextColor(Color.parseColor("#B4C8D0"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            setPadding(0, dp(activity, 6), 0, dp(activity, 10))
        }
        root.addView(instrView)

        val qrImageView = ImageView(activity).apply {
            background = createRoundedBg("#FFFFFF", "#1FE0C5", 3, 12f)
            setPadding(dp(activity, 10), dp(activity, 10), dp(activity, 10), dp(activity, 10))
        }
        root.addView(qrImageView, LinearLayout.LayoutParams(dp(activity, 235), dp(activity, 235)).apply {
            bottomMargin = dp(activity, 10)
        })

        val urlBox = TextView(activity).apply {
            setTextColor(Color.parseColor("#FAD02C"))
            typeface = Typeface.MONOSPACE
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            gravity = Gravity.CENTER
            background = createRoundedBg("#0E1D22", "#223A42", 1, 8f)
            setPadding(dp(activity, 10), dp(activity, 8), dp(activity, 10), dp(activity, 8))
        }
        root.addView(urlBox, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        val statusCounterView = TextView(activity).apply {
            text = "⏳ Serwer aktywny — oczekiwanie na połączenia graczy (Pobrano: 0x)"
            setTextColor(Color.parseColor("#2ECC71"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            gravity = Gravity.CENTER
            setPadding(0, dp(activity, 8), 0, dp(activity, 4))
        }
        root.addView(statusCounterView)

        var currentUrl = ""
        fun refreshHotspotIpAndQr() {
            currentUrl = startLocalFtakServer(activity) { count ->
                activity.runOnUiThread {
                    statusCounterView.text = "✓ Przesłano paczkę .ftak do graczy: ${count}x"
                }
            }
            urlBox.text = currentUrl
            qrImageView.setImageBitmap(generateStandardQrBitmap(currentUrl, 520))
        }

        refreshHotspotIpAndQr()

        val dlg = AlertDialog.Builder(activity)
            .setView(ScrollView(activity).apply { addView(root) })
            .setPositiveButton("🔄 Odśwież adres IP") { _, _ -> }
            .setNeutralButton("📋 Kopiuj link") { _, _ -> }
            .setNegativeButton("⏹ Zatrzymaj i zamknij") { _, _ ->
                stopLocalFtakServer()
            }
            .setOnDismissListener {
                stopLocalFtakServer()
            }
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", "#1FE0C5", 2, 14f))
        dlg.show()

        dlg.getButton(AlertDialog.BUTTON_POSITIVE)?.setOnClickListener {
            refreshHotspotIpAndQr()
            Toast.makeText(activity, "🔄 Odświeżono adres IP serwera: $currentUrl", Toast.LENGTH_SHORT).show()
        }
        dlg.getButton(AlertDialog.BUTTON_NEUTRAL)?.setOnClickListener {
            val cb = activity.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("Hotspot URL", currentUrl))
            Toast.makeText(activity, "✓ Skopiowano adres: $currentUrl", Toast.LENGTH_SHORT).show()
        }
    }

    /**
     * Pokazuje na ekranie kod QR z samą konfiguracją aktywnej strony (JSON),
     * dzięki czemu kolega stojący obok może zeskanować konfigurację nawet bez włączania Wi-Fi.
     */
    fun showOfflineFactionQrDialog(activity: Activity) {
        val fac = FtakProvisioningEngine.getActiveFaction(activity)
        if (fac == null) {
            Toast.makeText(activity, "⚠️ Brak aktywnej strony konfliktu!", Toast.LENGTH_SHORT).show()
            return
        }

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
            text = "📱 KOD QR STRONY: ${fac.name}"
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
            gravity = Gravity.CENTER
        })

        root.addView(TextView(activity).apply {
            text = "Pokaż ten ekran koledze z drużyny ${fac.name}. Po zeskanowaniu otrzyma klucze Meshtastic, adres serwera i tabelę radiową:"
            setTextColor(Color.parseColor("#B4C8D0"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            gravity = Gravity.CENTER
            setPadding(0, dp(activity, 6), 0, dp(activity, 10))
        })

        val qrImg = ImageView(activity).apply {
            setImageBitmap(generateStandardQrBitmap(compactFactionJson, 560))
            background = createRoundedBg("#FFFFFF", fac.colorHex, 3, 12f)
            setPadding(dp(activity, 10), dp(activity, 10), dp(activity, 10), dp(activity, 10))
        }
        root.addView(qrImg, LinearLayout.LayoutParams(dp(activity, 250), dp(activity, 250)))

        val dlg = AlertDialog.Builder(activity)
            .setView(ScrollView(activity).apply { addView(root) })
            .setPositiveButton("Zamknij", null)
            .create()

        dlg.window?.setBackgroundDrawable(createRoundedBg("#081316", fac.colorHex, 2, 14f))
        dlg.show()
    }

    /**
     * Otwiera menu udostępniania plików w polu:
     * - Cała paczka .ftak (Quick Share / Bluetooth / Discord)
     * - Pojedyncze warstwy wybranej strony (.GPX do zegarka Garmin, .KMZ, .MBTILES)
     */
    fun showShareFilesPickerDialog(activity: Activity) {
        val fac = FtakProvisioningEngine.getActiveFaction(activity)
        if (fac == null) {
            Toast.makeText(activity, "⚠️ Najpierw wczytaj paczkę .ftak!", Toast.LENGTH_SHORT).show()
            return
        }

        val items = mutableListOf<Pair<String, File>>()
        val ftakArchive = FtakProvisioningEngine.getSavedRawFtakArchiveFile(activity)
        if (ftakArchive.exists()) {
            val kb = (ftakArchive.length() / 1024.0).roundToInt().coerceAtLeast(1)
            items.add("📦 Cała paczka operacji (.ftak — $kb KB)" to ftakArchive)
        }

        val factionFiles = FtakProvisioningEngine.getFactionFilteredFiles(activity, fac)
        for (f in factionFiles) {
            val ext = f.extension.uppercase(Locale.ROOT)
            val icon = when (ext) {
                "GPX" -> "⌚"
                "KMZ", "KML" -> "🗺️"
                "MBTILES", "SQLITE" -> "📴"
                else -> "📄"
            }
            val kb = (f.length() / 1024.0).roundToInt().coerceAtLeast(1)
            items.add("$icon [$ext] ${f.name} ($kb KB)" to f)
        }

        if (items.isEmpty()) {
            Toast.makeText(activity, "⚠️ Brak plików do udostępnienia w bieżącym profilu.", Toast.LENGTH_SHORT).show()
            return
        }

        val labels = items.map { it.first }.toTypedArray()
        val dlg = AlertDialog.Builder(activity)
            .setTitle("📤 Wybierz plik do wysłania (Quick Share / Garmin / Bluetooth)")
            .setItems(labels) { _, which ->
                val chosenFile = items[which].second
                shareSingleFileViaProvider(activity, chosenFile)
            }
            .setNegativeButton("Anuluj", null)
            .create()

        dlg.show()
    }

    fun shareSingleFileViaProvider(activity: Activity, file: File) {
        try {
            val uri = FtakShareContentProvider.buildUriForFile(activity, file)
            val mime = when (file.extension.lowercase(Locale.ROOT)) {
                "gpx" -> "application/gpx+xml"
                "kmz" -> "application/vnd.google-earth.kmz"
                "zip" -> "application/zip"
                else -> "application/octet-stream"
            }
            val sendIntent = Intent(Intent.ACTION_SEND).apply {
                type = mime
                putExtra(Intent.EXTRA_STREAM, uri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            activity.startActivity(Intent.createChooser(sendIntent, "Udostępnij plik: ${file.name}"))
        } catch (ex: Exception) {
            Toast.makeText(activity, "⚠️ Nie udało się udostępnić pliku: ${ex.message}", Toast.LENGTH_LONG).show()
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
