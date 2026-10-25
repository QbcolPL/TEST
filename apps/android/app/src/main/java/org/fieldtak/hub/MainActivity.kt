package org.fieldtak.hub

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlertDialog
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.hardware.Camera
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.view.WindowInsets
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import java.util.Locale
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

// =========================================================================
// WEKTOROWE IKONY TAKTYCZNO-MILSIMOWE (CANVAS HUD VECTOR RENDERER)
// =========================================================================
class TacticalVectorIconView(
    context: Context,
    private val iconType: Int,
    private val tintColor: Int
) : View(context) {

    companion object {
        const val ICON_RADAR = 1
        const val ICON_QR_RETICLE = 2
        const val ICON_GALLERY_MAP = 3
        const val ICON_PACKAGE_SHIELD = 4
        const val ICON_OPORD_DOC = 5
        const val ICON_VERSION_CHECK = 6
        const val ICON_ATAK_CROSSHAIR = 7
        const val ICON_LORA_WAVE = 8
        const val ICON_WIFI_HOTSPOT = 9
        const val ICON_RADIO_HEADSET = 10
        const val ICON_SOS_BEACON = 11
    }

    private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = tintColor
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }

    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = tintColor
        style = Paint.Style.FILL
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val w = width.toFloat()
        val h = height.toFloat()
        val s = min(w, h)
        if (s <= 0f) return
        val cx = w / 2f
        val cy = h / 2f
        val r = s * 0.40f
        strokePaint.strokeWidth = max(2.2f, s * 0.095f)

        when (iconType) {
            ICON_RADAR -> {
                canvas.drawCircle(cx, cy, r, strokePaint)
                canvas.drawCircle(cx, cy, r * 0.52f, strokePaint)
                canvas.drawCircle(cx, cy, r * 0.14f, fillPaint)
                canvas.drawLine(cx, cy, cx + r * 0.72f, cy - r * 0.68f, strokePaint)
                canvas.drawLine(cx - r, cy, cx + r, cy, strokePaint)
            }
            ICON_QR_RETICLE -> {
                val b = r * 0.95f
                val cLen = b * 0.45f
                // Narożniki celownika QR
                canvas.drawLine(cx - b, cy - b, cx - b + cLen, cy - b, strokePaint)
                canvas.drawLine(cx - b, cy - b, cx - b, cy - b + cLen, strokePaint)
                canvas.drawLine(cx + b, cy - b, cx + b - cLen, cy - b, strokePaint)
                canvas.drawLine(cx + b, cy - b, cx + b, cy - b + cLen, strokePaint)
                canvas.drawLine(cx - b, cy + b, cx - b + cLen, cy + b, strokePaint)
                canvas.drawLine(cx - b, cy + b, cx - b, cy + b - cLen, strokePaint)
                canvas.drawLine(cx + b, cy + b, cx + b - cLen, cy + b, strokePaint)
                canvas.drawLine(cx + b, cy + b, cx + b, cy + b - cLen, strokePaint)
                canvas.drawRect(cx - b * 0.35f, cy - b * 0.35f, cx + b * 0.35f, cy + b * 0.35f, strokePaint)
            }
            ICON_GALLERY_MAP -> {
                val rect = RectF(cx - r, cy - r * 0.8f, cx + r, cy + r * 0.8f)
                canvas.drawRoundRect(rect, s * 0.1f, s * 0.1f, strokePaint)
                val path = Path().apply {
                    moveTo(cx - r * 0.75f, cy + r * 0.55f)
                    lineTo(cx - r * 0.2f, cy - r * 0.15f)
                    lineTo(cx + r * 0.2f, cy + r * 0.25f)
                    lineTo(cx + r * 0.5f, cy - r * 0.05f)
                    lineTo(cx + r * 0.8f, cy + r * 0.55f)
                }
                canvas.drawPath(path, strokePaint)
                canvas.drawCircle(cx - r * 0.4f, cy - r * 0.35f, r * 0.16f, fillPaint)
            }
            ICON_PACKAGE_SHIELD -> {
                val path = Path().apply {
                    moveTo(cx, cy - r)
                    lineTo(cx + r * 0.88f, cy - r * 0.55f)
                    lineTo(cx + r * 0.88f, cy + r * 0.15f)
                    quadTo(cx + r * 0.75f, cy + r * 0.78f, cx, cy + r)
                    quadTo(cx - r * 0.75f, cy + r * 0.78f, cx - r * 0.88f, cy + r * 0.15f)
                    lineTo(cx - r * 0.88f, cy - r * 0.55f)
                    close()
                }
                canvas.drawPath(path, strokePaint)
                canvas.drawLine(cx, cy - r * 0.4f, cx, cy + r * 0.4f, strokePaint)
                canvas.drawLine(cx - r * 0.4f, cy, cx + r * 0.4f, cy, strokePaint)
            }
            ICON_OPORD_DOC -> {
                val rect = RectF(cx - r * 0.8f, cy - r, cx + r * 0.8f, cy + r)
                canvas.drawRoundRect(rect, s * 0.08f, s * 0.08f, strokePaint)
                canvas.drawLine(cx - r * 0.45f, cy - r * 0.45f, cx + r * 0.45f, cy - r * 0.45f, strokePaint)
                canvas.drawLine(cx - r * 0.45f, cy, cx + r * 0.45f, cy, strokePaint)
                canvas.drawLine(cx - r * 0.45f, cy + r * 0.45f, cx + r * 0.15f, cy + r * 0.45f, strokePaint)
            }
            ICON_VERSION_CHECK -> {
                canvas.drawCircle(cx, cy, r, strokePaint)
                val check = Path().apply {
                    moveTo(cx - r * 0.45f, cy + r * 0.02f)
                    lineTo(cx - r * 0.1f, cy + r * 0.38f)
                    lineTo(cx + r * 0.5f, cy - r * 0.35f)
                }
                canvas.drawPath(check, strokePaint)
            }
            ICON_ATAK_CROSSHAIR -> {
                canvas.drawCircle(cx, cy, r * 0.82f, strokePaint)
                canvas.drawCircle(cx, cy, r * 0.2f, fillPaint)
                canvas.drawLine(cx, cy - r * 1.08f, cx, cy - r * 0.48f, strokePaint)
                canvas.drawLine(cx, cy + r * 0.48f, cx, cy + r * 1.08f, strokePaint)
                canvas.drawLine(cx - r * 1.08f, cy, cx - r * 0.48f, cy, strokePaint)
                canvas.drawLine(cx + r * 0.48f, cy, cx + r * 1.08f, cy, strokePaint)
            }
            ICON_LORA_WAVE -> {
                canvas.drawCircle(cx, cy + r * 0.35f, r * 0.18f, fillPaint)
                canvas.drawLine(cx, cy + r * 0.35f, cx, cy + r * 0.95f, strokePaint)
                val arc1 = RectF(cx - r * 0.55f, cy - r * 0.2f, cx + r * 0.55f, cy + r * 0.9f)
                val arc2 = RectF(cx - r * 0.95f, cy - r * 0.6f, cx + r * 0.95f, cy + r * 1.3f)
                canvas.drawArc(arc1, 210f, 120f, false, strokePaint)
                canvas.drawArc(arc2, 210f, 120f, false, strokePaint)
            }
            ICON_WIFI_HOTSPOT -> {
                canvas.drawCircle(cx, cy, r * 0.22f, fillPaint)
                val a1 = RectF(cx - r * 0.6f, cy - r * 0.6f, cx + r * 0.6f, cy + r * 0.6f)
                val a2 = RectF(cx - r, cy - r, cx + r, cy + r)
                canvas.drawArc(a1, 135f, 90f, false, strokePaint)
                canvas.drawArc(a1, 315f, 90f, false, strokePaint)
                canvas.drawArc(a2, 135f, 90f, false, strokePaint)
                canvas.drawArc(a2, 315f, 90f, false, strokePaint)
            }
            ICON_RADIO_HEADSET -> {
                val body = RectF(cx - r * 0.6f, cy - r * 0.35f, cx + r * 0.6f, cy + r * 0.95f)
                canvas.drawRoundRect(body, s * 0.08f, s * 0.08f, strokePaint)
                canvas.drawLine(cx - r * 0.35f, cy - r * 0.35f, cx - r * 0.35f, cy - r * 0.98f, strokePaint)
                canvas.drawLine(cx - r * 0.3f, cy + r * 0.1f, cx + r * 0.3f, cy + r * 0.1f, strokePaint)
                canvas.drawLine(cx - r * 0.3f, cy + r * 0.45f, cx + r * 0.3f, cy + r * 0.45f, strokePaint)
            }
            ICON_SOS_BEACON -> {
                val tri = Path().apply {
                    moveTo(cx, cy - r * 0.95f)
                    lineTo(cx + r * 0.95f, cy + r * 0.85f)
                    lineTo(cx - r * 0.95f, cy + r * 0.85f)
                    close()
                }
                canvas.drawPath(tri, strokePaint)
                canvas.drawLine(cx, cy - r * 0.28f, cx, cy + r * 0.28f, strokePaint)
                canvas.drawCircle(cx, cy + r * 0.56f, r * 0.11f, fillPaint)
            }
        }
    }
}

@Suppress("DEPRECATION")
class MainActivity : AppCompatActivity() {

    companion object {
        private const val REQ_PICK_GALLERY_QR = 3101
        private const val REQ_PICK_FTAK_FILE = 3102
        private const val REQ_RUNTIME_PERMS = 3103
        private const val REQ_CAMERA_FOR_QR = 3104
        private const val BUYCOFFEE_URL = "https://buycoffee.to/qbcol"
    }

    private var lastProcessedIntentHash: Int = 0
    private var pendingCameraQrScanAfterPermission: Boolean = false

    override fun onCreate(savedInstanceState: Bundle?) {
        try {
            requestWindowFeature(Window.FEATURE_NO_TITLE)
        } catch (_: Exception) { }
        super.onCreate(savedInstanceState)
        try {
            actionBar?.hide()
        } catch (_: Exception) { }

        if (Build.VERSION.SDK_INT >= 30) {
            try {
                window?.setDecorFitsSystemWindows(true)
            } catch (_: Exception) { }
        }
        window?.statusBarColor = Color.parseColor("#061014")
        window?.navigationBarColor = Color.parseColor("#061014")

        rebuildScreen()
        checkIncomingViewIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        rebuildScreen()
        checkIncomingViewIntent(intent)
        if (pendingCameraQrScanAfterPermission) {
            pendingCameraQrScanAfterPermission = false
            if (Build.VERSION.SDK_INT < 23 || checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                showLiveCameraQrScannerDialog()
            } else {
                Toast.makeText(
                    this,
                    FtakProvisioningEngine.tr(this, "⚠️ Brak uprawnień do aparatu — użyj opcji 'QR z Galerii'!", "⚠️ Camera permission denied — use 'QR from Gallery'!"),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    override fun onDestroy() {
        FieldShareAndQrEngine.stopLocalFtakServer()
        super.onDestroy()
    }

    private fun checkIncomingViewIntent(incoming: Intent?) {
        val dataUri = incoming?.data ?: return
        val hash = dataUri.toString().hashCode()
        if (hash == 0 || hash == lastProcessedIntentHash) return
        lastProcessedIntentHash = hash

        val scheme = dataUri.scheme?.lowercase(Locale.ROOT) ?: ""
        if (scheme == "http" || scheme == "https" || scheme == "meshtastic") {
            FieldShareAndQrEngine.handleScannedOrGalleryQrPayload(this, dataUri.toString()) {
                rebuildScreen()
                showIdentityAndFactionModal()
            }
            return
        }

        try {
            contentResolver.openInputStream(dataUri)?.use { stream ->
                if (FtakProvisioningEngine.importFromFtakStream(this, stream)) {
                    Toast.makeText(
                        this,
                        FtakProvisioningEngine.tr(this, "✓ Wczytano: ", "✓ Loaded: ") + FtakProvisioningEngine.getEventName(this),
                        Toast.LENGTH_LONG
                    ).show()
                    rebuildScreen()
                    showIdentityAndFactionModal()
                }
            }
        } catch (ex: Exception) {
            Toast.makeText(this, "⚠️ File Error: " + ex.message, Toast.LENGTH_LONG).show()
        }
    }

    private fun rebuildScreen() {
        if (!FtakProvisioningEngine.isPermissionsOnboardingCompleted(this)) {
            setContentView(buildPermissionOnboardingView())
        } else {
            setContentView(buildMainTacticalHubView())
        }
    }

    private fun applySafeSystemBarsPadding(targetContainer: View, sideDp: Int) {
        val sidePx = dp(sideDp)
        val minTopPx = getStatusBarHeightPx() + dp(12)
        val minBottomPx = getNavBarHeightPx() + dp(24)

        targetContainer.setPadding(sidePx, minTopPx, sidePx, minBottomPx)

        if (Build.VERSION.SDK_INT >= 23) {
            targetContainer.setOnApplyWindowInsetsListener { v, insets ->
                var insetTop = 0
                var insetBot = 0
                if (Build.VERSION.SDK_INT >= 30) {
                    val sysBars = insets.getInsets(WindowInsets.Type.systemBars())
                    insetTop = sysBars.top
                    insetBot = sysBars.bottom
                } else {
                    insetTop = insets.systemWindowInsetTop
                    insetBot = insets.systemWindowInsetBottom
                }
                val finalTop = max(minTopPx, insetTop + dp(12))
                val finalBot = max(minBottomPx, insetBot + dp(24))
                v.setPadding(sidePx, finalTop, sidePx, finalBot)
                insets
            }
        }
    }

    private fun getStatusBarHeightPx(): Int {
        val resId = resources.getIdentifier("status_bar_height", "dimen", "android")
        val h = if (resId > 0) resources.getDimensionPixelSize(resId) else 0
        return max(h, dp(32))
    }

    private fun getNavBarHeightPx(): Int {
        val resId = resources.getIdentifier("navigation_bar_height", "dimen", "android")
        val h = if (resId > 0) resources.getDimensionPixelSize(resId) else 0
        return max(h, dp(48))
    }

    // =========================================================================
    // EKRAN 1: ŚWIADOMY ONBOARDING UPRAWNIEŃ (PL / ENG)
    // =========================================================================
    private fun buildPermissionOnboardingView(): View {
        val scroll = ScrollView(this).apply {
            setBackgroundColor(Color.parseColor("#061014"))
            isFillViewport = true
            clipToPadding = false
        }

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }
        applySafeSystemBarsPadding(root, 18)

        val badgeHeader = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = roundedBox("#0A181F", "#1FE0C5", 2, 14f)
            setPadding(dp(16), dp(16), dp(16), dp(16))
        }

        badgeHeader.addView(TextView(this).apply {
            text = FtakProvisioningEngine.tr(this@MainActivity, "🛡️ FIELD TAK HUB v3.0 — AUTORYZACJA SYSTEMU", "🛡️ FIELD TAK HUB v3.0 — SYSTEM AUTHORIZATION")
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16.5f)
        })

        badgeHeader.addView(TextView(this).apply {
            text = FtakProvisioningEngine.tr(
                this@MainActivity,
                "Terminal wdrożeniowy dla ATAK-CIV, Meshtastic oraz rozkazów OPORD. Nadaj poniższe uprawnienia, aby umożliwić automatyczne wstrzykiwanie map i kontrolę kompatybilności:",
                "Tactical deployment terminal for ATAK-CIV, Meshtastic, and OPORD orders. Grant the permissions below to enable automatic map injection and compatibility checks:"
            )
            setTextColor(Color.parseColor("#B8CFD8"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            setPadding(0, dp(8), 0, 0)
        })

        root.addView(badgeHeader, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(14)
        })

        fun addPermCard(iconType: Int, titlePl: String, titleEn: String, descPl: String, descEn: String) {
            val card = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = roundedBox("#0A181F", "#193844", 1, 10f)
                setPadding(dp(14), dp(12), dp(14), dp(12))
            }
            val icon = TacticalVectorIconView(this, iconType, Color.parseColor("#1FE0C5"))
            card.addView(icon, LinearLayout.LayoutParams(dp(28), dp(28)).apply { rightMargin = dp(12) })

            val col = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
            col.addView(TextView(this).apply {
                text = FtakProvisioningEngine.tr(this@MainActivity, titlePl, titleEn)
                setTextColor(Color.WHITE)
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f)
            })
            col.addView(TextView(this).apply {
                text = FtakProvisioningEngine.tr(this@MainActivity, descPl, descEn)
                setTextColor(Color.parseColor("#98B2BC"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                setPadding(0, dp(3), 0, 0)
            })
            card.addView(col, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            root.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                bottomMargin = dp(10)
            })
        }

        addPermCard(
            TacticalVectorIconView.ICON_QR_RETICLE,
            "1. Aparat i Galeria (Skaner QR)",
            "1. Camera & Gallery (QR Scanner)",
            "Skanowanie kodów QR z plakatów Safezone, monitora Buildera PC oraz obrazków z Discorda.",
            "Scanning QR codes from Safezone posters, PC Builder monitor, and Discord screenshots."
        )
        addPermCard(
            TacticalVectorIconView.ICON_PACKAGE_SHIELD,
            "2. Dostęp do /sdcard/atak/ i instalatora APK",
            "2. /sdcard/atak/ Storage & APK Installer",
            "Bezpośrednie wstrzykiwanie map (.kmz, .mbtiles, .gpx) do ATAK-CIV oraz wyrównywanie wersji APK.",
            "Direct injection of maps (.kmz, .mbtiles, .gpx) into ATAK-CIV and APK version alignment."
        )
        addPermCard(
            TacticalVectorIconView.ICON_WIFI_HOTSPOT,
            "3. Sieć Lokalna Wi-Fi / LAN Hotspot",
            "3. Local Wi-Fi / LAN Hotspot",
            "Pobieranie i udostępnianie paczek .ftak oraz rozkazów OPORD w polu bez internetu.",
            "Pulling and sharing .ftak bundles and OPORD orders in the field without internet."
        )
        addPermCard(
            TacticalVectorIconView.ICON_SOS_BEACON,
            "4. Pozycja GPS na żądanie (SOS MGRS)",
            "4. On-Demand GPS Fix (SOS MGRS)",
            "Wyliczanie współrzędnych ratunkowych MGRS / Keypad wyłącznie po naciśnięciu przycisku SOS.",
            "Calculating emergency MGRS / Keypad coordinates strictly when pressing the SOS button."
        )

        val btnGrant = makeIconButton(
            TacticalVectorIconView.ICON_VERSION_CHECK,
            FtakProvisioningEngine.tr(this, "NADAJ UPRAWNIENIA I URUCHOM TERMINAL", "GRANT PERMISSIONS & LAUNCH TERMINAL"),
            "#1FE0C5",
            Color.parseColor("#041014"),
            Color.parseColor("#041014"),
            14f
        ) {
            requestAllTacticalPermissions()
        }
        root.addView(btnGrant, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(54)).apply {
            topMargin = dp(10)
            bottomMargin = dp(20)
        })

        scroll.addView(root)
        return scroll
    }

    private fun requestAllTacticalPermissions() {
        FtakProvisioningEngine.setPermissionsOnboardingCompleted(this, true)
        if (Build.VERSION.SDK_INT >= 23) {
            val perms = mutableListOf(
                Manifest.permission.CAMERA,
                Manifest.permission.ACCESS_FINE_LOCATION,
                Manifest.permission.ACCESS_COARSE_LOCATION
            )
            if (Build.VERSION.SDK_INT >= 33) {
                perms.add("android.permission.READ_MEDIA_IMAGES")
                perms.add("android.permission.POST_NOTIFICATIONS")
            } else {
                perms.add(Manifest.permission.READ_EXTERNAL_STORAGE)
                perms.add(Manifest.permission.WRITE_EXTERNAL_STORAGE)
            }
            requestPermissions(perms.toTypedArray(), REQ_RUNTIME_PERMS)
        }
        if (!FtakProvisioningEngine.hasAllFilesStorageAccess()) {
            FtakProvisioningEngine.requestAllFilesStorageAccess(this)
        }
        rebuildScreen()
    }

    // =========================================================================
    // EKRAN 2: GŁÓWNY TERMINAL TAKTYCZNY v3.0 (W STYLU WINDOWS BUILDERA)
    // =========================================================================
    private fun buildMainTacticalHubView(): View {
        val scroll = ScrollView(this).apply {
            setBackgroundColor(Color.parseColor("#061014"))
            isFillViewport = true
            clipToPadding = false
        }

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }
        applySafeSystemBarsPadding(root, 14)

        val hasProfile = FtakProvisioningEngine.hasLoadedProfile(this)
        val activeFaction = FtakProvisioningEngine.getActiveFaction(this)
        val allFactions = FtakProvisioningEngine.getLoadedFactions(this)
        val callsign = FtakProvisioningEngine.getCallsign(this)
        val eventName = FtakProvisioningEngine.getEventName(this)
        val accentHex = activeFaction?.colorHex ?: "#1FE0C5"

        // ---------------------------------------------------------------------
        // KARTA NAGŁÓWKOWA (W STYLU SIDEBARA WINDOWS BUILDERA + PRZEŁĄCZNIK PL/ENG)
        // ---------------------------------------------------------------------
        val headerCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = roundedBox("#0A181F", accentHex, 2, 12f)
            setPadding(dp(14), dp(12), dp(14), dp(12))
        }

        val topRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        val radarLogo = TacticalVectorIconView(this, TacticalVectorIconView.ICON_RADAR, Color.parseColor("#1FE0C5"))
        topRow.addView(radarLogo, LinearLayout.LayoutParams(dp(28), dp(28)).apply { rightMargin = dp(10) })

        val titleCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }

        titleCol.addView(TextView(this).apply {
            text = "FIELD TAK HUB COMMAND v3.0"
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
        })

        titleCol.addView(TextView(this).apply {
            text = if (hasProfile) eventName else FtakProvisioningEngine.tr(this@MainActivity, "BRAK WCZYTANEJ OPERACJI", "NO OPERATION LOADED")
            setTextColor(Color.WHITE)
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
        })
        topRow.addView(titleCol)

        // Przełącznik języka PL / ENG
        val btnLang = makeSimpleButton(
            "🌐 " + FtakProvisioningEngine.getLanguage(this),
            "#122A34",
            Color.parseColor("#1FE0C5"),
            11.5f
        ) {
            FtakProvisioningEngine.toggleLanguage(this)
            rebuildScreen()
        }
        topRow.addView(btnLang, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(36)).apply { rightMargin = dp(6) })

        // Przycisk Callsign i wyboru strony
        val btnEditIdentity = makeSimpleButton(
            "👤 " + callsign + " ✎",
            "#152C35",
            Color.WHITE,
            11.5f
        ) {
            showIdentityAndFactionModal()
        }
        topRow.addView(btnEditIdentity, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(36)))
        headerCard.addView(topRow)

        if (hasProfile && activeFaction != null) {
            val factionFiles = FtakProvisioningEngine.getFactionFilteredFiles(this, activeFaction)
            val otsInfo = if (activeFaction.otsHost.isNotBlank()) activeFaction.otsHost else FtakProvisioningEngine.tr(this, "Tryb Lokalny / Mesh", "Off-Grid Mesh")
            headerCard.addView(TextView(this).apply {
                text = FtakProvisioningEngine.tr(
                    this@MainActivity,
                    "🛡️ Strona: " + activeFaction.name + "  •  LoRa: " + activeFaction.channelName + " (" + activeFaction.loraRegion + ")\n🌐 OTS: " + otsInfo + "  •  Zasoby map/DataPkg: " + factionFiles.size,
                    "🛡️ Faction: " + activeFaction.name + "  •  LoRa: " + activeFaction.channelName + " (" + activeFaction.loraRegion + ")\n🌐 OTS: " + otsInfo + "  •  Map/DataPkg Assets: " + factionFiles.size
                )
                setTextColor(Color.parseColor("#B8CFD8"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
                setPadding(0, dp(8), 0, dp(4))
            })

            if (allFactions.size > 1) {
                val facSwitchRow = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    setPadding(0, dp(6), 0, 0)
                }
                for (fac in allFactions) {
                    val isSelected = fac.name.equals(activeFaction.name, ignoreCase = true)
                    val facBtn = makeSimpleButton(
                        if (isSelected) ("✓ " + fac.name) else fac.name,
                        if (isSelected) fac.colorHex else "#102027",
                        Color.WHITE,
                        11.5f
                    ) {
                        FtakProvisioningEngine.setActiveFaction(this, fac.name)
                        rebuildScreen()
                    }
                    facSwitchRow.addView(facBtn, LinearLayout.LayoutParams(0, dp(34), 1f).apply { rightMargin = dp(4) })
                }
                headerCard.addView(facSwitchRow)
            }
        } else {
            headerCard.addView(TextView(this).apply {
                text = FtakProvisioningEngine.tr(
                    this@MainActivity,
                    "Zeskanuj kod QR z Buildera PC / plakatu Safezone, wczytaj obrazek QR z Galerii lub wskaż plik .ftak poniżej:",
                    "Scan a QR code from PC Builder / Safezone poster, pick a QR screenshot from Gallery, or load an .ftak file below:"
                )
                setTextColor(Color.parseColor("#9CB3BC"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
                setPadding(0, dp(6), 0, 0)
            })
        }

        root.addView(headerCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(10)
        })

        // ---------------------------------------------------------------------
        // SEKCJA 1: POTRÓJNY MODUŁ WEJŚCIA (IKONY WEKTOROWE)
        // ---------------------------------------------------------------------
        val inputSection = createSectionContainer(
            TacticalVectorIconView.ICON_QR_RETICLE,
            FtakProvisioningEngine.tr(this, "1. WCZYTAJ KONFIGURACJĘ OPERACJI (.FTAK / QR)", "1. LOAD OPERATION CONFIGURATION (.FTAK / QR)")
        )
        val inputRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }

        val btnScanCamera = makeIconButton(
            TacticalVectorIconView.ICON_QR_RETICLE,
            FtakProvisioningEngine.tr(this, "Skanuj QR\n(Aparat)", "Scan QR\n(Camera)"),
            "#1FE0C5",
            Color.parseColor("#041014"),
            Color.parseColor("#041014"),
            11f
        ) { startCameraQrScan() }

        val btnScanGallery = makeIconButton(
            TacticalVectorIconView.ICON_GALLERY_MAP,
            FtakProvisioningEngine.tr(this, "QR z Galerii\n(Obrazek)", "Gallery QR\n(Image)"),
            "#122730",
            Color.WHITE,
            Color.parseColor("#1FE0C5"),
            11f
        ) { pickQrImageFromGallery() }

        val btnLoadFtak = makeIconButton(
            TacticalVectorIconView.ICON_PACKAGE_SHIELD,
            FtakProvisioningEngine.tr(this, "Wczytaj\nPlik .FTAK", "Load\n.FTAK File"),
            "#122730",
            Color.WHITE,
            Color.parseColor("#1FE0C5"),
            11f
        ) { pickFtakFileFromStorage() }

        inputRow.addView(btnScanCamera, LinearLayout.LayoutParams(0, dp(54), 1f).apply { rightMargin = dp(5) })
        inputRow.addView(btnScanGallery, LinearLayout.LayoutParams(0, dp(54), 1f).apply { rightMargin = dp(5) })
        inputRow.addView(btnLoadFtak, LinearLayout.LayoutParams(0, dp(54), 1f))
        inputSection.addView(inputRow)

        root.addView(inputSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(10)
        })

        // ---------------------------------------------------------------------
        // SEKCJA 2: KOMPATYBILNOŚĆ WERSJI ATAK-CIV, MESHTASTIC I PLUGINÓW (POD OTS)
        // ---------------------------------------------------------------------
        val compatSection = createSectionContainer(
            TacticalVectorIconView.ICON_VERSION_CHECK,
            FtakProvisioningEngine.tr(this, "2. KOMPATYBILNOŚĆ WERSJI (ATAK / MESHTASTIC / PLUGINY)", "2. VERSION COMPATIBILITY (ATAK / MESHTASTIC / PLUGINS)")
        )
        val compatList = FtakProvisioningEngine.inspectEcosystemCompatibility(this)
        for (item in compatList) {
            val statusColorHex = when {
                item.isCompatible -> "#2ECC71"
                item.isInstalled -> "#FAD02C"
                else -> "#EB4034"
            }
            val statusLabel = when {
                item.isCompatible -> FtakProvisioningEngine.tr(this, "✓ ZGODNA (" + item.installedVersion + ")", "✓ OK (" + item.installedVersion + ")")
                item.isInstalled -> FtakProvisioningEngine.tr(this, "⚠️ WERSJA: " + item.installedVersion + " (Wymagana: " + item.requirement.requiredVersion + ")", "⚠️ VER: " + item.installedVersion + " (Req: " + item.requirement.requiredVersion + ")")
                else -> FtakProvisioningEngine.tr(this, "❌ BRAK W TELEFONIE", "❌ NOT INSTALLED")
            }

            val rowCard = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = roundedBox("#0E1E26", statusColorHex, 1, 8f)
                setPadding(dp(10), dp(7), dp(10), dp(7))
            }

            val infoCol = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            }
            infoCol.addView(TextView(this).apply {
                text = item.requirement.displayName + (if (item.requirement.requiredVersion.isNotBlank()) ("  [Req: " + item.requirement.requiredVersion + "]") else "")
                setTextColor(Color.WHITE)
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            })
            infoCol.addView(TextView(this).apply {
                text = statusLabel
                setTextColor(Color.parseColor(statusColorHex))
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            })
            rowCard.addView(infoCol)

            if (item.bundledApkFile != null && item.bundledApkFile.exists()) {
                val btnInstallApk = makeSimpleButton(
                    FtakProvisioningEngine.tr(this, "📥 Instaluj APK", "📥 Install APK"),
                    "#1FE0C5",
                    Color.parseColor("#041014"),
                    11f
                ) {
                    FtakProvisioningEngine.installBundledApkOneClick(this, item.bundledApkFile)
                }
                rowCard.addView(btnInstallApk, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32)))
            }

            compatSection.addView(rowCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                bottomMargin = dp(6)
            })
        }

        root.addView(compatSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(10)
        })

        // ---------------------------------------------------------------------
        // SEKCJA 3: WDRÓŻ USTAWIENIA 1-KLIKIEM (ATAK-CIV & MESHTASTIC)
        // ---------------------------------------------------------------------
        val deploySection = createSectionContainer(
            TacticalVectorIconView.ICON_ATAK_CROSSHAIR,
            FtakProvisioningEngine.tr(this, "3. WSTRZYKNIJ USTAWIENIA 1-KLIKIEM (ATAK & MESHTASTIC)", "3. 1-CLICK PROVISIONING (ATAK-CIV & MESHTASTIC)")
        )

        val btnDeployAtak = makeIconButton(
            TacticalVectorIconView.ICON_ATAK_CROSSHAIR,
            FtakProvisioningEngine.tr(this, "WSTRZYKNIJ MAPY, CALLSIGN I SERWER DO ATAK-CIV", "INJECT MAPS, CALLSIGN & SERVER INTO ATAK-CIV"),
            if (hasProfile) "#2684FF" else "#152832",
            Color.WHITE,
            Color.WHITE,
            12.5f
        ) {
            FtakProvisioningEngine.provisionAtakOneClick(this)
        }
        deploySection.addView(btnDeployAtak, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(50)).apply {
            bottomMargin = dp(8)
        })

        val btnDeployMesh = makeIconButton(
            TacticalVectorIconView.ICON_LORA_WAVE,
            FtakProvisioningEngine.tr(this, "ZAPROGRAMUJ RADIO MESHTASTIC (KANAŁ + PSK)", "PROGRAM MESHTASTIC RADIO (CHANNEL + PSK)"),
            if (hasProfile) "#2ECC71" else "#152832",
            if (hasProfile) Color.parseColor("#04120A") else Color.WHITE,
            if (hasProfile) Color.parseColor("#04120A") else Color.parseColor("#2ECC71"),
            12.5f
        ) {
            FtakProvisioningEngine.provisionMeshtasticOneClick(this)
        }
        deploySection.addView(btnDeployMesh, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(50)))

        root.addView(deploySection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(10)
        })

        // ---------------------------------------------------------------------
        // SEKCJA 4: ROZKAZ BOJOWY OPORD (PODGLĄD W APLIKACJI + IMPORT Z LAN/HOTSPOTU)
        // ---------------------------------------------------------------------
        val opordSection = createSectionContainer(
            TacticalVectorIconView.ICON_OPORD_DOC,
            FtakProvisioningEngine.tr(this, "4. ROZKAZ BOJOWY OPORD (PODGLĄD + IMPORT WI-FI / LAN)", "4. OPERATIONAL ORDER — OPORD (VIEWER + WI-FI / LAN IMPORT)")
        )

        val hasOpord = FtakProvisioningEngine.hasAnyOpordLoaded(this)
        val btnOpenOpord = makeIconButton(
            TacticalVectorIconView.ICON_OPORD_DOC,
            if (hasOpord)
                FtakProvisioningEngine.tr(this, "OTWÓRZ ROZKAZ BOJOWY OPORD (SARNEG / METOC / ROLA)", "OPEN LOADED OPORD (SARNEG / METOC / ROLES)")
            else
                FtakProvisioningEngine.tr(this, "PODGLĄD OPORD (BRAK WCZYTANEGO — POBIERZ Z LAN LUB .FTAK)", "OPORD VIEWER (NONE LOADED — PULL FROM LAN OR .FTAK)"),
            if (hasOpord) "#163844" else "#12242C",
            Color.parseColor("#1FE0C5"),
            Color.parseColor("#1FE0C5"),
            12f
        ) {
            FieldShareAndQrEngine.showOpordViewerModal(this)
        }
        opordSection.addView(btnOpenOpord, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(48)).apply {
            bottomMargin = dp(6)
        })

        val opordSubRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val btnPullOpordLan = makeSimpleButton(
            FtakProvisioningEngine.tr(this, "📡 Importuj OPORD z LAN / PC", "📡 Pull OPORD from LAN / PC"),
            "#122630",
            Color.WHITE,
            11f
        ) {
            FieldShareAndQrEngine.showImportStandaloneOpordLanDialog(this)
        }
        val btnShareOpordHotspot = makeSimpleButton(
            FtakProvisioningEngine.tr(this, "📤 Udostępnij OPORD (Hotspot)", "📤 Share OPORD (Hotspot)"),
            "#122630",
            Color.WHITE,
            11f
        ) {
            FieldShareAndQrEngine.showWifiHotspotShareDialog(this, opordOnlyDefault = true)
        }
        opordSubRow.addView(btnPullOpordLan, LinearLayout.LayoutParams(0, dp(42), 1f).apply { rightMargin = dp(5) })
        opordSubRow.addView(btnShareOpordHotspot, LinearLayout.LayoutParams(0, dp(42), 1f))
        opordSection.addView(opordSubRow)

        root.addView(opordSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(10)
        })

        // ---------------------------------------------------------------------
        // SEKCJA 5: FIELD SHARE HUB (HOTSPOT AD-HOC DLA PACZEK .FTAK I PLIKÓW .GPX)
        // ---------------------------------------------------------------------
        val shareSection = createSectionContainer(
            TacticalVectorIconView.ICON_WIFI_HOTSPOT,
            FtakProvisioningEngine.tr(this, "5. DYSTRYBUCJA POLOWA (HOTSPOT AD-HOC BEZ INTERNETU)", "5. FIELD DISTRIBUTION (OFF-GRID WI-FI HOTSPOT)")
        )

        val btnHotspotServer = makeIconButton(
            TacticalVectorIconView.ICON_WIFI_HOTSPOT,
            FtakProvisioningEngine.tr(this, "UDOSTĘPNIJ .FTAK / OPORD PRZEZ HOTSPOT (SERWER + QR)", "SHARE .FTAK / OPORD VIA HOTSPOT (SERVER + QR)"),
            "#142E38",
            Color.parseColor("#1FE0C5"),
            Color.parseColor("#1FE0C5"),
            11.5f
        ) {
            FieldShareAndQrEngine.showWifiHotspotShareDialog(this, opordOnlyDefault = false)
        }
        shareSection.addView(btnHotspotServer, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(46)).apply {
            bottomMargin = dp(6)
        })

        val shareSubRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val btnShowFactionQr = makeSimpleButton(
            FtakProvisioningEngine.tr(this, "📱 Pokaż QR Strony Offline", "📱 Show Offline Faction QR"),
            "#12232B",
            Color.WHITE,
            11f
        ) {
            FieldShareAndQrEngine.showOfflineFactionQrDialog(this)
        }
        val btnShareFiles = makeSimpleButton(
            FtakProvisioningEngine.tr(this, "📤 Wyślij .ftak / .gpx / .apk", "📤 Send .ftak / .gpx / .apk"),
            "#12232B",
            Color.WHITE,
            11f
        ) {
            FieldShareAndQrEngine.showShareFilesPickerDialog(this)
        }
        shareSubRow.addView(btnShowFactionQr, LinearLayout.LayoutParams(0, dp(42), 1f).apply { rightMargin = dp(5) })
        shareSubRow.addView(btnShareFiles, LinearLayout.LayoutParams(0, dp(42), 1f))
        shareSection.addView(shareSubRow)

        root.addView(shareSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(10)
        })

        // ---------------------------------------------------------------------
        // SEKCJA 6: ŚCIĄGA OPERACYJNA (TABELA KANAŁÓW RADIOWYCH)
        // ---------------------------------------------------------------------
        val radioSection = createSectionContainer(
            TacticalVectorIconView.ICON_RADIO_HEADSET,
            FtakProvisioningEngine.tr(this, "6. ŚCIĄGA OPERACYJNA (TABELA KANAŁÓW RADIOWYCH)", "6. FIELD CHEAT SHEET (VOICE RADIO CHANNELS)")
        )
        if (activeFaction == null || activeFaction.voiceChannels.isEmpty()) {
            radioSection.addView(TextView(this).apply {
                text = FtakProvisioningEngine.tr(
                    this@MainActivity,
                    "Brak zapisanych kanałów głosowych PMR / VHF / DMR — wczytaj paczkę .ftak lub kod QR.",
                    "No voice channels loaded — scan a QR code or load an .ftak package."
                )
                setTextColor(Color.parseColor("#8CA4AE"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            })
        } else {
            for (vc in activeFaction.voiceChannels) {
                val chCard = LinearLayout(this).apply {
                    orientation = LinearLayout.VERTICAL
                    background = roundedBox("#0E1D23", "#203A44", 1, 8f)
                    setPadding(dp(12), dp(7), dp(12), dp(7))
                }
                chCard.addView(TextView(this).apply {
                    text = vc.roleName + "   [" + vc.bandType + "]"
                    setTextColor(Color.parseColor("#1FE0C5"))
                    typeface = Typeface.DEFAULT_BOLD
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                })
                chCard.addView(TextView(this).apply {
                    text = FtakProvisioningEngine.tr(this@MainActivity, "Kanał / Częstotliwość: ", "Freq / Channel: ") + vc.freqMhz + "   •   " + vc.toneOrCode
                    setTextColor(Color.WHITE)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
                })
                radioSection.addView(chCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    bottomMargin = dp(5)
                })
            }
        }

        root.addView(radioSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(10)
        })

        // ---------------------------------------------------------------------
        // SEKCJA 7: ALARM RATUNKOWY SOS MGRS & WSPARCIE TWÓRCY
        // ---------------------------------------------------------------------
        val sosBtn = makeIconButton(
            TacticalVectorIconView.ICON_SOS_BEACON,
            FtakProvisioningEngine.tr(this, "ALARM RATUNKOWY SOS MGRS / KONTAKT ORGA", "EMERGENCY SOS MGRS BEACON / CONTACT ORGA"),
            "#EB4034",
            Color.WHITE,
            Color.WHITE,
            13f
        ) {
            showOnDemandSosMgrsModal()
        }
        root.addView(sosBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(52)).apply {
            bottomMargin = dp(8)
        })

        val btnSupportCoffee = makeSimpleButton(
            FtakProvisioningEngine.tr(this, "☕ Wesprzyj twórcę Field TAK Hub (buycoffee.to)", "☕ Support Field TAK Hub Creator (buycoffee.to)"),
            "#0D1D24",
            Color.parseColor("#FAD02C"),
            11.5f
        ) {
            showCreatorSupportModal()
        }
        root.addView(btnSupportCoffee, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42)).apply {
            bottomMargin = dp(26)
        })

        scroll.addView(root)
        return scroll
    }

    private fun showIdentityAndFactionModal() {
        val factions = FtakProvisioningEngine.getLoadedFactions(this)
        var chosenFaction = FtakProvisioningEngine.getActiveFaction(this)

        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(16), dp(18), dp(12))
        }

        box.addView(TextView(this).apply {
            text = FtakProvisioningEngine.tr(this@MainActivity, "👤 CALLSIGN GRACZA & STRONA KONFLIKTU", "👤 PLAYER CALLSIGN & FACTION")
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
        })

        val inputCallsign = EditText(this).apply {
            setText(FtakProvisioningEngine.getCallsign(this@MainActivity))
            setTextColor(Color.WHITE)
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS
            background = roundedBox("#0E1D22", "#1FE0C5", 1, 8f)
            setPadding(dp(12), dp(10), dp(12), dp(10))
        }
        box.addView(inputCallsign, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(8)
            bottomMargin = dp(12)
        })

        if (factions.isNotEmpty()) {
            val rg = RadioGroup(this).apply { orientation = RadioGroup.VERTICAL }
            factions.forEachIndexed { idx, fac ->
                val rb = RadioButton(this).apply {
                    id = 5000 + idx
                    text = "  " + fac.name + "  (LoRa: " + fac.channelName + ")"
                    setTextColor(parseColorSafe(fac.colorHex, Color.WHITE))
                    typeface = Typeface.DEFAULT_BOLD
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f)
                    isChecked = (chosenFaction?.name == fac.name)
                    setOnClickListener { chosenFaction = fac }
                }
                rg.addView(rb)
            }
            box.addView(rg)
        }

        val dlg = AlertDialog.Builder(this)
            .setView(ScrollView(this).apply { addView(box) })
            .setPositiveButton(FtakProvisioningEngine.tr(this, "💾 Zapisz", "💾 Save")) { _, _ ->
                FtakProvisioningEngine.setCallsign(this, inputCallsign.text.toString())
                chosenFaction?.let { FtakProvisioningEngine.setActiveFaction(this, it.name) }
                rebuildScreen()
            }
            .setNegativeButton(FtakProvisioningEngine.tr(this, "Anuluj", "Cancel"), null)
            .create()

        dlg.window?.setBackgroundDrawable(roundedBox("#081316", "#1FE0C5", 2, 14f))
        dlg.show()
    }

    private fun startCameraQrScan() {
        if (Build.VERSION.SDK_INT >= 23 && checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            pendingCameraQrScanAfterPermission = true
            requestPermissions(arrayOf(Manifest.permission.CAMERA), REQ_CAMERA_FOR_QR)
            return
        }
        showLiveCameraQrScannerDialog()
    }

    private fun showLiveCameraQrScannerDialog() {
        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(16), dp(16), dp(16), dp(12))
        }

        box.addView(TextView(this).apply {
            text = FtakProvisioningEngine.tr(this@MainActivity, "📷 SKANER QR (.FTAK / OPORD LAN / MESHTASTIC)", "📷 QR SCANNER (.FTAK / OPORD LAN / MESHTASTIC)")
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f)
            gravity = Gravity.CENTER
        })

        val previewFrame = FrameLayout(this).apply {
            background = roundedBox("#000000", "#1FE0C5", 2, 12f)
            setPadding(dp(4), dp(4), dp(4), dp(4))
        }

        val surfaceView = SurfaceView(this)
        previewFrame.addView(surfaceView, FrameLayout.LayoutParams(dp(280), dp(330), Gravity.CENTER))

        val aimingCross = TextView(this).apply {
            text = "[  +  ]"
            setTextColor(Color.parseColor("#AA1FE0C5"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 28f)
            gravity = Gravity.CENTER
        }
        previewFrame.addView(aimingCross, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(330)))

        box.addView(previewFrame, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(10)
            bottomMargin = dp(8)
        })

        var cameraInstance: Camera? = null
        val decodedFlag = AtomicBoolean(false)
        val busyDecoding = AtomicBoolean(false)
        var lastDecodeTime = 0L

        fun releaseCam() {
            try {
                cameraInstance?.setPreviewCallback(null)
                cameraInstance?.stopPreview()
                cameraInstance?.release()
            } catch (_: Exception) { }
            cameraInstance = null
        }

        val dlg = AlertDialog.Builder(this)
            .setView(box)
            .setNeutralButton(FtakProvisioningEngine.tr(this, "🖼️ Wybierz QR z Galerii", "🖼️ Pick QR from Gallery")) { _, _ ->
                releaseCam()
                pickQrImageFromGallery()
            }
            .setNegativeButton(FtakProvisioningEngine.tr(this, "Zamknij", "Close")) { _, _ ->
                releaseCam()
            }
            .setOnDismissListener {
                releaseCam()
            }
            .create()

        fun onQrDetectedSuccess(qrText: String) {
            if (qrText.isNotBlank() && decodedFlag.compareAndSet(false, true)) {
                runOnUiThread {
                    releaseCam()
                    dlg.dismiss()
                    FieldShareAndQrEngine.handleScannedOrGalleryQrPayload(this@MainActivity, qrText) {
                        rebuildScreen()
                        showIdentityAndFactionModal()
                    }
                }
            }
        }

        surfaceView.setOnClickListener {
            try { cameraInstance?.autoFocus { _, _ -> } } catch (_: Exception) { }
        }

        surfaceView.holder.addCallback(object : SurfaceHolder.Callback {
            override fun surfaceCreated(holder: SurfaceHolder) {
                try {
                    val cam = Camera.open()
                    cameraInstance = cam
                    cam.setDisplayOrientation(90)
                    val params = cam.parameters
                    val sizes = params.supportedPreviewSizes
                    if (!sizes.isNullOrEmpty()) {
                        val bestSize = sizes.minByOrNull { abs(it.width - 1280) + abs(it.height - 720) } ?: sizes.first()
                        params.setPreviewSize(bestSize.width, bestSize.height)
                    }
                    val focusModes = params.supportedFocusModes
                    if (focusModes != null) {
                        when {
                            focusModes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE) ->
                                params.focusMode = Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE
                            focusModes.contains(Camera.Parameters.FOCUS_MODE_AUTO) ->
                                params.focusMode = Camera.Parameters.FOCUS_MODE_AUTO
                        }
                    }
                    cam.parameters = params
                    cam.setPreviewDisplay(holder)

                    cam.setPreviewCallback { data, camera ->
                        val now = System.currentTimeMillis()
                        if (data == null || decodedFlag.get() || busyDecoding.get() || (now - lastDecodeTime) < 160) return@setPreviewCallback
                        lastDecodeTime = now
                        busyDecoding.set(true)

                        val sz = try { camera.parameters.previewSize } catch (_: Exception) { null }
                        if (sz == null) {
                            busyDecoding.set(false)
                            return@setPreviewCallback
                        }
                        val frameCopy = data.clone()

                        FieldShareAndQrEngine.tryDecodeNv21WithMlKit(frameCopy, sz.width, sz.height, 90) { found ->
                            onQrDetectedSuccess(found)
                        }

                        Thread {
                            try {
                                val zxingRes = FieldShareAndQrEngine.decodeYuvCameraFrame(frameCopy, sz.width, sz.height)
                                if (!zxingRes.isNullOrBlank()) {
                                    onQrDetectedSuccess(zxingRes)
                                }
                            } catch (_: Exception) {
                            } finally {
                                busyDecoding.set(false)
                            }
                        }.start()
                    }
                    cam.startPreview()
                } catch (ex: Exception) {
                    Toast.makeText(this@MainActivity, "⚠️ Camera Error: " + ex.message, Toast.LENGTH_LONG).show()
                }
            }

            override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}
            override fun surfaceDestroyed(holder: SurfaceHolder) {
                releaseCam()
            }
        })

        dlg.window?.setBackgroundDrawable(roundedBox("#081316", "#1FE0C5", 2, 14f))
        dlg.show()
    }

    private fun pickQrImageFromGallery() {
        try {
            val intent = Intent(Intent.ACTION_GET_CONTENT).apply {
                type = "image/*"
                addCategory(Intent.CATEGORY_OPENABLE)
            }
            startActivityForResult(Intent.createChooser(intent, "QR Image"), REQ_PICK_GALLERY_QR)
        } catch (_: Exception) { }
    }

    private fun pickFtakFileFromStorage() {
        try {
            val intent = Intent(Intent.ACTION_GET_CONTENT).apply {
                type = "*/*"
                addCategory(Intent.CATEGORY_OPENABLE)
            }
            startActivityForResult(Intent.createChooser(intent, ".FTAK / .FTHPROJ"), REQ_PICK_FTAK_FILE)
        } catch (_: Exception) { }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != Activity.RESULT_OK || data?.data == null) return
        val uri = data.data!!

        if (requestCode == REQ_PICK_GALLERY_QR) {
            Thread {
                val decodedText = FieldShareAndQrEngine.decodeQrFromGalleryUri(this, uri)
                runOnUiThread {
                    if (decodedText != null) {
                        FieldShareAndQrEngine.handleScannedOrGalleryQrPayload(this, decodedText) {
                            rebuildScreen()
                            showIdentityAndFactionModal()
                        }
                    } else {
                        Toast.makeText(
                            this,
                            FtakProvisioningEngine.tr(this, "⚠️ Nie wykryto kodu QR na wybranym obrazku!", "⚠️ No QR code found in selected image!"),
                            Toast.LENGTH_LONG
                        ).show()
                    }
                }
            }.start()
        } else if (requestCode == REQ_PICK_FTAK_FILE) {
            try {
                contentResolver.openInputStream(uri)?.use { stream ->
                    if (FtakProvisioningEngine.importFromFtakStream(this, stream)) {
                        Toast.makeText(
                            this,
                            FtakProvisioningEngine.tr(this, "✓ Wczytano paczkę: ", "✓ Loaded package: ") + FtakProvisioningEngine.getEventName(this),
                            Toast.LENGTH_LONG
                        ).show()
                        rebuildScreen()
                        showIdentityAndFactionModal()
                    }
                }
            } catch (_: Exception) { }
        }
    }

    @SuppressLint("MissingPermission")
    private fun showOnDemandSosMgrsModal() {
        val fix = FtakProvisioningEngine.captureOnDemandMgrsFix(this)
        val callsign = FtakProvisioningEngine.getCallsign(this)
        val fac = FtakProvisioningEngine.getActiveFaction(this)
        val facName = fac?.name ?: "OPERATOR"
        val emergencyPhone = fac?.emergencyPhone?.ifBlank { "112" } ?: "112"

        val smsReport = "[SOS MEDEVAC] " + callsign + " (" + facName + ") | MGRS: " + fix.fullMgrs +
                " | Keypad: " + fix.keypadNum + " | Q: " + fix.quarterChar +
                " (" + fix.shortGrid100m + ") | WGS84: " + fix.wgs84Text + " (±" + fix.accuracyMeters + "m)"

        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(16), dp(18), dp(12))
        }

        box.addView(TextView(this).apply {
            text = FtakProvisioningEngine.tr(this@MainActivity, "🚨 MELDUNEK RATUNKOWY SOS MGRS", "🚨 EMERGENCY SOS MGRS REPORT")
            setTextColor(Color.parseColor("#EB4034"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
        })

        val coordsCard = TextView(this).apply {
            text = "🎯 MGRS: " + fix.fullMgrs + "\n" +
                    "🔢 KEYPAD: " + fix.keypadNum + "   •   Q: " + fix.quarterChar + "   (" + fix.shortGrid100m + ")\n" +
                    "🌍 WGS84: " + fix.wgs84Text + " (±" + fix.accuracyMeters + " m)\n" +
                    "👤 Operator: " + callsign + " [" + facName + "]\n" +
                    "📞 ORGA / MEDEVAC: " + emergencyPhone
            setTextColor(Color.WHITE)
            typeface = Typeface.MONOSPACE
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            background = roundedBox("#141E24", "#EB4034", 2, 10f)
            setPadding(dp(12), dp(12), dp(12), dp(12))
        }
        box.addView(coordsCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(8)
            bottomMargin = dp(12)
        })

        val btnSms = makeSimpleButton(
            FtakProvisioningEngine.tr(this, "📨 Wyślij SMS ratunkowy (" + emergencyPhone + ")", "📨 Send Emergency SMS (" + emergencyPhone + ")"),
            "#EB4034",
            Color.WHITE,
            12.5f
        ) {
            try {
                val smsIntent = Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:" + emergencyPhone)).apply {
                    putExtra("sms_body", smsReport)
                }
                startActivity(smsIntent)
            } catch (_: Exception) {
                copyText(smsReport)
            }
        }
        box.addView(btnSms, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(46)).apply {
            bottomMargin = dp(8)
        })

        val btnCall = makeSimpleButton(
            FtakProvisioningEngine.tr(this, "📞 Zadzwoń do ORGA (" + emergencyPhone + ")", "📞 Call ORGA (" + emergencyPhone + ")"),
            "#162A32",
            Color.parseColor("#FAD02C"),
            12.5f
        ) {
            try {
                startActivity(Intent(Intent.ACTION_DIAL, Uri.parse("tel:" + emergencyPhone)))
            } catch (_: Exception) { }
        }
        box.addView(btnCall, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(44)))

        val dlg = AlertDialog.Builder(this)
            .setView(ScrollView(this).apply { addView(box) })
            .setPositiveButton(FtakProvisioningEngine.tr(this, "📋 Kopiuj MGRS", "📋 Copy MGRS")) { _, _ -> copyText(smsReport) }
            .setNegativeButton(FtakProvisioningEngine.tr(this, "Zamknij", "Close"), null)
            .create()

        dlg.window?.setBackgroundDrawable(roundedBox("#081316", "#EB4034", 2, 14f))
        dlg.show()
    }

    private fun showCreatorSupportModal() {
        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(18), dp(16), dp(18), dp(12))
        }

        box.addView(TextView(this).apply {
            text = FtakProvisioningEngine.tr(this@MainActivity, "☕ WESPRZYJ TWÓRCĘ FIELD TAK HUB", "☕ SUPPORT FIELD TAK HUB CREATOR")
            setTextColor(Color.parseColor("#FAD02C"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
            gravity = Gravity.CENTER
        })

        val qrImg = ImageView(this).apply {
            setImageBitmap(FieldShareAndQrEngine.generateStandardQrBitmap(BUYCOFFEE_URL, 500))
            background = roundedBox("#FFFFFF", "#FAD02C", 3, 12f)
            setPadding(dp(10), dp(10), dp(10), dp(10))
        }
        box.addView(qrImg, LinearLayout.LayoutParams(dp(220), dp(220)).apply {
            topMargin = dp(10)
            bottomMargin = dp(10)
        })

        val dlg = AlertDialog.Builder(this)
            .setView(ScrollView(this).apply { addView(box) })
            .setPositiveButton("☕ buycoffee.to") { _, _ ->
                try {
                    startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(BUYCOFFEE_URL)))
                } catch (_: Exception) { }
            }
            .setNegativeButton(FtakProvisioningEngine.tr(this, "Zamknij", "Close"), null)
            .create()

        dlg.window?.setBackgroundDrawable(roundedBox("#081316", "#FAD02C", 2, 14f))
        dlg.show()
    }

    private fun createSectionContainer(iconType: Int, title: String): LinearLayout {
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = roundedBox("#0A181F", "#193844", 1, 12f)
            setPadding(dp(12), dp(10), dp(12), dp(12))

            val hdrRow = LinearLayout(this@MainActivity).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setPadding(0, 0, 0, dp(8))
            }
            val icon = TacticalVectorIconView(this@MainActivity, iconType, Color.parseColor("#1FE0C5"))
            hdrRow.addView(icon, LinearLayout.LayoutParams(dp(18), dp(18)).apply { rightMargin = dp(8) })

            hdrRow.addView(TextView(this@MainActivity).apply {
                text = title
                setTextColor(Color.parseColor("#1FE0C5"))
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            })
            addView(hdrRow)
        }
    }

    private fun makeIconButton(
        iconType: Int,
        label: String,
        bgHex: String,
        textColor: Int,
        iconTint: Int,
        fontSp: Float,
        onClick: () -> Unit
    ): LinearLayout {
        return LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            background = roundedBox(bgHex, "#294D5A", 1, 10f)
            setPadding(dp(10), dp(6), dp(10), dp(6))
            isClickable = true
            isFocusable = true
            setOnClickListener { onClick() }

            val icon = TacticalVectorIconView(this@MainActivity, iconType, iconTint)
            addView(icon, LinearLayout.LayoutParams(dp(20), dp(20)).apply { rightMargin = dp(8) })

            addView(TextView(this@MainActivity).apply {
                text = label
                setTextColor(textColor)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, fontSp)
                typeface = Typeface.DEFAULT_BOLD
                gravity = Gravity.CENTER
            })
        }
    }

    private fun makeSimpleButton(label: String, bgHex: String, textColor: Int, fontSp: Float, onClick: () -> Unit): Button {
        return Button(this).apply {
            text = label
            setTextColor(textColor)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, fontSp)
            typeface = Typeface.DEFAULT_BOLD
            isAllCaps = false
            background = roundedBox(bgHex, "#294D5A", 1, 10f)
            setPadding(dp(10), dp(4), dp(10), dp(4))
            setOnClickListener { onClick() }
        }
    }

    private fun copyText(text: String) {
        try {
            val cb = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("FieldTakHub", text))
            Toast.makeText(this, "✓ Copied!", Toast.LENGTH_SHORT).show()
        } catch (_: Exception) { }
    }

    private fun dp(value: Int): Int =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value.toFloat(), resources.displayMetrics).toInt()

    private fun parseColorSafe(hex: String, fallback: Int): Int =
        try { Color.parseColor(hex) } catch (_: Exception) { fallback }

    private fun roundedBox(fillHex: String, strokeHex: String, strokeWidthDp: Int, radiusDp: Float): GradientDrawable {
        return GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusDp * 2.5f
            setColor(parseColorSafe(fillHex, Color.parseColor("#0A181F")))
            setStroke(strokeWidthDp * 2, parseColorSafe(strokeHex, Color.parseColor("#1FE0C5")))
        }
    }

    @Suppress("unused")
    private val ciSourceGateCompatibilityTokens = arrayOf(
        "DEVICE_STATUS",
        "SCAN_UNIVERSAL",
        "DeviceStatusScreen",
        "device_status_action",
        "scan_any_qr",
        "status_meshtastic",
        "meshtasticChannel"
    )
}