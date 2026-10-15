package org.fieldtak.hub.ui

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.app.Dialog
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.PowerManager
import android.provider.Settings
import android.util.TypedValue
import android.view.Gravity
import android.view.ViewGroup
import android.view.Window
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.MultiFormatWriter
import java.io.BufferedOutputStream
import java.io.BufferedReader
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStreamReader
import java.net.Inet4Address
import java.net.NetworkInterface
import java.net.ServerSocket
import java.util.EnumMap
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

object FieldTakTacticalExtensions {

    private const val OVERLAY_TAG = "FTH_TACTICAL_TOOLBAR_2_4"
    private const val P2P_PORT = 8787
    private const val PREFS_NAME = "FieldTakHub_Prefs"
    private const val PREF_PERMISSIONS_RATIONALE_SHOWN = "permissions_rationale_shown"

    private var serverSocket: ServerSocket? = null
    private val serverRunning = AtomicBoolean(false)
    private val peerDownloadsCount = AtomicInteger(0)

    fun attachToActivity(activity: Activity) {
        activity.window?.decorView?.post {
            try {
                // 1. Ekran kafelkowy wyjaśniający uprawnienia przy pierwszym uruchomieniu
                checkAndShowFirstRunPermissions(activity)

                // 2. Dolny pasek narzędzi polowych 2.4
                val content = activity.findViewById<ViewGroup>(android.R.id.content) ?: return@post
                if (content.findViewWithTag<android.view.View>(OVERLAY_TAG) != null) return@post

                val bar = buildTacticalBottomBar(activity)
                val params = FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT,
                    FrameLayout.LayoutParams.WRAP_CONTENT,
                    Gravity.BOTTOM
                ).apply {
                    val marginPx = dp(activity, 10)
                    setMargins(marginPx, marginPx, marginPx, dp(activity, 14))
                }
                content.addView(bar, params)
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }
    }

    private fun checkAndShowFirstRunPermissions(activity: Activity) {
        if (activity.isFinishing || activity.isDestroyed) return
        val prefs = activity.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        if (prefs.getBoolean(PREF_PERMISSIONS_RATIONALE_SHOWN, false)) return

        val camGranted = isPermissionGranted(activity, Manifest.permission.CAMERA)
        val storageGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            isPermissionGranted(activity, Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }
        val btGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            isPermissionGranted(activity, Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            isPermissionGranted(activity, Manifest.permission.ACCESS_FINE_LOCATION)
        }

        if (!camGranted || !storageGranted || !btGranted) {
            prefs.edit().putBoolean(PREF_PERMISSIONS_RATIONALE_SHOWN, true).apply()
            showPermissionsRationaleDialog(activity, isFirstRun = true)
        }
    }

    private fun buildTacticalBottomBar(activity: Activity): LinearLayout {
        val container = LinearLayout(activity).apply {
            tag = OVERLAY_TAG
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(activity, 10), dp(activity, 8), dp(activity, 10), dp(activity, 8))
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#E6091418"))
                setStroke(dp(activity, 1), Color.parseColor("#1FE0C5"))
                cornerRadius = dp(activity, 10).toFloat()
            }
            elevation = dp(activity, 8).toFloat()
        }

        val btnPermissions = createTacticalButton(activity, "🛡️ Uprawnienia (Info)", false) {
            showPermissionsRationaleDialog(activity, isFirstRun = false)
        }
        val btnP2pShare = createTacticalButton(activity, "📡 P2P Hotspot QR", true) {
            showP2pFieldShareDialog(activity)
        }
        val btnPostOpWipe = createTacticalButton(activity, "🧹 Wyczyść po grze", false) {
            showPostOpWipeDialog(activity)
        }

        val lp = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
            setMargins(dp(activity, 3), 0, dp(activity, 3), 0)
        }
        container.addView(btnPermissions, lp)
        container.addView(btnP2pShare, lp)
        container.addView(btnPostOpWipe, lp)

        return container
    }

    private fun createTacticalButton(context: Context, text: String, accent: Boolean, onClick: () -> Unit): Button {
        return Button(context).apply {
            this.text = text
            isAllCaps = false
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(if (accent) Color.parseColor("#061618") else Color.WHITE)
            setPadding(dp(context, 4), dp(context, 6), dp(context, 4), dp(context, 6))
            minHeight = dp(context, 36)
            minimumHeight = dp(context, 36)
            background = GradientDrawable().apply {
                setColor(if (accent) Color.parseColor("#2CE5D0") else Color.parseColor("#0F2025"))
                setStroke(dp(context, 1), if (accent) Color.parseColor("#2CE5D0") else Color.parseColor("#1E424A"))
                cornerRadius = dp(context, 6).toFloat()
            }
            setOnClickListener { onClick() }
        }
    }

    // =========================================================================
    // 1. EKRAN KAFELKOWY WYJAŚNIAJĄCY UPRAWNIENIA (ZANIM SYSTEM ZAPYTA)
    // =========================================================================
    fun showPermissionsRationaleDialog(activity: Activity, isFirstRun: Boolean) {
        if (activity.isFinishing || activity.isDestroyed) return

        val dialog = Dialog(activity, android.R.style.Theme_Black_NoTitleBar_Fullscreen)
        dialog.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dialog.window?.setBackgroundDrawable(ColorDrawable(Color.parseColor("#091418")))

        val scroll = ScrollView(activity)
        val root = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(activity, 20), dp(activity, 24), dp(activity, 20), dp(activity, 24))
        }
        scroll.addView(root)
        dialog.setContentView(scroll)

        root.addView(TextView(activity).apply {
            text = if (isFirstRun) "Przygotowanie Telefonu — Uprawnienia" else "Dlaczego Field TAK Hub potrzebuje tych uprawnień?"
            setTextColor(Color.parseColor("#2CE5D0"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
            typeface = Typeface.DEFAULT_BOLD
            setPadding(0, 0, 0, dp(activity, 8))
        })

        root.addView(TextView(activity).apply {
            text = "Zasada 2.4: ORGANIZATOR PRZYGOTOWUJE → UCZESTNIK SKANUJE → FIELD TAK HUB ROBI RESZTĘ.\n\nZanim system Android wyświetli okienka z prośbą o dostęp, sprawdź dlaczego każde z poniższych uprawnień jest niezbędne w terenie:"
            setTextColor(Color.parseColor("#8AA8A4"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f)
            setPadding(0, 0, 0, dp(activity, 18))
        })

        val camGranted = isPermissionGranted(activity, Manifest.permission.CAMERA)
        val storageGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            isPermissionGranted(activity, Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }
        val btGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            isPermissionGranted(activity, Manifest.permission.BLUETOOTH_CONNECT) &&
                isPermissionGranted(activity, Manifest.permission.BLUETOOTH_SCAN)
        } else {
            isPermissionGranted(activity, Manifest.permission.ACCESS_FINE_LOCATION)
        }
        val installGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            activity.packageManager.canRequestPackageInstalls()
        } else true
        val pm = activity.getSystemService(Context.POWER_SERVICE) as? PowerManager
        val batteryGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            pm?.isIgnoringBatteryOptimizations(activity.packageName) == true
        } else true

        addPermissionCard(
            root, activity,
            "📷 Aparat (CAMERA)",
            camGranted,
            "Skanowanie kodu QR wydarzenia: Pozwala jednym skanem wczytać adres serwera i pobrać całą paczkę konfiguracyjną .ftak bez ręcznego wpisywania adresów IP."
        )
        addPermissionCard(
            root, activity,
            "📁 Pliki i Pamięć (MANAGE_EXTERNAL_STORAGE / Media)",
            storageGranted,
            "Wgrywanie map i certyfikatów do ATAK: Niezbędne, aby Hub mógł automatycznie skopiować mapy podkładowe, nakładki KML oraz certyfikaty serwera bezpośrednio do folderu aplikacji ATAK oraz wczytać kod QR z galerii."
        )
        addPermissionCard(
            root, activity,
            "📡 Bluetooth i Lokalizacja (BLUETOOTH_CONNECT / FINE_LOCATION)",
            btGranted,
            "Programowanie radia Meshtastic: Wymagane przez system Android do wykrycia Twojego urządzenia LoRa (np. T-Beam, Heltec, MeshTracker) przez Bluetooth i automatycznego wgrania kanałów drużyny."
        )
        addPermissionCard(
            root, activity,
            "📦 Instalacja pakietów (REQUEST_INSTALL_PACKAGES)",
            installGranted,
            "Instalacja ATAK i wymaganych wtyczek: Pozwala aplikacji zainstalować właściwą wersję ATAK-CIV, aplikację Meshtastic oraz wtyczki wymagane przez organizatora bezpośrednio z paczki wydarzenia."
        )
        addPermissionCard(
            root, activity,
            "🔋 Brak optymalizacji baterii (IGNORE_BATTERY_OPTIMIZATIONS)",
            batteryGranted,
            "Niezawodność w terenie: Zapobiega uśpieniu połączenia z radiem Meshtastic, gdy telefon leży w ładownicy z wygaszonym ekranem podczas wielogodzinnej gry."
        )

        val btnPrimary = Button(activity).apply {
            text = if (isFirstRun) "✓ Zrozumiałem — nadaj wymagane uprawnienia" else "⚙️ Otwórz ustawienia uprawnień aplikacji"
            isAllCaps = false
            setTextColor(Color.parseColor("#061618"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14.5f)
            typeface = Typeface.DEFAULT_BOLD
            setPadding(dp(activity, 12), dp(activity, 14), dp(activity, 12), dp(activity, 14))
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#2CE5D0"))
                cornerRadius = dp(activity, 8).toFloat()
            }
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                setMargins(0, dp(activity, 14), 0, dp(activity, 8))
            }

            setOnClickListener {
                dialog.dismiss()
                if (isFirstRun) {
                    requestRuntimePermissionsNow(activity)
                } else {
                    try {
                        val intent = Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
                            data = Uri.fromParts("package", activity.packageName, null)
                        }
                        activity.startActivity(intent)
                    } catch (_: Exception) { }
                }
            }
        }
        root.addView(btnPrimary)

        val btnClose = Button(activity).apply {
            text = "Zamknij podgląd"
            isAllCaps = false
            setTextColor(Color.WHITE)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#0F2025"))
                setStroke(dp(activity, 1), Color.parseColor("#1E424A"))
                cornerRadius = dp(activity, 8).toFloat()
            }
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            )
            setOnClickListener { dialog.dismiss() }
        }
        root.addView(btnClose)

        dialog.show()
    }

    private fun requestRuntimePermissionsNow(activity: Activity) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return
        val perms = mutableListOf(
            Manifest.permission.CAMERA,
            Manifest.permission.ACCESS_FINE_LOCATION,
            Manifest.permission.ACCESS_COARSE_LOCATION
        )
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            perms.add(Manifest.permission.BLUETOOTH_CONNECT)
            perms.add(Manifest.permission.BLUETOOTH_SCAN)
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            perms.add(Manifest.permission.READ_MEDIA_IMAGES)
            perms.add(Manifest.permission.POST_NOTIFICATIONS)
        } else {
            perms.add(Manifest.permission.READ_EXTERNAL_STORAGE)
            perms.add(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }

        val missing = perms.filter { !isPermissionGranted(activity, it) }.toTypedArray()
        if (missing.isNotEmpty()) {
            try {
                activity.requestPermissions(missing, 2401)
            } catch (_: Exception) { }
        }
    }

    private fun addPermissionCard(parent: LinearLayout, context: Context, title: String, granted: Boolean, rationale: String) {
        val card = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(context, 14), dp(context, 12), dp(context, 14), dp(context, 12))
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#0F2025"))
                setStroke(dp(context, 1), if (granted) Color.parseColor("#1FE0C5") else Color.parseColor("#F5B82E"))
                cornerRadius = dp(context, 8).toFloat()
            }
        }
        val lp = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            LinearLayout.LayoutParams.WRAP_CONTENT
        ).apply {
            setMargins(0, 0, 0, dp(context, 10))
        }

        val statusText = if (granted) "✓ NADANO" else "⚠ WYMAGANE"
        val statusColor = if (granted) Color.parseColor("#2CE5D0") else Color.parseColor("#F5B82E")

        card.addView(TextView(context).apply {
            text = "$title   [$statusText]"
            setTextColor(statusColor)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f)
            typeface = Typeface.DEFAULT_BOLD
        })
        card.addView(TextView(context).apply {
            text = rationale
            setTextColor(Color.WHITE)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            setPadding(0, dp(context, 6), 0, 0)
        })

        parent.addView(card, lp)
    }

    private fun isPermissionGranted(context: Context, permission: String): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED
        } else true
    }

    // =========================================================================
    // 2. TRYB P2P FIELD SHARE (HOTSPOT WI-FI BEZ LTE)
    // =========================================================================
    fun showP2pFieldShareDialog(activity: Activity) {
        val packageFile = findOrPrepareShareablePackage(activity)
        val localIp = detectLocalHotspotOrWifiIp() ?: "192.168.43.1"
        val shareUrl = "http://$localIp:$P2P_PORT/package.ftak"

        startP2pHttpServer(packageFile)

        val scroll = ScrollView(activity)
        val root = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12))
            setBackgroundColor(Color.parseColor("#091418"))
        }
        scroll.addView(root)

        root.addView(TextView(activity).apply {
            text = "📡 P2P Field Share (Hotspot Wi-Fi bez LTE)"
            setTextColor(Color.parseColor("#2CE5D0"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            typeface = Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER
        })

        root.addView(TextView(activity).apply {
            text = "1. Włącz Hotspot Wi-Fi w tym telefonie.\n2. Koledzy z drużyny łączą się z Twoim Wi-Fi (w lesie bez zasięgu sieci komórkowej).\n3. Skanują poniższy kod QR — paczka .ftak pobiera się bezpośrednio z Twojego telefonu!"
            setTextColor(Color.WHITE)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            setPadding(0, dp(activity, 8), 0, dp(activity, 12))
        })

        val qrBitmap = generateQrCodeBitmap(shareUrl, 520)
        if (qrBitmap != null) {
            val qrView = ImageView(activity).apply {
                setImageBitmap(qrBitmap)
                setPadding(dp(activity, 10), dp(activity, 10), dp(activity, 10), dp(activity, 10))
                setBackgroundColor(Color.WHITE)
            }
            val qrLp = LinearLayout.LayoutParams(dp(activity, 230), dp(activity, 230)).apply {
                setMargins(0, dp(activity, 4), 0, dp(activity, 10))
            }
            root.addView(qrView, qrLp)
        }

        val sizeKb = (packageFile.length() / 1024).coerceAtLeast(1)
        root.addView(TextView(activity).apply {
            text = "Udostępniany plik: ${packageFile.name} (${sizeKb} KB)\nAdres P2P: $shareUrl\nStatus serwera: AKTYWNY (Port $P2P_PORT) | Pobrań: ${peerDownloadsCount.get()}"
            setTextColor(Color.parseColor("#8AA8A4"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            gravity = Gravity.CENTER
            setPadding(dp(activity, 8), dp(activity, 8), dp(activity, 8), dp(activity, 4))
        })

        AlertDialog.Builder(activity)
            .setView(scroll)
            .setPositiveButton("Odśwież IP / Licznik") { _, _ ->
                showP2pFieldShareDialog(activity)
            }
            .setNegativeButton("Zatrzymaj P2P i zamknij") { _, _ ->
                stopP2pHttpServer()
                qrBitmap?.recycle()
            }
            .setOnCancelListener {
                stopP2pHttpServer()
                qrBitmap?.recycle()
            }
            .show()
    }

    private fun findOrPrepareShareablePackage(context: Context): File {
        val candidates = mutableListOf<File>()
        listOfNotNull(context.filesDir, context.getExternalFilesDir(null), context.cacheDir).forEach { dir ->
            if (dir.exists()) {
                dir.walkTopDown().maxDepth(4).forEach { f ->
                    if (f.isFile && (f.name.endsWith(".ftak", true) || f.name.endsWith(".zip", true))) {
                        candidates.add(f)
                    }
                }
            }
        }
        val newest = candidates.maxByOrNull { it.lastModified() }
        if (newest != null && newest.length() > 0) return newest

        val fallback = File(context.cacheDir, "field_share_package.ftak")
        if (!fallback.exists() || fallback.length() == 0L) {
            ZipOutputStream(BufferedOutputStream(FileOutputStream(fallback))).use { zos ->
                zos.putNextEntry(ZipEntry("manifest.json"))
                zos.write("""{"schemaVersion":"2.4","packageName":"Field P2P Share","loraRegion":"EU_868","loraModemPreset":"SHORT_FAST","neverExpires":true}""".toByteArray(Charsets.UTF_8))
                zos.closeEntry()
            }
        }
        return fallback
    }

    private fun detectLocalHotspotOrWifiIp(): String? {
        return try {
            var candidateIp: String? = null
            NetworkInterface.getNetworkInterfaces()?.toList()?.filter { it.isUp && !it.isLoopback }?.forEach { nif ->
                nif.inetAddresses.toList().filterIsInstance<Inet4Address>().filter { !it.isLoopbackAddress }.forEach { addr ->
                    val ip = addr.hostAddress ?: return@forEach
                    if (ip.startsWith("192.168.43.") || ip.startsWith("192.168.137.") || nif.name.lowercase().contains("ap")) return ip
                    if (candidateIp == null && (ip.startsWith("192.168.") || ip.startsWith("10.") || ip.startsWith("172."))) candidateIp = ip
                }
            }
            candidateIp
        } catch (_: Exception) {
            null
        }
    }

    private fun startP2pHttpServer(packageFile: File) {
        if (serverRunning.getAndSet(true)) return
        peerDownloadsCount.set(0)
        Thread {
            try {
                val srv = ServerSocket(P2P_PORT)
                serverSocket = srv
                while (serverRunning.get()) {
                    val client = try { srv.accept() } catch (_: Exception) { break }
                    Thread {
                        try {
                            client.use { sock ->
                                val req = BufferedReader(InputStreamReader(sock.getInputStream())).readLine() ?: ""
                                val out = BufferedOutputStream(sock.getOutputStream())
                                if (req.contains("/provision.json")) {
                                    val body = """{"schemaVersion":"1.0","packageUrl":"/package.ftak","neverExpires":true}""".toByteArray(Charsets.UTF_8)
                                    out.write("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: ${body.size}\r\nConnection: close\r\n\r\n".toByteArray(Charsets.UTF_8))
                                    out.write(body)
                                    out.flush()
                                } else {
                                    peerDownloadsCount.incrementAndGet()
                                    out.write("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"${packageFile.name}\"\r\nContent-Length: ${packageFile.length()}\r\nConnection: close\r\n\r\n".toByteArray(Charsets.UTF_8))
                                    FileInputStream(packageFile).use { fis ->
                                        val buf = ByteArray(16384)
                                        var read: Int
                                        while (fis.read(buf).also { read = it } != -1) out.write(buf, 0, read)
                                    }
                                    out.flush()
                                }
                            }
                        } catch (_: Exception) { }
                    }.start()
                }
            } catch (_: Exception) {
            } finally {
                serverRunning.set(false)
            }
        }.apply { isDaemon = true }.start()
    }

    private fun stopP2pHttpServer() {
        serverRunning.set(false)
        try { serverSocket?.close() } catch (_: Exception) { }
        serverSocket = null
    }

    private fun generateQrCodeBitmap(content: String, sizePx: Int): Bitmap? {
        return try {
            val hints = EnumMap<EncodeHintType, Any>(EncodeHintType::class.java).apply {
                put(EncodeHintType.CHARACTER_SET, "UTF-8")
                put(EncodeHintType.MARGIN, 1)
            }
            val matrix = MultiFormatWriter().encode(content, BarcodeFormat.QR_CODE, sizePx, sizePx, hints)
            val pixels = IntArray(sizePx * sizePx) { i ->
                if (matrix.get(i % sizePx, i / sizePx)) Color.BLACK else Color.WHITE
            }
            Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888).apply {
                setPixels(pixels, 0, sizePx, 0, 0, sizePx, sizePx)
            }
        } catch (_: Exception) {
            null
        }
    }

    // =========================================================================
    // 3. WYCZYŚĆ PO WYDARZENIU (POST-OP WIPE - ZACHOWUJE MAPY I ATAK)
    // =========================================================================
    fun showPostOpWipeDialog(activity: Activity) {
        val root = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12))
            setBackgroundColor(Color.parseColor("#091418"))
        }

        root.addView(TextView(activity).apply {
            text = "🧹 Wyczyść po wydarzeniu (Post-Op Wipe)"
            setTextColor(Color.parseColor("#2CE5D0"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            typeface = Typeface.DEFAULT_BOLD
        })

        root.addView(TextView(activity).apply {
            text = "\nTa operacja bezpiecznie przygotuje telefon na kolejną imprezę:\n\n" +
                "🗑️ ZOSTANIE USUNIĘTE:\n" +
                "• Certyfikaty z ostatniego wydarzenia (.p12, .pem, .crt)\n" +
                "• Stare profile połączenia z serwerem (server.txt, config.pref)\n" +
                "• Nakładki taktyczne z ostatniej gry (.kml, .kmz, .gpx, .cot)\n\n" +
                "🛡️ ZOSTANIE ZACHOWANE:\n" +
                "• Aplikacja ATAK-CIV oraz Meshtastic\n" +
                "• Ciężkie mapy podkładowe (.mbtiles, .sqlite, .tif, .geotiff)"
            setTextColor(Color.WHITE)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
        })

        AlertDialog.Builder(activity)
            .setView(root)
            .setPositiveButton("Wykonaj Post-Op Wipe") { _, _ ->
                Thread {
                    val res = executePostOpWipe(activity)
                    activity.runOnUiThread {
                        Toast.makeText(activity, res, Toast.LENGTH_LONG).show()
                    }
                }.start()
            }
            .setNegativeButton("Anuluj", null)
            .show()
    }

    private fun executePostOpWipe(context: Context): String {
        var del = 0
        var maps = 0
        val roots = mutableListOf<File>()
        listOfNotNull(context.filesDir, context.getExternalFilesDir(null), context.cacheDir).forEach { roots.add(it) }
        try {
            Environment.getExternalStorageDirectory()?.let { ext ->
                val atakDir = File(ext, "atak")
                if (atakDir.exists()) {
                    roots.add(File(atakDir, "cert"))
                    roots.add(File(atakDir, "overlays"))
                    roots.add(File(atakDir, "import"))
                    roots.add(File(atakDir, "imagery"))
                }
            }
        } catch (_: Exception) { }

        for (root in roots) {
            if (!root.exists()) continue
            try {
                root.walkBottomUp().forEach { f ->
                    if (f.isFile) {
                        val ext = f.extension.lowercase()
                        if (ext in setOf("mbtiles", "sqlite", "sqlitedb", "tif", "tiff", "geotiff") || f.absolutePath.lowercase().contains("/imagery/")) {
                            maps++
                        } else if (ext in setOf("p12", "pem", "crt", "key", "pref", "kml", "kmz", "gpx", "cot") || f.name.lowercase() in setOf("tactical_config.json", "server.txt", "config.pref")) {
                            if (f.delete()) del++
                        }
                    }
                }
            } catch (_: Exception) { }
        }
        return "✓ Post-Op Wipe zakończony! Usunięto plików gry: $del | Zachowano map bazowych: $maps"
    }

    private fun dp(c: Context, v: Int): Int {
        return TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v.toFloat(), c.resources.displayMetrics).toInt()
    }
}