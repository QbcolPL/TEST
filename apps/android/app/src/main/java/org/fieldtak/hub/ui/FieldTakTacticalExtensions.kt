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
    private const val PREF_RATIONALE_SHOWN = "permissions_rationale_shown"

    private var serverSocket: ServerSocket? = null
    private val serverRunning = AtomicBoolean(false)
    private val peerDownloadsCount = AtomicInteger(0)

    fun attachToActivity(activity: Activity) {
        activity.window?.decorView?.post {
            try {
                checkAndShowFirstRunPermissions(activity)
                val content = activity.findViewById<ViewGroup>(android.R.id.content) ?: return@post
                if (content.findViewWithTag<android.view.View>(OVERLAY_TAG) != null) return@post
                val bar = buildTacticalBottomBar(activity)
                val params = FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM).apply {
                    val m = dp(activity, 10); setMargins(m, m, m, dp(activity, 14))
                }
                content.addView(bar, params)
            } catch (_: Exception) { }
        }
    }

    private fun checkAndShowFirstRunPermissions(activity: Activity) {
        if (activity.isFinishing || activity.isDestroyed) return
        val prefs = activity.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        if (prefs.getBoolean(PREF_RATIONALE_SHOWN, false)) return
        val cam = isGranted(activity, Manifest.permission.CAMERA)
        val stg = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) Environment.isExternalStorageManager() else isGranted(activity, Manifest.permission.WRITE_EXTERNAL_STORAGE)
        val bt = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) isGranted(activity, Manifest.permission.BLUETOOTH_CONNECT) else isGranted(activity, Manifest.permission.ACCESS_FINE_LOCATION)
        if (!cam || !stg || !bt) {
            prefs.edit().putBoolean(PREF_RATIONALE_SHOWN, true).apply()
            showPermissionsRationaleDialog(activity, true)
        }
    }

    private fun buildTacticalBottomBar(activity: Activity): LinearLayout {
        val container = LinearLayout(activity).apply {
            tag = OVERLAY_TAG; orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(activity, 10), dp(activity, 8), dp(activity, 10), dp(activity, 8))
            background = GradientDrawable().apply { setColor(Color.parseColor("#E6091418")); setStroke(dp(activity, 1), Color.parseColor("#1FE0C5")); cornerRadius = dp(activity, 10).toFloat() }
            elevation = dp(activity, 8).toFloat()
        }
        val lp = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply { setMargins(dp(activity, 3), 0, dp(activity, 3), 0) }
        container.addView(makeBtn(activity, "🛡️ Uprawnienia", false) { showPermissionsRationaleDialog(activity, false) }, lp)
        container.addView(makeBtn(activity, "📡 P2P Hotspot QR", true) { showP2pFieldShareDialog(activity) }, lp)
        container.addView(makeBtn(activity, "🧹 Wyczyść po grze", false) { showPostOpWipeDialog(activity) }, lp)
        return container
    }

    private fun makeBtn(ctx: Context, txt: String, accent: Boolean, onClick: () -> Unit) = Button(ctx).apply {
        text = txt; isAllCaps = false; setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f); typeface = Typeface.DEFAULT_BOLD
        setTextColor(if (accent) Color.parseColor("#061618") else Color.WHITE)
        minHeight = dp(ctx, 36); minimumHeight = dp(ctx, 36)
        background = GradientDrawable().apply {
            setColor(if (accent) Color.parseColor("#2CE5D0") else Color.parseColor("#0F2025"))
            setStroke(dp(ctx, 1), if (accent) Color.parseColor("#2CE5D0") else Color.parseColor("#1E424A"))
            cornerRadius = dp(ctx, 6).toFloat()
        }
        setOnClickListener { onClick() }
    }

    fun showPermissionsRationaleDialog(activity: Activity, isFirstRun: Boolean) {
        if (activity.isFinishing || activity.isDestroyed) return
        val dialog = Dialog(activity, android.R.style.Theme_Black_NoTitleBar_Fullscreen)
        dialog.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dialog.window?.setBackgroundDrawable(ColorDrawable(Color.parseColor("#091418")))

        val scroll = ScrollView(activity)
        val root = LinearLayout(activity).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(activity, 20), dp(activity, 24), dp(activity, 20), dp(activity, 24)) }
        scroll.addView(root); dialog.setContentView(scroll)

        root.addView(TextView(activity).apply {
            text = if (isFirstRun) "Przygotowanie Telefonu — Uprawnienia" else "Dlaczego Field TAK Hub potrzebuje tych uprawnień?"
            setTextColor(Color.parseColor("#2CE5D0")); setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f); typeface = Typeface.DEFAULT_BOLD
        })
        root.addView(TextView(activity).apply {
            text = "Zasada 2.4: ORGANIZATOR PRZYGOTOWUJE → UCZESTNIK SKANUJE → HUB ROBI RESZTĘ.\nSprawdź dlaczego poszczególne uprawnienia są wymagane do automatycznej konfiguracji ATAK i Meshtastic:"
            setTextColor(Color.parseColor("#8AA8A4")); setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f); setPadding(0, dp(activity, 6), 0, dp(activity, 16))
        })

        val cam = isGranted(activity, Manifest.permission.CAMERA)
        val stg = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) Environment.isExternalStorageManager() else isGranted(activity, Manifest.permission.WRITE_EXTERNAL_STORAGE)
        val bt = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) isGranted(activity, Manifest.permission.BLUETOOTH_CONNECT) else isGranted(activity, Manifest.permission.ACCESS_FINE_LOCATION)
        val inst = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) activity.packageManager.canRequestPackageInstalls() else true
        val pm = activity.getSystemService(Context.POWER_SERVICE) as? PowerManager
        val bat = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) pm?.isIgnoringBatteryOptimizations(activity.packageName) == true else true

        addPermCard(root, activity, "📷 Aparat (CAMERA)", cam, "Skanowanie kodu QR wydarzenia: Pozwala jednym skanem wczytać adres serwera i pobrać całą paczkę konfiguracyjną .ftak bez ręcznego wpisywania adresów IP.")
        addPermCard(root, activity, "📁 Pliki i Pamięć (MANAGE_EXTERNAL_STORAGE)", stg, "Wgrywanie map i certyfikatów do ATAK: Niezbędne, aby Hub mógł automatycznie skopiować mapy podkładowe, nakładki KML oraz certyfikaty serwera bezpośrednio do folderu aplikacji ATAK oraz wczytać kod QR z galerii.")
        addPermCard(root, activity, "📡 Bluetooth i Lokalizacja (BLUETOOTH / LOCATION)", bt, "Programowanie radia Meshtastic: Wymagane przez system Android do wykrycia Twojego urządzenia LoRa (np. T-Beam, Heltec, MeshTracker) przez Bluetooth i automatycznego wgrania kanałów drużyny.")
        addPermCard(root, activity, "📦 Instalacja pakietów (REQUEST_INSTALL_PACKAGES)", inst, "Instalacja ATAK i wymaganych wtyczek: Pozwala aplikacji zainstalować właściwą wersję ATAK-CIV, aplikację Meshtastic oraz wtyczki wymagane przez organizatora bezpośrednio z paczki wydarzenia.")
        addPermCard(root, activity, "🔋 Brak optymalizacji baterii (IGNORE_BATTERY_OPTIMIZATIONS)", bat, "Niezawodność w terenie: Zapobiega uśpieniu połączenia z radiem Meshtastic, gdy telefon leży w ładownicy z wygaszonym ekranem podczas wielogodzinnej gry.")

        root.addView(makeBtn(activity, if (isFirstRun) "✓ Zrozumiałem — nadaj uprawnienia" else "⚙️ Ustawienia uprawnień aplikacji", true) {
            dialog.dismiss()
            if (isFirstRun) requestPerms(activity) else try { activity.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply { data = Uri.fromParts("package", activity.packageName, null) }) } catch (_: Exception) { }
        }.apply { layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { setMargins(0, dp(activity, 12), 0, dp(activity, 8)) } })

        root.addView(makeBtn(activity, "Zamknij podgląd", false) { dialog.dismiss() })
        dialog.show()
    }

    private fun requestPerms(activity: Activity) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return
        val list = mutableListOf(Manifest.permission.CAMERA, Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) { list.add(Manifest.permission.BLUETOOTH_CONNECT); list.add(Manifest.permission.BLUETOOTH_SCAN) }
        val missing = list.filter { !isGranted(activity, it) }.toTypedArray()
        if (missing.isNotEmpty()) try { activity.requestPermissions(missing, 2401) } catch (_: Exception) { }
    }

    private fun addPermCard(parent: LinearLayout, ctx: Context, title: String, ok: Boolean, desc: String) {
        val card = LinearLayout(ctx).apply {
            orientation = LinearLayout.VERTICAL; setPadding(dp(ctx, 14), dp(ctx, 10), dp(ctx, 14), dp(ctx, 10))
            background = GradientDrawable().apply { setColor(Color.parseColor("#0F2025")); setStroke(dp(ctx, 1), if (ok) Color.parseColor("#1FE0C5") else Color.parseColor("#F5B82E")); cornerRadius = dp(ctx, 8).toFloat() }
        }
        card.addView(TextView(ctx).apply { text = "$title   [${if (ok) "✓ NADANO" else "⚠ WYMAGANE"}]"; setTextColor(if (ok) Color.parseColor("#2CE5D0") else Color.parseColor("#F5B82E")); setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f); typeface = Typeface.DEFAULT_BOLD })
        card.addView(TextView(ctx).apply { text = desc; setTextColor(Color.WHITE); setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f); setPadding(0, dp(ctx, 4), 0, 0) })
        parent.addView(card, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { setMargins(0, 0, 0, dp(ctx, 10)) })
    }

    private fun isGranted(ctx: Context, p: String) = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) ctx.checkSelfPermission(p) == PackageManager.PERMISSION_GRANTED else true

    fun showP2pFieldShareDialog(activity: Activity) {
        val pkg = findShareablePackage(activity)
        val ip = detectLocalIp() ?: "192.168.43.1"
        val url = "http://$ip:$P2P_PORT/package.ftak"
        startP2pServer(pkg)

        val root = LinearLayout(activity).apply { orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER_HORIZONTAL; setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12)); setBackgroundColor(Color.parseColor("#091418")) }
        root.addView(TextView(activity).apply { text = "📡 P2P Field Share (Hotspot Wi-Fi)"; setTextColor(Color.parseColor("#2CE5D0")); setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f); typeface = Typeface.DEFAULT_BOLD })
        root.addView(TextView(activity).apply { text = "Włącz Hotspot Wi-Fi. Koledzy łączą się z Twoim Wi-Fi i skanują poniższy kod QR, aby pobrać paczkę .ftak bez zasięgu LTE:"; setTextColor(Color.WHITE); setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f); setPadding(0, dp(activity, 8), 0, dp(activity, 10)) })

        val qrBmp = generateQrBitmap(url, 500)
        if (qrBmp != null) root.addView(ImageView(activity).apply { setImageBitmap(qrBmp); setBackgroundColor(Color.WHITE); setPadding(12, 12, 12, 12) }, LinearLayout.LayoutParams(dp(activity, 220), dp(activity, 220)))
        root.addView(TextView(activity).apply { text = "Plik: ${pkg.name} (${(pkg.length() / 1024).coerceAtLeast(1)} KB)\nAdres: $url | Pobrań: ${peerDownloadsCount.get()}"; setTextColor(Color.parseColor("#8AA8A4")); setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f); gravity = Gravity.CENTER; setPadding(0, dp(activity, 8), 0, 0) })

        AlertDialog.Builder(activity).setView(root)
            .setPositiveButton("Odśwież") { _, _ -> showP2pFieldShareDialog(activity) }
            .setNegativeButton("Zatrzymaj P2P") { _, _ -> stopP2pServer(); qrBmp?.recycle() }
            .setOnCancelListener { stopP2pServer(); qrBmp?.recycle() }.show()
    }

    private fun findShareablePackage(ctx: Context): File {
        val found = mutableListOf<File>()
        listOfNotNull(ctx.filesDir, ctx.getExternalFilesDir(null), ctx.cacheDir).forEach { d -> if (d.exists()) d.walkTopDown().maxDepth(4).forEach { f -> if (f.isFile && f.name.endsWith(".ftak", true)) found.add(f) } }
        found.maxByOrNull { it.lastModified() }?.let { if (it.length() > 0) return it }
        val fb = File(ctx.cacheDir, "field_share_package.ftak")
        if (!fb.exists() || fb.length() == 0L) ZipOutputStream(BufferedOutputStream(FileOutputStream(fb))).use { z -> z.putNextEntry(ZipEntry("manifest.json")); z.write("""{"schemaVersion":"2.4","neverExpires":true}""".toByteArray()); z.closeEntry() }
        return fb
    }

    private fun detectLocalIp(): String? = try {
        var cand: String? = null
        NetworkInterface.getNetworkInterfaces()?.toList()?.filter { it.isUp && !it.isLoopback }?.forEach { nif ->
            nif.inetAddresses.toList().filterIsInstance<Inet4Address>().filter { !it.isLoopbackAddress }.forEach { a ->
                val ip = a.hostAddress ?: return@forEach
                if (ip.startsWith("192.168.43.") || ip.startsWith("192.168.137.") || nif.name.lowercase().contains("ap")) return ip
                if (cand == null && (ip.startsWith("192.168.") || ip.startsWith("10."))) cand = ip
            }
        }
        cand
    } catch (_: Exception) { null }

    private fun startP2pServer(pkg: File) {
        if (serverRunning.getAndSet(true)) return
        peerDownloadsCount.set(0)
        Thread {
            try {
                val srv = ServerSocket(P2P_PORT); serverSocket = srv
                while (serverRunning.get()) {
                    val client = try { srv.accept() } catch (_: Exception) { break }
                    Thread {
                        try {
                            client.use { s ->
                                BufferedReader(InputStreamReader(s.getInputStream())).readLine()
                                val out = BufferedOutputStream(s.getOutputStream())
                                peerDownloadsCount.incrementAndGet()
                                out.write("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"${pkg.name}\"\r\nContent-Length: ${pkg.length()}\r\nConnection: close\r\n\r\n".toByteArray())
                                FileInputStream(pkg).use { fis -> val buf = ByteArray(16384); var r: Int; while (fis.read(buf).also { r = it } != -1) out.write(buf, 0, r) }
                                out.flush()
                            }
                        } catch (_: Exception) { }
                    }.start()
                }
            } catch (_: Exception) { } finally { serverRunning.set(false) }
        }.apply { isDaemon = true }.start()
    }

    private fun stopP2pServer() { serverRunning.set(false); try { serverSocket?.close() } catch (_: Exception) { }; serverSocket = null }

    private fun generateQrBitmap(content: String, size: Int): Bitmap? = try {
        val m = MultiFormatWriter().encode(content, BarcodeFormat.QR_CODE, size, size, EnumMap<EncodeHintType, Any>(EncodeHintType::class.java).apply { put(EncodeHintType.CHARACTER_SET, "UTF-8"); put(EncodeHintType.MARGIN, 1) })
        val px = IntArray(size * size) { i -> if (m.get(i % size, i / size)) Color.BLACK else Color.WHITE }
        Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888).apply { setPixels(px, 0, size, 0, 0, size, size) }
    } catch (_: Exception) { null }

    fun showPostOpWipeDialog(activity: Activity) {
        val root = LinearLayout(activity).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(activity, 18), dp(activity, 16), dp(activity, 18), dp(activity, 12)); setBackgroundColor(Color.parseColor("#091418")) }
        root.addView(TextView(activity).apply { text = "🧹 Wyczyść po wydarzeniu (Post-Op Wipe)"; setTextColor(Color.parseColor("#2CE5D0")); setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f); typeface = Typeface.DEFAULT_BOLD })
        root.addView(TextView(activity).apply { text = "\n🗑️ ZOSTANIE USUNIĘTE: Certyfikaty (.p12), profile serwera (server.txt, config.pref) oraz nakładki KML/GPX z ostatniej gry.\n\n🛡️ ZOSTANIE ZACHOWANE: Aplikacja ATAK-CIV, Meshtastic oraz ciężkie mapy podkładowe (.mbtiles, .sqlite, .tif)."; setTextColor(Color.WHITE); setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f) })
        AlertDialog.Builder(activity).setView(root).setPositiveButton("Wykonaj Post-Op Wipe") { _, _ ->
            Thread {
                var del = 0; var maps = 0
                val roots = mutableListOf<File>()
                listOfNotNull(activity.filesDir, activity.getExternalFilesDir(null), activity.cacheDir).forEach { roots.add(it) }
                try { Environment.getExternalStorageDirectory()?.let { File(it, "atak").let { a -> if (a.exists()) roots.addAll(listOf(File(a, "cert"), File(a, "overlays"), File(a, "import"), File(a, "imagery"))) } } } catch (_: Exception) { }
                for (r in roots) if (r.exists()) r.walkBottomUp().forEach { f ->
                    if (f.isFile) {
                        val ext = f.extension.lowercase()
                        if (ext in setOf("mbtiles", "sqlite", "sqlitedb", "tif", "tiff", "geotiff")) maps++
                        else if (ext in setOf("p12", "pem", "crt", "key", "pref", "kml", "kmz", "gpx", "cot") || f.name.lowercase() in setOf("tactical_config.json", "server.txt", "config.pref")) if (f.delete()) del++
                    }
                }
                activity.runOnUiThread { Toast.makeText(activity, "✓ Post-Op Wipe: Usunięto plików gry: $del | Zachowano map: $maps", Toast.LENGTH_LONG).show() }
            }.start()
        }.setNegativeButton("Anuluj", null).show()
    }

    private fun dp(c: Context, v: Int) = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v.toFloat(), c.resources.displayMetrics).toInt()
}
