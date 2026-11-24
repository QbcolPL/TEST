package com.fieldtakhub.hub

import android.app.Application
import android.content.Intent
import android.location.Location
import android.net.Network
import android.net.Uri
import android.net.wifi.WifiManager
import android.provider.OpenableColumns
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.fieldtakhub.hub.core.AppRole
import com.fieldtakhub.hub.core.Atak
import com.fieldtakhub.hub.core.AtakSetting
import com.fieldtakhub.hub.core.Callsign
import com.fieldtakhub.hub.core.Codec
import com.fieldtakhub.hub.core.FtakException
import com.fieldtakhub.hub.core.FtakParser
import com.fieldtakhub.hub.core.HubPackage
import com.fieldtakhub.hub.core.Hex
import com.fieldtakhub.hub.core.Iso
import com.fieldtakhub.hub.core.MeshChannel
import com.fieldtakhub.hub.core.Mgrs
import com.fieldtakhub.hub.core.PackageKind
import com.fieldtakhub.hub.core.ProvisionDescriptor
import com.fieldtakhub.hub.core.QrParser
import com.fieldtakhub.hub.core.QrPayload
import com.fieldtakhub.hub.core.SignatureState
import com.fieldtakhub.hub.core.Versions
import com.fieldtakhub.hub.core.WifiCredentials
import com.fieldtakhub.hub.core.humanBytes
import com.fieldtakhub.hub.data.ApkInfo
import com.fieldtakhub.hub.data.HubPrefs
import com.fieldtakhub.hub.data.PackageStore
import com.fieldtakhub.hub.data.StoredPackage
import com.fieldtakhub.hub.platform.AppInspector
import com.fieldtakhub.hub.platform.ApkInstaller
import com.fieldtakhub.hub.platform.Downloader
import com.fieldtakhub.hub.platform.HotspotController
import com.fieldtakhub.hub.platform.InstallEvent
import com.fieldtakhub.hub.platform.InstalledApp
import com.fieldtakhub.hub.platform.Intents
import com.fieldtakhub.hub.platform.LocalServer
import com.fieldtakhub.hub.platform.LocationTracker
import com.fieldtakhub.hub.platform.TransferService
import com.fieldtakhub.hub.platform.WifiJoiner
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.net.InetSocketAddress
import java.net.Socket
import java.security.DigestInputStream
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class HubViewModel(app: Application) : AndroidViewModel(app) {
    private val ctx: Application get() = getApplication()
    private val prefs = HubPrefs(app)
    private val store = PackageStore(app)
    private val hotspot = HotspotController(app)
    private val joiner = WifiJoiner(app)
    val location = LocationTracker(app)

    private val _state = MutableStateFlow(
        HubState(
            onboardingDone = prefs.onboardingDone,
            callsign = prefs.callsign,
            shortName = prefs.shortName,
            shortManual = prefs.shortManual
        )
    )
    val state: StateFlow<HubState> = _state.asStateFlow()

    private val commandChannel = Channel<UiCommand>(Channel.BUFFERED)
    val commands: Flow<UiCommand> = commandChannel.receiveAsFlow()

    private var importJob: Job? = null
    private var statusJob: Job? = null
    private var shareJob: Job? = null
    private var lastScan: Pair<String, Long>? = null
    private var pendingConfirm: CompletableDeferred<Boolean>? = null

    init {
        reload()
        viewModelScope.launch {
            ApkInstaller.events.receiveAsFlow().collect { onInstallEvent(it) }
        }
        viewModelScope.launch {
            LocalServer.downloads.collect { counts ->
                _state.update { s ->
                    val share = s.share ?: return@update s
                    s.copy(share = share.copy(downloads = share.token?.let { counts[it] } ?: share.downloads))
                }
            }
        }
    }

    // ------------------------------------------------------------------ general

    fun completeOnboarding() {
        prefs.onboardingDone = true
        _state.update { it.copy(onboardingDone = true, showPermissions = false) }
    }

    fun showPermissions(show: Boolean) = _state.update { it.copy(showPermissions = show) }

    fun selectTab(tab: Tab) {
        if (tab == Tab.OPORD && _state.value.opordSource == null) {
            notify(
                "OPORD jeszcze niedostępny",
                "Rozkaz pojawi się po imporcie paczki, która go zawiera, albo pliku OPORD (w Builderze: kod „OPORD (LAN)” lub plik opord.json)."
            )
            return
        }
        _state.update { it.copy(tab = tab) }
    }

    fun notify(title: String, text: String, error: Boolean = false, actions: List<NoticeAction> = emptyList()) {
        _state.update { it.copy(notice = Notice(title, text, error, actions)) }
    }

    fun dismissNotice() {
        pendingConfirm?.complete(false)
        pendingConfirm = null
        _state.update { it.copy(notice = null) }
    }

    fun startIntent(intent: Intent, fallback: Intent? = null, failMessage: String? = null) {
        commandChannel.trySend(UiCommand.Launch(intent, fallback, failMessage))
    }

    private suspend fun confirm(title: String, text: String, yes: String, no: String = "Anuluj"): Boolean {
        val deferred = CompletableDeferred<Boolean>()
        pendingConfirm = deferred
        _state.update {
            it.copy(
                notice = Notice(
                    title, text, error = true,
                    actions = listOf(
                        NoticeAction(no) { deferred.complete(false); pendingConfirm = null; _state.update { s -> s.copy(notice = null) } },
                        NoticeAction(yes, primary = true) { deferred.complete(true); pendingConfirm = null; _state.update { s -> s.copy(notice = null) } }
                    )
                )
            )
        }
        return deferred.await()
    }

    fun reload() {
        viewModelScope.launch {
            val list = withContext(Dispatchers.IO) { store.list() }
            var active = prefs.activeId?.takeIf { id -> list.any { it.id == id && it.pkg.hasDeployment } }
            if (active == null) active = list.firstOrNull { it.pkg.hasDeployment }?.id
            var opord = prefs.opordId?.takeIf { id -> list.any { it.id == id && it.pkg.hasOpord } }
            if (opord == null) opord = list.firstOrNull { it.id == active && it.pkg.hasOpord }?.id
                ?: list.firstOrNull { it.pkg.hasOpord }?.id
            prefs.activeId = active
            prefs.opordId = opord
            _state.update { s ->
                val tab = if (s.tab == Tab.OPORD && opord == null) Tab.SCANNER else s.tab
                s.copy(loaded = true, packages = list, activeId = active, opordId = opord, tab = tab)
            }
            refreshStatus()
        }
    }

    // ------------------------------------------------------------------ status of apps and server

    fun refreshStatus() {
        statusJob?.cancel()
        statusJob = viewModelScope.launch {
            val active = _state.value.active
            val checks = withContext(Dispatchers.IO) { computeChecks(active) }
            _state.update { it.copy(apps = checks) }
            val server = active?.pkg?.server
            if (server == null) {
                _state.update { it.copy(server = ServerState.NONE) }
                return@launch
            }
            _state.update { it.copy(server = ServerState.CHECKING) }
            val ok = Downloader.reachable(server.host, server.cotPort)
            _state.update { it.copy(server = if (ok) ServerState.REACHABLE else ServerState.UNREACHABLE) }
        }
    }

    private fun computeChecks(active: StoredPackage?): List<AppCheck> {
        if (active == null) {
            val atak = AppInspector.findAtak(ctx)
            val mesh = AppInspector.installed(ctx, Intents.MESHTASTIC)
            return listOf(
                baseline(AppRole.ATAK, "ATAK-CIV", Atak.CIV_PACKAGE, atak),
                baseline(AppRole.MESHTASTIC, "Meshtastic", Intents.MESHTASTIC, mesh)
            )
        }
        return active.pkg.requirements.map { req ->
            val apk = active.apkFor(req.bundledEntry)
            val name = apk?.label?.takeIf { it.isNotBlank() && req.role == AppRole.PLUGIN } ?: req.displayName
            val trustedName = apk?.packageName ?: req.packageName?.takeIf { req.packageNameReliable || req.role != AppRole.PLUGIN }
            val installed = when {
                req.role == AppRole.ATAK && apk == null -> AppInspector.findAtak(ctx)
                trustedName != null -> AppInspector.installed(ctx, trustedName)
                else -> null
            }
            val requirement = req.requirement
            when {
                installed == null -> AppCheck(
                    req, apk, null, CheckState.MISSING, name,
                    when {
                        apk != null -> "Brak na telefonie — paczka zawiera ${apk.versionName}"
                        req.role == AppRole.PLUGIN && trustedName == null -> "Nie można sprawdzić (nieznana nazwa pakietu)"
                        requirement != null -> "Brak na telefonie (wymagana ${requirement.describe()})"
                        else -> "Brak na telefonie"
                    }
                )
                apk != null && installed.versionCode < apk.versionCode -> AppCheck(
                    req, apk, installed, CheckState.OUTDATED, name,
                    "Zainstalowana ${installed.versionName}, w paczce ${apk.versionName}"
                )
                apk == null && requirement != null &&
                    Versions.parse(installed.versionName)?.let { !requirement.isSatisfiedBy(it) } == true -> AppCheck(
                    req, null, installed, CheckState.OUTDATED, name,
                    "Zainstalowana ${installed.versionName}, wymagana ${requirement.describe()}"
                )
                else -> AppCheck(req, apk, installed, CheckState.OK, name, "Wersja ${installed.versionName}")
            }
        }
    }

    private fun baseline(role: AppRole, name: String, pkg: String, installed: InstalledApp?): AppCheck {
        val req = com.fieldtakhub.hub.core.AppRequirement(role, name, pkg, true, null, null, 0)
        return if (installed != null) AppCheck(req, null, installed, CheckState.OK, name, "Wersja ${installed.versionName}")
        else AppCheck(req, null, null, CheckState.MISSING, name, "Brak na telefonie")
    }

    // ------------------------------------------------------------------ callsign

    fun setCallsign(value: String) {
        val clean = value.take(40)
        val short = if (_state.value.shortManual) _state.value.shortName else Callsign.suggest(clean)
        prefs.callsign = clean
        prefs.shortName = short
        _state.update { it.copy(callsign = clean, shortName = short) }
    }

    fun setShortName(value: String) {
        val clean = Callsign.cleanShort(value)
        prefs.shortName = clean
        prefs.shortManual = clean.isNotEmpty()
        _state.update { it.copy(shortName = clean, shortManual = clean.isNotEmpty()) }
    }

    fun resetShortName() {
        val short = Callsign.suggest(_state.value.callsign)
        prefs.shortManual = false
        prefs.shortName = short
        _state.update { it.copy(shortName = short, shortManual = false) }
    }

    // ------------------------------------------------------------------ importing

    fun onQrScanned(raw: String) {
        val now = System.currentTimeMillis()
        val last = lastScan
        if (last != null && last.first == raw && now - last.second < 5000) return
        if (_state.value.overlayOpen) return
        lastScan = raw to now
        handlePayload(QrParser.parse(raw))
    }

    fun handleLink(text: String) {
        lastScan = text to System.currentTimeMillis()
        handlePayload(QrParser.parse(text))
    }

    fun cancelImport() {
        importJob?.cancel()
    }

    private fun handlePayload(p: QrPayload) {
        when (p) {
            is QrPayload.Descriptor -> runImport("Pobieranie opisu paczki") {
                val text = Downloader.fetchText(p.url)
                importDownload(ProvisionDescriptor.parse(text), null)
            }
            is QrPayload.Download ->
                if (p.wifi != null) startWifiImport(p) else runImport("Pobieranie paczki") { importDownload(p, null) }
            is QrPayload.OpordLink -> runImport("Pobieranie rozkazu OPORD") {
                importJson(Downloader.fetchText(p.jsonUrl), "Builder LAN — OPORD")
            }
            is QrPayload.OfflineConfig -> runImport("Import ustawień z kodu QR") { importJson(p.json, "Kod QR offline") }
            is QrPayload.LegacyPoster -> handleLegacyPoster(p)
            is QrPayload.AtakEnroll -> {
                val atak = AppInspector.findAtak(ctx)
                notify(
                    "Link rejestracji w serwerze TAK",
                    "Kod rejestruje ATAK na serwerze ${Atak.enrollHost(p.uri).orEmpty()}. Otworzyć go w ATAK?",
                    actions = listOf(
                        NoticeAction("Zamknij") { dismissNotice() },
                        NoticeAction("Otwórz w ATAK", primary = true) {
                            dismissNotice()
                            startIntent(Intents.view(p.uri, atak?.packageName), Intents.view(p.uri), "ATAK nie jest zainstalowany.")
                        }
                    )
                )
            }
            is QrPayload.MeshtasticChannel -> {
                val set = MeshChannel.decodeUrl(p.url)
                val ch = set?.primary
                val lora = set?.lora
                val text = buildString {
                    append("Kanał: ").append(ch?.name?.ifBlank { "(domyślny)" } ?: "?")
                    lora?.let { append("\nRadio: ").append(MeshChannel.REGIONS[it.region] ?: it.region).append(" · ").append(MeshChannel.PRESETS[it.modemPreset] ?: it.modemPreset) }
                    ch?.let { append("\nKlucz: ").append(MeshChannel.describePsk(it.psk)) }
                    append("\n\nMeshtastic poprosi o potwierdzenie zmiany kanałów.")
                }
                notify(
                    "Kanał Meshtastic", text,
                    actions = listOf(
                        NoticeAction("Zamknij") { dismissNotice() },
                        NoticeAction("Otwórz w Meshtastic", primary = true) {
                            dismissNotice()
                            startIntent(Intents.view(p.url, Intents.MESHTASTIC), Intents.view(p.url), "Meshtastic nie jest zainstalowany.")
                        }
                    )
                )
            }
            is QrPayload.Unsupported -> notify("Nieznany kod", p.reason + "\n\n" + p.text.take(200), error = true)
        }
    }

    fun importFromUri(uri: Uri) {
        runImport("Wczytywanie pliku") {
            val resolver = ctx.contentResolver
            val name = withContext(Dispatchers.IO) {
                runCatching {
                    resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
                        if (c.moveToFirst()) c.getString(0) else null
                    }
                }.getOrNull()
            } ?: uri.lastPathSegment?.substringAfterLast('/') ?: "paczka.ftak"
            val dest = incomingFile()
            val sha = withContext(Dispatchers.IO) {
                val md = MessageDigest.getInstance("SHA-256")
                val input = resolver.openInputStream(uri) ?: throw FtakException("Nie można otworzyć pliku $name.")
                DigestInputStream(input, md).use { din -> dest.outputStream().use { din.copyTo(it, 256 * 1024) } }
                Hex.encode(md.digest())
            }
            finishImport(dest, sha, "Plik: $name", name)
        }
    }

    private fun runImport(title: String, block: suspend () -> Unit) {
        importJob?.cancel()
        importJob = viewModelScope.launch {
            _state.update { it.copy(busy = Busy(title)) }
            try {
                block()
            } catch (e: CancellationException) {
                throw e
            } catch (e: FtakException) {
                notify("Import nie powiódł się", e.message.orEmpty(), error = true)
            } catch (e: Exception) {
                notify("Import nie powiódł się", e.message ?: e.javaClass.simpleName, error = true)
            } finally {
                _state.update { it.copy(busy = null) }
                joiner.release()
            }
        }
    }

    private fun busy(title: String, detail: String = "", progress: Float? = null) {
        _state.update { it.copy(busy = Busy(title, detail, progress)) }
    }

    private fun incomingFile(): File = File(ctx.cacheDir, "incoming").apply { mkdirs() }.let { File(it, "in-${System.nanoTime()}.ftak") }

    private suspend fun importDownload(d: QrPayload.Download, network: Network?) {
        d.expiresUtc?.let { Iso.parseMillis(it) }?.let { if (it < System.currentTimeMillis()) throw FtakException("Kod QR wygasł. Poproś organizatora o nowy.") }
        val host = runCatching { Uri.parse(d.url).host }.getOrNull().orEmpty()
        busy("Pobieranie paczki", d.label ?: host, null)
        val dest = incomingFile()
        val res = Downloader.download(d.url, dest, network, d.sha256, d.bytes) { done, total ->
            val p = if (total != null && total > 0) (done.toFloat() / total).coerceIn(0f, 1f) else null
            busy("Pobieranie paczki", "${humanBytes(done)}${total?.let { " z " + humanBytes(it) } ?: ""} · ${d.label ?: host}", p)
        }
        val source = when {
            d.wifi != null -> "Hotspot ${d.wifi.ssid}"
            host.contains("google") -> "Dysk Google"
            com.fieldtakhub.hub.core.NetRules.isPrivateHost(host) -> "Sieć lokalna ($host)"
            else -> "Internet ($host)"
        }
        finishImport(res.file, res.sha256, source + if (d.sha256 != null) " · suma zgodna z QR" else "", res.fileName ?: d.label)
    }

    private suspend fun finishImport(file: File, sha: String, source: String, fileName: String?) {
        val first = withContext(Dispatchers.IO) { file.inputStream().use { s -> var c: Int; do { c = s.read() } while (c == ' '.code || c == '\n'.code || c == '\r'.code || c == '\t'.code || c == 0xEF || c == 0xBB || c == 0xBF); c } }
        if (first == '{'.code) {
            val text = withContext(Dispatchers.IO) { file.readText(Charsets.UTF_8) }
            file.delete()
            importJson(text, source)
            return
        }
        busy("Sprawdzanie paczki", "Weryfikacja podpisu i zawartości")
        val pkg = withContext(Dispatchers.IO) { FtakParser.inspect(file, verify = true) }
        if (pkg.signature.state == SignatureState.INVALID) {
            file.delete()
            throw FtakException("Paczka odrzucona: ${pkg.signature.detail}")
        }
        val fingerprint = pkg.signature.fingerprint
        if (pkg.kind == PackageKind.FTAK_V2 && fingerprint != null) {
            val known = prefs.publisherFor(pkg.packageId)
            if (known != null && !known.equals(fingerprint, ignoreCase = true)) {
                _state.update { it.copy(busy = null) }
                val ok = confirm(
                    "Inny wydawca paczki!",
                    "Paczkę „${pkg.title}” podpisał inny klucz niż poprzednio.\n\nPoprzedni: ${known.take(16)}\nTeraz: ${fingerprint.take(16)}\n\nImportuj tylko, jeśli organizator potwierdził zmianę klucza.",
                    "Importuj mimo to"
                )
                if (!ok) {
                    file.delete()
                    return
                }
            }
        }
        val existing = _state.value.packages.firstOrNull { it.sha256.equals(sha, ignoreCase = true) && it.archive != null }
        if (existing != null) {
            file.delete()
            activate(existing.id)
            selectTabFor(existing.pkg)
            notify("Paczka już jest w telefonie", "„${existing.pkg.title}” była już zaimportowana — ustawiono ją jako aktywną.")
            return
        }
        busy("Analiza aplikacji w paczce", "Odczyt wersji ATAK, wtyczek i Meshtastic")
        val apks = withContext(Dispatchers.IO) { readApkInfos(file, pkg) }
        val stored = withContext(Dispatchers.IO) { store.storeArchive(file, pkg, sha, source, fileName, apks) }
        if (fingerprint != null) prefs.rememberPublisher(pkg.packageId, fingerprint)
        afterImport(stored)
    }

    private suspend fun importJson(text: String, source: String) {
        val pkg = withContext(Dispatchers.Default) { FtakParser.fromJsonText(text) }
        val sha = Hex.encode(Codec.sha256(text.toByteArray(Charsets.UTF_8)))
        val stored = withContext(Dispatchers.IO) { store.storeJson(text, pkg, sha, source) }
        afterImport(stored)
    }

    private fun readApkInfos(file: File, pkg: HubPackage): List<ApkInfo> {
        val entries = pkg.requirements.mapNotNull { it.bundledEntry }.distinct()
        if (entries.isEmpty()) return emptyList()
        val probe = File(ctx.cacheDir, "apk-probe").apply { mkdirs() }
        val out = ArrayList<ApkInfo>()
        com.fieldtakhub.hub.core.FtakArchive(file).use { archive ->
            for (entry in entries) {
                val tmp = File(probe, "probe.apk")
                try {
                    archive.open(entry)?.use { input -> tmp.outputStream().use { input.copyTo(it, 256 * 1024) } } ?: continue
                    AppInspector.archiveInfo(ctx, tmp, entry)?.let { out += it }
                } finally {
                    tmp.delete()
                }
            }
        }
        return out
    }

    private fun afterImport(stored: StoredPackage) {
        val pkg = stored.pkg
        if (pkg.hasDeployment) prefs.activeId = stored.id
        if (pkg.hasOpord) prefs.opordId = stored.id
        reload()
        selectTabFor(pkg)
        val lines = ArrayList<String>()
        lines += pkg.kind.label
        when (pkg.signature.state) {
            SignatureState.VERIFIED -> lines += "Podpis wydawcy: zgodny (${pkg.signature.shortFingerprint})"
            SignatureState.UNSIGNED -> lines += "Paczka bez podpisu cyfrowego — sprawdź, czy pochodzi od organizatora."
            SignatureState.INVALID -> lines += "Podpis: NIEPRAWIDŁOWY"
        }
        pkg.server?.let { lines += "Serwer: ${it.endpoint}" }
        pkg.mesh?.let { lines += "Meshtastic: ${it.channelName.ifBlank { "kanał z linku" }}" + listOfNotNull(it.region, it.modemPreset).joinToString(" · ", prefix = " · ").takeIf { s -> s.length > 3 }.orEmpty() }
        if (pkg.hasOpord) lines += "Rozkaz OPORD: ${pkg.opord?.days?.size ?: 0} dni"
        val bundled = stored.apks.size
        if (bundled > 0) lines += "Aplikacje w paczce: $bundled"
        if (pkg.isExpired()) lines += "UWAGA: ważność paczki minęła."
        lines += pkg.notes
        val next = if (pkg.hasDeployment) "\n\nDalej: wpisz callsign i naciśnij „Wstrzyknij do ATAK + Meshtastic”." else ""
        notify("Zaimportowano: ${pkg.title}", lines.joinToString("\n") + next)
    }

    private fun selectTabFor(pkg: HubPackage) {
        _state.update { it.copy(tab = if (pkg.hasDeployment) Tab.SCANNER else Tab.OPORD) }
    }

    private fun handleLegacyPoster(p: QrPayload.LegacyPoster) {
        val prefix = p.shaPrefix?.lowercase()?.takeIf { it.length >= 8 }
        val list = _state.value.packages
        val match = list.firstOrNull { prefix != null && it.sha256.lowercase().startsWith(prefix) }
            ?: list.firstOrNull { it.pkg.faction.equals(p.faction, true) && it.pkg.eventName.equals(p.event, true) }
        if (match != null) {
            activate(match.id)
            notify("Aktywowano paczkę", "Kod z plakatu wskazuje „${match.pkg.title}”, która jest już w telefonie.")
        } else {
            notify(
                "Kod bez adresu pobrania",
                "To kod z plakatu Buildera v2.4: zawiera tylko nazwę wydarzenia (${p.event.orEmpty()}), stronę (${p.faction.orEmpty()}) i fragment sumy kontrolnej — nie da się z niego pobrać paczki.\n\nWczytaj plik .ftak (PACZKI → Importuj plik) albo poproś organizatora o kod „Pełny .FTAK (Wi-Fi)” z aktualnego Buildera."
            )
        }
    }

    // ------------------------------------------------------------------ Wi-Fi hotspot download

    private fun startWifiImport(p: QrPayload.Download) {
        val creds = p.wifi ?: return
        if (!joiner.supported) {
            _state.update { it.copy(wifiPrompt = WifiPrompt(creds, p)) }
            return
        }
        runImport("Łączenie z hotspotem") {
            busy("Łączenie z hotspotem", "Zatwierdź połączenie z siecią ${creds.ssid} w okienku systemu")
            val network = joiner.join(creds)
                ?: throw FtakException("Nie połączono z siecią ${creds.ssid}. Zatwierdź połączenie w okienku systemu i zeskanuj kod ponownie.")
            importDownload(p.copy(url = reachableUrl(p.url, network)), network)
        }
    }

    /** The sharing phone's IP in the QR may be wrong on some devices; fall back to the hotspot gateway. */
    private suspend fun reachableUrl(url: String, network: Network?): String = withContext(Dispatchers.IO) {
        val uri = Uri.parse(url)
        val host = uri.host ?: return@withContext url
        val port = if (uri.port > 0) uri.port else 80
        fun ok(h: String): Boolean = try {
            Socket().use { s ->
                network?.bindSocket(s)
                s.connect(InetSocketAddress(h, port), 2500)
            }
            true
        } catch (e: Exception) {
            false
        }
        if (ok(host)) return@withContext url
        val gateway = network?.let { joiner.gatewayAddress(it) } ?: legacyGateway()
        if (gateway != null && gateway != host && ok(gateway)) url.replaceFirst("//$host", "//$gateway") else url
    }

    @Suppress("DEPRECATION")
    private fun legacyGateway(): String? {
        val wm = ctx.applicationContext.getSystemService(android.content.Context.WIFI_SERVICE) as WifiManager
        val ip = wm.dhcpInfo?.gateway ?: return null
        if (ip == 0) return null
        return "${ip and 0xFF}.${(ip shr 8) and 0xFF}.${(ip shr 16) and 0xFF}.${(ip shr 24) and 0xFF}"
    }

    fun wifiManualDone() {
        val prompt = _state.value.wifiPrompt ?: return
        _state.update { it.copy(wifiPrompt = null) }
        runImport("Pobieranie paczki") { importDownload(prompt.payload.copy(url = reachableUrl(prompt.payload.url, null)), null) }
    }

    fun dismissWifiPrompt() = _state.update { it.copy(wifiPrompt = null) }

    // ------------------------------------------------------------------ package manager

    fun activate(id: String) {
        val sp = _state.value.packages.firstOrNull { it.id == id } ?: return
        if (sp.pkg.hasDeployment) prefs.activeId = id
        if (sp.pkg.hasOpord) prefs.opordId = id
        _state.update { it.copy(activeId = prefs.activeId, opordId = prefs.opordId, injectDone = emptySet()) }
        refreshStatus()
    }

    fun askDelete(id: String) {
        val sp = _state.value.packages.firstOrNull { it.id == id } ?: return
        notify(
            "Usunąć paczkę?",
            "„${sp.pkg.title}” (${humanBytes(sp.sizeBytes)}) zostanie usunięta z telefonu. Ustawienia już wstrzyknięte do ATAK i Meshtastic zostają.",
            error = true,
            actions = listOf(
                NoticeAction("Anuluj") { dismissNotice() },
                NoticeAction("Usuń", primary = true) {
                    dismissNotice()
                    viewModelScope.launch {
                        withContext(Dispatchers.IO) { store.delete(id) }
                        if (prefs.activeId == id) prefs.activeId = null
                        if (prefs.opordId == id) prefs.opordId = null
                        _state.update { it.copy(detailsId = null) }
                        reload()
                    }
                }
            )
        )
    }

    fun showDetails(id: String?) = _state.update { it.copy(detailsId = id) }

    fun showDocument(open: Boolean) = _state.update { it.copy(documentOpen = open) }

    fun readOpordHtml(): String? {
        val sp = _state.value.opordSource ?: return null
        val entry = sp.pkg.opordHtmlEntry ?: return null
        return runCatching { store.readText(sp, entry) }.getOrNull()
    }

    fun shareViaApps(id: String) {
        val sp = _state.value.packages.firstOrNull { it.id == id } ?: return
        val file = sp.archive ?: File(sp.dir, StoredPackage.CONTENT)
        val exportDir = File(ctx.cacheDir, "export").apply { mkdirs() }
        val name = com.fieldtakhub.hub.core.safeName(sp.fileName?.substringBeforeLast('.') ?: sp.pkg.title) + if (sp.archive != null) ".ftak" else ".json"
        val copy = File(exportDir, name)
        viewModelScope.launch {
            withContext(Dispatchers.IO) { file.copyTo(copy, overwrite = true) }
            startIntent(Intents.shareFile(ctx, copy, if (sp.archive != null) "application/octet-stream" else "application/json", "Udostępnij paczkę"))
        }
    }

    // ------------------------------------------------------------------ phone-to-phone sharing

    fun startShare(id: String, viaHotspot: Boolean = true) {
        val sp = _state.value.packages.firstOrNull { it.id == id } ?: return
        val file = sp.archive ?: File(sp.dir, StoredPackage.CONTENT)
        stopShareInternal()
        _state.update { it.copy(share = ShareState(id, sp.pkg.title, SharePhase.STARTING, viaHotspot = viaHotspot)) }
        shareJob = viewModelScope.launch {
            val wifi: WifiCredentials?
            val ip: String?
            if (viaHotspot) {
                val result = hotspot.start()
                val info = result.getOrElse { err ->
                    _state.update { it.copy(share = it.share?.copy(phase = SharePhase.FAILED, error = err.message)) }
                    return@launch
                }
                wifi = WifiCredentials(info.ssid, info.password, info.security)
                ip = info.ip ?: HotspotController.hotspotAddress()
            } else {
                wifi = null
                ip = currentWifiAddress()
                if (ip == null) {
                    _state.update { it.copy(share = it.share?.copy(phase = SharePhase.FAILED, error = "Telefon nie jest połączony z żadną siecią Wi-Fi.")) }
                    return@launch
                }
            }
            val port = withContext(Dispatchers.IO) { LocalServer.ensureStarted() }
            val name = if (sp.archive != null) "paczka.ftak" else "paczka.json"
            val share = LocalServer.publish(file, name, "application/vnd.fieldtak.package", ttlMs = 3L * 3600 * 1000)
            TransferService.start(ctx, "Udostępnianie: ${sp.pkg.title}")
            val url = "http://${ip ?: "192.168.43.1"}:$port${share.path}"
            val qr = QrParser.buildShareLink(url, sp.sha256, sp.sizeBytes, sp.pkg.title, wifi)
            _state.update {
                it.copy(
                    share = it.share?.copy(
                        phase = SharePhase.READY, qrText = qr, ssid = wifi?.ssid, password = wifi?.password,
                        url = url, token = share.token
                    )
                )
            }
        }
    }

    fun stopShare() {
        stopShareInternal()
        _state.update { it.copy(share = null) }
    }

    private fun stopShareInternal() {
        shareJob?.cancel()
        _state.value.share?.token?.let { LocalServer.unpublish(it) }
        hotspot.stop()
    }

    private fun currentWifiAddress(): String? = try {
        java.net.NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isUp && !it.isLoopback && it.name.startsWith("wlan") }
            .flatMap { it.inetAddresses.toList() }
            .firstOrNull { it is java.net.Inet4Address && it.isSiteLocalAddress }?.hostAddress
    } catch (e: Exception) {
        null
    }

    // ------------------------------------------------------------------ injection into ATAK and Meshtastic

    fun openInject() {
        if (_state.value.active == null) {
            notify("Brak aktywnej paczki", "Najpierw zeskanuj kod QR paczki albo zaimportuj plik .ftak.")
            return
        }
        refreshStatus()
        _state.update { it.copy(injectOpen = true) }
    }

    fun closeInject() = _state.update { it.copy(injectOpen = false) }

    private fun markDone(key: String) = _state.update { it.copy(injectDone = it.injectDone + key) }

    fun installApp(check: AppCheck) {
        val active = _state.value.active ?: return
        val entry = check.requirement.bundledEntry
        if (entry == null) {
            val pkg = check.packageName ?: return
            startIntent(AppInspector.storeIntent(pkg), AppInspector.storeWebIntent(pkg), "Brak sklepu z aplikacjami na tym telefonie.")
            return
        }
        if (!AppInspector.canInstallPackages(ctx)) {
            notify(
                "Potrzebna zgoda na instalację",
                "Włącz „Zezwalaj z tego źródła” dla Field TAK Hub, wróć i naciśnij „Zainstaluj” ponownie.",
                actions = listOf(
                    NoticeAction("Później") { dismissNotice() },
                    NoticeAction("Otwórz ustawienia", primary = true) {
                        dismissNotice()
                        startIntent(AppInspector.installPermissionIntent(ctx))
                    }
                )
            )
            return
        }
        viewModelScope.launch {
            busy("Przygotowanie instalacji", check.name)
            try {
                withContext(Dispatchers.IO) {
                    val input = store.openEntry(active, entry) ?: throw FtakException("Brak pliku $entry w paczce.")
                    val size = runCatching { com.fieldtakhub.hub.core.FtakArchive(active.archive!!).use { it.size(entry) } }.getOrDefault(-1L)
                    ApkInstaller.install(ctx, input, size, check.apk?.packageName)
                }
            } catch (e: Exception) {
                notify("Instalacja nie powiodła się", e.message ?: e.javaClass.simpleName, error = true)
            } finally {
                _state.update { it.copy(busy = null) }
            }
        }
    }

    private fun onInstallEvent(e: InstallEvent) {
        when (e) {
            is InstallEvent.NeedsUser -> startIntent(e.intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            is InstallEvent.Finished -> {
                if (!e.success) notify("Instalacja nie powiodła się", e.message, error = true)
                refreshStatus()
            }
        }
    }

    fun atakSettingsPreview(): List<AtakSetting> {
        val s = _state.value
        val pkg = s.active?.pkg ?: return emptyList()
        return Atak.identitySettings(s.callsign.trim(), pkg.factionColor) + pkg.atakSettings
    }

    fun injectAtakSettings() {
        val s = _state.value
        if (s.callsign.isBlank()) {
            notify("Brak callsign", "Wpisz swój znak wywoławczy na ekranie SKANER.")
            return
        }
        val atak = AppInspector.findAtak(ctx)
        if (atak == null) {
            notify("Brak ATAK", "Najpierw zainstaluj ATAK-CIV.", error = true)
            return
        }
        val version = Versions.parse(atak.versionName)
        val settings = atakSettingsPreview()
        if (version != null && version < Atak.DEEP_LINK_MIN) {
            // Older ATAK: the same settings as a preference file in a data package,
            // handed over through the local server (suspending), so it runs in a coroutine.
            viewModelScope.launch {
                try {
                    val bytes = Atak.preferenceDataPackage(settings, "Field TAK Hub — ustawienia", atak.packageName)
                    sendToAtak(bytes, "fieldtakhub-ustawienia.zip", atak)
                    markDone("atak_identity")
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    notify("Nie wysłano do ATAK", e.message ?: e.javaClass.simpleName, error = true)
                }
            }
            return
        }
        startIntent(Intents.view(Atak.preferenceUri(settings), atak.packageName), null, "ATAK nie obsłużył linku ustawień.")
        markDone("atak_identity")
    }

    fun injectEnrollment() {
        val pkg = _state.value.active?.pkg ?: return
        val uri = pkg.enrollmentUri ?: return
        val atak = AppInspector.findAtak(ctx)
        startIntent(Intents.view(uri, atak?.packageName), Intents.view(uri), "ATAK nie jest zainstalowany.")
        markDone("atak_enroll")
    }

    /** [entry] = file inside the package, or null for a server connection generated from the package. */
    fun injectAtakFile(entry: String?, packageId: String? = null) {
        val active = packageId?.let { id -> _state.value.packages.firstOrNull { it.id == id } } ?: _state.value.active ?: return
        val atak = AppInspector.findAtak(ctx)
        if (atak == null) {
            notify("Brak ATAK", "Najpierw zainstaluj ATAK-CIV.", error = true)
            return
        }
        viewModelScope.launch {
            try {
                if (entry == null) {
                    val server = active.pkg.server ?: return@launch
                    val bytes = Atak.serverDataPackage(server, "Serwer ${server.name}")
                    sendToAtak(bytes, "fieldtakhub-serwer.zip", atak)
                    markDone("import:server")
                } else {
                    val name = entry.substringAfterLast('/')
                    val tmp = File(File(ctx.cacheDir, "atak").apply { mkdirs() }, "${System.nanoTime()}-$name")
                    withContext(Dispatchers.IO) { store.extractEntry(active, entry, tmp) } ?: throw FtakException("Brak pliku $entry w paczce.")
                    publishForAtak(tmp, name, atak)
                    markDone("import:$entry")
                }
            } catch (e: Exception) {
                notify("Nie wysłano do ATAK", e.message ?: e.javaClass.simpleName, error = true)
            }
        }
    }

    private suspend fun sendToAtak(bytes: ByteArray, name: String, atak: InstalledApp) {
        val tmp = File(File(ctx.cacheDir, "atak").apply { mkdirs() }, "${System.nanoTime()}-$name")
        withContext(Dispatchers.IO) { tmp.writeBytes(bytes) }
        publishForAtak(tmp, name, atak)
    }

    private suspend fun publishForAtak(file: File, name: String, atak: InstalledApp) {
        val port = withContext(Dispatchers.IO) { LocalServer.ensureStarted() }
        val share = LocalServer.publish(file, name, "application/zip", ttlMs = 15L * 60 * 1000, maxDownloads = 3, deleteAfter = true)
        TransferService.start(ctx, "Przekazywanie do ATAK: $name")
        val url = "http://127.0.0.1:$port${share.path}"
        startIntent(Intents.view(Atak.importUri(url), atak.packageName), null, "ATAK nie obsłużył importu.")
    }

    fun injectMeshChannel() {
        val mesh = _state.value.active?.pkg?.mesh
        val url = mesh?.effectiveUrl()
        if (url == null) {
            notify("Brak kanału", "Paczka nie zawiera ustawień kanału Meshtastic.")
            return
        }
        startIntent(Intents.view(url, Intents.MESHTASTIC), Intents.view(url), "Meshtastic nie jest zainstalowany.")
        markDone("mesh_channel")
    }

    fun injectMeshOwner() {
        val s = _state.value
        val long = Callsign.longName(s.callsign)
        if (long.isBlank() || s.shortName.isBlank()) {
            notify("Brak callsign", "Wpisz callsign i skrót na ekranie SKANER.")
            return
        }
        Intents.copy(ctx, "Krótka nazwa Meshtastic", s.shortName)
        val launchIntent = AppInspector.launchIntent(ctx, Intents.MESHTASTIC)
        notify(
            "Nazwa w Meshtastic",
            "Meshtastic nie udostępnia innym aplikacjom zmiany nazwy radia, więc ustaw ją raz ręcznie:\n\n" +
                "Meshtastic → Ustawienia radia → Użytkownik\n" +
                "• Długa nazwa: $long\n" +
                "• Krótka nazwa: ${s.shortName} (skopiowana do schowka)",
            actions = listOfNotNull(
                NoticeAction("Zamknij") { dismissNotice() },
                launchIntent?.let { li ->
                    NoticeAction("Otwórz Meshtastic", primary = true) {
                        dismissNotice()
                        startIntent(li)
                    }
                }
            )
        )
        markDone("mesh_owner")
    }

    // ------------------------------------------------------------------ SOS

    fun sendSos(loc: Location?) {
        val s = _state.value
        val phone = s.emergencyPhone
        if (phone.isNullOrBlank()) {
            notify("Brak numeru organizatora", "Paczka nie zawiera numeru alarmowego organizatora.", error = true)
            return
        }
        val time = SimpleDateFormat("HH:mm", Locale.ROOT).format(Date())
        val who = s.callsign.ifBlank { "gracz" }
        val body = if (loc != null && Mgrs.isSupported(loc.latitude, loc.longitude)) {
            "SOS $who MGRS ${Mgrs.format(loc.latitude, loc.longitude)} (${Mgrs.formatLatLon(loc.latitude, loc.longitude)}, ±${loc.accuracy.toInt()} m) $time"
        } else {
            "SOS $who — brak pozycji GPS, $time"
        }
        startIntent(Intents.sms(phone, body), null, "Brak aplikacji SMS na tym telefonie.")
    }

    override fun onCleared() {
        hotspot.stop()
        joiner.release()
        location.stop()
        super.onCleared()
    }
}
