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
import android.graphics.*
import android.graphics.drawable.GradientDrawable
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.text.Editable
import android.text.InputFilter
import android.text.InputType
import android.text.TextWatcher
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.*
import java.util.*

class FthTacticalDashboardController(private val activity: Activity) {

    private var activeTabIndex: Int = 0
    private lateinit var rootLayout: LinearLayout
    private lateinit var headerCallsignText: TextView
    private lateinit var headerFactionBadge: TextView
    private lateinit var headerLangBtn: Button
    private lateinit var contentScroll: ScrollViewerWrapper
    private lateinit var contentContainer: LinearLayout
    private val navButtons = mutableListOf<LinearLayout>()
    private val deploymentLogs = mutableListOf<String>()

    private var lastLat: Double = 49.850568
    private var lastLon: Double = 18.813347
    private var lastAccuracyMeters: Float = 4.5f
    private var hasLiveGpsFix: Boolean = false
    private var locationManager: LocationManager? = null
    private var gpsLocationListener: LocationListener? = null

    fun createRootView(): View {
        FthSessionManager.init(activity)
        if (deploymentLogs.isEmpty()) {
            appendLog(
                FthSessionManager.t(
                    "🟢 System Field TAK Hub v3.4 gotowy. Zeskanuj kod QR strony lub wybierz profil poniżej.",
                    "🟢 Field TAK Hub v3.4 ready. Scan faction QR code or select a profile below."
                )
            )
        }

        rootLayout = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(FthTheme.BgDeep)
            layoutParams = ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
        }

        rootLayout.addView(buildTopHeaderBar())

        val scrollView = ScrollView(activity).apply {
            isFillViewport = true
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                0,
                1f
            )
        }
        contentContainer = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            val pad = FthTheme.dp(activity, 14f)
            setPadding(pad, pad, pad, pad)
        }
        scrollView.addView(contentContainer)
        contentScroll = ScrollViewerWrapper(scrollView)
        rootLayout.addView(scrollView)

        rootLayout.addView(buildBottomNavigationBar())

        refreshHeaderStatus()
        rebuildActiveTabContent()
        startGpsTrackingIfPermitted()

        return rootLayout
    }

    class ScrollViewerWrapper(val scrollView: ScrollView)

    private fun buildTopHeaderBar(): View {
        val bar = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setBackgroundColor(FthTheme.BgSidebar)
            val padH = FthTheme.dp(activity, 12f)
            val padV = FthTheme.dp(activity, 10f)
            setPadding(padH, padV, padH, padV)
        }

        val radarIcon = TacticalVectorIconView(activity, TacticalIconType.RADAR_HUB, FthTheme.AccentCyan, 26f)
        bar.addView(radarIcon)

        val titleCol = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            val lp = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            lp.marginStart = FthTheme.dp(activity, 10f)
            layoutParams = lp
        }

        val appTitle = TextView(activity).apply {
            text = "FIELD TAK HUB"
            setTextColor(FthTheme.TextPrimary)
            textSize = 14f
            setTypeface(typeface, Typeface.BOLD)
        }
        headerCallsignText = TextView(activity).apply {
            text = "Wilk-1 (Mesh: Wlk1)"
            setTextColor(FthTheme.AccentCyan)
            textSize = 11f
            setTypeface(Typeface.MONOSPACE, Typeface.BOLD)
        }
        titleCol.addView(appTitle)
        titleCol.addView(headerCallsignText)
        bar.addView(titleCol)

        headerFactionBadge = TextView(activity).apply {
            text = "BLUFOR"
            setTextColor(Color.WHITE)
            textSize = 11f
            setTypeface(typeface, Typeface.BOLD)
            val pH = FthTheme.dp(activity, 10f)
            val pV = FthTheme.dp(activity, 5f)
            setPadding(pH, pV, pH, pV)
            background = FthTheme.buttonDrawable(FthTheme.AccentBlue, FthTheme.AccentBlue, 6f, 2, activity)
            setOnClickListener {
                showDynamicCallsignInjectionModal(FthSessionManager.activeProfile, isNewScan = false)
            }
        }
        bar.addView(headerFactionBadge)

        headerLangBtn = Button(activity).apply {
            text = FthSessionManager.currentLang
            setTextColor(FthTheme.AccentCyan)
            textSize = 11f
            setTypeface(typeface, Typeface.BOLD)
            minWidth = 0
            minimumWidth = 0
            val pH = FthTheme.dp(activity, 10f)
            val pV = FthTheme.dp(activity, 4f)
            setPadding(pH, pV, pH, pV)
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, FthTheme.dp(activity, 34f))
            lp.marginStart = FthTheme.dp(activity, 8f)
            layoutParams = lp
            background = FthTheme.buttonDrawable(FthTheme.BgInput, FthTheme.BorderInput, 6f, 2, activity)
            setOnClickListener {
                val newLang = FthSessionManager.toggleLanguage(activity)
                text = newLang
                refreshHeaderStatus()
                rebuildBottomNavLabels()
                rebuildActiveTabContent()
            }
        }
        bar.addView(headerLangBtn)

        return bar
    }

    private fun buildBottomNavigationBar(): View {
        val navBar = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
            setBackgroundColor(FthTheme.BgSidebar)
            val pad = FthTheme.dp(activity, 6f)
            setPadding(pad, pad, pad, pad)
        }

        navButtons.clear()
        val items = listOf(
            Triple(0, TacticalIconType.QR_SCANNER, FthSessionManager.t("Skaner & Callsign", "QR & Callsign")),
            Triple(1, TacticalIconType.RADIO_COMMS, FthSessionManager.t("Ściąga & SARNEG", "Comms & SARNEG")),
            Triple(2, TacticalIconType.OPORD_CLIPBOARD, FthSessionManager.t("Rozkaz OPORD", "OPORD Brief")),
            Triple(3, TacticalIconType.WIFI_HOTSPOT, FthSessionManager.t("Hotspot Wi-Fi", "Wi-Fi P2P")),
            Triple(4, TacticalIconType.SOS_BEACON, FthSessionManager.t("SOS MGRS", "SOS MGRS"))
        )

        for ((idx, iconType, label) in items) {
            val tabCell = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
                val lp = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                lp.setMargins(4, 0, 4, 0)
                layoutParams = lp
                val pV = FthTheme.dp(activity, 7f)
                setPadding(4, pV, 4, pV)
                setOnClickListener {
                    switchTab(idx)
                }
            }

            val iconColor = if (idx == 4) FthTheme.AccentRed else FthTheme.AccentCyan
            val iconView = TacticalVectorIconView(activity, iconType, iconColor, 20f)
            val lblView = TextView(activity).apply {
                text = label
                textSize = 9.5f
                gravity = Gravity.CENTER
                maxLines = 1
                setTypeface(typeface, Typeface.BOLD)
            }

            tabCell.addView(iconView)
            tabCell.addView(lblView)
            navButtons.add(tabCell)
            navBar.addView(tabCell)
        }

        updateBottomNavSelection()
        return navBar
    }

    private fun rebuildBottomNavLabels() {
        val labels = listOf(
            FthSessionManager.t("Skaner & Callsign", "QR & Callsign"),
            FthSessionManager.t("Ściąga & SARNEG", "Comms & SARNEG"),
            FthSessionManager.t("Rozkaz OPORD", "OPORD Brief"),
            FthSessionManager.t("Hotspot Wi-Fi", "Wi-Fi P2P"),
            FthSessionManager.t("SOS MGRS", "SOS MGRS")
        )
        for (i in navButtons.indices) {
            val cell = navButtons[i]
            if (cell.childCount >= 2 && cell.getChildAt(1) is TextView) {
                (cell.getChildAt(1) as TextView).text = labels[i]
            }
        }
    }

    private fun updateBottomNavSelection() {
        for (i in navButtons.indices) {
            val cell = navButtons[i]
            val isSelected = (i == activeTabIndex)
            val strokeCol = if (i == 4 && isSelected) FthTheme.AccentRed else if (isSelected) FthTheme.AccentCyan else FthTheme.BorderSubtle
            val fillCol = if (isSelected) FthTheme.BgElevated else FthTheme.BgCard
            cell.background = FthTheme.cardDrawable(fillCol, strokeCol, 8f, if (isSelected) 4 else 2, activity)

            if (cell.childCount >= 2 && cell.getChildAt(1) is TextView) {
                val tv = cell.getChildAt(1) as TextView
                tv.setTextColor(
                    when {
                        i == 4 -> FthTheme.AccentRed
                        isSelected -> FthTheme.AccentCyan
                        else -> FthTheme.TextSecondary
                    }
                )
            }
        }
    }

    fun switchTab(index: Int) {
        activeTabIndex = index.coerceIn(0, 4)
        updateBottomNavSelection()
        rebuildActiveTabContent()
    }

    fun refreshHeaderStatus() {
        val p = FthSessionManager.activeProfile
        headerCallsignText.text = "${p.operatorCallsign}  •  Mesh: ${p.meshtasticShortName}"
        headerFactionBadge.text = p.factionName.uppercase(Locale.ROOT)
        val facColor = FthTheme.parseColorSafe(p.colorHex, FthTheme.AccentBlue)
        headerFactionBadge.background = FthTheme.buttonDrawable(facColor, Color.WHITE, 6f, 2, activity)
    }

    fun appendLog(msg: String) {
        val timeStr = java.text.SimpleDateFormat("HH:mm:ss", Locale.US).format(Date())
        deploymentLogs.add(0, "[$timeStr] $msg")
        if (deploymentLogs.size > 40) {
            deploymentLogs.removeAt(deploymentLogs.lastIndex)
        }
    }

    fun showDynamicCallsignInjectionModal(profile: FthFactionProfile, isNewScan: Boolean = true) {
        val dialogLayout = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(FthTheme.BgCard)
            val pad = FthTheme.dp(activity, 18f)
            setPadding(pad, pad, pad, pad)
        }

        val facColor = FthTheme.parseColorSafe(profile.colorHex, FthTheme.AccentBlue)
        val teamColorName = CallsignFormatter.mapHexToAtakTeamColorName(profile.colorHex)

        val banner = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            background = FthTheme.cardDrawable(FthTheme.BgElevated, facColor, 8f, 4, activity)
            val p = FthTheme.dp(activity, 12f)
            setPadding(p, p, p, p)
        }

        val bannerTitle = TextView(activity).apply {
            text = if (isNewScan) {
                FthSessionManager.t(
                    "🎯 ZESKANOWANO STRONĘ: ${profile.factionName.uppercase(Locale.ROOT)}",
                    "🎯 SCANNED FACTION: ${profile.factionName.uppercase(Locale.ROOT)}"
                )
            } else {
                FthSessionManager.t(
                    "🛡️ IDENTYFIKACJA OPERATORA — ${profile.factionName.uppercase(Locale.ROOT)}",
                    "🛡️ OPERATOR IDENTITY — ${profile.factionName.uppercase(Locale.ROOT)}"
                )
            }
            setTextColor(FthTheme.AccentCyan)
            textSize = 14f
            setTypeface(typeface, Typeface.BOLD)
        }

        val bannerSub = TextView(activity).apply {
            text = FthSessionManager.t(
                "Operacja: ${profile.eventName}  |  Kanał LoRa: ${profile.channelName} (${profile.loraRegion})\nSerwer OTS: ${profile.otsHost.ifBlank { "Off-Grid Mesh" }}  |  Zespół ATAK: $teamColorName",
                "Operation: ${profile.eventName}  |  LoRa Channel: ${profile.channelName} (${profile.loraRegion})\nOTS Server: ${profile.otsHost.ifBlank { "Off-Grid Mesh" }}  |  ATAK Team: $teamColorName"
            )
            setTextColor(FthTheme.TextSecondary)
            textSize = 11f
            setPadding(0, FthTheme.dp(activity, 4f), 0, 0)
        }
        banner.addView(bannerTitle)
        banner.addView(bannerSub)
        dialogLayout.addView(banner)

        dialogLayout.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "1. Wpisz swój Callsign (wywołanie taktyczne, np. Wilk-1):",
                "1. Enter your Tactical Callsign (e.g., Wilk-1):"
            )
            setTextColor(FthTheme.AccentGold)
            textSize = 12f
            setTypeface(typeface, Typeface.BOLD)
            setPadding(0, FthTheme.dp(activity, 14f), 0, FthTheme.dp(activity, 6f))
        })

        val callsignInput = EditText(activity).apply {
            setText(profile.operatorCallsign.ifBlank { "Wilk-1" })
            hint = FthSessionManager.t("np. Wilk-1, Alfa-2, Szturm-3", "e.g., Wilk-1, Alpha-2")
            setTextColor(Color.WHITE)
            setHintTextColor(FthTheme.TextMuted)
            textSize = 16f
            setTypeface(Typeface.MONOSPACE, Typeface.BOLD)
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_WORDS
            val p = FthTheme.dp(activity, 12f)
            setPadding(p, p, p, p)
            background = FthTheme.cardDrawable(FthTheme.BgInput, FthTheme.AccentCyan, 8f, 3, activity)
        }
        dialogLayout.addView(callsignInput)

        dialogLayout.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "2. Skrócona nazwa dla Meshtastic (automatyczna konwersja np. Wilk-1 -> Wlk1):",
                "2. Meshtastic Short Name (auto-converted e.g. Wilk-1 -> Wlk1):"
            )
            setTextColor(FthTheme.TextSecondary)
            textSize = 11.5f
            setTypeface(typeface, Typeface.BOLD)
            setPadding(0, FthTheme.dp(activity, 10f), 0, FthTheme.dp(activity, 4f))
        })

        val initialShort = CallsignFormatter.generateMeshtasticShortName(callsignInput.text.toString(), 4)
        val shortNameInput = EditText(activity).apply {
            setText(initialShort)
            setTextColor(FthTheme.AccentCyan)
            textSize = 14f
            setTypeface(Typeface.MONOSPACE, Typeface.BOLD)
            filters = arrayOf(InputFilter.LengthFilter(6))
            val p = FthTheme.dp(activity, 10f)
            setPadding(p, p, p, p)
            background = FthTheme.cardDrawable(FthTheme.BgInput, FthTheme.BorderInput, 8f, 2, activity)
        }
        dialogLayout.addView(shortNameInput)

        val livePreviewText = TextView(activity).apply {
            setTextColor(FthTheme.TextPrimary)
            textSize = 11f
            typeface = Typeface.MONOSPACE
            val p = FthTheme.dp(activity, 10f)
            setPadding(p, p, p, p)
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            lp.topMargin = FthTheme.dp(activity, 10f)
            layoutParams = lp
            background = FthTheme.cardDrawable(FthTheme.BgDeep, FthTheme.BorderSubtle, 8f, 2, activity)
        }

        fun updateLivePreview(autoUpdateShort: Boolean) {
            val rawCall = callsignInput.text?.toString() ?: "Wilk-1"
            val atakCall = CallsignFormatter.formatAtakCallsign(rawCall)
            if (autoUpdateShort) {
                val computedShort = CallsignFormatter.generateMeshtasticShortName(rawCall, 4)
                shortNameInput.setText(computedShort)
            }
            val meshShort = (shortNameInput.text?.toString() ?: "Wlk1").trim().ifEmpty { "Wlk1" }
            val meshLong = CallsignFormatter.formatMeshtasticLongName(atakCall, profile.factionName)

            livePreviewText.text = FthSessionManager.t(
                "⚡ PODGLĄD WSTRZYKIWANIA NA ŻYWO:\n" +
                        "• ATAK-CIV Callsign : $atakCall  (Zespół: $teamColorName)\n" +
                        "• Meshtastic Long   : $meshLong\n" +
                        "• Meshtastic Short  : $meshShort  (LoRa Node ID)",
                "⚡ LIVE INJECTION PREVIEW:\n" +
                        "• ATAK-CIV Callsign : $atakCall  (Team: $teamColorName)\n" +
                        "• Meshtastic Long   : $meshLong\n" +
                        "• Meshtastic Short  : $meshShort  (LoRa Node ID)"
            )
        }

        var internalShortEdit = false
        callsignInput.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                internalShortEdit = true
                updateLivePreview(autoUpdateShort = true)
                internalShortEdit = false
            }
        })

        shortNameInput.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                if (!internalShortEdit) {
                    updateLivePreview(autoUpdateShort = false)
                }
            }
        })

        updateLivePreview(autoUpdateShort = true)
        dialogLayout.addView(livePreviewText)

        val alert = AlertDialog.Builder(activity)
            .setView(dialogLayout)
            .setCancelable(true)
            .create()

        val btnDeployAll = Button(activity).apply {
            text = FthSessionManager.t(
                "⚡ ZATWIERDŹ I WDRÓŻ DO ATAK & MESHTASTIC (<15s)",
                "⚡ CONFIRM & INJECT INTO ATAK & MESHTASTIC (<15s)"
            )
            setTextColor(FthTheme.DarkFg)
            textSize = 12f
            setTypeface(typeface, Typeface.BOLD)
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, FthTheme.dp(activity, 46f))
            lp.topMargin = FthTheme.dp(activity, 14f)
            layoutParams = lp
            background = FthTheme.buttonDrawable(FthTheme.AccentCyan, FthTheme.AccentCyan, 8f, 2, activity)
            setOnClickListener {
                val finalCall = CallsignFormatter.formatAtakCallsign(callsignInput.text?.toString() ?: "Wilk-1")
                val finalShort = (shortNameInput.text?.toString() ?: "").trim().ifEmpty {
                    CallsignFormatter.generateMeshtasticShortName(finalCall, 4)
                }

                profile.operatorCallsign = finalCall
                profile.meshtasticShortName = finalShort
                FthSessionManager.saveActiveProfile(activity, profile)

                alert.dismiss()
                refreshHeaderStatus()
                executeFullZeroTouchProvisioning(profile)
            }
        }
        dialogLayout.addView(btnDeployAll)

        val btnSaveOnly = Button(activity).apply {
            text = FthSessionManager.t(
                "💾 Zapisz tylko w Ściądze Operacyjnej (bez otwierania ATAK)",
                "💾 Save to Field Cheat Sheet Only"
            )
            setTextColor(FthTheme.TextSecondary)
            textSize = 11f
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, FthTheme.dp(activity, 38f))
            lp.topMargin = FthTheme.dp(activity, 8f)
            layoutParams = lp
            background = FthTheme.buttonDrawable(FthTheme.BgInput, FthTheme.BorderInput, 8f, 2, activity)
            setOnClickListener {
                val finalCall = CallsignFormatter.formatAtakCallsign(callsignInput.text?.toString() ?: "Wilk-1")
                val finalShort = (shortNameInput.text?.toString() ?: "").trim().ifEmpty {
                    CallsignFormatter.generateMeshtasticShortName(finalCall, 4)
                }
                profile.operatorCallsign = finalCall
                profile.meshtasticShortName = finalShort
                FthSessionManager.saveActiveProfile(activity, profile)
                appendLog(
                    FthSessionManager.t(
                        "✅ Zapisano profil ${profile.factionName}: Callsign='$finalCall', Meshtastic Short='$finalShort'.",
                        "✅ Saved ${profile.factionName} profile: Callsign='$finalCall', Meshtastic Short='$finalShort'."
                    )
                )
                alert.dismiss()
                refreshHeaderStatus()
                rebuildActiveTabContent()
            }
        }
        dialogLayout.addView(btnSaveOnly)

        alert.show()
    }

    private fun executeFullZeroTouchProvisioning(profile: FthFactionProfile) {
        appendLog(
            FthSessionManager.t(
                "🚀 Start Zero-Touch Provisioning dla '${profile.operatorCallsign}' (Mesh: '${profile.meshtasticShortName}') w frakcji ${profile.factionName}...",
                "🚀 Starting Zero-Touch Provisioning for '${profile.operatorCallsign}' (Mesh: '${profile.meshtasticShortName}') in faction ${profile.factionName}..."
            )
        )
        rebuildActiveTabContent()

        AtakProvisioningEngine.injectCallsignAndConfigIntoAtak(activity, profile) { logMsg ->
            appendLog(logMsg)
            rebuildActiveTabContent()
        }

        MeshtasticBleProvisioner.provisionMeshtasticNode(
            activity,
            profile,
            onLog = { logMsg ->
                appendLog(logMsg)
                rebuildActiveTabContent()
            },
            onFinished = {
                rebuildActiveTabContent()
            }
        )
    }

    fun rebuildActiveTabContent() {
        contentContainer.removeAllViews()
        when (activeTabIndex) {
            0 -> buildTab0ScannerAndCallsign()
            1 -> buildTab1RadioAndSarneg()
            2 -> buildTab2OpordAndMetoc()
            3 -> buildTab3HotspotP2p()
            4 -> buildTab4SosMgrs()
        }
    }

    private fun buildTab0ScannerAndCallsign() {
        val p = FthSessionManager.activeProfile
        val facColor = FthTheme.parseColorSafe(p.colorHex, FthTheme.AccentBlue)
        val teamColorName = CallsignFormatter.mapHexToAtakTeamColorName(p.colorHex)

        val idCard = createSectionCard(
            FthSessionManager.t(
                "🛡️ AKTYWNA TOŻSAMOŚĆ OPERATORA (ATAK & MESHTASTIC)",
                "🛡️ ACTIVE OPERATOR IDENTITY (ATAK & MESHTASTIC)"
            ),
            facColor
        )

        val idSummary = TextView(activity).apply {
            text = FthSessionManager.t(
                "• Operacja / Wydarzenie : ${p.eventName}\n" +
                        "• Strona konfliktu      : ${p.factionName} (Zespół ATAK: $teamColorName)\n" +
                        "• Twój Callsign (ATAK)  : ${p.operatorCallsign}\n" +
                        "• Węzeł Meshtastic      : Long='${p.operatorCallsign}'  |  Short='${p.meshtasticShortName}'\n" +
                        "• Kanał LoRa Mesh       : ${p.channelName} (${p.loraRegion} / ${p.loraModemPreset})\n" +
                        "• Serwer OpenTAK (OTS)  : ${p.otsHost.ifBlank { "Tryb Off-Grid" }}",
                "• Operation / Event     : ${p.eventName}\n" +
                        "• Faction / Side        : ${p.factionName} (ATAK Team: $teamColorName)\n" +
                        "• Your Callsign (ATAK)  : ${p.operatorCallsign}\n" +
                        "• Meshtastic Node       : Long='${p.operatorCallsign}'  |  Short='${p.meshtasticShortName}'\n" +
                        "• LoRa Mesh Channel     : ${p.channelName} (${p.loraRegion} / ${p.loraModemPreset})\n" +
                        "• OpenTAKServer (OTS)   : ${p.otsHost.ifBlank { "Off-Grid Mode" }}"
            )
            setTextColor(FthTheme.TextPrimary)
            textSize = 11.5f
            typeface = Typeface.MONOSPACE
            setPadding(0, FthTheme.dp(activity, 4f), 0, FthTheme.dp(activity, 10f))
        }
        idCard.addView(idSummary)

        val btnEditCallsign = createActionButton(
            FthSessionManager.t("✏️ Zmień Callsign (np. Wilk-1 -> Wlk1) i Wdróż", "✏️ Change Callsign (e.g. Wilk-1 -> Wlk1) & Deploy"),
            FthTheme.AccentCyan,
            FthTheme.DarkFg
        ) {
            showDynamicCallsignInjectionModal(FthSessionManager.activeProfile, isNewScan = false)
        }
        idCard.addView(btnEditCallsign)

        val quickInjectRow = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            lp.topMargin = FthTheme.dp(activity, 8f)
            layoutParams = lp
        }

        val btnReinjectAtak = createActionButton(
            FthSessionManager.t("🛡️ Wstrzyknij do ATAK", "🛡️ Inject to ATAK"),
            FthTheme.BgElevated,
            FthTheme.AccentCyan,
            FthTheme.AccentCyan
        ) {
            AtakProvisioningEngine.injectCallsignAndConfigIntoAtak(activity, p) { msg ->
                appendLog(msg)
                rebuildActiveTabContent()
            }
        }.apply {
            layoutParams = LinearLayout.LayoutParams(0, FthTheme.dp(activity, 42f), 1f).apply { marginEnd = 6 }
        }

        val btnReinjectMesh = createActionButton(
            FthSessionManager.t("📻 Wgraj BLE Meshtastic", "📻 BLE Flash Meshtastic"),
            FthTheme.BgElevated,
            FthTheme.AccentGold,
            FthTheme.AccentGold
        ) {
            MeshtasticBleProvisioner.provisionMeshtasticNode(
                activity,
                p,
                onLog = { msg -> appendLog(msg); rebuildActiveTabContent() },
                onFinished = { rebuildActiveTabContent() }
            )
        }.apply {
            layoutParams = LinearLayout.LayoutParams(0, FthTheme.dp(activity, 42f), 1f).apply { marginStart = 6 }
        }

        quickInjectRow.addView(btnReinjectAtak)
        quickInjectRow.addView(btnReinjectMesh)
        idCard.addView(quickInjectRow)
        contentContainer.addView(idCard)

        val scanCard = createSectionCard(
            FthSessionManager.t(
                "📷 SKANUJ KOD QR STRONY LUB WCZYTAJ PACZKĘ (.FTAK / .FTHPROJ)",
                "📷 SCAN FACTION QR CODE OR LOAD PACKAGE (.FTAK / .FTHPROJ)"
            ),
            FthTheme.AccentCyan
        )

        scanCard.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "Zeskanuj kod QR swojej strony z plakatu Safezone, ekranu Windows Buildera lub telefonu dowódcy. Po skanie aplikacja poprosi o Twój Callsign (np. Wilk-1) i skonfiguruje urządzenie w <15 sekund:",
                "Scan your faction QR code from the Safezone poster, Windows Builder screen, or squad leader's phone. After scanning, you will be prompted for your Callsign (e.g., Wilk-1) and provisioned in <15s:"
            )
            setTextColor(FthTheme.TextSecondary)
            textSize = 11f
            setPadding(0, 0, 0, FthTheme.dp(activity, 10f))
        })

        val btnLaunchQrScanner = createActionButton(
            FthSessionManager.t("📷 SKANUJ KOD QR STRONY (APARAT / WKLEJ QR)", "📷 SCAN FACTION QR CODE (CAMERA / PASTE)"),
            FthTheme.AccentCyan,
            FthTheme.DarkFg
        ) {
            launchCameraQrScannerOrPasteDialog()
        }
        scanCard.addView(btnLaunchQrScanner)

        val btnPickFile = createActionButton(
            FthSessionManager.t("📂 Wczytaj plik .FTAK / .FTHPROJ z pamięci telefonu", "📂 Load .FTAK / .FTHPROJ File from Storage"),
            FthTheme.BgInput,
            Color.WHITE,
            FthTheme.BorderInput
        ) {
            launchSystemFilePicker()
        }.apply {
            (layoutParams as LinearLayout.LayoutParams).topMargin = FthTheme.dp(activity, 8f)
        }
        scanCard.addView(btnPickFile)

        scanCard.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "⚡ Szybka zmiana strony konfliktu (Test / Trening Atak-Obrona):",
                "⚡ Quick Faction Switch (Training Attack-Defense):"
            )
            setTextColor(FthTheme.AccentGold)
            textSize = 11f
            setTypeface(typeface, Typeface.BOLD)
            setPadding(0, FthTheme.dp(activity, 12f), 0, FthTheme.dp(activity, 6f))
        })

        val presetsRow = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        }

        val btnPresetBlufor = createActionButton(
            "🔵 BLUFOR (GGZS-TAK)",
            FthTheme.AccentBlue,
            Color.WHITE
        ) {
            val bluProfile = buildBuiltInFactionPreset(isOpfor = false)
            appendLog(FthSessionManager.t("🔵 Wybrano profil BLUFOR (GGZS-TAK). Podaj Callsign...", "🔵 Selected BLUFOR (GGZS-TAK). Enter Callsign..."))
            showDynamicCallsignInjectionModal(bluProfile, isNewScan = true)
        }.apply {
            layoutParams = LinearLayout.LayoutParams(0, FthTheme.dp(activity, 40f), 1f).apply { marginEnd = 6 }
        }

        val btnPresetOpfor = createActionButton(
            "🔴 OPFOR (OPFOR-TAK)",
            FthTheme.AccentRed,
            Color.WHITE
        ) {
            val opProfile = buildBuiltInFactionPreset(isOpfor = true)
            appendLog(FthSessionManager.t("🔴 Wybrano profil OPFOR (OPFOR-TAK). Podaj Callsign...", "🔴 Selected OPFOR (OPFOR-TAK). Enter Callsign..."))
            showDynamicCallsignInjectionModal(opProfile, isNewScan = true)
        }.apply {
            layoutParams = LinearLayout.LayoutParams(0, FthTheme.dp(activity, 40f), 1f).apply { marginStart = 6 }
        }

        presetsRow.addView(btnPresetBlufor)
        presetsRow.addView(btnPresetOpfor)
        scanCard.addView(presetsRow)
        contentContainer.addView(scanCard)

        val logCard = createSectionCard(
            FthSessionManager.t("📟 DZIENNIK OPERACYJNY PROVISIONINGU", "📟 PROVISIONING TELEMETRY LOG"),
            FthTheme.BorderSubtle
        )
        val logTv = TextView(activity).apply {
            text = deploymentLogs.joinToString("\n")
            setTextColor(FthTheme.AccentCyan)
            textSize = 10.5f
            typeface = Typeface.MONOSPACE
            val p = FthTheme.dp(activity, 8f)
            setPadding(p, p, p, p)
            background = FthTheme.cardDrawable(FthTheme.BgDeep, FthTheme.BorderSubtle, 6f, 2, activity)
        }
        logCard.addView(logTv)
        contentContainer.addView(logCard)
    }

    private fun buildTab1RadioAndSarneg() {
        val p = FthSessionManager.activeProfile
        val activeDay = p.opordDays.getOrElse(p.activeDayIndex.coerceIn(0, (p.opordDays.size - 1).coerceAtLeast(0))) {
            FthOpordDayPlan()
        }

        val radioCard = createSectionCard(
            FthSessionManager.t(
                "📻 KANAŁY GŁOSOWE (${p.factionName}) — ŚCIĄGA OPERACYJNA",
                "📻 VOICE CHANNELS (${p.factionName}) — FIELD CHEAT SHEET"
            ),
            FthTheme.AccentCyan
        )

        for (vc in p.voiceChannels) {
            val rowCard = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                background = FthTheme.cardDrawable(FthTheme.BgInput, FthTheme.BorderInput, 6f, 2, activity)
                val pad = FthTheme.dp(activity, 10f)
                setPadding(pad, pad, pad, pad)
                val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                lp.bottomMargin = FthTheme.dp(activity, 6f)
                layoutParams = lp
            }

            rowCard.addView(TextView(activity).apply {
                text = "🎙️ ${vc.roleName}  [${vc.bandType}]"
                setTextColor(FthTheme.AccentGold)
                textSize = 12f
                setTypeface(typeface, Typeface.BOLD)
            })
            rowCard.addView(TextView(activity).apply {
                text = FthSessionManager.t(
                    "Częstotliwość / Kanał: ${vc.freqMhz}   |   Ton / Kod: ${vc.toneOrCode}",
                    "Frequency / Channel: ${vc.freqMhz}   |   Tone / Code: ${vc.toneOrCode}"
                )
                setTextColor(Color.WHITE)
                textSize = 11.5f
                typeface = Typeface.MONOSPACE
            })
            radioCard.addView(rowCard)
        }

        radioCard.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "📡 LoRa Meshtastic: ${p.channelName} (${p.loraRegion} / ${p.loraModemPreset} / Hop: ${p.hopLimit})\n" +
                        "🌐 Serwer ATAK OTS: ${p.otsHost.ifBlank { "Off-Grid" }}\n" +
                        "🚨 Telefon alarmowy ORGA: ${p.emergencyPhone}",
                "📡 LoRa Meshtastic: ${p.channelName} (${p.loraRegion} / ${p.loraModemPreset} / Hop: ${p.hopLimit})\n" +
                        "🌐 ATAK OTS Server: ${p.otsHost.ifBlank { "Off-Grid" }}\n" +
                        "🚨 ORGA Emergency Phone: ${p.emergencyPhone}"
            )
            setTextColor(FthTheme.AccentCyan)
            textSize = 11f
            typeface = Typeface.MONOSPACE
            setPadding(0, FthTheme.dp(activity, 6f), 0, 0)
        })
        contentContainer.addView(radioCard)

        val sarnegCard = createSectionCard(
            FthSessionManager.t(
                "🔐 TABELA KRYPTO SARNEG (${activeDay.dayTitle}) + SZYFRATOR POLOWY",
                "🔐 SARNEG CRYPTO TABLE (${activeDay.dayTitle}) + FIELD ENCODER"
            ),
            FthTheme.AccentGold
        )

        val cleanKey = CallsignFormatter.normalizeAscii(activeDay.sarnegKeyword10)
            .uppercase(Locale.ROOT).trim().padEnd(10, 'X').substring(0, 10)

        val lettersLine = StringBuilder("LITERA: ")
        val digitsLine = StringBuilder("CYFRA : ")
        for (i in 0 until 10) {
            lettersLine.append("| ").append(cleanKey[i]).append(' ')
            digitsLine.append("| ").append(i).append(' ')
        }
        lettersLine.append("|")
        digitsLine.append("|")

        val matrixTv = TextView(activity).apply {
            text = "$lettersLine\n$digitsLine"
            setTextColor(Color.WHITE)
            textSize = 12f
            setTypeface(Typeface.MONOSPACE, Typeface.BOLD)
            val p = FthTheme.dp(activity, 10f)
            setPadding(p, p, p, p)
            background = FthTheme.cardDrawable(FthTheme.BgDeep, FthTheme.AccentGold, 6f, 3, activity)
        }
        sarnegCard.addView(matrixTv)

        sarnegCard.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "🧮 Kalkulator SARNEG (wpisz cyfry współrzędnych MGRS, aby zaszyfrować, lub litery z radia, aby odszyfrować):",
                "🧮 SARNEG Calculator (type MGRS digits to encrypt, or radio letters to decrypt):"
            )
            setTextColor(FthTheme.TextSecondary)
            textSize = 11f
            setPadding(0, FthTheme.dp(activity, 10f), 0, FthTheme.dp(activity, 4f))
        })

        val calcInput = EditText(activity).apply {
            hint = FthSessionManager.t("Wpisz np. 49850 18813 lub PRZED...", "Type e.g. 49850 18813 or PRZED...")
            setTextColor(Color.WHITE)
            setHintTextColor(FthTheme.TextMuted)
            textSize = 14f
            typeface = Typeface.MONOSPACE
            val p = FthTheme.dp(activity, 10f)
            setPadding(p, p, p, p)
            background = FthTheme.cardDrawable(FthTheme.BgInput, FthTheme.BorderInput, 6f, 2, activity)
        }

        val calcOutput = TextView(activity).apply {
            text = FthSessionManager.t("Wynik SARNEG: —", "SARNEG Output: —")
            setTextColor(FthTheme.AccentGold)
            textSize = 14f
            setTypeface(Typeface.MONOSPACE, Typeface.BOLD)
            setPadding(0, FthTheme.dp(activity, 8f), 0, FthTheme.dp(activity, 8f))
        }

        calcInput.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                val raw = s?.toString() ?: ""
                val converted = TacticalGeoAndSarnegEngine.encodeOrDecodeSarneg(raw, activeDay.sarnegKeyword10)
                calcOutput.text = FthSessionManager.t("Wynik SARNEG:  $converted", "SARNEG Output:  $converted")
            }
        })

        sarnegCard.addView(calcInput)
        sarnegCard.addView(calcOutput)

        val btnDrawSarneg = createActionButton(
            FthSessionManager.t("🎲 Losuj nowe słowo SARNEG (${activeDay.dayTitle})", "🎲 Draw New SARNEG Word (${activeDay.dayTitle})"),
            FthTheme.BgElevated,
            FthTheme.AccentGold,
            FthTheme.AccentGold
        ) {
            val pool = TacticalGeoAndSarnegEngine.BuiltInSarnegWords
            val newWord = pool[Random().nextInt(pool.size)]
            val updatedDay = activeDay.copy(sarnegKeyword10 = newWord)
            p.opordDays[p.activeDayIndex.coerceIn(0, p.opordDays.size - 1)] = updatedDay
            FthSessionManager.saveActiveProfile(activity, p)
            rebuildActiveTabContent()
        }
        sarnegCard.addView(btnDrawSarneg)

        sarnegCard.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "\n🎯 Kryptonimy celów (${activeDay.dayTitle}):\n${activeDay.targetCodeNames}\n\n" +
                        "🔑 Hasła rozpoznawcze (${activeDay.dayTitle}):\n${activeDay.challengePassword}",
                "\n🎯 Objective Code Names (${activeDay.dayTitle}):\n${activeDay.targetCodeNames}\n\n" +
                        "🔑 Challenge & Password (${activeDay.dayTitle}):\n${activeDay.challengePassword}"
            )
            setTextColor(FthTheme.TextPrimary)
            textSize = 11.5f
        })

        contentContainer.addView(sarnegCard)
    }

    private fun buildTab2OpordAndMetoc() {
        val p = FthSessionManager.activeProfile
        if (p.opordDays.isEmpty()) {
            p.opordDays.add(FthOpordDayPlan())
        }
        p.activeDayIndex = p.activeDayIndex.coerceIn(0, p.opordDays.size - 1)
        val d = p.opordDays[p.activeDayIndex]

        val daysCard = createSectionCard(
            FthSessionManager.t("📅 DNI OPERACJI (NIEZALEŻNE ZAKŁADKI OPORD)", "📅 OPERATION DAYS (INDEPENDENT OPORD TABS)"),
            FthTheme.AccentCyan
        )

        val daysRow = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
        }
        for (i in p.opordDays.indices) {
            val dayItem = p.opordDays[i]
            val isSel = (i == p.activeDayIndex)
            val btnDay = Button(activity).apply {
                text = "📆 ${dayItem.dayTitle} (${dayItem.dateIso})"
                textSize = 10.5f
                setTypeface(typeface, Typeface.BOLD)
                setTextColor(if (isSel) FthTheme.DarkFg else Color.WHITE)
                background = FthTheme.buttonDrawable(
                    if (isSel) FthTheme.AccentCyan else FthTheme.BgInput,
                    if (isSel) FthTheme.AccentCyan else FthTheme.BorderInput,
                    6f, 2, activity
                )
                val lp = LinearLayout.LayoutParams(0, FthTheme.dp(activity, 36f), 1f)
                lp.marginEnd = FthTheme.dp(activity, 6f)
                layoutParams = lp
                setOnClickListener {
                    p.activeDayIndex = i
                    rebuildActiveTabContent()
                }
            }
            daysRow.addView(btnDay)
        }
        daysCard.addView(daysRow)
        contentContainer.addView(daysCard)

        val opordCard = createSectionCard(
            "📋 ${d.operationNumber} — ${d.dayTitle.uppercase(Locale.ROOT)} (${d.dateIso})",
            if (d.isSecret) FthTheme.AccentRed else FthTheme.AccentCyan
        )

        opordCard.addView(TextView(activity).apply {
            text = "🔒 ${d.classification}\nHQ: ${d.issuingHq}  |  DTG: ${d.dtgIssued}\nRejon AO: ${d.locationName} (${d.locationLat}°N, ${d.locationLon}°E)"
            setTextColor(FthTheme.AccentGold)
            textSize = 11f
            typeface = Typeface.MONOSPACE
            setPadding(0, 0, 0, FthTheme.dp(activity, 8f))
        })

        val sections = listOf(
            FthSessionManager.t("⏱️ HARMONOGRAM DNIA (${d.dayTitle})", "⏱️ DAY SCHEDULE (${d.dayTitle})") to d.daySchedule,
            FthSessionManager.t("I. SYTUACJA & POGODA METOC (${d.dayTitle})", "I. SITUATION & METOC (${d.dayTitle})") to
                    "• POI / Mapa: ${d.aoiPoiDesc}\n• Teren i METOC: ${d.terrainAndMetoc}\n• Siły wroga (COA): ${d.enemyForces}\n• Siły własne: ${d.friendlyForces}",
            FthSessionManager.t("II. MISJA (GŁÓWNE ZADANIE 5W)", "II. MISSION (PRIMARY 5W TASK)") to
                    "🎯 ${d.missionFullText}\n\nZadania dodatkowe:\n${d.additionalTasks}",
            FthSessionManager.t("III. WYKONANIE, ŁAŃCUCH DOWODZENIA I MEDEVAC", "III. EXECUTION, CHAIN OF COMMAND & MEDEVAC") to
                    "• Zamiar dowódcy: ${d.commandersIntent}\n• Łańcuch dowodzenia: ${d.chainOfCommand}\n• Słowo SARNEG (${d.dayTitle}): ${d.sarnegKeyword10}\n• Zasady MEDEVAC / Respawn: ${d.medevacRules}",
            FthSessionManager.t("IV. LOGISTYKA (SZPEJLISTA)", "IV. LOGISTICS (GEAR CHECKLIST)") to d.gearChecklist
        )

        for ((secTitle, secBody) in sections) {
            val secBox = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                background = FthTheme.cardDrawable(FthTheme.BgInput, FthTheme.BorderSubtle, 6f, 2, activity)
                val pad = FthTheme.dp(activity, 10f)
                setPadding(pad, pad, pad, pad)
                val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                lp.topMargin = FthTheme.dp(activity, 8f)
                layoutParams = lp
            }
            secBox.addView(TextView(activity).apply {
                text = secTitle
                setTextColor(FthTheme.AccentCyan)
                textSize = 11.5f
                setTypeface(typeface, Typeface.BOLD)
            })
            secBox.addView(TextView(activity).apply {
                text = secBody
                setTextColor(Color.WHITE)
                textSize = 11f
                setPadding(0, FthTheme.dp(activity, 4f), 0, 0)
            })
            opordCard.addView(secBox)
        }

        contentContainer.addView(opordCard)
    }

    private fun buildTab3HotspotP2p() {
        val p = FthSessionManager.activeProfile
        val shareUrl = FthHotspotP2pServer.startServer()

        val p2pCard = createSectionCard(
            FthSessionManager.t(
                "📡 UDOSTĘPNIANIE PACZKI PRZEZ HOTSPOT WI-FI (OFF-GRID W LESIE)",
                "📡 SHARE PACKAGE OVER WI-FI HOTSPOT (OFF-GRID IN FOREST)"
            ),
            FthTheme.AccentCyan
        )

        p2pCard.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "Jesteś na odprawie w lesie bez zasięgu LTE? Włącz w telefonie 'Hotspot Wi-Fi', niech drużyna podłączy się do Twojego Wi-Fi i zeskanuje lub otworzy poniższy adres. Każdy członek drużyny pobierze paczkę '${p.factionName}' i wpisze swój własny Callsign!",
                "In the forest without LTE? Turn on your phone's Wi-Fi Hotspot, let your squad connect and scan or open the URL below. Each member will receive the '${p.factionName}' package and enter their own Callsign!"
            )
            setTextColor(FthTheme.TextSecondary)
            textSize = 11f
            setPadding(0, 0, 0, FthTheme.dp(activity, 10f))
        })

        val urlBox = TextView(activity).apply {
            text = "🔗 Adres lokalnego serwera P2P:\n$shareUrl"
            setTextColor(FthTheme.AccentGold)
            textSize = 12.5f
            setTypeface(Typeface.MONOSPACE, Typeface.BOLD)
            gravity = Gravity.CENTER
            val pad = FthTheme.dp(activity, 12f)
            setPadding(pad, pad, pad, pad)
            background = FthTheme.cardDrawable(FthTheme.BgDeep, FthTheme.AccentCyan, 8f, 3, activity)
        }
        p2pCard.addView(urlBox)

        val btnCopyP2pUrl = createActionButton(
            FthSessionManager.t("📋 Kopiuj adres P2P / JSON paczki do schowka", "📋 Copy P2P URL / Package JSON to Clipboard"),
            FthTheme.AccentCyan,
            FthTheme.DarkFg
        ) {
            val cb = activity.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("FTH_P2P_URL", shareUrl))
            Toast.makeText(
                activity,
                FthSessionManager.t("Skopiowano link: $shareUrl", "Copied link: $shareUrl"),
                Toast.LENGTH_SHORT
            ).show()
        }.apply {
            (layoutParams as LinearLayout.LayoutParams).topMargin = FthTheme.dp(activity, 10f)
        }
        p2pCard.addView(btnCopyP2pUrl)

        contentContainer.addView(p2pCard)
    }

    private fun buildTab4SosMgrs() {
        val p = FthSessionManager.activeProfile
        val mgrsCoord = TacticalGeoAndSarnegEngine.latLonToMgrs(lastLat, lastLon)

        val sosCard = createSectionCard(
            FthSessionManager.t(
                "🚨 SYSTEM ALARMOWY SOS MGRS (MEDEVAC / ORGA)",
                "🚨 EMERGENCY SOS MGRS BEACON (MEDEVAC / ORGA)"
            ),
            FthTheme.AccentRed
        )

        val gpsStatusLabel = if (hasLiveGpsFix) {
            FthSessionManager.t("🟢 AKTYWNY FIX GPS (±${lastAccuracyMeters.toInt()} m)", "🟢 LIVE GPS FIX (±${lastAccuracyMeters.toInt()} m)")
        } else {
            FthSessionManager.t("🟡 OSTATNIA ZNANA POZYCJA AO (Oczekiwanie na satelity GPS...)", "🟡 LAST KNOWN AO POSITION (Waiting for GPS satellites...)")
        }

        val mgrsBox = TextView(activity).apply {
            text = "📍 MGRS:  $mgrsCoord\n" +
                    String.format(Locale.US, "🌐 WGS84: %.6f°N, %.6f°E\n", lastLat, lastLon) +
                    "🛡️ Callsign: ${p.operatorCallsign} [${p.factionName}]\n" +
                    "📡 Status: $gpsStatusLabel"
            setTextColor(Color.WHITE)
            textSize = 13f
            setTypeface(Typeface.MONOSPACE, Typeface.BOLD)
            val pad = FthTheme.dp(activity, 14f)
            setPadding(pad, pad, pad, pad)
            background = FthTheme.cardDrawable(FthTheme.BgDeep, FthTheme.AccentRed, 8f, 4, activity)
        }
        sosCard.addView(mgrsBox)

        val smsBody = "🚨 SOS MEDEVAC! Operator: ${p.operatorCallsign} (${p.factionName}) | MGRS: $mgrsCoord | GPS: " +
                String.format(Locale.US, "%.5f, %.5f", lastLat, lastLon)

        val btnSendSosSms = createActionButton(
            FthSessionManager.t("📩 WYŚLIJ SMS RATUNKOWY Z POZYCJĄ MGRS DO ORGA", "📩 SEND EMERGENCY MGRS SMS TO ORGA"),
            FthTheme.AccentRed,
            Color.WHITE
        ) {
            try {
                val uri = Uri.parse("smsto:${p.emergencyPhone}")
                val intent = Intent(Intent.ACTION_SENDTO, uri).apply {
                    putExtra("sms_body", smsBody)
                }
                activity.startActivity(intent)
            } catch (_: Exception) {
            }
        }.apply {
            (layoutParams as LinearLayout.LayoutParams).topMargin = FthTheme.dp(activity, 12f)
        }
        sosCard.addView(btnSendSosSms)

        val btnCallOrga = createActionButton(
            FthSessionManager.t("📞 ZADZWOŃ DO ORGA / MEDEVAC (${p.emergencyPhone})", "📞 CALL ORGA / MEDEVAC (${p.emergencyPhone})"),
            FthTheme.AccentOrange,
            FthTheme.DarkFg
        ) {
            try {
                val intent = Intent(Intent.ACTION_DIAL, Uri.parse("tel:${p.emergencyPhone}"))
                activity.startActivity(intent)
            } catch (_: Exception) {
            }
        }.apply {
            (layoutParams as LinearLayout.LayoutParams).topMargin = FthTheme.dp(activity, 8f)
        }
        sosCard.addView(btnCallOrga)

        contentContainer.addView(sosCard)
    }

    private fun launchCameraQrScannerOrPasteDialog() {
        showQrInputOrLanFetchModal()
    }

    private fun showQrInputOrLanFetchModal() {
        val box = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(FthTheme.BgCard)
            val pad = FthTheme.dp(activity, 16f)
            setPadding(pad, pad, pad, pad)
        }

        box.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "📷 SKANER QR / POBIERANIE PACZKI PO WI-FI Z BUILDERA",
                "📷 QR SCANNER / WI-FI PACKAGE FETCH FROM BUILDER"
            )
            setTextColor(FthTheme.AccentCyan)
            textSize = 13.5f
            setTypeface(typeface, Typeface.BOLD)
        })

        box.addView(TextView(activity).apply {
            text = FthSessionManager.t(
                "Wklej zeskanowany kod QR (JSON / link z plakatu Safezone) lub wpisz adres IP komputera z Windows Builderem / Hotspotu dowódcy:",
                "Paste scanned QR payload (JSON / Safezone poster link) or enter the Windows Builder / Squad Leader Hotspot URL:"
            )
            setTextColor(FthTheme.TextSecondary)
            textSize = 11f
            setPadding(0, FthTheme.dp(activity, 6f), 0, FthTheme.dp(activity, 8f))
        })

        val inputEdit = EditText(activity).apply {
            hint = "http://192.168.1.100:8769/opord/BLUFOR.json  lub  {...}"
            setTextColor(Color.WHITE)
            setHintTextColor(FthTheme.TextMuted)
            textSize = 12f
            typeface = Typeface.MONOSPACE
            minLines = 3
            val p = FthTheme.dp(activity, 10f)
            setPadding(p, p, p, p)
            background = FthTheme.cardDrawable(FthTheme.BgInput, FthTheme.BorderInput, 6f, 2, activity)
        }

        try {
            val cb = activity.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val clipTxt = cb.primaryClip?.getItemAt(0)?.text?.toString()?.trim() ?: ""
            if (clipTxt.startsWith("{") || clipTxt.startsWith("http")) {
                inputEdit.setText(clipTxt)
            }
        } catch (_: Exception) {
        }

        box.addView(inputEdit)

        val dlg = AlertDialog.Builder(activity)
            .setView(box)
            .setCancelable(true)
            .create()

        val btnProcess = createActionButton(
            FthSessionManager.t("⚡ Przetwórz Kod QR i Przejdź do Callsignu", "⚡ Process QR & Proceed to Callsign"),
            FthTheme.AccentCyan,
            FthTheme.DarkFg
        ) {
            val raw = inputEdit.text?.toString() ?: ""
            dlg.dismiss()
            handleIncomingQrOrFilePayload(raw)
        }.apply {
            (layoutParams as LinearLayout.LayoutParams).topMargin = FthTheme.dp(activity, 12f)
        }
        box.addView(btnProcess)

        dlg.show()
    }

    private fun launchSystemFilePicker() {
        try {
            val intent = Intent(Intent.ACTION_GET_CONTENT).apply {
                type = "*/*"
                addCategory(Intent.CATEGORY_OPENABLE)
            }
            activity.startActivityForResult(
                Intent.createChooser(intent, FthSessionManager.t("Wybierz plik .ftak / .fthproj", "Select .ftak / .fthproj file")),
                MainActivity.REQ_PICK_FILE
            )
        } catch (_: Exception) {
        }
    }

    fun handleIncomingQrOrFilePayload(rawPayload: String) {
        FthPayloadParser.processScannedOrImportedPayload(
            activity,
            rawPayload,
            onStatus = { statusMsg ->
                appendLog(statusMsg)
                rebuildActiveTabContent()
            },
            onProfileReadyForCallsign = { parsedProfile ->
                showDynamicCallsignInjectionModal(parsedProfile, isNewScan = true)
            }
        )
    }

    fun handlePickedFileUri(uri: Uri) {
        try {
            val bytes = activity.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: return
            val parsed = FthPayloadParser.parseBytesOrJson(activity, bytes)
            if (parsed != null) {
                appendLog(
                    FthSessionManager.t(
                        "📂 Wczytano plik projektu: ${parsed.eventName} (${parsed.factionName}). Podaj swój Callsign...",
                        "📂 Loaded project file: ${parsed.eventName} (${parsed.factionName}). Enter your Callsign..."
                    )
                )
                showDynamicCallsignInjectionModal(parsed, isNewScan = true)
            } else {
                appendLog(FthSessionManager.t("⚠️ Nie udało się rozpoznać struktury pliku.", "⚠️ Unrecognized file structure."))
                rebuildActiveTabContent()
            }
        } catch (ex: Exception) {
            appendLog("❌ Błąd odczytu pliku: ${ex.message}")
            rebuildActiveTabContent()
        }
    }

    private fun buildBuiltInFactionPreset(isOpfor: Boolean): FthFactionProfile {
        return if (!isOpfor) {
            FthFactionProfile(
                eventName = "MILSIM 2026",
                factionName = "BLUFOR",
                colorHex = "#2684FF",
                operatorCallsign = FthSessionManager.activeProfile.operatorCallsign.ifBlank { "Wilk-1" },
                meshtasticShortName = FthSessionManager.activeProfile.meshtasticShortName.ifBlank { "Wlk1" },
                channelName = "GGZS-TAK",
                pskBase64 = "YCVv8+pxmmV26ff2U0M4mami64OL7A9Z0Nyv6VgsdmE=",
                loraRegion = "EU_868",
                loraModemPreset = "SHORT_FAST",
                hopLimit = 3,
                otsHost = "ggzstak.duckdns.org",
                emergencyPhone = "+48 600 000 000",
                voiceChannels = mutableListOf(
                    FthVoiceChannel("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"),
                    FthVoiceChannel("Taktyczny Alfa", "VHF/UHF", "145.500 MHz", "CTCSS 88.5 Hz"),
                    FthVoiceChannel("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
                )
            )
        } else {
            FthFactionProfile(
                eventName = "MILSIM 2026",
                factionName = "OPFOR",
                colorHex = "#EB4034",
                operatorCallsign = FthSessionManager.activeProfile.operatorCallsign.ifBlank { "Kruk-1" },
                meshtasticShortName = "Krk1",
                channelName = "OPFOR-TAK",
                pskBase64 = "r0wqyK/h1QvcAkHvtiWwGmCVfw7hesRuXLqMBmsbCvI=",
                loraRegion = "EU_868",
                loraModemPreset = "SHORT_FAST",
                hopLimit = 3,
                otsHost = "ggzstak.duckdns.org",
                emergencyPhone = "+48 600 000 000",
                voiceChannels = mutableListOf(
                    FthVoiceChannel("Dowodzenie HQ", "PMR", "CH 4 (446.04375)", "CTCSS 100.0 Hz"),
                    FthVoiceChannel("Taktyczny Bravo", "DMR", "433.450 MHz", "CC1 TS1 TG99"),
                    FthVoiceChannel("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
                )
            )
        }
    }

    @SuppressLint("MissingPermission")
    private fun startGpsTrackingIfPermitted() {
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                val hasFine = activity.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
                if (!hasFine) {
                    activity.requestPermissions(
                        arrayOf(
                            Manifest.permission.ACCESS_FINE_LOCATION,
                            Manifest.permission.ACCESS_COARSE_LOCATION
                        ),
                        101
                    )
                    return
                }
            }

            locationManager = activity.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
            val listener = object : LocationListener {
                override fun onLocationChanged(loc: Location) {
                    lastLat = loc.latitude
                    lastLon = loc.longitude
                    lastAccuracyMeters = loc.accuracy
                    hasLiveGpsFix = true
                    if (activeTabIndex == 4) {
                        rebuildActiveTabContent()
                    }
                }
                override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
                override fun onProviderEnabled(provider: String) {}
                override fun onProviderDisabled(provider: String) {}
            }
            gpsLocationListener = listener
            locationManager?.requestLocationUpdates(LocationManager.GPS_PROVIDER, 3000L, 2f, listener)
            locationManager?.requestLocationUpdates(LocationManager.NETWORK_PROVIDER, 5000L, 5f, listener)
        } catch (_: Exception) {
        }
    }

    private fun createSectionCard(title: String, borderColor: Int): LinearLayout {
        val card = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            background = FthTheme.cardDrawable(FthTheme.BgCard, borderColor, 10f, 3, activity)
            val pad = FthTheme.dp(activity, 14f)
            setPadding(pad, pad, pad, pad)
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            lp.bottomMargin = FthTheme.dp(activity, 12f)
            layoutParams = lp
        }

        val hdr = TextView(activity).apply {
            text = title
            setTextColor(FthTheme.AccentCyan)
            textSize = 12.5f
            setTypeface(typeface, Typeface.BOLD)
            setPadding(0, 0, 0, FthTheme.dp(activity, 8f))
        }
        card.addView(hdr)
        return card
    }

    private fun createActionButton(
        label: String,
        bgColor: Int,
        fgColor: Int,
        strokeColor: Int = bgColor,
        onClick: () -> Unit
    ): Button {
        return Button(activity).apply {
            text = label
            setTextColor(fgColor)
            textSize = 11.5f
            isAllCaps = false
            setTypeface(typeface, Typeface.BOLD)
            background = FthTheme.buttonDrawable(bgColor, strokeColor, 8f, 3, activity)
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                FthTheme.dp(activity, 42f)
            )
            setOnClickListener { onClick() }
        }
    }
}
//===PAYLOAD_ANDROID_UI_KT===

//===PAYLOAD_ANDROID_MAIN_ACTIVITY_KT===
import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.view.Window

class MainActivity : Activity() {

    companion object {
        const val REQ_QR_SCAN = 9001
        const val REQ_PICK_FILE = 9002
    }

    private var dashboardController: FthTacticalDashboardController? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        requestWindowFeature(Window.FEATURE_NO_TITLE)

        val controller = FthTacticalDashboardController(this)
        dashboardController = controller
        setContentView(controller.createRootView())

        handleIncomingIntent(intent)
    }

    override fun onNewIntent(intent: Intent?) {
        super.onNewIntent(intent)
        handleIncomingIntent(intent)
    }

    private fun handleIncomingIntent(intent: Intent?) {
        val dataUri = intent?.data ?: return
        val scheme = dataUri.scheme?.lowercase() ?: ""
        if (scheme == "http" || scheme == "https" || scheme == "meshtastic") {
            dashboardController?.handleIncomingQrOrFilePayload(dataUri.toString())
        } else {
            dashboardController?.handlePickedFileUri(dataUri)
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != RESULT_OK || data == null) return

        when (requestCode) {
            REQ_QR_SCAN -> {
                val contents = data.getStringExtra("SCAN_RESULT") ?: return
                dashboardController?.handleIncomingQrOrFilePayload(contents)
            }
            REQ_PICK_FILE -> {
                val uri = data.data ?: return
                dashboardController?.handlePickedFileUri(uri)
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        FthHotspotP2pServer.stopServer()
    }
}
//===PAYLOAD_ANDROID_MAIN_ACTIVITY_KT===
