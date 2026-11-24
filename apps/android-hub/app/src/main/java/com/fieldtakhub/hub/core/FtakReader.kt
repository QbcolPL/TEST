package com.fieldtakhub.hub.core

import java.io.Closeable
import java.io.File
import java.io.InputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipException
import java.util.zip.ZipFile

/** Read-only view of a .ftak (ZIP) file with normalized entry names. */
class FtakArchive(file: File) : Closeable {
    private val zip = ZipFile(file)
    private val byName = LinkedHashMap<String, ZipEntry>()
    private val byLower = HashMap<String, String>()

    init {
        val all = zip.entries()
        while (all.hasMoreElements()) {
            val z = all.nextElement()
            if (z.isDirectory) continue
            val name = normalize(z.name)
            if (name.isEmpty() || name.endsWith("/")) continue
            if (byName.putIfAbsent(name, z) == null) byLower.putIfAbsent(name.lowercase(), name)
        }
    }

    val names: Set<String> get() = byName.keys

    fun resolve(name: String): String? = if (byName.containsKey(name)) name else byLower[normalize(name).lowercase()]

    fun has(name: String): Boolean = resolve(name) != null

    fun size(name: String): Long = resolve(name)?.let { byName[it]?.size }?.takeIf { it >= 0 } ?: 0L

    fun open(name: String): InputStream? = resolve(name)?.let { byName[it] }?.let { zip.getInputStream(it) }

    fun bytes(name: String, limit: Long = 64L * 1024 * 1024): ByteArray? {
        val key = resolve(name) ?: return null
        val entry = byName[key] ?: return null
        if (entry.size > limit) throw FtakException("Plik $key w paczce jest zbyt duży do odczytu.")
        return zip.getInputStream(entry).use { it.readBytes() }
    }

    fun text(name: String): String? = bytes(name)?.let { String(it, Charsets.UTF_8) }

    override fun close() = zip.close()

    companion object {
        fun normalize(name: String): String = name.replace('\\', '/').trimStart('/')
    }
}

/**
 * Turns any Builder output into one [HubPackage]:
 *  - FTAK v2 signed      META-INF/fieldtak.json + checksums + Ed25519 signature, files under payload/
 *  - FTAK v3.0 faction   config.json / manifest.json (schemaVersion "3.0"), opord.json, files at root
 *  - FTAK v2.4 faction   manifest.json (schemaVersion "2.4"), files under source/
 *  - offline QR JSON     v3.0 config without files
 *  - OPORD only          opord.json from Builder's LAN OPORD link
 */
object FtakParser {
    private val SIGNATURE_FILES = setOf("META-INF/checksums.sha256", "META-INF/signature.ed25519", "META-INF/publisher.pub")

    fun inspect(file: File, verify: Boolean = true): HubPackage {
        val archive = try {
            FtakArchive(file)
        } catch (e: ZipException) {
            throw FtakException("To nie jest paczka .ftak — plik nie jest archiwum ZIP.")
        }
        return archive.use { a ->
            if (a.has("META-INF/fieldtak.json")) return@use parseV2(a, verify)
            val cfgText = a.text("config.json") ?: a.text("manifest.json")
            val cfg = cfgText?.let { Json.parseObjectOrNull(it) }
            val schema = cfg?.str("schemaVersion")?.trim().orEmpty()
            when {
                cfg != null && (schema.startsWith("3") || cfg.has("RequiredPackages", "activeFaction", "factions")) ->
                    parseV3(a, cfg, PackageKind.FTAK_V30)
                cfg != null && (schema.startsWith("2.4") || cfg.has("packageId", "faction")) -> parseV24(a, cfg)
                a.has("opord.json") -> {
                    val opord = Json.parseObjectOrNull(a.text("opord.json"))
                        ?: throw FtakException("Uszkodzony plik opord.json.")
                    opordOnly(OpordParser.parse(opord))
                }
                else -> throw FtakException(
                    "Nie rozpoznano formatu paczki. Oczekiwano META-INF/fieldtak.json (v2), config.json (v3.0) albo manifest.json (v2.4)."
                )
            }
        }
    }

    /** JSON from a QR code or an OPORD link: v3.0 offline config or Builder's OPORD model. */
    fun fromJsonText(text: String): HubPackage {
        val obj = try { Json.parseObject(text) } catch (e: JsonException) { throw FtakException("Kod zawiera uszkodzony JSON.") }
        return when {
            OpordParser.looksLikeOpord(obj) -> opordOnly(OpordParser.parse(obj))
            obj.has("factions", "schemaVersion", "ChannelName", "activeFaction") -> parseV3(null, obj, PackageKind.OFFLINE_QR)
            else -> throw FtakException("Kod zawiera JSON, którego nie rozpoznaję jako konfiguracji Field TAK.")
        }
    }

    // ---------------------------------------------------------------- v2 signed

    private fun parseV2(a: FtakArchive, verify: Boolean): HubPackage {
        val m = Json.parseObjectOrNull(a.text("META-INF/fieldtak.json"))
            ?: throw FtakException("Uszkodzony manifest META-INF/fieldtak.json.")
        val schema = m.text("schema")
        val schemaVersion = m.int("schemaVersion") ?: 0
        if (!schema.equals("fieldtak.package", ignoreCase = true) || schemaVersion != 2) {
            throw FtakException("Nieobsługiwana wersja paczki: \"$schema\" v$schemaVersion (obsługiwana: fieldtak.package v2).")
        }
        val pkg = m.obj("package")
        val id = pkg?.text("id").orEmpty().ifBlank { "fieldtak-v2" }
        val name = pkg?.text("name").orEmpty().ifBlank { id }
        val target = m.obj("target")
        val atakTarget = Versions.requirement(target?.text("minVersion"), target?.text("maxVersion"))

        val srv = m.obj("server")
        val rawHost = srv?.text("host").orEmpty()
        val server = if (rawHost.isNotBlank()) ServerInfo(
            name = srv?.text("name").orEmpty().ifBlank { "TAK Server" },
            host = hostOnly(rawHost),
            cotPort = srv?.int("cotPort") ?: portOf(rawHost) ?: 8089,
            type = srv?.text("type").orEmpty().ifBlank { "OpenTAK" }
        ) else null

        val enrollFile = a.text("payload/enrollment/enroll.url")?.trim()
        val enrollment = m.obj("enrollment")
        val enroll = enrollFile?.takeIf { Atak.isEnrollUri(it) }
            ?: enrollment?.text("takUri")?.takeIf { enrollment.bool("enabled") != false && Atak.isEnrollUri(it) }

        val meshObj = m.obj("meshtastic")
        val channelUrl = a.text("payload/meshtastic/channel.url")?.trim()?.takeIf { MeshChannel.isChannelUrl(it) }
        val mesh = if (meshObj?.bool("enabled") == true || channelUrl != null) MeshConfig(
            channelName = meshObj?.text("channelName").orEmpty(),
            pskBase64 = null,
            channelUrl = channelUrl,
            region = meshObj?.str("region"),
            modemPreset = meshObj?.str("modemPreset"),
            hopLimit = meshObj?.int("hopLimit")
        ) else null

        val reqs = ArrayList<AppRequirement>()
        val atakApk = a.resolve("payload/atak/atak.apk")
        reqs += AppRequirement(AppRole.ATAK, "ATAK-CIV", Atak.CIV_PACKAGE, atakApk == null, atakTarget, atakApk, atakApk?.let { a.size(it) } ?: 0)
        val meshApk = a.resolve("payload/apps/meshtastic.apk")
        if (meshApk != null || mesh != null) {
            reqs += AppRequirement(AppRole.MESHTASTIC, "Meshtastic", MESHTASTIC_PACKAGE, meshApk == null, null, meshApk, meshApk?.let { a.size(it) } ?: 0)
        }
        for (n in a.names.filter { it.startsWith("payload/plugins/", true) && it.endsWith(".apk", true) }.sorted()) {
            reqs += AppRequirement(AppRole.PLUGIN, n.substringAfterLast('/').removeSuffix(".apk"), null, false, null, n, a.size(n))
        }

        val payload = a.names.filter { it.startsWith("payload/", true) }
        val expires = m.obj("distribution")?.text("expiresUtc")?.takeUnless { Iso.isEffectivelyNever(it) }
        val opordEntry = a.names.firstOrNull { it.substringAfterLast('/').equals("opord.json", true) }
        val opord = opordEntry?.let { Json.parseObjectOrNull(a.text(it)) }?.let { OpordParser.parse(it) }
        val signature = if (verify) verifyV2(a, m) else SignatureInfo(SignatureState.VERIFIED, null, "Zweryfikowano przy imporcie")
        val version = pkg?.text("version").orEmpty()

        return HubPackage(
            kind = PackageKind.FTAK_V2,
            packageId = id,
            title = if (version.isNotBlank()) "$name $version" else name,
            eventName = name,
            faction = "",
            factionColor = null,
            version = version,
            createdUtc = pkg?.text("createdUtc"),
            expiresUtc = expires,
            signature = signature,
            server = server,
            enrollmentUri = enroll,
            mesh = mesh,
            emergencyPhone = null,
            voiceChannels = emptyList(),
            requirements = reqs,
            atakImports = payload.filter { isAtakImport(it) && !it.startsWith("payload/plugins/", true) },
            overlays = payload.filter { isOverlay(it) },
            mapLayers = payload.filter { isMapLayer(it) },
            certificates = payload.filter { it.endsWith(".p12", true) },
            opord = opord,
            opordHtmlEntry = a.names.firstOrNull { it.substringAfterLast('/').equals("opord.html", true) },
            atakSettings = emptyList(),
            atakTarget = atakTarget,
            notes = listOfNotNull(
                if (server != null && enroll == null && a.resolve("payload/atak/mission-package.zip") == null)
                    "Paczka ma adres serwera, ale nie ma linku rejestracji ani paczki misji — aplikacja utworzy połączenie z serwerem sama." else null
            )
        )
    }

    private fun verifyV2(a: FtakArchive, manifest: JsonObj): SignatureInfo {
        fun invalid(why: String) = SignatureInfo(SignatureState.INVALID, null, why)
        val checks = a.bytes("META-INF/checksums.sha256") ?: return invalid("Brak listy sum kontrolnych (META-INF/checksums.sha256).")
        val sig = Codec.base64Decode(a.text("META-INF/signature.ed25519"))
        val pub = Codec.base64Decode(a.text("META-INF/publisher.pub"))
        if (sig == null || sig.size != 64 || pub == null || pub.size != 32) return invalid("Brak lub uszkodzony podpis wydawcy.")
        if (!Ed25519.verify(pub, checks, sig)) return invalid("Podpis nie zgadza się z zawartością — paczkę zmieniono po podpisaniu.")

        val listed = LinkedHashMap<String, String>()
        for (raw in String(checks, Charsets.UTF_8).split('\n')) {
            val line = raw.trimEnd('\r')
            if (line.isBlank()) continue
            val split = line.indexOf("  ")
            if (split != 64 || !Hex.isHex(line.substring(0, 64))) return invalid("Uszkodzona linia listy sum kontrolnych.")
            listed[line.substring(split + 2)] = line.substring(0, 64).lowercase()
        }
        for (name in a.names) {
            if (name in SIGNATURE_FILES) continue
            if (!listed.containsKey(name)) return invalid("Plik spoza podpisu: $name")
        }
        for ((name, hash) in listed) {
            val stream = a.open(name) ?: return invalid("Brak podpisanego pliku: $name")
            val actual = stream.use { Codec.sha256Hex(it) }
            if (actual != hash) return invalid("Zmieniony plik: $name")
        }
        val fingerprint = Hex.encode(Codec.sha256(pub))
        val declared = manifest.obj("security")?.text("publisherFingerprintSha256").orEmpty()
        if (declared.isNotBlank() && !declared.equals(fingerprint, ignoreCase = true)) {
            return invalid("Odcisk klucza wydawcy nie zgadza się z manifestem.")
        }
        return SignatureInfo(SignatureState.VERIFIED, fingerprint, "Podpis Ed25519 i sumy SHA-256 zgodne")
    }

    // ---------------------------------------------------------------- v3.0 faction / offline QR

    private fun parseV3(a: FtakArchive?, cfg: JsonObj, kind: PackageKind): HubPackage {
        val factions = cfg.objects("factions")
        val active = cfg.text("activeFaction")
        val fac = factions.firstOrNull { it.text("Name").equals(active, ignoreCase = true) } ?: factions.firstOrNull()
        fun f(vararg keys: String): String = cfg.text(*keys).ifBlank { fac?.text(*keys).orEmpty() }

        val event = cfg.text("eventName")
        val faction = active.ifBlank { f("Name") }.ifBlank { "BLUFOR" }
        val otsHost = f("OtsHost")
        val server = if (otsHost.isNotBlank()) ServerInfo("OpenTAK Server", hostOnly(otsHost), portOf(otsHost) ?: 8089, "OpenTAK") else null
        val enroll = f("EnrollmentUri").takeIf { Atak.isEnrollUri(it) }
        val channelName = f("ChannelName")
        val psk = f("Psk")
        val channelUrl = f("ChannelUrl")
        val mesh = if (channelName.isNotBlank() || psk.isNotBlank() || MeshChannel.isChannelUrl(channelUrl)) MeshConfig(
            channelName = channelName,
            pskBase64 = psk.ifBlank { null },
            channelUrl = channelUrl.ifBlank { null },
            region = f("LoraRegion").ifBlank { null },
            modemPreset = f("LoraModemPreset").ifBlank { null },
            hopLimit = cfg.int("HopLimit") ?: fac?.int("HopLimit")
        ) else null

        val voiceObjs = cfg.objects("VoiceChannels").ifEmpty { fac?.objects("VoiceChannels").orEmpty() }
        val voice = voiceObjs.map { voiceChannel(it) }.filter { it.frequency.isNotBlank() || it.role.isNotBlank() }

        val reqs = ArrayList<AppRequirement>()
        val referenced = HashSet<String>()
        val reqObjs = cfg.objects("RequiredPackages").ifEmpty { fac?.objects("RequiredPackages").orEmpty() }
        for (r in reqObjs) {
            val role = when (r.text("role").uppercase()) {
                "ATAK-CIV", "ATAK" -> AppRole.ATAK
                "MESHTASTIC" -> AppRole.MESHTASTIC
                else -> AppRole.PLUGIN
            }
            val pkgName = r.text("packageName").ifBlank { null }
            val bundled = r.text("bundledZipPath").takeIf { it.isNotBlank() }?.let { a?.resolve(it) }
            if (bundled != null) referenced += bundled
            val guessed = role == AppRole.PLUGIN && (pkgName == null || pkgName.startsWith("com.atakmap.android.plugin."))
            reqs += AppRequirement(
                role = role,
                displayName = r.text("displayName").ifBlank { role.label },
                packageName = pkgName,
                packageNameReliable = !guessed && bundled == null,
                requirement = Versions.requirement(r.text("requiredVersion")),
                bundledEntry = bundled,
                bundledSize = bundled?.let { a?.size(it) } ?: r.long("fileSizeBytes") ?: 0L
            )
        }
        if (a != null) {
            for (n in a.names.filter { it.endsWith(".apk", true) && it !in referenced }.sorted()) {
                val (role, pkgName) = guessRole(n.substringAfterLast('/'))
                reqs += AppRequirement(role, n.substringAfterLast('/').removeSuffix(".apk"), pkgName, false, null, n, a.size(n))
            }
        }
        if (reqs.none { it.role == AppRole.ATAK }) {
            reqs.add(0, AppRequirement(AppRole.ATAK, "ATAK-CIV", Atak.CIV_PACKAGE, true, null, null, 0))
        }

        val expires = if (cfg.bool("neverExpires") == true) null else cfg.text("expiresAt").takeUnless { Iso.isEffectivelyNever(it) }
        val opordJson = a?.text("opord.json")?.let { Json.parseObjectOrNull(it) }
        val summary = cfg.obj("OpordSummary") ?: fac?.obj("OpordSummary")
        val opord = opordJson?.let { OpordParser.parse(it) }
            ?: summary?.takeIf { it.text("missionStatement").isNotBlank() || it.text("operationNumber").isNotBlank() }
                ?.let { OpordParser.fromSummary(it, event, faction) }
        val names = a?.names.orEmpty()

        return HubPackage(
            kind = kind,
            packageId = safeName(listOf(event, faction).filter { it.isNotBlank() }.joinToString("-"), "frakcja").lowercase(),
            title = listOf(event, faction).filter { it.isNotBlank() }.joinToString(" · "),
            eventName = event,
            faction = faction,
            factionColor = f("ColorHex").ifBlank { null },
            version = cfg.text("schemaVersion").ifBlank { "3.0" },
            createdUtc = null,
            expiresUtc = expires,
            signature = SignatureInfo.UNSIGNED,
            server = server,
            enrollmentUri = enroll,
            mesh = mesh,
            emergencyPhone = f("EmergencyPhone").ifBlank { null },
            voiceChannels = voice,
            requirements = reqs,
            atakImports = names.filter { isAtakImport(it) },
            overlays = names.filter { isOverlay(it) },
            mapLayers = names.filter { isMapLayer(it) },
            certificates = names.filter { it.endsWith(".p12", true) },
            opord = opord,
            opordHtmlEntry = a?.resolve("opord.html"),
            atakSettings = Atak.reportingFromProfiles(f("PliProfile"), f("MeshProfile")),
            atakTarget = reqs.firstOrNull { it.role == AppRole.ATAK }?.requirement,
            notes = listOfNotNull(
                if (kind == PackageKind.OFFLINE_QR) "Kod offline zawiera tylko ustawienia — bez aplikacji, map i rozkazu." else null,
                if (mesh != null && Codec.base64Decode(mesh.pskBase64)?.let { MeshChannel.isPrivatePsk(it) } == false)
                    "Kanał Meshtastic używa klucza publicznego — rozmowy może czytać każdy." else null
            )
        )
    }

    // ---------------------------------------------------------------- v2.4 faction

    private fun parseV24(a: FtakArchive, m: JsonObj): HubPackage {
        val tacticalName = a.names.firstOrNull { it.substringAfterLast('/').equals("tactical_config.json", true) }
        val tactical = tacticalName?.let { Json.parseObjectOrNull(a.text(it)) }
        val ots = tactical?.obj("openTakServer")
        val meshObj = tactical?.obj("meshtastic")
        val faction = m.text("faction").ifBlank { tactical?.text("faction").orEmpty() }.ifBlank { "BLUFOR" }
        val packageName = m.text("packageName")
        val event = packageName.substringBefore(" (").trim()
        val host = ots?.text("host").orEmpty()
        val server = if (host.isNotBlank()) ServerInfo("OpenTAK Server", hostOnly(host), portOf(host) ?: 8089, "OpenTAK") else null
        val channelName = m.text("channelName").ifBlank { meshObj?.text("channelName").orEmpty() }
        val psk = m.text("psk").ifBlank { meshObj?.text("psk").orEmpty() }
        val mesh = if (channelName.isNotBlank() || psk.isNotBlank()) MeshConfig(
            channelName = channelName,
            pskBase64 = psk.ifBlank { null },
            channelUrl = meshObj?.text("channelUrl")?.ifBlank { null },
            region = m.text("loraRegion").ifBlank { meshObj?.text("region").orEmpty() }.ifBlank { null },
            modemPreset = m.text("loraModemPreset").ifBlank { meshObj?.text("modemPreset").orEmpty() }.ifBlank { null },
            hopLimit = meshObj?.int("hopLimit")
        ) else null
        val voiceObjs = m.objects("voiceChannels").ifEmpty { tactical?.objects("voiceChannels").orEmpty() }

        val reqs = ArrayList<AppRequirement>()
        for (n in a.names.filter { it.endsWith(".apk", true) }.sorted()) {
            val (role, pkgName) = guessRole(n.substringAfterLast('/'))
            reqs += AppRequirement(role, n.substringAfterLast('/').removeSuffix(".apk"), pkgName, false, null, n, a.size(n))
        }
        if (reqs.none { it.role == AppRole.ATAK }) reqs.add(0, AppRequirement(AppRole.ATAK, "ATAK-CIV", Atak.CIV_PACKAGE, true, null, null, 0))

        val factionSlug = safeName(faction).lowercase()
        val opordEntry = a.names.firstOrNull {
            val file = it.substringAfterLast('/').lowercase()
            file == "opord.json" || file == "opord_$factionSlug.json"
        } ?: a.names.firstOrNull { it.substringAfterLast('/').lowercase().let { f -> f.startsWith("opord_") && f.endsWith(".json") } }
        val opord = opordEntry?.let { Json.parseObjectOrNull(a.text(it)) }?.let { OpordParser.parse(it) }
        val settings = tactical?.obj("atak")?.let { Atak.reportingFromConfig(it) }.orEmpty().ifEmpty {
            m.text("pliStrategy").takeIf { it.isNotBlank() }?.let { Atak.reportingFromConfig(JsonObj(mapOf("locationReportingStrategy" to it))) }.orEmpty()
        }
        val expires = if (m.bool("neverExpires") == true) null else m.text("expiresAt").takeUnless { Iso.isEffectivelyNever(it) }
        val enroll = ots?.text("enrollmentUri")?.takeIf { Atak.isEnrollUri(it) }

        return HubPackage(
            kind = PackageKind.FTAK_V24,
            packageId = m.text("packageId").ifBlank { safeName("$event-$faction").lowercase() },
            title = packageName.ifBlank { listOf(event, faction).filter { it.isNotBlank() }.joinToString(" · ") },
            eventName = event,
            faction = faction,
            factionColor = m.text("factionColor").ifBlank { tactical?.text("factionColor").orEmpty() }.ifBlank { null },
            version = "2.4",
            createdUtc = m.text("createdAt").ifBlank { null },
            expiresUtc = expires,
            signature = SignatureInfo.UNSIGNED,
            server = server,
            enrollmentUri = enroll,
            mesh = mesh,
            emergencyPhone = m.text("emergencyPhone").ifBlank { tactical?.text("emergencyPhone").orEmpty() }.ifBlank { null },
            voiceChannels = voiceObjs.map { voiceChannel(it) }.filter { it.frequency.isNotBlank() || it.role.isNotBlank() },
            requirements = reqs,
            atakImports = a.names.filter { isAtakImport(it) },
            overlays = a.names.filter { isOverlay(it) },
            mapLayers = a.names.filter { isMapLayer(it) },
            certificates = a.names.filter { it.endsWith(".p12", true) },
            opord = opord,
            opordHtmlEntry = a.names.firstOrNull { it.substringAfterLast('/').lowercase().let { f -> f == "opord.html" || (f.startsWith("opord_") && f.endsWith(".html")) } },
            atakSettings = settings,
            atakTarget = null,
            notes = listOf("Starszy format v2.4 — zalecana przebudowa paczki w aktualnym Builderze.")
        )
    }

    // ---------------------------------------------------------------- helpers

    private fun opordOnly(opord: Opord): HubPackage {
        val label = listOf(opord.eventName, opord.faction).filter { it.isNotBlank() }.joinToString(" · ")
        return HubPackage(
            kind = PackageKind.OPORD_ONLY,
            packageId = "opord-" + safeName(label.ifBlank { opord.operationNumber }, "rozkaz").lowercase(),
            title = (label.ifBlank { "Rozkaz" }) + " — OPORD",
            eventName = opord.eventName,
            faction = opord.faction,
            factionColor = null,
            version = opord.operationNumber,
            createdUtc = null,
            expiresUtc = null,
            signature = SignatureInfo.UNSIGNED,
            server = null,
            enrollmentUri = null,
            mesh = null,
            emergencyPhone = null,
            voiceChannels = emptyList(),
            requirements = emptyList(),
            atakImports = emptyList(),
            overlays = emptyList(),
            mapLayers = emptyList(),
            certificates = emptyList(),
            opord = opord,
            opordHtmlEntry = null,
            atakSettings = emptyList(),
            atakTarget = null,
            notes = emptyList()
        )
    }

    private fun voiceChannel(o: JsonObj) = VoiceChannel(
        role = o.text("roleName", "RoleName"),
        band = o.text("bandType", "BandType"),
        frequency = o.text("freqMhz", "FreqMhz"),
        tone = o.text("toneOrCode", "ToneOrCode")
    )

    const val MESHTASTIC_PACKAGE = "com.geeksville.mesh"

    fun guessRole(fileName: String): Pair<AppRole, String?> {
        val n = fileName.lowercase()
        return when {
            n.contains("atak") && !n.contains("plugin") && !n.contains("meshtastic") -> AppRole.ATAK to Atak.CIV_PACKAGE
            n.contains("meshtastic") && !n.contains("plugin") && !n.contains("atak") -> AppRole.MESHTASTIC to MESHTASTIC_PACKAGE
            else -> AppRole.PLUGIN to null
        }
    }

    private fun isAtakImport(name: String): Boolean = name.endsWith(".zip", true) || name.endsWith(".pref", true)
    private fun isOverlay(name: String): Boolean = name.endsWith(".kml", true) || name.endsWith(".kmz", true) || name.endsWith(".gpx", true)
    private fun isMapLayer(name: String): Boolean = name.endsWith(".mbtiles", true) || name.endsWith(".sqlite", true)

    /** "ssl://10.0.0.5:8089" -> "10.0.0.5" */
    fun hostOnly(raw: String): String {
        var h = raw.trim().substringAfter("://")
        h = h.substringBefore('/')
        if (h.count { it == ':' } == 1) h = h.substringBefore(':')
        return h
    }

    fun portOf(raw: String): Int? {
        val h = raw.trim().substringAfter("://").substringBefore('/')
        if (h.count { it == ':' } != 1) return null
        return h.substringAfter(':').toIntOrNull()?.takeIf { it in 1..65535 }
    }
}
