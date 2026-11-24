package com.fieldtakhub.hub.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.io.File
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.Signature
import java.util.Base64
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

class CoreTest {

    // ------------------------------------------------------------------ JSON

    @Test
    fun jsonHandlesBomEscapesAndCase() {
        val o = Json.parseObject("﻿{\"ChannelName\":\"BLUFOR-MESH\",\"n\":3,\"pl\":\"Dzie\\u0144 \\u2014 ok\",\"arr\":[1,2.5,true,null],\"o\":{\"x\":-1e2}}")
        assertEquals("BLUFOR-MESH", o.text("channelName"))
        assertEquals(3, o.int("N"))
        assertEquals("Dzień — ok", o.text("pl"))
        assertEquals(-100.0, o.obj("o")!!.double("x")!!, 0.0001)
        val back = Json.parseObject(Json.write(o.map))
        assertEquals("Dzień — ok", back.text("pl"))
    }

    // ------------------------------------------------------------------ Ed25519

    @Test
    fun ed25519MatchesJdk() {
        val kpg = KeyPairGenerator.getInstance("Ed25519")
        repeat(4) { round ->
            val kp = kpg.generateKeyPair()
            val raw = rawEd25519Public(kp.public.encoded)
            val msg = "checksums round $round\n".repeat(round + 1).toByteArray()
            val s = Signature.getInstance("Ed25519")
            s.initSign(kp.private)
            s.update(msg)
            val sig = s.sign()
            assertTrue(Ed25519.verify(raw, msg, sig))
            val tampered = msg.copyOf().also { it[0] = (it[0] + 1).toByte() }
            assertFalse(Ed25519.verify(raw, tampered, sig))
            val badSig = sig.copyOf().also { it[10] = (it[10].toInt() xor 1).toByte() }
            assertFalse(Ed25519.verify(raw, msg, badSig))
        }
    }

    // ------------------------------------------------------------------ callsign

    @Test
    fun shortNamesFollowTheFullName() {
        assertEquals("WLK1", Callsign.suggest("Wilk-1"))
        assertEquals("SOWA", Callsign.suggest("Sowa"))
        assertEquals("SOW2", Callsign.suggest("Sowa 2"))
        assertEquals("LIS2", Callsign.suggest("Lis-2"))
        assertEquals("BOR2", Callsign.suggest("Biały Orzeł 2"))
        assertEquals("GRZL", Callsign.suggest("Grizzly"))
        assertEquals("ZBK3", Callsign.suggest("Żbik-3"))
        assertEquals("AL12", Callsign.suggest("Alfa 12"))
        assertEquals("K105", Callsign.suggest("Kruk-105"))
        assertEquals("ECH7", Callsign.suggest("Echo-7"))
        assertTrue(Callsign.suggestions("Wilk-1").contains("WIL1"))
        assertTrue(Callsign.suggestions("Wilk-1").all { it.length <= 4 })
        assertEquals("wlk1", Callsign.cleanShort("wł-k1x"))
        assertEquals(39, Callsign.longName("A".repeat(60)).toByteArray().size)
        assertEquals("", Callsign.suggest("---"))
    }

    // ------------------------------------------------------------------ MGRS

    @Test
    fun mgrsKnownPoints() {
        // Standard transverse Mercator test vectors (WGS84)
        val equator = Mgrs.toUtm(0.0, 0.0)
        assertEquals(31, equator.zone)
        assertEquals(166021.4431, equator.easting, 0.01)
        assertEquals(0.0, equator.northing, 0.01)
        val meridian = Mgrs.toUtm(45.0, 3.0)
        assertEquals(500000.0, meridian.easting, 0.01)
        assertEquals(4982950.400, meridian.northing, 0.01)
        // Washington Monument, published as 18S UJ 23487 06483 (reference point differs by a few metres)
        val wm = Mgrs.format(38.889467, -77.035239)
        assertTrue(wm, wm.startsWith("18S UJ 234"))
        assertTrue(wm, wm.substring(13).startsWith("064"))
        assertTrue(Mgrs.format(52.2297, 21.0122).startsWith("34U "))
        assertTrue(Mgrs.format(60.0, 5.0).startsWith("32V "))
        assertTrue(Mgrs.format(-33.8568, 151.2153).startsWith("56H "))
        assertTrue(Mgrs.format(78.0, 15.0).startsWith("33X "))
    }

    // ------------------------------------------------------------------ Meshtastic

    @Test
    fun meshtasticChannelLinkRoundTrip() {
        val psk = ByteArray(32) { (it * 7 + 3).toByte() }
        val url = MeshChannel.buildUrl("BLUFOR-MESH", Base64.getEncoder().encodeToString(psk), "EU_868", "SHORT_FAST", 3, mqttUplink = true)
        // Same byte layout as Builder's BuildMeshtasticChannelSetUrl
        val settings = ByteArrayOutputStream().apply {
            write(0x12); write(32); write(psk)
            write(0x1A); write(11); write("BLUFOR-MESH".toByteArray())
            write(0x28); write(1); write(0x30); write(1)
        }.toByteArray()
        val lora = byteArrayOf(0x08, 1, 0x10, 6, 0x38, 3, 0x40, 3, 0x48, 1)
        val set = ByteArrayOutputStream().apply {
            write(0x0A); write(settings.size); write(settings)
            write(0x12); write(lora.size); write(lora)
        }.toByteArray()
        assertEquals("https://meshtastic.org/e/#" + Base64.getUrlEncoder().withoutPadding().encodeToString(set), url)

        val decoded = MeshChannel.decodeUrl(url)!!
        assertEquals("BLUFOR-MESH", decoded.primary!!.name)
        assertTrue(decoded.primary!!.psk.contentEquals(psk))
        assertEquals(3, decoded.lora!!.region)
        assertEquals(6, decoded.lora!!.modemPreset)
        assertEquals("AES-256 (prywatny)", MeshChannel.describePsk(psk))
        assertEquals("domyślny klucz publiczny (każdy może czytać)", MeshChannel.describePsk(byteArrayOf(1)))
    }

    // ------------------------------------------------------------------ versions / ATAK

    @Test
    fun versionRequirements() {
        val r = Versions.requirement("5.0+")!!
        assertTrue(r.isSatisfiedBy(Versions.parse("5.6.0.12 (bebdc3f4)")!!))
        assertFalse(r.isSatisfiedBy(Versions.parse("4.10.0")!!))
        val range = Versions.requirement("5.6", "5.8")!!
        assertTrue(range.isSatisfiedBy(Versions.parse("5.8.1")!!))
        assertFalse(range.isSatisfiedBy(Versions.parse("5.9.0")!!))
        assertNull(Versions.requirement("Zgodna z paczką .FTAK"))
    }

    @Test
    fun atakLinksAndTeams() {
        assertEquals("Blue", Atak.nearestTeam("#2684FF"))
        assertEquals("Red", Atak.nearestTeam("#EB4034"))
        assertEquals("White", Atak.nearestTeam("#EEEEEE"))
        val uri = Atak.preferenceUri(listOf(AtakSetting("locationCallsign", "string", "Wilk 1", "")))
        assertEquals("tak://com.atakmap.app/preference?key1=locationCallsign&type1=string&value1=Wilk%201", uri)
        assertTrue(Atak.isEnrollUri("tak://com.atakmap.app/enroll?host=10.0.0.5&username=wilk1&token=abc"))
        assertFalse(Atak.isEnrollUri("tak://com.atakmap.app/enroll?host=10.0.0.5"))
        val pkg = Atak.serverDataPackage(ServerInfo("OTS", "10.0.0.5", 8089, "OpenTAK"), "test")
        val names = zipNames(pkg)
        assertTrue(names.contains("MANIFEST/manifest.xml"))
        assertTrue(names.contains("server.pref"))
    }

    // ------------------------------------------------------------------ QR

    @Test
    fun qrPayloadsFromBuilder() {
        val lan = QrParser.parse("fieldtak://provision?url=http%3A%2F%2F192.168.1.10%3A8765%2Fd%2Fabc.json")
        assertEquals(QrPayload.Descriptor("http://192.168.1.10:8765/d/abc.json"), lan)

        val sha = "a".repeat(64)
        val cloud = QrParser.parse("fieldtak://provision?packageUrl=https%3A%2F%2Fexample.org%2Fp.ftak&sha256=$sha&packageBytes=123&expiresUtc=2026-11-24T10%3A00%3A00.0000000Z&label=Gra")
        assertTrue(cloud is QrPayload.Download)
        cloud as QrPayload.Download
        assertEquals("https://example.org/p.ftak", cloud.url)
        assertEquals(sha, cloud.sha256)
        assertEquals(123L, cloud.bytes)

        val wifi = QrParser.parse("http://192.168.0.5:8766/deploy/BLUFOR.ftak")
        assertTrue(wifi is QrPayload.Download)

        val opord = QrParser.parse("http://192.168.0.5:8766/opord/BLUFOR.html")
        assertEquals(QrPayload.OpordLink("http://192.168.0.5:8766/opord/BLUFOR.json", "http://192.168.0.5:8766/opord/BLUFOR.html"), opord)

        assertTrue(QrParser.parse("{\"schemaVersion\":\"3.0\",\"factions\":[]}") is QrPayload.OfflineConfig)
        assertTrue(QrParser.parse("ftak://provision?event=Gra&faction=BLUFOR&channel=X&sha256=0123456789ABCDEF") is QrPayload.LegacyPoster)
        assertTrue(QrParser.parse("https://meshtastic.org/e/#CgMSAQE") is QrPayload.MeshtasticChannel)
        assertTrue(QrParser.parse("tak://com.atakmap.app/enroll?host=h&username=u&token=t") is QrPayload.AtakEnroll)

        val drive = QrParser.parse("https://drive.google.com/file/d/FILEID123/view?usp=sharing") as QrPayload.Download
        assertEquals("https://drive.usercontent.google.com/download?id=FILEID123&export=download&confirm=t", drive.url)

        val share = QrParser.buildShareLink("http://192.168.49.1:8780/p/tok/x.ftak", sha, 10, "Gra BLUFOR", WifiCredentials("DIRECT-ab", "pass word", "wpa2"))
        val back = QrParser.parse(share) as QrPayload.Download
        assertEquals("http://192.168.49.1:8780/p/tok/x.ftak", back.url)
        assertEquals("pass word", back.wifi!!.password)

        assertNull(NetRules.downloadUrlProblem("http://192.168.1.10/x.ftak"))
        assertNotNull(NetRules.downloadUrlProblem("http://example.org/x.ftak"))
        assertNull(NetRules.downloadUrlProblem("https://example.org/x.ftak"))
    }

    // ------------------------------------------------------------------ packages

    @Test
    fun v2SignedPackageVerifies() {
        val kp = KeyPairGenerator.getInstance("Ed25519").generateKeyPair()
        val pub = rawEd25519Public(kp.public.encoded)
        val fp = hex(MessageDigest.getInstance("SHA-256").digest(pub))
        val manifest = """
            {
              "schema": "fieldtak.package",
              "schemaVersion": 2,
              "package": { "id": "gra.wilk", "name": "Gra Wilk", "version": "2.1.0", "createdUtc": "2026-11-24T06:00:00Z", "publisher": "Orga" },
              "target": { "platform": "ATAK-CIV", "minVersion": "5.6", "maxVersion": "5.8" },
              "server": { "type": "OpenTAK", "name": "GGZS OpenTAK", "host": "10.0.0.5", "cotPort": 8089, "apiPort": 8446, "webPort": 8443 },
              "enrollment": { "enabled": true, "takUri": "", "label": "" },
              "meshtastic": { "enabled": true, "channelName": "BLUFOR-MESH", "region": "EU_868", "modemPreset": "SHORT_FAST", "hopLimit": 3 },
              "security": { "hash": "SHA-256", "signature": "Ed25519", "publisherFingerprintSha256": "$fp" },
              "distribution": { "expiresUtc": "2099-01-01T00:00:00Z", "maxDownloads": 50 }
            }
        """.trimIndent()
        val files = linkedMapOf(
            "META-INF/fieldtak.json" to manifest.toByteArray(),
            "META-INF/server.txt" to "HOST=10.0.0.5\n".toByteArray(),
            "payload/enrollment/enroll.url" to "tak://com.atakmap.app/enroll?host=10.0.0.5&username=wilk&token=t0k\n".toByteArray(),
            "payload/meshtastic/channel.url" to "https://meshtastic.org/e/#CgMSAQE\n".toByteArray(),
            "payload/atak/mission-package.zip" to Atak.serverDataPackage(ServerInfo("x", "10.0.0.5", 8089, "OpenTAK"), "m"),
            "payload/plugins/datasync-1.2.apk" to ByteArray(100) { 7 }
        )
        val lines = files.keys.sorted().joinToString("") { "${hex(MessageDigest.getInstance("SHA-256").digest(files[it]!!))}  $it\n" }
        val checks = lines.toByteArray()
        val signer = Signature.getInstance("Ed25519").apply { initSign(kp.private); update(checks) }
        val all = LinkedHashMap(files)
        all["META-INF/checksums.sha256"] = checks
        all["META-INF/signature.ed25519"] = Base64.getEncoder().encodeToString(signer.sign()).toByteArray()
        all["META-INF/publisher.pub"] = Base64.getEncoder().encodeToString(pub).toByteArray()

        val pkg = FtakParser.inspect(zipFile(all))
        assertEquals(PackageKind.FTAK_V2, pkg.kind)
        assertEquals(SignatureState.VERIFIED, pkg.signature.state)
        assertEquals(fp, pkg.signature.fingerprint)
        assertEquals("10.0.0.5", pkg.server!!.host)
        assertTrue(Atak.isEnrollUri(pkg.enrollmentUri))
        assertEquals("https://meshtastic.org/e/#CgMSAQE", pkg.mesh!!.channelUrl)
        assertEquals(listOf("payload/atak/mission-package.zip"), pkg.atakImports)
        assertTrue(pkg.requirements.any { it.role == AppRole.PLUGIN && it.bundledEntry == "payload/plugins/datasync-1.2.apk" })
        assertNull(pkg.expiresUtc)

        val tampered = LinkedHashMap(all)
        tampered["payload/plugins/datasync-1.2.apk"] = ByteArray(100) { 8 }
        assertEquals(SignatureState.INVALID, FtakParser.inspect(zipFile(tampered)).signature.state)

        val extra = LinkedHashMap(all)
        extra["payload/data/sneaky.txt"] = "x".toByteArray()
        assertEquals(SignatureState.INVALID, FtakParser.inspect(zipFile(extra)).signature.state)
    }

    @Test
    fun v30FactionPackageReads() {
        val config = """
            {
              "schemaVersion": "3.0", "packageValidityHours": 0, "neverExpires": true,
              "expiresAt": "2126-11-24T06:00:00.0000000+00:00", "eventName": "Operacja Wilk", "activeFaction": "BLUFOR",
              "Name": "BLUFOR", "ColorHex": "#2684FF", "ChannelName": "BLUFOR-MESH", "Psk": "${Base64.getEncoder().encodeToString(ByteArray(32) { 1 })}",
              "ChannelUrl": "", "LoraRegion": "EU_868", "LoraModemPreset": "SHORT_FAST", "HopLimit": 3,
              "OtsHost": "192.168.1.20", "EnrollmentUri": "", "EmergencyPhone": "+48600100200",
              "PliProfile": "ConstRateUnreliable", "MeshProfile": "StationaryMesh",
              "VoiceChannels": [ { "roleName": "Dowodzenie HQ", "bandType": "PMR", "freqMhz": "CH 1 (446.00625)", "toneOrCode": "CTCSS 67.0 Hz" } ],
              "RequiredPackages": [
                { "role": "ATAK-CIV", "packageName": "com.atakmap.app.civ", "displayName": "ATAK-CIV", "requiredVersion": "5.6.0", "bundledZipPath": "apk/ATAK-CIV-5.6.0.apk", "sha256Short": "abc", "fileSizeBytes": 10 },
                { "role": "MESHTASTIC", "packageName": "com.geeksville.mesh", "displayName": "Meshtastic Android", "requiredVersion": "2.5+", "bundledZipPath": "", "sha256Short": "", "fileSizeBytes": 0 },
                { "role": "PLUGIN", "packageName": "com.atakmap.android.plugin.vns", "displayName": "vns", "requiredVersion": "1.0", "bundledZipPath": "plugins/vns-1.0.apk", "sha256Short": "", "fileSizeBytes": 5 }
              ],
              "OpordSummary": { "operationNumber": "OPORD 01-2026", "missionStatement": "Zdobyć wzgórze." },
              "factions": [ { "Name": "BLUFOR", "ColorHex": "#2684FF" } ]
            }
        """.trimIndent()
        val opord = """
            {
              "EventName": "Operacja Wilk", "TargetFaction": "BLUFOR",
              "Days": [
                { "DayTitle": "Dzień 1", "IncludeInExport": true, "DateIso": "2026-11-28", "MissionWho": "Drużyna BLUFOR", "MissionWhat": "zdobywa wzgórze 404",
                  "PrimaryRadioFreq": "446.00625 MHz (PMR CH 1)", "DaySpecificTasks": "• 09:00 — wejście",
                  "CodewordsTable": [ { "CodeWord": "ZAMEK", "CodeLetter": "A", "CodeDigit": "1", "DuressWord": "SPALONY" } ] },
                { "DayTitle": "Dzień 2", "IncludeInExport": true, "MissionFullStatement": "Utrzymać rejon." },
                { "DayTitle": "Szkic", "IncludeInExport": false }
              ],
              "CommandersIntent": "Działamy zespołowo."
            }
        """.trimIndent()
        val html = "<html><img src='data:image/png;base64," + Base64.getEncoder().encodeToString(ByteArray(200) { 9 }) + "'/></html>"
        val files = linkedMapOf(
            "config.json" to config.toByteArray(),
            "manifest.json" to config.toByteArray(),
            "opord.json" to opord.toByteArray(),
            "opord.html" to html.toByteArray(),
            "apk/ATAK-CIV-5.6.0.apk" to ByteArray(10),
            "plugins/vns-1.0.apk" to ByteArray(5),
            "datapackage/serwer.zip" to ByteArray(4),
            "maps/teren.kmz" to ByteArray(4),
            "factions/blufor/strefa_blufor.kml" to ByteArray(4)
        )
        val pkg = FtakParser.inspect(zipFile(files))
        assertEquals(PackageKind.FTAK_V30, pkg.kind)
        assertEquals("Operacja Wilk · BLUFOR", pkg.title)
        assertEquals("192.168.1.20", pkg.server!!.host)
        assertEquals("+48600100200", pkg.emergencyPhone)
        assertNull(pkg.expiresUtc)
        assertEquals(1, pkg.voiceChannels.size)
        assertEquals("apk/ATAK-CIV-5.6.0.apk", pkg.requirements.first { it.role == AppRole.ATAK }.bundledEntry)
        assertFalse(pkg.requirements.first { it.role == AppRole.PLUGIN }.packageNameReliable)
        assertEquals(listOf("datapackage/serwer.zip"), pkg.atakImports)
        assertEquals(2, pkg.overlays.size)
        assertEquals(2, pkg.opord!!.days.size)
        assertEquals("Drużyna BLUFOR zdobywa wzgórze 404.", pkg.opord!!.days[0].mission)
        assertEquals("Działamy zespołowo.", pkg.opord!!.days[1].sections.first { it.title.startsWith("3.") }.items.first().second)
        assertEquals("SPALONY", pkg.opord!!.codewords.first().duress)
        assertTrue(pkg.mesh!!.effectiveUrl()!!.startsWith("https://meshtastic.org/e/#"))
        assertEquals("Constant", pkg.atakSettings.first { it.key == "locationReportingStrategy" }.value)
        assertEquals(1, OpordParser.extractImages(html).size)

        val offline = FtakParser.fromJsonText("{\"schemaVersion\":\"3.0\",\"eventName\":\"Gra\",\"activeFaction\":\"OPFOR\",\"factions\":[{\"Name\":\"OPFOR\",\"ColorHex\":\"#EB4034\",\"ChannelName\":\"OPFOR-MESH\",\"Psk\":\"\",\"OtsHost\":\"\",\"EmergencyPhone\":\"112\",\"VoiceChannels\":[]}]}")
        assertEquals(PackageKind.OFFLINE_QR, offline.kind)
        assertEquals("OPFOR", offline.faction)
        assertEquals("112", offline.emergencyPhone)

        val opordOnly = FtakParser.fromJsonText(opord)
        assertEquals(PackageKind.OPORD_ONLY, opordOnly.kind)
        assertEquals(2, opordOnly.opord!!.days.size)
    }

    @Test
    fun v24FactionPackageReads() {
        val manifest = "﻿{\"schemaVersion\":\"2.4\",\"packageId\":\"gra-blufor\",\"packageName\":\"Gra (BLUFOR)\",\"faction\":\"BLUFOR\",\"factionColor\":\"#2684FF\",\"channelName\":\"BLUFOR-MESH\",\"psk\":\"AQ==\",\"pliStrategy\":\"Constant\",\"loraRegion\":\"EU_868\",\"loraModemPreset\":\"SHORT_FAST\",\"emergencyPhone\":\"600\",\"voiceChannels\":[{\"roleName\":\"HQ\",\"bandType\":\"PMR\",\"freqMhz\":\"CH 1\",\"toneOrCode\":\"\"}],\"neverExpires\":true,\"expiresAt\":\"2126-01-01T00:00:00+00:00\"}"
        val tactical = "{\"faction\":\"BLUFOR\",\"atak\":{\"locationReportingStrategy\":\"Constant\",\"constantReportingRateReliable\":0,\"constantReportingRateUnreliable\":5,\"dispatchLocationCotExternal\":true},\"openTakServer\":{\"host\":\"10.1.1.1:8089\",\"enrollmentUri\":\"\"},\"meshtastic\":{\"hopLimit\":4}}"
        val files = linkedMapOf(
            "source/apk/atak-civ-5.6.apk" to ByteArray(3),
            "source/apk/meshtastic-2.7.apk" to ByteArray(3),
            "source/tactical_config.json" to tactical.toByteArray(),
            "manifest.json" to manifest.toByteArray()
        )
        val pkg = FtakParser.inspect(zipFile(files))
        assertEquals(PackageKind.FTAK_V24, pkg.kind)
        assertEquals("Gra", pkg.eventName)
        assertEquals(8089, pkg.server!!.cotPort)
        assertEquals("10.1.1.1", pkg.server!!.host)
        assertEquals(4, pkg.mesh!!.hopLimit)
        assertTrue(pkg.atakSettings.any { it.key == "constantReportingRateUnreliable" && it.value == "5" })
        assertFalse(pkg.atakSettings.any { it.key == "constantReportingRateReliable" })
        assertEquals(AppRole.MESHTASTIC, pkg.requirements.first { it.bundledEntry == "source/apk/meshtastic-2.7.apk" }.role)
    }

    // ------------------------------------------------------------------ helpers

    private fun rawEd25519Public(x509: ByteArray): ByteArray = x509.copyOfRange(x509.size - 32, x509.size)

    private fun hex(b: ByteArray) = b.joinToString("") { String.format("%02x", it) }

    private fun zipFile(entries: Map<String, ByteArray>): File {
        val f = File.createTempFile("ftak-test", ".ftak")
        f.deleteOnExit()
        ZipOutputStream(f.outputStream()).use { zip ->
            for ((name, bytes) in entries) {
                zip.putNextEntry(ZipEntry(name))
                zip.write(bytes)
                zip.closeEntry()
            }
        }
        return f
    }

    private fun zipNames(bytes: ByteArray): Set<String> {
        val f = File.createTempFile("datapkg", ".zip")
        f.deleteOnExit()
        f.writeBytes(bytes)
        return java.util.zip.ZipFile(f).use { z -> z.entries().toList().map { it.name }.toSet() }
    }
}
