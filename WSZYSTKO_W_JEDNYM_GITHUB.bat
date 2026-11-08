@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [ETAP 3] Wdrazanie Silnika Wstrzykiwania ATAK + Meshtastic, Taktycznych Ikon
echo          Wektorowych UI oraz funkcji wczytywania kodu QR z Galerii...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'ETAP3_MOBILE_CS===')[1]); [FthEtap3MobileProvisioner]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Etapu 3 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - ETAP 3] Silnik wstrzykiwania Callsign/OTS/LoRa, ikony taktyczne
echo oraz skanowanie QR z galerii zostaly wdrozone i wyslane na GitHub!
echo ============================================================================
pause
exit /b 0

//===ETAP3_MOBILE_CS===
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class FthEtap3MobileProvisioner
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string payloadMarker = "//===" + "MOBILE_FILES_PAYLOAD===";
        int payloadIdx = rawBat.IndexOf(payloadMarker, StringComparison.Ordinal);
        if (payloadIdx < 0)
            throw new Exception("Nie znaleziono znacznika MOBILE_FILES_PAYLOAD w pliku .bat!");

        string payloadSection = rawBat.Substring(payloadIdx + payloadMarker.Length);

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);

        string pattern = @"//===" + @"FILE:([^\r\n]+)===\r?\n(.*?)\r?\n//===" + @"END_FILE===";
        MatchCollection matches = Regex.Matches(payloadSection, pattern, RegexOptions.Singleline);
        if (matches.Count == 0)
            throw new Exception("Nie znaleziono zadnych blokow FILE w sekcji MOBILE_FILES_PAYLOAD!");

        foreach (Match m in matches)
        {
            string relPath = m.Groups[1].Value.Trim().Replace('/', Path.DirectorySeparatorChar);
            string content = m.Groups[2].Value;
            string fullPath = Path.Combine(targetDir, relPath);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content, utf8NoBom);
            Console.WriteLine("[ZAPISANO] " + relPath);
        }

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Mobile App Etap 3 Provisioner & Tactical UI UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string ResolveTargetProjectDirectory(string baseDir)
    {
        string csprojRel = @"apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj";
        if (File.Exists(Path.Combine(baseDir, csprojRel))) return baseDir;
        string subTestMain = Path.Combine(baseDir, "TEST-main");
        if (File.Exists(Path.Combine(subTestMain, csprojRel))) return subTestMain;
        string subGotowe = Path.Combine(baseDir, "TEST-main-Gotowe");
        if (File.Exists(Path.Combine(subGotowe, csprojRel))) return subGotowe;
        string parentDir = Path.GetDirectoryName(baseDir);
        if (!string.IsNullOrEmpty(parentDir))
        {
            if (File.Exists(Path.Combine(parentDir, csprojRel))) return parentDir;
            string parentTestMain = Path.Combine(parentDir, "TEST-main");
            if (File.Exists(Path.Combine(parentTestMain, csprojRel))) return parentTestMain;
        }
        throw new Exception("Nie znaleziono projektu FieldTakHub w katalogu: " + baseDir);
    }

    private static void PushAndTriggerGitHubActions(string targetDir)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================================");
        Console.WriteLine("[2/3] Wysylka na GitHub (QbcolPL/TEST) i uruchomienie GitHub Actions...");
        Console.WriteLine("============================================================================");

        string gitExe = "git";
        string g1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\cmd\git.exe");
        string g2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe");
        if (File.Exists(g1)) gitExe = g1;
        else if (File.Exists(g2)) gitExe = g2;

        string lockFile = Path.Combine(targetDir, ".git", "index.lock");
        if (File.Exists(lockFile))
        {
            try { File.Delete(lockFile); } catch { }
        }

        RunProcess(gitExe, "init", targetDir, ignoreError: false);
        RunProcess(gitExe, "config user.name \"QbcolPL\"", targetDir, ignoreError: true);
        RunProcess(gitExe, "config user.email \"QbcolPL@users.noreply.github.com\"", targetDir, ignoreError: true);
        RunProcess(gitExe, "branch -M main", targetDir, ignoreError: true);
        RunProcess(gitExe, "remote add origin " + RepoRemoteUrl, targetDir, ignoreError: true);
        RunProcess(gitExe, "remote set-url origin " + RepoRemoteUrl, targetDir, ignoreError: false);

        RunProcess(gitExe, "add -A", targetDir, ignoreError: false);
        RunProcess(gitExe, "commit -m \"Etap 3: Add ATAK & Meshtastic Provisioning Engine, QR from Gallery, and Tactical Vector Icon UI\" --allow-empty", targetDir, ignoreError: false);

        int pushExitCode = RunProcess(gitExe, "push -u origin main --force", targetDir, ignoreError: false);
        if (pushExitCode != 0)
        {
            throw new Exception("Polecenie 'git push -u origin main --force' zwrocilo kod bledu: " + pushExitCode);
        }

        Console.WriteLine("[3/3] Otwieranie zakladki GitHub Actions...");
        try
        {
            Process.Start(new ProcessStartInfo { FileName = RepoActionsUrl, UseShellExecute = true });
        }
        catch { }
    }

    private static int RunProcess(string fileName, string arguments, string workingDir, bool ignoreError)
    {
        Console.WriteLine("> " + Path.GetFileName(fileName) + " " + arguments);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        using (var proc = Process.Start(psi))
        {
            proc.WaitForExit();
            if (proc.ExitCode != 0 && !ignoreError)
            {
                Console.WriteLine("[UWAGA] Kod wyjscia procesu: " + proc.ExitCode);
            }
            return proc.ExitCode;
        }
    }
}
//===ETAP3_MOBILE_CS===

//===MOBILE_FILES_PAYLOAD===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/ui/components/TacticalVectorIcons.kt===
package com.fieldtak.mobile.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.fieldtak.mobile.ui.theme.*

enum class TacticalIconType {
    GPS_SATELLITE,
    CAMERA_QR,
    BLUETOOTH_LORA,
    FILES_CRATE,
    APK_SHIELD,
    GALLERY_QR,
    FOLDER_OPEN,
    WIFI_HOTSPOT,
    INJECT_ARROWS
}

@Composable
fun TacticalIconBadge(
    type: TacticalIconType,
    tint: Color = RadarCyan,
    boxSize: Dp = 36.dp,
    iconSize: Dp = 20.dp
) {
    Surface(
        color = InputTrench,
        shape = RoundedCornerShape(6.dp),
        border = BorderStroke(1.dp, MutedGrid),
        modifier = Modifier.size(boxSize)
    ) {
        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
            TacticalVectorIcon(type = type, tint = tint, size = iconSize)
        }
    }
}

@Composable
fun TacticalVectorIcon(
    type: TacticalIconType,
    tint: Color = RadarCyan,
    size: Dp = 20.dp
) {
    Canvas(modifier = Modifier.size(size)) {
        val w = this.size.width
        val h = this.size.height
        val s = 1.8.dp.toPx()

        when (type) {
            TacticalIconType.GPS_SATELLITE -> {
                // Korpus satelity i panele sloneczne (styl taktyczny)
                drawLine(tint, Offset(w * 0.15f, h * 0.15f), Offset(w * 0.85f, h * 0.85f), s)
                drawRect(
                    color = tint,
                    topLeft = Offset(w * 0.36f, h * 0.36f),
                    size = Size(w * 0.28f, h * 0.28f),
                    style = Stroke(s)
                )
                drawLine(tint, Offset(w * 0.12f, h * 0.38f), Offset(w * 0.38f, h * 0.12f), s)
                drawLine(tint, Offset(w * 0.62f, h * 0.88f), Offset(w * 0.88f, h * 0.62f), s)
                // Fale namierzania MGRS w lewym dolnym rogu
                drawArc(
                    color = MilSimGreen,
                    startAngle = 100f,
                    sweepAngle = 70f,
                    useCenter = false,
                    topLeft = Offset(w * 0.08f, h * 0.45f),
                    size = Size(w * 0.45f, h * 0.45f),
                    style = Stroke(s * 0.85f)
                )
            }

            TacticalIconType.CAMERA_QR -> {
                // Obudowa kamery z celownikiem optycznym
                drawRoundRect(
                    color = tint,
                    topLeft = Offset(w * 0.08f, h * 0.24f),
                    size = Size(w * 0.84f, h * 0.62f),
                    cornerRadius = CornerRadius(3.dp.toPx()),
                    style = Stroke(s)
                )
                drawLine(tint, Offset(w * 0.32f, h * 0.14f), Offset(w * 0.68f, h * 0.14f), s * 1.2f)
                drawCircle(
                    color = tint,
                    radius = w * 0.18f,
                    center = Offset(w * 0.5f, h * 0.55f),
                    style = Stroke(s)
                )
                drawCircle(color = MilSimGreen, radius = w * 0.05f, center = Offset(w * 0.5f, h * 0.55f))
            }

            TacticalIconType.BLUETOOTH_LORA -> {
                // Runa Bluetooth po lewej + fale radiowe LoRa po prawej
                val btPath = Path().apply {
                    moveTo(w * 0.22f, h * 0.28f)
                    lineTo(w * 0.52f, h * 0.72f)
                    lineTo(w * 0.36f, h * 0.88f)
                    lineTo(w * 0.36f, h * 0.12f)
                    lineTo(w * 0.52f, h * 0.28f)
                    lineTo(w * 0.22f, h * 0.72f)
                }
                drawPath(btPath, color = tint, style = Stroke(s))
                drawArc(
                    color = MilSimGreen,
                    startAngle = -45f,
                    sweepAngle = 90f,
                    useCenter = false,
                    topLeft = Offset(w * 0.42f, h * 0.26f),
                    size = Size(w * 0.32f, h * 0.48f),
                    style = Stroke(s * 0.9f)
                )
                drawArc(
                    color = tint,
                    startAngle = -45f,
                    sweepAngle = 90f,
                    useCenter = false,
                    topLeft = Offset(w * 0.54f, h * 0.14f),
                    size = Size(w * 0.40f, h * 0.72f),
                    style = Stroke(s * 0.9f)
                )
            }

            TacticalIconType.FILES_CRATE -> {
                // Skrzynia taktyczna .ftak
                drawRoundRect(
                    color = tint,
                    topLeft = Offset(w * 0.10f, h * 0.22f),
                    size = Size(w * 0.80f, h * 0.64f),
                    cornerRadius = CornerRadius(2.dp.toPx()),
                    style = Stroke(s)
                )
                drawLine(tint, Offset(w * 0.10f, h * 0.42f), Offset(w * 0.90f, h * 0.42f), s)
                drawRect(
                    color = MilSimGreen,
                    topLeft = Offset(w * 0.40f, h * 0.36f),
                    size = Size(w * 0.20f, h * 0.14f)
                )
            }

            TacticalIconType.APK_SHIELD -> {
                // Tarcza instalatora pakietow APK
                val shield = Path().apply {
                    moveTo(w * 0.50f, h * 0.08f)
                    lineTo(w * 0.88f, h * 0.24f)
                    lineTo(w * 0.82f, h * 0.64f)
                    lineTo(w * 0.50f, h * 0.92f)
                    lineTo(w * 0.18f, h * 0.64f)
                    lineTo(w * 0.12f, h * 0.24f)
                    close()
                }
                drawPath(shield, color = tint, style = Stroke(s))
                drawLine(MilSimGreen, Offset(w * 0.50f, h * 0.30f), Offset(w * 0.50f, h * 0.66f), s)
                drawLine(MilSimGreen, Offset(w * 0.35f, h * 0.52f), Offset(w * 0.50f, h * 0.68f), s)
                drawLine(MilSimGreen, Offset(w * 0.65f, h * 0.52f), Offset(w * 0.50f, h * 0.68f), s)
            }

            TacticalIconType.GALLERY_QR -> {
                // Ramka obrazu z znacznikami QR w naroznikach
                drawRoundRect(
                    color = tint,
                    topLeft = Offset(w * 0.10f, h * 0.14f),
                    size = Size(w * 0.80f, h * 0.72f),
                    cornerRadius = CornerRadius(2.dp.toPx()),
                    style = Stroke(s)
                )
                drawRect(color = tint, topLeft = Offset(w * 0.22f, h * 0.26f), size = Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(color = tint, topLeft = Offset(w * 0.60f, h * 0.26f), size = Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(color = MilSimGreen, topLeft = Offset(w * 0.22f, h * 0.54f), size = Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawCircle(color = MilSimGreen, radius = w * 0.08f, center = Offset(w * 0.69f, h * 0.63f))
            }

            TacticalIconType.FOLDER_OPEN -> {
                val p = Path().apply {
                    moveTo(w * 0.08f, h * 0.24f)
                    lineTo(w * 0.38f, h * 0.24f)
                    lineTo(w * 0.48f, h * 0.34f)
                    lineTo(w * 0.92f, h * 0.34f)
                    lineTo(w * 0.92f, h * 0.82f)
                    lineTo(w * 0.08f, h * 0.82f)
                    close()
                }
                drawPath(p, color = tint, style = Stroke(s))
            }

            TacticalIconType.WIFI_HOTSPOT -> {
                drawCircle(color = tint, radius = w * 0.10f, center = Offset(w * 0.50f, h * 0.58f))
                drawLine(tint, Offset(w * 0.50f, h * 0.64f), Offset(w * 0.50f, h * 0.90f), s)
                drawArc(
                    color = tint,
                    startAngle = 210f,
                    sweepAngle = 120f,
                    useCenter = false,
                    topLeft = Offset(w * 0.22f, h * 0.26f),
                    size = Size(w * 0.56f, h * 0.56f),
                    style = Stroke(s)
                )
                drawArc(
                    color = tint,
                    startAngle = 205f,
                    sweepAngle = 130f,
                    useCenter = false,
                    topLeft = Offset(w * 0.08f, h * 0.10f),
                    size = Size(w * 0.84f, h * 0.80f),
                    style = Stroke(s * 0.85f)
                )
            }

            TacticalIconType.INJECT_ARROWS -> {
                // Strzalka w dol (po lewej) i w gore (po prawej) - ↓↑
                drawLine(tint, Offset(w * 0.30f, h * 0.15f), Offset(w * 0.30f, h * 0.85f), s * 1.1f)
                drawLine(tint, Offset(w * 0.14f, h * 0.65f), Offset(w * 0.30f, h * 0.85f), s * 1.1f)
                drawLine(tint, Offset(w * 0.46f, h * 0.65f), Offset(w * 0.30f, h * 0.85f), s * 1.1f)

                drawLine(tint, Offset(w * 0.70f, h * 0.85f), Offset(w * 0.70f, h * 0.15f), s * 1.1f)
                drawLine(tint, Offset(w * 0.54f, h * 0.35f), Offset(w * 0.70f, h * 0.15f), s * 1.1f)
                drawLine(tint, Offset(w * 0.86f, h * 0.35f), Offset(w * 0.70f, h * 0.15f), s * 1.1f)
            }
        }
    }
}

/**
 * Taktyczne narozniki celownika QR + pozioma linia lasera jak na wizualizacji
 */
@Composable
fun TacticalQrReticleOverlay(modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val w = size.width
        val h = size.height
        val padX = w * 0.12f
        val padY = h * 0.12f
        val left = padX
        val right = w - padX
        val top = padY
        val bottom = h - padY
        val cornerLen = minOf(w, h) * 0.16f
        val stroke = 3.2.dp.toPx()

        // 4 zielono-cyjanowe narozniki celownika
        // Lewy-Gorny
        drawLine(RadarCyan, Offset(left, top), Offset(left + cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(left, top), Offset(left, top + cornerLen), stroke)
        // Prawy-Gorny
        drawLine(RadarCyan, Offset(right, top), Offset(right - cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(right, top), Offset(right, top + cornerLen), stroke)
        // Lewy-Dolny
        drawLine(RadarCyan, Offset(left, bottom), Offset(left + cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(left, bottom), Offset(left, bottom - cornerLen), stroke)
        // Prawy-Dolny
        drawLine(RadarCyan, Offset(right, bottom), Offset(right - cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(right, bottom), Offset(right, bottom - cornerLen), stroke)

        // Pozioma linia lasera skanujacego w polowie wysokosci
        drawLine(
            color = RadarCyan,
            start = Offset(left * 0.75f, h * 0.5f),
            end = Offset(right + left * 0.25f, h * 0.5f),
            strokeWidth = 2.2.dp.toPx()
        )
    }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/data/AtakMeshtasticProvisioner.kt===
package com.fieldtak.mobile.data

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Environment
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

data class ProvisioningReport(
    val success: Boolean,
    val summaryTitle: String,
    val logLines: List<String>
)

object AtakMeshtasticProvisioner {

    /**
     * Glowna procedura Etapu 3:
     * 1. Generuje spersonalizowana paczke misji ATAK (.zip) z plikiem preference.pref
     *    (wstrzykuje Callsign, druzyne, interwaly PLI, adres OpenTAKServer i certyfikaty).
     * 2. Kopiuje mapy (.mbtiles) oraz nakladki (.kmz / .gpx) do folderow ATAK i wywoluje IMPORT_MISSION_PACKAGE.
     * 3. Wstrzykuje do Meshtastic kanaly LoRa, region, preset i klucz PSK oraz przekazuje skrot Callsign (np. WLK1).
     */
    suspend fun provisionAll(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        shortCallsign: String
    ): ProvisioningReport = withContext(Dispatchers.IO) {
        val logs = mutableListOf<String>()
        val cleanFull = fullCallsign.trim().ifEmpty { "Wilk-1" }
        val cleanShort = shortCallsign.trim().ifEmpty { FtakPackageRepository.generateMeshtasticShortName(cleanFull) }

        FtakPackageRepository.saveOperatorCallsign(context, cleanFull, cleanShort)
        logs.add("[✓] Zapisano Callsign operatora: ATAK='$cleanFull' | LoRa='[$cleanShort]'")

        // KROK A: Przygotowanie i wstrzykniecie paczki konfiguracyjnej do ATAK-CIV
        val atakResult = runCatching {
            buildAndInjectAtakMissionPackage(context, pkg, cleanFull, logs)
        }
        if (atakResult.isFailure) {
            logs.add("[!] Ostrzeżenie ATAK: ${atakResult.exceptionOrNull()?.message}")
        }

        // KROK B: Wstrzykniecie konfiguracji LoRa (kanaly, PSK, Region, Preset) + Callsign do Meshtastic
        val meshResult = runCatching {
            injectMeshtasticConfigAndOwner(context, pkg, cleanFull, cleanShort, logs)
        }
        if (meshResult.isFailure) {
            logs.add("[!] Ostrzeżenie Meshtastic: ${meshResult.exceptionOrNull()?.message}")
        }

        ProvisioningReport(
            success = atakResult.isSuccess || meshResult.isSuccess,
            summaryTitle = "Wdrożono profil ${pkg.factionName}: $cleanFull [$cleanShort]",
            logLines = logs
        )
    }

    private fun buildAndInjectAtakMissionPackage(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        logs: MutableList<String>
    ) {
        val ftakFile = File(pkg.localFilePath)
        require(ftakFile.exists()) { "Brak pliku paczki .ftak na urządzeniu." }

        val outZipName = "FTH_${pkg.factionName}_${fullCallsign.replace(Regex("[^a-zA-Z0-9_-]"), "_")}.zip"
        val outMissionZip = File(context.cacheDir, outZipName)

        val extractedFilesForManifest = mutableListOf<String>()
        val certEntries = mutableListOf<String>()

        // Opcjonalny bezposredni zapis na karcie /sdcard/atak jesli nadano MANAGE_EXTERNAL_STORAGE
        val sdcardAtakDir = File(Environment.getExternalStorageDirectory(), "atak")
        val canWriteSdcardAtak = runCatching {
            if (!sdcardAtakDir.exists()) sdcardAtakDir.mkdirs()
            sdcardAtakDir.canWrite()
        }.getOrDefault(false)

        ZipOutputStream(FileOutputStream(outMissionZip)).use { zos ->
            // 1. Przepisz certyfikaty, warstwy map (.kmz, .kml, .gpx, .mbtiles) oraz server.txt z .ftak
            ZipInputStream(BufferedInputStream(FileInputStream(ftakFile))).use { zis ->
                var entry: ZipEntry?
                while (zis.nextEntry.also { entry = it } != null) {
                    val rawName = entry!!.name.replace('\\', '/')
                    val shortName = rawName.substringAfterLast('/')
                    val lower = shortName.lowercase()

                    val isMapOrOverlay = lower.endsWith(".kmz") || lower.endsWith(".kml") ||
                        lower.endsWith(".gpx") || lower.endsWith(".mbtiles") || lower.endsWith(".xml")
                    val isCertOrServer = lower.endsWith(".p12") || lower == "server.txt" || lower.endsWith(".pref")

                    if (!entry!!.isDirectory && (isMapOrOverlay || isCertOrServer) && !lower.endsWith("manifest.xml")) {
                        val bytes = zis.readBytes()
                        val targetZipPath = when {
                            lower.endsWith(".p12") -> {
                                certEntries.add("certs/$shortName")
                                "certs/$shortName"
                            }
                            else -> "files/$shortName"
                        }

                        zos.putNextEntry(ZipEntry(targetZipPath))
                        zos.write(bytes)
                        zos.closeEntry()
                        extractedFilesForManifest.add(targetZipPath)

                        // Jezeli mamy pelny dostep do plikow, wrzuc mapy rowniez bezposrednio do katalogow ATAK
                        if (canWriteSdcardAtak) {
                            runCatching {
                                val targetSubDir = when {
                                    lower.endsWith(".mbtiles") -> File(sdcardAtakDir, "imagery")
                                    lower.endsWith(".kmz") || lower.endsWith(".kml") || lower.endsWith(".gpx") -> File(sdcardAtakDir, "overlays")
                                    lower.endsWith(".p12") -> File(sdcardAtakDir, "cert")
                                    else -> null
                                }
                                if (targetSubDir != null) {
                                    targetSubDir.mkdirs()
                                    File(targetSubDir, shortName).writeBytes(bytes)
                                }
                            }
                        }
                    }
                    zis.closeEntry()
                }
            }

            // 2. Wygeneruj plik preference.pref wstrzykujacy Callsign, druzyne, interwaly PLI oraz serwer OTS
            val prefXml = buildAtakPreferenceXml(pkg, fullCallsign, certEntries.firstOrNull())
            val prefEntryPath = "config/preference.pref"
            zos.putNextEntry(ZipEntry(prefEntryPath))
            zos.write(prefXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
            extractedFilesForManifest.add(prefEntryPath)

            // 3. Wygeneruj MANIFEST/manifest.xml wymagany przez ATAK Mission Package Importer
            val manifestXml = buildAtakMissionManifestXml(pkg, fullCallsign, extractedFilesForManifest)
            zos.putNextEntry(ZipEntry("MANIFEST/manifest.xml"))
            zos.write(manifestXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
        }

        // Jezeli mozliwe, skopiuj gotowa paczke misji prosto do /sdcard/atak/tools/datapackage/
        if (canWriteSdcardAtak) {
            runCatching {
                val dpDir = File(sdcardAtakDir, "tools/datapackage")
                dpDir.mkdirs()
                outMissionZip.copyTo(File(dpDir, outZipName), overwrite = true)
                logs.add("[✓] Skopiowano paczkę misji i mapy bezpośrednio do /sdcard/atak/")
            }
        }

        val pliInterval = if (pkg.pliConstMeshSec > 0) "${pkg.pliConstMeshSec}s" else "${pkg.pliConstLteSec}s"
        logs.add("[✓] Wygenerowano preference.pref: Callsign='$fullCallsign', PLI=${pkg.pliStrategy} ($pliInterval)")
        if (pkg.otsHost.isNotBlank() && !pkg.otsHost.contains("Tryb lokalny", true)) {
            logs.add("[✓] Wstrzyknięto profil OpenTAKServer: ${pkg.otsHost}")
        }

        // 4. Wywolaj oficjalny Intent importu paczki misji w ATAK-CIV (lub Enrollment URI)
        val zipUri = FileProvider.getUriForFile(
            context,
            "${context.packageName}.fileprovider",
            outMissionZip
        )

        val atakImportIntent = Intent("com.atakmap.app.IMPORT_MISSION_PACKAGE").apply {
            setPackage("com.atakmap.app.civ")
            setDataAndType(zipUri, "application/zip")
            putExtra("importFile", outMissionZip.absolutePath)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        }

        val launchedSpecific = runCatching {
            context.startActivity(atakImportIntent)
            true
        }.getOrDefault(false)

        if (!launchedSpecific) {
            // Fallback1: Enrollment URI tak:// jezeli dostepny
            if (pkg.enrollmentUri.startsWith("tak://", ignoreCase = true)) {
                runCatching {
                    val enrollIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.enrollmentUri)).apply {
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    }
                    context.startActivity(enrollIntent)
                    logs.add("[✓] Uruchomiono automatyczny Enrollment OTS w ATAK.")
                }
            } else {
                // Fallback2: Otworz paczke ZIP w ATAK przez ACTION_VIEW
                runCatching {
                    val viewIntent = Intent(Intent.ACTION_VIEW).apply {
                        setDataAndType(zipUri, "application/zip")
                        setPackage("com.atakmap.app.civ")
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
                    }
                    context.startActivity(viewIntent)
                }
            }
        } else {
            logs.add("[✓] Przekazano paczkę konfiguracyjną do ATAK-CIV.")
        }
    }

    private fun injectMeshtasticConfigAndOwner(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        shortCallsign: String,
        logs: MutableList<String>
    ) {
        // 1. Skopiuj Callsign oraz ShortName do schowka i wyslij broadcast pomocniczy dla Meshtastic
        runCatching {
            val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
            clipboard?.setPrimaryClip(ClipData.newPlainText("Meshtastic Callsign", "$fullCallsign ($shortCallsign)"))

            val ownerIntent = Intent("com.geeksville.mesh.SET_OWNER").apply {
                setPackage("com.geeksville.mesh")
                putExtra("longName", fullCallsign)
                putExtra("shortName", shortCallsign)
            }
            context.sendBroadcast(ownerIntent)
        }
        logs.add("[✓] Przygotowano identyfikator LoRa: Long='$fullCallsign' | Short='$shortCallsign'")

        // 2. Jezeli paczka zawiera zakodowany URL kanalu Meshtastic (https://meshtastic.org/e/#...),
        //    wywolaj bezposredni Intent do aplikacji com.geeksville.mesh
        if (pkg.meshtasticChannelUrl.isNotBlank() && pkg.meshtasticChannelUrl.startsWith("http", ignoreCase = true)) {
            val meshIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.meshtasticChannelUrl)).apply {
                setPackage("com.geeksville.mesh")
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            val started = runCatching {
                context.startActivity(meshIntent)
                true
            }.getOrDefault(false)

            if (!started) {
                val genericIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.meshtasticChannelUrl)).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                runCatching { context.startActivity(genericIntent) }
            }
            logs.add("[✓] Wstrzyknięto kanał LoRa '${pkg.loraChannelName}' (${pkg.loraRegion} / ${pkg.loraPreset}) + klucz PSK.")
        } else {
            logs.add("[i] Paczka używa domyślnego kanału LoRa (${pkg.loraRegion} / ${pkg.loraPreset}).")
        }
    }

    private fun mapFactionToAtakTeamColor(faction: String, hex: String): String {
        val f = faction.uppercase()
        return when {
            f.contains("BLU") || hex.contains("2684FF", true) -> "Blue"
            f.contains("OPFOR") || f.contains("RED") || hex.contains("EB4034", true) -> "Red"
            f.contains("ORGA") || f.contains("YEL") || hex.contains("F5B82E", true) -> "Yellow"
            f.contains("GREEN") || hex.contains("2ECC71", true) -> "Green"
            else -> "Cyan"
        }
    }

    private fun buildAtakPreferenceXml(
        pkg: FtakPackageSummary,
        fullCallsign: String,
        firstCertPath: String?
    ): String {
        val teamColor = mapFactionToAtakTeamColor(pkg.factionName, pkg.colorHex)
        val rateSec = (if (pkg.pliConstMeshSec > 0) pkg.pliConstMeshSec else pkg.pliConstLteSec).coerceAtLeast(5)
        val hasOts = pkg.otsHost.isNotBlank() && !pkg.otsHost.contains("Tryb lokalny", true)
        val cleanHost = pkg.otsHost.substringAfter("://").substringBefore("/").substringBefore(":")

        return buildString {
            appendLine("<?xml version='1.0' standalone='yes'?>")
            appendLine("<preferences>")
            appendLine("  <preference version=\"1\" name=\"com.atakmap.app_preferences\">")
            appendLine("    <entry key=\"locationCallsign\" class=\"class java.lang.String\">${escapeXml(fullCallsign)}</entry>")
            appendLine("    <entry key=\"locationTeam\" class=\"class java.lang.String\">$teamColor</entry>")
            appendLine("    <entry key=\"locationReportingStrategy\" class=\"class java.lang.String\">${escapeXml(pkg.pliStrategy)}</entry>")
            appendLine("    <entry key=\"constantReportingRateUnreliable\" class=\"class java.lang.String\">$rateSec</entry>")
            appendLine("    <entry key=\"constantReportingRateReliable\" class=\"class java.lang.String\">$rateSec</entry>")
            appendLine("  </preference>")
            if (hasOts && cleanHost.isNotBlank()) {
                appendLine("  <preference version=\"1\" name=\"cot_streams\">")
                appendLine("    <entry key=\"count\" class=\"class java.lang.Integer\">1</entry>")
                appendLine("    <entry key=\"description0\" class=\"class java.lang.String\">FieldTAKHub OTS (${escapeXml(pkg.factionName)})</entry>")
                appendLine("    <entry key=\"enabled0\" class=\"class java.lang.Boolean\">true</entry>")
                appendLine("    <entry key=\"connectString0\" class=\"class java.lang.String\">$cleanHost:8089:ssl</entry>")
                if (firstCertPath != null) {
                    val certFileName = firstCertPath.substringAfterLast('/')
                    appendLine("    <entry key=\"caLocation0\" class=\"class java.lang.String\">cert/$certFileName</entry>")
                    appendLine("    <entry key=\"clientPassword0\" class=\"class java.lang.String\">atakatak</entry>")
                }
                appendLine("  </preference>")
            }
            appendLine("</preferences>")
        }
    }

    private fun buildAtakMissionManifestXml(
        pkg: FtakPackageSummary,
        fullCallsign: String,
        entries: List<String>
    ): String {
        val uid = UUID.nameUUIDFromBytes("${pkg.eventName}_${pkg.factionName}_$fullCallsign".toByteArray()).toString()
        return buildString {
            appendLine("<MissionPackageManifest version=\"2\">")
            appendLine("  <Configuration>")
            appendLine("    <Parameter name=\"uid\" value=\"$uid\"/>")
            appendLine("    <Parameter name=\"name\" value=\"${escapeXml(pkg.eventName)}_${escapeXml(pkg.factionName)}_${escapeXml(fullCallsign)}\"/>")
            appendLine("    <Parameter name=\"onReceiveImport\" value=\"true\"/>")
            appendLine("    <Parameter name=\"onReceiveDelete\" value=\"false\"/>")
            appendLine("  </Configuration>")
            appendLine("  <Contents>")
            for (path in entries) {
                appendLine("    <Content ignore=\"false\" zipEntry=\"${escapeXml(path)}\"/>")
            }
            appendLine("  </Contents>")
            appendLine("</MissionPackageManifest>")
        }
    }

    private fun escapeXml(input: String): String {
        return input
            .replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace("\"", "&quot;")
            .replace("'", "&apos;")
    }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/ui/onboarding/OnboardingScreen.kt===
package com.fieldtak.mobile.ui.onboarding

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.fieldtak.mobile.data.FtakPackageRepository
import com.fieldtak.mobile.ui.components.TacticalIconBadge
import com.fieldtak.mobile.ui.components.TacticalIconType
import com.fieldtak.mobile.ui.theme.*

data class TacticalPermissionState(
    val id: String,
    val title: String,
    val desc: String,
    val iconType: TacticalIconType,
    val isGranted: Boolean
)

fun evaluatePermissions(context: Context): List<TacticalPermissionState> {
    val hasGps = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
    val hasCamera = ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
    val hasBt = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED &&
        ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED
    } else true

    val hasStorage = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
        Environment.isExternalStorageManager()
    } else {
        ContextCompat.checkSelfPermission(context, Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED
    }

    val hasInstallApks = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
        context.packageManager.canRequestPackageInstalls()
    } else true

    return listOf(
        TacticalPermissionState("GPS", "Lokalizacja GPS", "Wyliczanie kwadratu MGRS i pozycji do meldunku SOS.", TacticalIconType.GPS_SATELLITE, hasGps),
        TacticalPermissionState("CAM", "Dostęp do aparatu (QR)", "Błyskawiczny odczyt kodów QR paczek .ftak z chmury i LAN.", TacticalIconType.CAMERA_QR, hasCamera),
        TacticalPermissionState("BT", "Łączność Bluetooth & LoRa", "Wstrzykiwanie kanałów, PSK oraz ksywy do Meshtastic.", TacticalIconType.BLUETOOTH_LORA, hasBt),
        TacticalPermissionState("FILES", "Dostęp do plików (.ftak)", "Zapisywanie map .mbtiles/.kmz oraz paczek operacji.", TacticalIconType.FILES_CRATE, hasStorage),
        TacticalPermissionState("APK", "Instalacja pakietów (.apk)", "Automatyczna instalacja ATAK-CIV, Meshtastic i pluginów.", TacticalIconType.APK_SHIELD, hasInstallApks)
    )
}

@Composable
fun OnboardingScreen(onContinueToApp: () -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    var permissions by remember { mutableStateOf(evaluatePermissions(context)) }

    val activeFaction = remember {
        val pkgs = FtakPackageRepository.listInstalledPackages(context)
        pkgs.firstOrNull()?.factionName ?: "BLUFOR"
    }

    val runtimeLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestMultiplePermissions()
    ) {
        permissions = evaluatePermissions(context)
    }

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                permissions = evaluatePermissions(context)
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    fun requestSinglePermission(id: String) {
        when (id) {
            "GPS" -> runtimeLauncher.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
            "CAM" -> runtimeLauncher.launch(arrayOf(Manifest.permission.CAMERA))
            "BT" -> {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    runtimeLauncher.launch(arrayOf(Manifest.permission.BLUETOOTH_CONNECT, Manifest.permission.BLUETOOTH_SCAN))
                }
            }
            "FILES" -> {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                        data = Uri.parse("package:${context.packageName}")
                    }
                    runCatching { context.startActivity(intent) }.onFailure {
                        context.startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
                    }
                } else {
                    runtimeLauncher.launch(arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE, Manifest.permission.WRITE_EXTERNAL_STORAGE))
                }
            }
            "APK" -> {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES).apply {
                        data = Uri.parse("package:${context.packageName}")
                    }
                    context.startActivity(intent)
                }
            }
        }
    }

    val grantedCount = permissions.count { it.isGranted }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(16.dp)
    ) {
        Column(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.SpaceBetween
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = "MODUŁ: UPRAWNIENIA & PACZKI",
                    color = TextMuted,
                    fontSize = 11.sp,
                    fontFamily = FontFamily.Monospace
                )
                Text(
                    text = "Onboarding & Permissions",
                    color = TextWhite,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(top = 4.dp, bottom = 14.dp)
                )

                LazyColumn(
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    items(permissions.size) { index ->
                        val perm = permissions[index]
                        Card(
                            colors = CardDefaults.cardColors(containerColor = TacticalCard),
                            shape = RoundedCornerShape(6.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .border(1.2.dp, if (perm.isGranted) MutedGrid else RadarCyan, RoundedCornerShape(6.dp))
                                .clickable { requestSinglePermission(perm.id) }
                        ) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 12.dp, vertical = 11.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    modifier = Modifier.weight(1f).padding(end = 8.dp)
                                ) {
                                    TacticalIconBadge(
                                        type = perm.iconType,
                                        tint = RadarCyan,
                                        boxSize = 36.dp,
                                        iconSize = 20.dp
                                    )
                                    Spacer(modifier = Modifier.width(12.dp))
                                    Column {
                                        Text(
                                            text = perm.title,
                                            color = TextWhite,
                                            fontSize = 13.sp,
                                            fontWeight = FontWeight.Bold
                                        )
                                        Text(
                                            text = perm.desc,
                                            color = TextMuted,
                                            fontSize = 10.sp
                                        )
                                    }
                                }

                                if (perm.isGranted) {
                                    Text(
                                        text = "✓",
                                        color = MilSimGreen,
                                        fontSize = 18.sp,
                                        fontWeight = FontWeight.Bold,
                                        modifier = Modifier.padding(horizontal = 8.dp)
                                    )
                                } else {
                                    Surface(
                                        color = InputTrench,
                                        shape = RoundedCornerShape(4.dp),
                                        border = BorderStroke(1.dp, RadarCyan),
                                        modifier = Modifier.clickable { requestSinglePermission(perm.id) }
                                    ) {
                                        Text(
                                            text = "NADAJ",
                                            color = RadarCyan,
                                            fontSize = 10.sp,
                                            fontWeight = FontWeight.Bold,
                                            fontFamily = FontFamily.Monospace,
                                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp)
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                if (grantedCount < permissions.size) {
                    OutlinedButton(
                        onClick = {
                            val firstMissing = permissions.firstOrNull { !it.isGranted }
                            if (firstMissing != null) requestSinglePermission(firstMissing.id)
                        },
                        border = BorderStroke(1.2.dp, RadarCyan),
                        shape = RoundedCornerShape(6.dp),
                        modifier = Modifier.fillMaxWidth().height(42.dp)
                    ) {
                        Text(
                            text = "NADAJ BRAKUJĄCE UPRAWNIENIA ($grantedCount/${permissions.size})",
                            color = RadarCyan,
                            fontWeight = FontWeight.Bold,
                            fontSize = 11.sp,
                            fontFamily = FontFamily.Monospace
                        )
                    }
                }

                // Neonowa karta AKTYWNY PROFIL / WEJSCIE DO GRY wzorowana na wizualizacji
                Surface(
                    color = TacticalCard,
                    shape = RoundedCornerShape(8.dp),
                    border = BorderStroke(1.8.dp, RadarCyan),
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable { onContinueToApp() }
                ) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        modifier = Modifier.padding(vertical = 12.dp, horizontal = 16.dp)
                    ) {
                        Text(
                            text = "AKTYWNY PROFIL:",
                            color = TextMuted,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = activeFaction,
                            color = RadarCyan,
                            fontSize = 17.sp,
                            fontWeight = FontWeight.Bold
                        )
                        Text(
                            text = "Kliknij, aby przejść do Skanera QR i Gry ->",
                            color = MilSimGreen,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace,
                            modifier = Modifier.padding(top = 2.dp)
                        )
                    }
                }
            }
        }
    }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/ui/scanner/ScannerScreen.kt===
package com.fieldtak.mobile.ui.scanner

import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import android.util.Size
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.fieldtak.mobile.data.*
import com.fieldtak.mobile.ui.components.TacticalIconType
import com.fieldtak.mobile.ui.components.TacticalQrReticleOverlay
import com.fieldtak.mobile.ui.components.TacticalVectorIcon
import com.fieldtak.mobile.ui.theme.*
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.common.InputImage
import kotlinx.coroutines.launch
import java.util.concurrent.Executors

@Composable
fun ScannerScreen(
    activePackage: FtakPackageSummary?,
    onPackageLoaded: (FtakPackageSummary) -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val scrollState = rememberScrollState()

    var hasCameraPerm by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
        )
    }
    val camPermLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        hasCameraPerm = it
    }

    var isDownloading by remember { mutableStateOf(false) }
    var progressPct by remember { mutableStateOf(0) }
    var statusMessage by remember { mutableStateOf("ZESKANUJ QR / .FTAK") }
    var provisionReport by remember { mutableStateOf<ProvisioningReport?>(null) }

    val savedCallsigns = remember { FtakPackageRepository.loadOperatorCallsign(context) }
    var fullCallsign by remember { mutableStateOf(savedCallsigns.first) }
    var shortCallsign by remember { mutableStateOf(savedCallsigns.second) }
    var showRosterDropdown by remember { mutableStateOf(false) }

    var apkStatuses by remember(activePackage) {
        mutableStateOf(
            if (activePackage != null) ApkCompatibilityEngine.checkCompatibility(context, activePackage)
            else emptyList()
        )
    }

    fun onQrDetected(rawValue: String) {
        if (isDownloading) return
        isDownloading = true
        scope.launch {
            runCatching {
                FtakPackageRepository.handleScannedQrPayload(context, rawValue) { pct, msg ->
                    progressPct = pct
                    statusMessage = msg
                }
            }.onSuccess { summary ->
                isDownloading = false
                statusMessage = "AKTYWNA PACZKA: ${summary.eventName} (${summary.factionName})"
                onPackageLoaded(summary)
                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
            }.onFailure { err ->
                isDownloading = false
                statusMessage = "Błąd QR: ${err.message}"
            }
        }
    }

    // 1. Wczytywanie kodu QR ze zdjecia / zrzutu ekranu z Galerii telefonu
    val galleryQrLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
        if (uri != null) {
            statusMessage = "Analiza obrazu QR z galerii..."
            runCatching {
                val inputImage = InputImage.fromFilePath(context, uri)
                BarcodeScanning.getClient().process(inputImage)
                    .addOnSuccessListener { barcodes ->
                        val raw = barcodes.firstOrNull { !it.rawValue.isNullOrBlank() }?.rawValue
                        if (!raw.isNullOrBlank()) {
                            onQrDetected(raw)
                        } else {
                            statusMessage = "Błąd: Nie wykryto kodu QR na wybranym obrazie z galerii."
                        }
                    }
                    .addOnFailureListener { e ->
                        statusMessage = "Błąd odczytu obrazu: ${e.message}"
                    }
            }.onFailure { e ->
                statusMessage = "Nie udało się otworzyć obrazu: ${e.message}"
            }
        }
    }

    // 2. Bezposrednie wczytanie pliku .ftak z pamieci telefonu
    val filePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val summary = FtakPackageRepository.importFromUri(context, uri)
                if (summary != null) {
                    onPackageLoaded(summary)
                    apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
                    statusMessage = "Wczytano plik: ${summary.fileName}"
                }
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .verticalScroll(scrollState)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        // SEKCA 1: CELOWNIK SKANERA QR + PRZYCISKI GALERII I PLIKU
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(8.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(8.dp))
        ) {
            Column(
                modifier = Modifier.padding(10.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(190.dp)
                        .background(InputTrench, RoundedCornerShape(6.dp)),
                    contentAlignment = Alignment.Center
                ) {
                    if (hasCameraPerm) {
                        CameraQrPreview(onQrScanned = { onQrDetected(it) })
                    } else {
                        Button(
                            onClick = { camPermLauncher.launch(Manifest.permission.CAMERA) },
                            colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                            shape = RoundedCornerShape(4.dp)
                        ) {
                            Text("WŁĄCZ APARAT SKANERA QR", color = AbyssalSlate, fontWeight = FontWeight.Bold, fontSize = 11.sp)
                        }
                    }

                    // Taktyczne narozniki celownika QR + linia lasera
                    TacticalQrReticleOverlay(modifier = Modifier.fillMaxSize())
                }

                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = statusMessage,
                    color = if (statusMessage.startsWith("Błąd")) DangerCrimson else TextMuted,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )

                if (isDownloading) {
                    LinearProgressIndicator(
                        progress = progressPct / 100f,
                        color = RadarCyan,
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 6.dp)
                    )
                }

                // Pasek akcji pod celownikiem: QR Z GALERII + PLIK .FTAK
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = { galleryQrLauncher.launch("image/*") },
                        border = BorderStroke(1.2.dp, RadarCyan),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(36.dp),
                        contentPadding = PaddingValues(horizontal = 6.dp)
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.GALLERY_QR, tint = RadarCyan, size = 15.dp)
                        Spacer(modifier = Modifier.width(6.dp))
                        Text("QR Z GALERII", color = RadarCyan, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }

                    OutlinedButton(
                        onClick = { filePickerLauncher.launch("*/*") },
                        border = BorderStroke(1.2.dp, MutedGrid),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(36.dp),
                        contentPadding = PaddingValues(horizontal = 6.dp)
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.FOLDER_OPEN, tint = TextWhite, size = 15.dp)
                        Spacer(modifier = Modifier.width(6.dp))
                        Text("PLIK .FTAK", color = TextWhite, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        // SEKCJA 2: STATUS INTEGRACJI (ATAK-CIV + MESHTASTIC + OTS)
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Status integracji",
                        color = TextWhite,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold
                    )
                    if (activePackage != null) {
                        Text(
                            text = "Odśwież ↻",
                            color = RadarCyan,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace,
                            modifier = Modifier.clickable {
                                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, activePackage)
                            }
                        )
                    }
                }

                if (activePackage == null) {
                    Text(
                        text = "● ATAK-CIV (Oczekuje na skan QR)\n● Meshtastic (Oczekuje na skan QR)\n● Server OTS (Brak aktywnej paczki)",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )
                } else {
                    apkStatuses.forEach { st ->
                        val dotColor = when {
                            st.isUpToDate -> MilSimGreen
                            st.isInstalled -> CautionAmber
                            else -> DangerCrimson
                        }
                        val stateLabel = when {
                            st.isUpToDate -> "(OK v${st.installedVersion})"
                            st.isInstalled -> "(Wymaga v${st.item.requiredVersion})"
                            else -> "(Brak -> Wymagana v${st.item.requiredVersion})"
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                                Text(text = "● ", color = dotColor, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                Text(text = "${st.item.role} ", color = TextWhite, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                                Text(text = stateLabel, color = dotColor, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
                            }

                            if (!st.isUpToDate) {
                                Surface(
                                    color = CautionAmber,
                                    shape = RoundedCornerShape(4.dp),
                                    modifier = Modifier.clickable {
                                        scope.launch {
                                            val res = ApkCompatibilityEngine.extractAndInstallBundledApk(context, activePackage, st.item)
                                            res.onFailure { err -> statusMessage = err.message ?: "Błąd instalacji APK" }
                                        }
                                    }
                                ) {
                                    Text(
                                        text = if (st.isInstalled) "AKTUALIZUJ" else "ZAINSTALUJ",
                                        color = AbyssalSlate,
                                        fontSize = 9.sp,
                                        fontWeight = FontWeight.Bold,
                                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp)
                                    )
                                }
                            }
                        }
                    }

                    // Wiersz statusu serwera OTS
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(text = "● ", color = MilSimGreen, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text(text = "Server OTS ", color = TextWhite, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                        Text(
                            text = "(${activePackage.otsHost})",
                            color = MilSimGreen,
                            fontSize = 11.sp,
                            fontFamily = FontFamily.Monospace
                        )
                    }
                }
            }
        }

        // SEKCJA 3: ZNAK WYWOLAWCZY (CALLSIGN) + AUTOMATED PREVIEW + PRZYCISK WSTRZYKNIJ
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "ZNAK WYWOŁAWCZY (Callsign)",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace
                    )

                    if (activePackage != null && activePackage.squadCallsigns.isNotEmpty()) {
                        Box {
                            Text(
                                text = "[Lista z OPORD ▼]",
                                color = RadarCyan,
                                fontSize = 10.sp,
                                fontFamily = FontFamily.Monospace,
                                modifier = Modifier.clickable { showRosterDropdown = true }
                            )
                            DropdownMenu(
                                expanded = showRosterDropdown,
                                onDismissRequest = { showRosterDropdown = false }
                            ) {
                                activePackage.squadCallsigns.forEach { cs ->
                                    DropdownMenuItem(
                                        text = { Text(cs) },
                                        onClick = {
                                            fullCallsign = cs
                                            shortCallsign = FtakPackageRepository.generateMeshtasticShortName(cs)
                                            FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                                            showRosterDropdown = false
                                        }
                                    )
                                }
                            }
                        }
                    }
                }

                // Pole pelnego Callsignu (w stylu z wizualizacji)
                OutlinedTextField(
                    value = fullCallsign,
                    onValueChange = { newVal ->
                        fullCallsign = newVal
                        shortCallsign = FtakPackageRepository.generateMeshtasticShortName(newVal)
                        FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                    },
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = RadarCyan,
                        unfocusedBorderColor = MutedGrid,
                        focusedContainerColor = InputTrench,
                        unfocusedContainerColor = InputTrench,
                        focusedTextColor = TextWhite,
                        unfocusedTextColor = TextWhite
                    ),
                    modifier = Modifier.fillMaxWidth()
                )

                // Wiersz Automated preview: [WLK1] z mozliwoscia recznej edycji 4-znakowego skrotu
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Automated preview (Meshtastic 4-zn.):",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )

                    OutlinedTextField(
                        value = shortCallsign,
                        onValueChange = { newShort ->
                            shortCallsign = newShort.filter { it.isLetterOrDigit() }.take(4).uppercase()
                            FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                        },
                        singleLine = true,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = RadarCyan,
                            unfocusedBorderColor = MutedGrid,
                            focusedContainerColor = InputTrench,
                            unfocusedContainerColor = InputTrench,
                            focusedTextColor = RadarCyan,
                            unfocusedTextColor = RadarCyan
                        ),
                        modifier = Modifier.width(96.dp)
                    )
                }

                // Glowny przycisk WSTRZYKNIJ DO ATAK + MESHTASTIC z ikona ↓↑
                Button(
                    onClick = {
                        if (activePackage == null) {
                            statusMessage = "Błąd: Najpierw zeskanuj kod QR lub wczytaj plik .ftak!"
                            return@Button
                        }
                        scope.launch {
                            statusMessage = "Wstrzykiwanie konfiguracji do ATAK-CIV i Meshtastic..."
                            val report = AtakMeshtasticProvisioner.provisionAll(
                                context = context,
                                pkg = activePackage,
                                fullCallsign = fullCallsign,
                                shortCallsign = shortCallsign
                            )
                            provisionReport = report
                            statusMessage = report.summaryTitle
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(50.dp)
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.Center
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.INJECT_ARROWS, tint = AbyssalSlate, size = 20.dp)
                        Spacer(modifier = Modifier.width(10.dp))
                        Text(
                            text = "WSTRZYKNIJ DO ATAK + MESHTASTIC",
                            color = AbyssalSlate,
                            fontWeight = FontWeight.Bold,
                            fontSize = 12.sp
                        )
                    }
                }

                // Konsola raportu wdrozenia (wyswietlana po kliknieciu WSTRZYKNIJ)
                if (provisionReport != null) {
                    Surface(
                        color = InputTrench,
                        shape = RoundedCornerShape(4.dp),
                        border = BorderStroke(1.dp, MilSimGreen),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Column(modifier = Modifier.padding(8.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                            Text(
                                text = provisionReport!!.summaryTitle,
                                color = MilSimGreen,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                fontFamily = FontFamily.Monospace
                            )
                            provisionReport!!.logLines.forEach { line ->
                                Text(
                                    text = line,
                                    color = TextWhite,
                                    fontSize = 10.sp,
                                    fontFamily = FontFamily.Monospace
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
@androidx.annotation.OptIn(androidx.camera.core.ExperimentalGetImage::class)
private fun CameraQrPreview(onQrScanned: (String) -> Unit) {
    val lifecycleOwner = LocalLifecycleOwner.current
    val executor = remember { Executors.newSingleThreadExecutor() }
    var lastScanned by remember { mutableStateOf("") }

    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            val previewView = PreviewView(ctx)
            val cameraProviderFuture = ProcessCameraProvider.getInstance(ctx)
            cameraProviderFuture.addListener({
                val cameraProvider = cameraProviderFuture.get()
                val preview = Preview.Builder().build().also {
                    it.setSurfaceProvider(previewView.surfaceProvider)
                }

                val scanner = BarcodeScanning.getClient()
                val analysis = ImageAnalysis.Builder()
                    .setTargetResolution(Size(1280, 720))
                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                    .build()

                analysis.setAnalyzer(executor) { imageProxy ->
                    val mediaImage = imageProxy.image
                    if (mediaImage != null) {
                        val image = InputImage.fromMediaImage(mediaImage, imageProxy.imageInfo.rotationDegrees)
                        scanner.process(image)
                            .addOnSuccessListener { barcodes ->
                                val raw = barcodes.firstOrNull { !it.rawValue.isNullOrBlank() }?.rawValue
                                if (!raw.isNullOrBlank() && raw != lastScanned) {
                                    lastScanned = raw
                                    onQrScanned(raw)
                                }
                            }
                            .addOnCompleteListener {
                                imageProxy.close()
                            }
                    } else {
                        imageProxy.close()
                    }
                }

                runCatching {
                    cameraProvider.unbindAll()
                    cameraProvider.bindToLifecycle(
                        lifecycleOwner,
                        CameraSelector.DEFAULT_BACK_CAMERA,
                        preview,
                        analysis
                    )
                }
            }, ContextCompat.getMainExecutor(ctx))
            previewView
        }
    )
}
//===END_FILE===