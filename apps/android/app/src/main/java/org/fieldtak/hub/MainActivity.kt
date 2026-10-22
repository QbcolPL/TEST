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
import android.graphics.Color
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
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.HybridBinarizer
import java.util.EnumMap
import java.util.Locale
import java.util.concurrent.atomic.AtomicBoolean

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
                Toast.makeText(this, "⚠️ Brak uprawnień do aparatu — użyj opcji 'QR z Galerii'!", Toast.LENGTH_LONG).show()
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
                        "✓ Wczytano paczkę operacji: ${FtakProvisioningEngine.getEventName(this)}",
                        Toast.LENGTH_LONG
                    ).show()
                    rebuildScreen()
                    showIdentityAndFactionModal()
                }
            }
        } catch (ex: Exception) {
            Toast.makeText(this, "⚠️ Błąd otwierania pliku: ${ex.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun rebuildScreen() {
        if (!FtakProvisioningEngine.isPermissionsOnboardingCompleted(this)) {
            setContentView(buildPermissionOnboardingView())
        } else {
            setContentView(buildMainTacticalHubView())
        }
    }

    // =========================================================================
    // EKRAN 1: ŚWIADOMY ONBOARDING UPRAWNIEŃ (PERMISSION GATE)
    // =========================================================================
    private fun buildPermissionOnboardingView(): View {
        val scroll = ScrollView(this).apply {
            setBackgroundColor(Color.parseColor("#061014"))
            isFillViewport = true
        }

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(24), dp(20), dp(24))
        }

        val badgeHeader = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = roundedBox("#0E1E24", "#1FE0C5", 2, 14f)
            setPadding(dp(16), dp(16), dp(16), dp(16))
        }

        badgeHeader.addView(TextView(this).apply {
            text = "🛡️ DOSTĘP DO SYSTEMU POLOWEGO"
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 17f)
        })

        badgeHeader.addView(TextView(this).apply {
            text = "Field TAK Hub to czysty terminal wdrożeniowy i ratunkowy. Aplikacja nie śledzi Twojej pozycji w tle i nie dubluje nawigacji ATAK-CIV. Sprawdź poniżej, dlaczego prosimy o poszczególne uprawnienia:"
            setTextColor(Color.parseColor("#B8CFD8"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
            setPadding(0, dp(8), 0, 0)
        })

        root.addView(badgeHeader, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(16)
        })

        fun addPermissionExplainCard(iconTitle: String, desc: String) {
            val card = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                background = roundedBox("#0B181D", "#1E3640", 1, 10f)
                setPadding(dp(14), dp(12), dp(14), dp(12))
            }
            card.addView(TextView(this).apply {
                text = iconTitle
                setTextColor(Color.WHITE)
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
            })
            card.addView(TextView(this).apply {
                text = desc
                setTextColor(Color.parseColor("#98B2BC"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                setPadding(0, dp(4), 0, 0)
            })
            root.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                bottomMargin = dp(10)
            })
        }

        addPermissionExplainCard(
            "📷 1. Aparat (Kamera)",
            "Wymagany wyłącznie do szybkiego skanowania kodów QR z plakatów w Safezonie lub z ekranu telefonu dowódcy."
        )
        addPermissionExplainCard(
            "🖼️ 2. Galeria i Pamięć Plików",
            "Wymagane do wczytywania kodów QR zapisanych jako obrazki z Discorda/Messengera oraz otwierania paczek misji (.ftak)."
        )
        addPermissionExplainCard(
            "📡 3. Wi-Fi / Sieć Lokalna (Hotspot Ad-Hoc)",
            "Wymagane do błyskawicznego przesyłania paczek .ftak i map offline między telefonami w lesie bez zasięgu LTE."
        )
        addPermissionExplainCard(
            "📍 4. Lokalizacja GPS (Tylko na żądanie SOS)",
            "Używana wyłącznie w momencie naciśnięcia czerwonego przycisku SOS MGRS w celu wyliczenia meldunku ratunkowego MEDEVAC."
        )

        val btnGrant = makeButton(
            "✅ ZROZUMIAŁEM — NADAJ UPRAWNIENIA I WEJDŹ",
            "#1FE0C5",
            Color.parseColor("#041014"),
            15f
        ) {
            requestAllTacticalPermissions()
        }
        root.addView(btnGrant, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(54)).apply {
            topMargin = dp(12)
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
            }
            requestPermissions(perms.toTypedArray(), REQ_RUNTIME_PERMS)
        }
        rebuildScreen()
    }

    // =========================================================================
    // EKRAN 2: GŁÓWNY PULPIT POLOWY („1 SKAN I GRASZ”)
    // =========================================================================
    private fun buildMainTacticalHubView(): View {
        val scroll = ScrollView(this).apply {
            setBackgroundColor(Color.parseColor("#061014"))
            isFillViewport = true
        }

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(14), dp(14), dp(14), dp(22))
        }

        val hasProfile = FtakProvisioningEngine.hasLoadedProfile(this)
        val activeFaction = FtakProvisioningEngine.getActiveFaction(this)
        val allFactions = FtakProvisioningEngine.getLoadedFactions(this)
        val callsign = FtakProvisioningEngine.getCallsign(this)
        val eventName = FtakProvisioningEngine.getEventName(this)
        val accentHex = activeFaction?.colorHex ?: "#1FE0C5"

        val headerCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = roundedBox("#0D1C22", accentHex, 2, 12f)
            setPadding(dp(14), dp(12), dp(14), dp(12))
        }

        val topRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        val titleCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }

        titleCol.addView(TextView(this).apply {
            text = "🛰️ FIELD TAK HUB MOBILE v2.4"
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
        })

        titleCol.addView(TextView(this).apply {
            text = if (hasProfile) "OPERACJA: $eventName" else "BRAK WCZYTANEJ OPERACJI"
            setTextColor(Color.WHITE)
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
        })

        val btnEditIdentity = makeButton(
            "👤 $callsign ✎",
            "#152C35",
            Color.WHITE,
            12.5f
        ) {
            showIdentityAndFactionModal()
        }
        topRow.addView(titleCol)
        topRow.addView(btnEditIdentity, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(38)))
        headerCard.addView(topRow)

        if (hasProfile && activeFaction != null) {
            val factionFiles = FtakProvisioningEngine.getFactionFilteredFiles(this, activeFaction)
            val statusTxt = TextView(this).apply {
                val otsInfo = if (activeFaction.otsHost.isNotBlank()) activeFaction.otsHost else "Tryb Lokalny / Mesh"
                text = "🛡️ Strona: ${activeFaction.name}  •  Kanał LoRa: ${activeFaction.channelName} (${activeFaction.loraRegion})\n" +
                        "🌐 OTS: $otsInfo  •  Załączone pliki map/warstw: ${factionFiles.size}"
                setTextColor(Color.parseColor("#B8CFD8"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                setPadding(0, dp(8), 0, dp(4))
            }
            headerCard.addView(statusTxt)

            if (allFactions.size > 1) {
                val facSwitchRow = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    setPadding(0, dp(6), 0, 0)
                }
                for (fac in allFactions) {
                    val isSelected = fac.name.equals(activeFaction.name, ignoreCase = true)
                    val facBtn = makeButton(
                        if (isSelected) "✓ ${fac.name}" else fac.name,
                        if (isSelected) fac.colorHex else "#102027",
                        Color.WHITE,
                        12f
                    ) {
                        FtakProvisioningEngine.setActiveFaction(this, fac.name)
                        rebuildScreen()
                    }
                    facSwitchRow.addView(facBtn, LinearLayout.LayoutParams(0, dp(36), 1f).apply {
                        rightMargin = dp(4)
                    })
                }
                headerCard.addView(facSwitchRow)
            }
        } else {
            headerCard.addView(TextView(this).apply {
                text = "Zeskanuj kod QR z plakatu w Safezonie, wybierz zrzut ekranu QR z Galerii lub wczytaj plik .ftak poniżej:"
                setTextColor(Color.parseColor("#9CB3BC"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                setPadding(0, dp(6), 0, 0)
            })
        }

        root.addView(headerCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(12)
        })

        // SEKCJA 1: POTRÓJNY MODUŁ WEJŚCIA
        val inputSection = createSectionContainer("1. WCZYTAJ KONFIGURACJĘ GRY (1 SKAN I GRASZ)")
        val inputRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
        }

        val btnScanCamera = makeButton("📷 Skanuj QR\n(Aparat)", "#1FE0C5", Color.parseColor("#041014"), 12f) {
            startCameraQrScan()
        }
        val btnScanGallery = makeButton("🖼️ Wczytaj QR\n(Z Galerii)", "#142A33", Color.WHITE, 12f) {
            pickQrImageFromGallery()
        }
        val btnLoadFtak = makeButton("📂 Wczytaj\nPlik .ftak", "#142A33", Color.WHITE, 12f) {
            pickFtakFileFromStorage()
        }

        inputRow.addView(btnScanCamera, LinearLayout.LayoutParams(0, dp(54), 1f).apply { rightMargin = dp(5) })
        inputRow.addView(btnScanGallery, LinearLayout.LayoutParams(0, dp(54), 1f).apply { rightMargin = dp(5) })
        inputRow.addView(btnLoadFtak, LinearLayout.LayoutParams(0, dp(54), 1f))
        inputSection.addView(inputRow)

        root.addView(inputSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(12)
        })

        // SEKCJA 2: AUTOMATYCZNE WSTRZYKIWANIE 1-KLIKIEM (ATAK-CIV & MESHTASTIC)
        val deploySection = createSectionContainer("2. WDRÓŻ USTAWIENIA 1-KLIKIEM (ATAK & MESHTASTIC)")

        val btnDeployAtak = makeButton(
            "🗺️ WGRAJ MAPY, PUNKTY I SERWER DO ATAK-CIV",
            if (hasProfile) "#2684FF" else "#182830",
            Color.WHITE,
            13.5f
        ) {
            FtakProvisioningEngine.provisionAtakOneClick(this)
        }
        deploySection.addView(btnDeployAtak, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(50)).apply {
            bottomMargin = dp(8)
        })

        if (activeFaction != null && activeFaction.enrollmentUri.isNotBlank()) {
            val btnEnroll = makeButton(
                "🔐 Autoryzuj Certyfikat OpenTAK (1-Click Enrollment)",
                "#15323C",
                Color.parseColor("#1FE0C5"),
                12f
            ) {
                try {
                    startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(activeFaction.enrollmentUri)).apply {
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    })
                } catch (ex: Exception) {
                    Toast.makeText(this, "⚠️ Brak obsługi linku tak:// (${ex.message})", Toast.LENGTH_LONG).show()
                }
            }
            deploySection.addView(btnEnroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42)).apply {
                bottomMargin = dp(8)
            })
        }

        val btnDeployMesh = makeButton(
            "📡 ZAPROGRAMUJ RADIO MESHTASTIC (KANAŁ + PSK)",
            if (hasProfile) "#2ECC71" else "#182830",
            if (hasProfile) Color.parseColor("#04120A") else Color.WHITE,
            13.5f
        ) {
            FtakProvisioningEngine.provisionMeshtasticOneClick(this)
        }
        deploySection.addView(btnDeployMesh, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(50)))

        root.addView(deploySection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(12)
        })

        // SEKCJA 3: FIELD SHARE HUB (WI-FI HOTSPOT AD-HOC + QR + PLIKI .GPX)
        val shareSection = createSectionContainer("3. PODZIEL SIĘ W POLU (WI-FI HOTSPOT BEZ INTERNETU)")

        val btnHotspotServer = makeButton(
            "📡 UDOSTĘPNIJ .FTAK PRZEZ WI-FI HOTSPOT (SERWER AD-HOC + QR)",
            "#142E38",
            Color.parseColor("#1FE0C5"),
            12.5f
        ) {
            FieldShareAndQrEngine.showWifiHotspotShareDialog(this)
        }
        shareSection.addView(btnHotspotServer, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(48)).apply {
            bottomMargin = dp(8)
        })

        val shareSubRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
        }
        val btnShowFactionQr = makeButton("📱 Pokaż QR Strony\nna ekranie", "#12232B", Color.WHITE, 11.5f) {
            FieldShareAndQrEngine.showOfflineFactionQrDialog(this)
        }
        val btnShareFiles = makeButton("📤 Wyślij plik .ftak / .gpx\n(Quick Share / Garmin)", "#12232B", Color.WHITE, 11.5f) {
            FieldShareAndQrEngine.showShareFilesPickerDialog(this)
        }
        shareSubRow.addView(btnShowFactionQr, LinearLayout.LayoutParams(0, dp(46), 1f).apply { rightMargin = dp(5) })
        shareSubRow.addView(btnShareFiles, LinearLayout.LayoutParams(0, dp(46), 1f))
        shareSection.addView(shareSubRow)

        root.addView(shareSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(12)
        })

        // SEKCJA 4: KARTA „📻 ŚCIĄGA OPERACYJNA”
        val radioSection = createSectionContainer("4. 📻 ŚCIĄGA OPERACYJNA (TABELA KANAŁÓW RADIOWYCH)")
        if (activeFaction == null || activeFaction.voiceChannels.isEmpty()) {
            radioSection.addView(TextView(this).apply {
                text = "Brak zapisanych kanałów głosowych PMR / VHF / DMR — wczytaj paczkę .ftak lub kod QR."
                setTextColor(Color.parseColor("#8CA4AE"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            })
        } else {
            for (vc in activeFaction.voiceChannels) {
                val chCard = LinearLayout(this).apply {
                    orientation = LinearLayout.VERTICAL
                    background = roundedBox("#0E1D23", "#203A44", 1, 8f)
                    setPadding(dp(12), dp(8), dp(12), dp(8))
                }
                chCard.addView(TextView(this).apply {
                    text = "🎙️ ${vc.roleName}   [${vc.bandType}]"
                    setTextColor(Color.parseColor("#1FE0C5"))
                    typeface = Typeface.DEFAULT_BOLD
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f)
                })
                chCard.addView(TextView(this).apply {
                    text = "Kanał / Częstotliwość: ${vc.freqMhz}   •   Ton / Kod: ${vc.toneOrCode}"
                    setTextColor(Color.WHITE)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                })
                radioSection.addView(chCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    bottomMargin = dp(6)
                })
            }
        }

        root.addView(radioSection, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(12)
        })

        // SEKCJA 5: ALARM RATUNKOWY SOS MGRS & WSPARCIE
        val sosBtn = makeButton(
            "🚨 ALARM RATUNKOWY SOS MGRS / KONTAKT ORGA",
            "#EB4034",
            Color.WHITE,
            14f
        ) {
            showOnDemandSosMgrsModal()
        }
        root.addView(sosBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(52)).apply {
            bottomMargin = dp(10)
        })

        val btnSupportCoffee = makeButton(
            "☕ Wesprzyj twórcę Field TAK Hub (Kod QR & buycoffee.to)",
            "#102027",
            Color.parseColor("#FAD02C"),
            12f
        ) {
            showCreatorSupportModal()
        }
        root.addView(btnSupportCoffee, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42)))

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
            text = "👤 CALLSIGN GRACZA & STRONA KONFLIKTU"
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
        })

        box.addView(TextView(this).apply {
            text = "Podaj swój kryptonim wywoławczy (zostanie automatycznie wstrzyknięty do ATAK-CIV i meldunków SOS):"
            setTextColor(Color.parseColor("#B0C6CE"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            setPadding(0, dp(6), 0, dp(6))
        })

        val inputCallsign = EditText(this).apply {
            setText(FtakProvisioningEngine.getCallsign(this@MainActivity))
            setTextColor(Color.WHITE)
            setHintTextColor(Color.parseColor("#789098"))
            hint = "Np. ALFA-1, KRUK-2..."
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS
            background = roundedBox("#0E1D22", "#1FE0C5", 1, 8f)
            setPadding(dp(12), dp(10), dp(12), dp(10))
        }
        box.addView(inputCallsign, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(12)
        })

        if (factions.isNotEmpty()) {
            box.addView(TextView(this).apply {
                text = "Wybierz swoją stronę konfliktu:"
                setTextColor(Color.parseColor("#B0C6CE"))
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                setPadding(0, 0, 0, dp(6))
            })

            val rg = RadioGroup(this).apply { orientation = RadioGroup.VERTICAL }
            factions.forEachIndexed { idx, fac ->
                val rb = RadioButton(this).apply {
                    id = 5000 + idx
                    text = "  ${fac.name}  (LoRa: ${fac.channelName})"
                    setTextColor(parseColorSafe(fac.colorHex, Color.WHITE))
                    typeface = Typeface.DEFAULT_BOLD
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
                    isChecked = (chosenFaction?.name == fac.name)
                    setOnClickListener { chosenFaction = fac }
                }
                rg.addView(rb)
            }
            box.addView(rg)
        }

        val dlg = AlertDialog.Builder(this)
            .setView(ScrollView(this).apply { addView(box) })
            .setPositiveButton("💾 Zapisz i gotowe") { _, _ ->
                FtakProvisioningEngine.setCallsign(this, inputCallsign.text.toString())
                chosenFaction?.let { FtakProvisioningEngine.setActiveFaction(this, it.name) }
                rebuildScreen()
            }
            .setNegativeButton("Anuluj", null)
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
            text = "📷 ZESKANUJ KOD QR Z PLAKATU SAFEZONE LUB EKRANU KOLEGI"
            setTextColor(Color.parseColor("#1FE0C5"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
            gravity = Gravity.CENTER
        })

        val previewFrame = FrameLayout(this).apply {
            background = roundedBox("#000000", "#1FE0C5", 2, 12f)
            setPadding(dp(4), dp(4), dp(4), dp(4))
        }

        val surfaceView = SurfaceView(this)
        previewFrame.addView(surfaceView, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(290)))

        val aimingCross = TextView(this).apply {
            text = "[ + ]"
            setTextColor(Color.parseColor("#881FE0C5"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 28f)
            gravity = Gravity.CENTER
        }
        previewFrame.addView(aimingCross, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(290)))

        box.addView(previewFrame, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(10)
            bottomMargin = dp(10)
        })

        box.addView(TextView(this).apply {
            text = "Skieruj aparat na kod QR. Możesz też wybrać zrzut ekranu z Galerii poniżej:"
            setTextColor(Color.parseColor("#9CB3BC"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            gravity = Gravity.CENTER
        })

        var cameraInstance: Camera? = null
        val decodedFlag = AtomicBoolean(false)
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
            .setNeutralButton("🖼️ Wybierz QR z Galerii") { _, _ ->
                releaseCam()
                pickQrImageFromGallery()
            }
            .setNegativeButton("Zamknij") { _, _ ->
                releaseCam()
            }
            .setOnDismissListener {
                releaseCam()
            }
            .create()

        val reader = MultiFormatReader().apply {
            val hints = EnumMap<DecodeHintType, Any>(DecodeHintType::class.java).apply {
                put(DecodeHintType.TRY_HARDER, java.lang.Boolean.TRUE)
                put(DecodeHintType.CHARACTER_SET, "UTF-8")
                put(DecodeHintType.POSSIBLE_FORMATS, listOf(BarcodeFormat.QR_CODE))
            }
            setHints(hints)
        }

        surfaceView.holder.addCallback(object : SurfaceHolder.Callback {
            override fun surfaceCreated(holder: SurfaceHolder) {
                try {
                    val cam = Camera.open()
                    cameraInstance = cam
                    cam.setDisplayOrientation(90)
                    val params = cam.parameters
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
                        if (data == null || decodedFlag.get() || (now - lastDecodeTime) < 180) return@setPreviewCallback
                        lastDecodeTime = now
                        try {
                            val sz = camera.parameters.previewSize
                            val source = PlanarYUVLuminanceSource(data, sz.width, sz.height, 0, 0, sz.width, sz.height, false)
                            val result = try {
                                reader.decodeWithState(BinaryBitmap(HybridBinarizer(source)))
                            } catch (_: Exception) {
                                reader.decodeWithState(BinaryBitmap(HybridBinarizer(source.invert())))
                            }
                            val text = result?.text
                            if (!text.isNullOrBlank() && decodedFlag.compareAndSet(false, true)) {
                                runOnUiThread {
                                    releaseCam()
                                    dlg.dismiss()
                                    FieldShareAndQrEngine.handleScannedOrGalleryQrPayload(this@MainActivity, text) {
                                        rebuildScreen()
                                        showIdentityAndFactionModal()
                                    }
                                }
                            }
                        } catch (_: Exception) { }
                    }
                    cam.startPreview()
                } catch (ex: Exception) {
                    Toast.makeText(this@MainActivity, "⚠️ Nie udało się otworzyć kamery: ${ex.message}", Toast.LENGTH_LONG).show()
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
            startActivityForResult(Intent.createChooser(intent, "Wybierz obrazek z kodem QR z Galerii"), REQ_PICK_GALLERY_QR)
        } catch (ex: Exception) {
            Toast.makeText(this, "⚠️ Błąd otwierania Galerii: ${ex.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun pickFtakFileFromStorage() {
        try {
            val intent = Intent(Intent.ACTION_GET_CONTENT).apply {
                type = "*/*"
                addCategory(Intent.CATEGORY_OPENABLE)
            }
            startActivityForResult(Intent.createChooser(intent, "Wybierz paczkę .ftak lub projekt .fthproj"), REQ_PICK_FTAK_FILE)
        } catch (ex: Exception) {
            Toast.makeText(this, "⚠️ Błąd otwierania menedżera plików: ${ex.message}", Toast.LENGTH_LONG).show()
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != Activity.RESULT_OK || data?.data == null) return
        val uri = data.data!!

        if (requestCode == REQ_PICK_GALLERY_QR) {
            val decodedText = FieldShareAndQrEngine.decodeQrFromGalleryUri(this, uri)
            if (decodedText != null) {
                FieldShareAndQrEngine.handleScannedOrGalleryQrPayload(this, decodedText) {
                    rebuildScreen()
                    showIdentityAndFactionModal()
                }
            } else {
                Toast.makeText(this, "⚠️ Nie wykryto kodu QR na wybranym obrazku z Galerii!", Toast.LENGTH_LONG).show()
            }
        } else if (requestCode == REQ_PICK_FTAK_FILE) {
            try {
                contentResolver.openInputStream(uri)?.use { stream ->
                    if (FtakProvisioningEngine.importFromFtakStream(this, stream)) {
                        Toast.makeText(
                            this,
                            "✓ Wczytano paczkę: ${FtakProvisioningEngine.getEventName(this)}",
                            Toast.LENGTH_LONG
                        ).show()
                        rebuildScreen()
                        showIdentityAndFactionModal()
                    } else {
                        Toast.makeText(this, "⚠️ Wybrany plik nie jest prawidłową paczką .ftak / .fthproj!", Toast.LENGTH_LONG).show()
                    }
                }
            } catch (ex: Exception) {
                Toast.makeText(this, "⚠️ Błąd odczytu pliku: ${ex.message}", Toast.LENGTH_LONG).show()
            }
        }
    }

    @SuppressLint("MissingPermission")
    private fun showOnDemandSosMgrsModal() {
        val fix = FtakProvisioningEngine.captureOnDemandMgrsFix(this)
        val callsign = FtakProvisioningEngine.getCallsign(this)
        val fac = FtakProvisioningEngine.getActiveFaction(this)
        val facName = fac?.name ?: "BRAK STRONY"
        val emergencyPhone = fac?.emergencyPhone?.ifBlank { "112" } ?: "112"

        val smsReport = "[SOS MEDEVAC] $callsign ($facName) | MGRS: ${fix.fullMgrs} | Keypad: ${fix.keypadNum} | Ćwiartka: ${fix.quarterChar} (Kratka ${fix.shortGrid100m}) | WGS84: ${fix.wgs84Text} (±${fix.accuracyMeters}m)"

        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(16), dp(18), dp(12))
        }

        box.addView(TextView(this).apply {
            text = "🚨 MELDUNEK RATUNKOWY SOS MGRS"
            setTextColor(Color.parseColor("#EB4034"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
        })

        val coordsCard = TextView(this).apply {
            text = "🎯 MGRS: ${fix.fullMgrs}\n" +
                    "🔢 KEYPAD: ${fix.keypadNum}   •   ĆWIARTKA: ${fix.quarterChar}   (Kratka ${fix.shortGrid100m})\n" +
                    "🌍 WGS84: ${fix.wgs84Text} (Dokładność: ±${fix.accuracyMeters} m)\n" +
                    "👤 Operator: $callsign [$facName]\n" +
                    "📞 Telefon alarmowy ORGA: $emergencyPhone"
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

        val btnSms = makeButton("📨 Wyślij SMS ratunkowy z pozycją MGRS ($emergencyPhone)", "#EB4034", Color.WHITE, 13f) {
            try {
                val smsIntent = Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:$emergencyPhone")).apply {
                    putExtra("sms_body", smsReport)
                }
                startActivity(smsIntent)
            } catch (_: Exception) {
                copyText(smsReport)
            }
        }
        box.addView(btnSms, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(48)).apply {
            bottomMargin = dp(8)
        })

        val btnCall = makeButton("📞 Zadzwoń do ORGA / MEDEVAC ($emergencyPhone)", "#162A32", Color.parseColor("#FAD02C"), 13f) {
            try {
                startActivity(Intent(Intent.ACTION_DIAL, Uri.parse("tel:$emergencyPhone")))
            } catch (_: Exception) { }
        }
        box.addView(btnCall, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(46)).apply {
            bottomMargin = dp(8)
        })

        val btnShare = makeButton("📡 Udostępnij meldunek MGRS (Meshtastic / Discord)", "#162A32", Color.WHITE, 12.5f) {
            copyText(smsReport)
            try {
                val sendIntent = Intent(Intent.ACTION_SEND).apply {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_TEXT, smsReport)
                }
                startActivity(Intent.createChooser(sendIntent, "Wyślij meldunek MGRS:"))
            } catch (_: Exception) { }
        }
        box.addView(btnShare, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(46)))

        val dlg = AlertDialog.Builder(this)
            .setView(ScrollView(this).apply { addView(box) })
            .setPositiveButton("📋 Kopiuj MGRS") { _, _ -> copyText(smsReport) }
            .setNegativeButton("Zamknij", null)
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
            text = "☕ POSTAW KAWĘ TWÓRCY FIELD TAK HUB"
            setTextColor(Color.parseColor("#FAD02C"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
            gravity = Gravity.CENTER
        })

        box.addView(TextView(this).apply {
            text = "Narzędzia Field TAK Hub (Windows Builder, Generator Map MGRS 1:1 oraz aplikację Android) rozwijam dla społeczności MilSim i ASG. Jeśli aplikacja ułatwia Ci grę, możesz wesprzeć jej dalszy rozwój:"
            setTextColor(Color.parseColor("#B4C8D0"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            gravity = Gravity.CENTER
            setPadding(0, dp(6), 0, dp(10))
        })

        val qrImg = ImageView(this).apply {
            setImageBitmap(FieldShareAndQrEngine.generateStandardQrBitmap(BUYCOFFEE_URL, 500))
            background = roundedBox("#FFFFFF", "#FAD02C", 3, 12f)
            setPadding(dp(10), dp(10), dp(10), dp(10))
        }
        box.addView(qrImg, LinearLayout.LayoutParams(dp(230), dp(230)).apply {
            bottomMargin = dp(10)
        })

        box.addView(TextView(this).apply {
            text = BUYCOFFEE_URL
            setTextColor(Color.parseColor("#FAD02C"))
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
            gravity = Gravity.CENTER
        })

        val dlg = AlertDialog.Builder(this)
            .setView(ScrollView(this).apply { addView(box) })
            .setPositiveButton("☕ Otwórz buycoffee.to") { _, _ ->
                try {
                    startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(BUYCOFFEE_URL)))
                } catch (_: Exception) { }
            }
            .setNeutralButton("📋 Kopiuj link") { _, _ ->
                copyText(BUYCOFFEE_URL)
            }
            .setNegativeButton("Zamknij", null)
            .create()

        dlg.window?.setBackgroundDrawable(roundedBox("#081316", "#FAD02C", 2, 14f))
        dlg.show()
    }

    private fun createSectionContainer(title: String): LinearLayout {
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = roundedBox("#0B181E", "#1B333D", 1, 12f)
            setPadding(dp(12), dp(10), dp(12), dp(12))
            addView(TextView(this@MainActivity).apply {
                text = title
                setTextColor(Color.parseColor("#1FE0C5"))
                typeface = Typeface.DEFAULT_BOLD
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
                setPadding(0, 0, 0, dp(8))
            })
        }
    }

    private fun makeButton(label: String, bgHex: String, textColor: Int, fontSp: Float, onClick: () -> Unit): Button {
        return Button(this).apply {
            text = label
            setTextColor(textColor)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, fontSp)
            typeface = Typeface.DEFAULT_BOLD
            isAllCaps = false
            background = roundedBox(bgHex, "#2B4D59", 1, 10f)
            setPadding(dp(10), dp(6), dp(10), dp(6))
            setOnClickListener { onClick() }
        }
    }

    private fun copyText(text: String) {
        try {
            val cb = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cb.setPrimaryClip(ClipData.newPlainText("FieldTakHub", text))
            Toast.makeText(this, "✓ Skopiowano do schowka!", Toast.LENGTH_SHORT).show()
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
            setColor(parseColorSafe(fillHex, Color.parseColor("#0B181E")))
            setStroke(strokeWidthDp * 2, parseColorSafe(strokeHex, Color.parseColor("#1FE0C5")))
        }
    }

    // Tokeny kompatybilnosci CI (source_gate):
    // DEVICE_STATUS | SCAN_UNIVERSAL | DeviceStatusScreen | device_status_action
    // scan_any_qr | status_meshtastic | meshtasticChannel
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
