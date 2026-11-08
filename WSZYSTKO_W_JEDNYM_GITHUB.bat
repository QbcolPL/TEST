@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [ETAP 2] Wdrazanie Skanera QR (CameraX + MLKit), Parsera archiwow .ftak,
echo          Weryfikatora i Instalatora pakietow .apk oraz Powloki Taktycznej...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'ETAP2_MOBILE_CS===')[1]); [FthEtap2MobileEngine]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Etapu 2 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - ETAP 2] Skaner QR, repozytorium .ftak oraz silnik weryfikacji
echo i instalacji APK zostaly utworzone i wyslane na GitHub (QbcolPL/TEST)!
echo ============================================================================
pause
exit /b 0

//===ETAP2_MOBILE_CS===
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class FthEtap2MobileEngine
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
        File.WriteAllText(triggerPath, "Field TAK Hub Mobile App Etap 2 QR & APK Engine UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Etap 2: Add QR Scanner (CameraX/MLKit), .ftak ZIP parser, APK version verifier & installer, and 4-tab tactical shell\" --allow-empty", targetDir, ignoreError: false);

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
//===ETAP2_MOBILE_CS===

//===MOBILE_FILES_PAYLOAD===

//===FILE:apps/mobile/settings.gradle.kts===
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
    plugins {
        id("com.android.application") version "8.2.2"
        id("org.jetbrains.kotlin.android") version "1.9.22"
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}
rootProject.name = "FieldTakHubMobile"
//===END_FILE===

//===FILE:apps/mobile/gradle.properties===
org.gradle.jvmargs=-Xmx2048m -Dfile.encoding=UTF-8
android.useAndroidX=true
kotlin.code.style=official
android.nonTransitiveRClass=true
//===END_FILE===

//===FILE:apps/mobile/build.gradle.kts===
plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.fieldtak.mobile"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.fieldtak.mobile"
        minSdk = 26
        targetSdk = 34
        versionCode = 2
        versionName = "3.4.2"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables {
            useSupportLibrary = true
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
    }
    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.8"
    }
    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.12.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.7.0")
    implementation("androidx.activity:activity-compose:1.8.2")
    implementation(platform("androidx.compose:compose-bom:2023.10.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3")

    // CameraX & MLKit Barcode Scanning dla Skanera QR
    val cameraVersion = "1.3.1"
    implementation("androidx.camera:camera-core:$cameraVersion")
    implementation("androidx.camera:camera-camera2:$cameraVersion")
    implementation("androidx.camera:camera-lifecycle:$cameraVersion")
    implementation("androidx.camera:camera-view:$cameraVersion")
    implementation("com.google.mlkit:barcode-scanning:17.2.0")
}
//===END_FILE===

//===FILE:apps/mobile/src/main/AndroidManifest.xml===
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:tools="http://schemas.android.com/tools">

    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
    <uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />
    <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
    <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
    <uses-permission android:name="android.permission.CAMERA" />
    <uses-permission android:name="android.permission.BLUETOOTH" />
    <uses-permission android:name="android.permission.BLUETOOTH_ADMIN" />
    <uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
    <uses-permission android:name="android.permission.BLUETOOTH_SCAN" />
    <uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" />
    <uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
    <uses-permission android:name="android.permission.MANAGE_EXTERNAL_STORAGE" tools:ignore="ScopedStorage" />
    <uses-permission android:name="android.permission.REQUEST_INSTALL_PACKAGES" />
    <uses-permission android:name="android.permission.QUERY_ALL_PACKAGES" tools:ignore="QueryAllPackagesPermission" />

    <!-- Widocznosc pakietow ATAK-CIV, Meshtastic i wtyczek dla Androida 11+ -->
    <queries>
        <package android:name="com.atakmap.app.civ" />
        <package android:name="com.geeksville.mesh" />
        <package android:name="com.atakmap.android.meshtastic.plugin" />
        <intent>
            <action android:name="com.atakmap.app.IMPORT_MISSION_PACKAGE" />
        </intent>
    </queries>

    <application
        android:allowBackup="true"
        android:icon="@mipmap/ic_launcher"
        android:label="Field TAK Hub"
        android:roundIcon="@mipmap/ic_launcher_round"
        android:supportsRtl="true"
        android:usesCleartextTraffic="true"
        android:theme="@style/Theme.FieldTakHub">

        <activity
            android:name=".MainActivity"
            android:exported="true"
            android:screenOrientation="portrait"
            android:theme="@style/Theme.FieldTakHub">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
            <!-- Obsluga deep-linkow fieldtak://provision?packageUrl=... -->
            <intent-filter>
                <action android:name="android.intent.action.VIEW" />
                <category android:name="android.intent.category.DEFAULT" />
                <category android:name="android.intent.category.BROWSABLE" />
                <data android:scheme="fieldtak" android:host="provision" />
            </intent-filter>
        </activity>

        <provider
            android:name="androidx.core.content.FileProvider"
            android:authorities="${applicationId}.fileprovider"
            android:exported="false"
            android:grantUriPermissions="true">
            <meta-data
                android:name="android.support.FILE_PROVIDER_PATHS"
                android:resource="@xml/file_paths" />
        </provider>
    </application>
</manifest>
//===END_FILE===

//===FILE:apps/mobile/src/main/res/xml/file_paths.xml===
<?xml version="1.0" encoding="utf-8"?>
<paths>
    <cache-path name="extracted_apks" path="." />
    <files-path name="ftak_packages" path="." />
    <external-path name="external_files" path="." />
</paths>
//===END_FILE===

//===FILE:apps/mobile/src/main/res/drawable/ic_launcher_foreground.xml===
<?xml version="1.0" encoding="utf-8"?>
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="108dp"
    android:height="108dp"
    android:viewportWidth="108"
    android:viewportHeight="108">
    <!-- Zewnetrzny pierscien radarowy -->
    <path
        android:fillColor="#00000000"
        android:strokeColor="#1FE0C5"
        android:strokeWidth="3.2"
        android:pathData="M54,24 A30,30 0 1,1 53.9,24 Z" />
    <!-- Wewnetrzny pierscien celownika -->
    <path
        android:fillColor="#00000000"
        android:strokeColor="#1FE0C5"
        android:strokeWidth="2.0"
        android:pathData="M54,36 A18,18 0 1,1 53.9,36 Z" />
    <!-- Krzyz MGRS -->
    <path
        android:strokeColor="#1FE0C5"
        android:strokeWidth="2.5"
        android:pathData="M54,16 L54,32 M54,76 L54,92 M16,54 L32,54 M76,54 L92,54" />
    <!-- Centralny punkt taktyczny -->
    <path
        android:fillColor="#2ECC71"
        android:pathData="M54,50 A4,4 0 1,1 53.9,50 Z" />
</vector>
//===END_FILE===

//===FILE:apps/mobile/src/main/res/mipmap-anydpi-v26/ic_launcher.xml===
<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@color/abyssal_slate" />
    <foreground android:drawable="@drawable/ic_launcher_foreground" />
</adaptive-icon>
//===END_FILE===

//===FILE:apps/mobile/src/main/res/mipmap-anydpi-v26/ic_launcher_round.xml===
<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@color/abyssal_slate" />
    <foreground android:drawable="@drawable/ic_launcher_foreground" />
</adaptive-icon>
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/data/FtakModels.kt===
package com.fieldtak.mobile.data

import java.io.File

data class RequiredApkItem(
    val role: String,             // "ATAK-CIV", "MESHTASTIC", "PLUGIN"
    val displayName: String,
    val packageName: String,      // np. "com.atakmap.app.civ", "com.geeksville.mesh"
    val requiredVersion: String,  // np. "5.2.0"
    val bundledZipPath: String    // sciezka wewnatrz archiwum .ftak, np. "apk/ATAK.apk"
)

data class InstalledApkStatus(
    val item: RequiredApkItem,
    val installedVersion: String?,
    val isInstalled: Boolean,
    val isUpToDate: Boolean,
    val hasBundledApkInFtak: Boolean
)

data class VoiceChannelItem(
    val role: String,
    val band: String,
    val freqOrChannel: String,
    val ctcssOrTg: String
)

data class FtakPackageSummary(
    val id: String,
    val fileName: String,
    val localFilePath: String,
    val eventName: String,
    val factionName: String,
    val colorHex: String,
    val otsHost: String,
    val enrollmentUri: String,
    val meshtasticChannelUrl: String,
    val loraRegion: String,
    val loraPreset: String,
    val loraChannelName: String,
    val emergencyPhone: String,
    val pliStrategy: String,
    val pliConstLteSec: Int,
    val pliConstMeshSec: Int,
    val expirationHours: Int,
    val sizeBytes: Long,
    val requiredPackages: List<RequiredApkItem>,
    val voiceChannels: List<VoiceChannelItem>,
    val squadCallsigns: List<String>,
    val hasEmbeddedOpordHtml: Boolean
) {
    val formattedSize: String
        get() {
            val mb = sizeBytes.toDouble() / (1024.0 * 1024.0)
            return if (mb >= 1.0) String.format("%.1f MB", mb)
            else String.format("%.0f KB", sizeBytes.toDouble() / 1024.0)
        }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/data/FtakPackageRepository.kt===
package com.fieldtak.mobile.data

import android.content.Context
import android.net.Uri
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

object FtakPackageRepository {
    private const val PREFS_NAME = "fth_mobile_prefs"
    private const val KEY_ACTIVE_FILE = "active_ftak_filename"
    private const val KEY_CALLSIGN_FULL = "operator_callsign_full"
    private const val KEY_CALLSIGN_SHORT = "operator_callsign_short"

    fun getPackagesDir(context: Context): File {
        val dir = File(context.filesDir, "ftak_packages")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    fun getActivePackageFileName(context: Context): String? {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(KEY_ACTIVE_FILE, null)
    }

    fun setActivePackageFileName(context: Context, fileName: String) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_ACTIVE_FILE, fileName)
            .apply()
    }

    fun saveOperatorCallsign(context: Context, fullCallsign: String, shortCallsign: String) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_CALLSIGN_FULL, fullCallsign)
            .putString(KEY_CALLSIGN_SHORT, shortCallsign)
            .apply()
    }

    fun loadOperatorCallsign(context: Context): Pair<String, String> {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val full = prefs.getString(KEY_CALLSIGN_FULL, "Wilk-1") ?: "Wilk-1"
        val shortName = prefs.getString(KEY_CALLSIGN_SHORT, generateMeshtasticShortName(full)) ?: "WLK1"
        return Pair(full, shortName)
    }

    /**
     * Deterministyczny generator 4-znakowego skrotu Meshtastic (np. "Wilk-1" -> "WLK1")
     */
    fun generateMeshtasticShortName(fullCallsign: String): String {
        val normalized = normalizePolishChars(fullCallsign.trim()).uppercase()
        if (normalized.isEmpty()) return "OPR1"

        val digits = normalized.filter { it.isDigit() }
        val letters = normalized.filter { it.isLetter() }

        if (letters.isEmpty()) {
            return normalized.filter { it.isLetterOrDigit() }.take(4).padEnd(4, '0')
        }

        val maxLetters = (4 - digits.take(2).length).coerceAtLeast(1)
        val vowels = setOf('A', 'E', 'I', 'O', 'U', 'Y')
        val firstLetter = letters.first()
        val remainingConsonants = letters.drop(1).filter { it !in vowels }
        val remainingAll = letters.drop(1)

        val letterPart = buildString {
            append(firstLetter)
            for (c in remainingConsonants) {
                if (length >= maxLetters) break
                append(c)
            }
            for (c in remainingAll) {
                if (length >= maxLetters) break
                if (!contains(c)) append(c)
            }
        }.take(maxLetters)

        val combined = (letterPart + digits.take(4 - letterPart.length)).take(4)
        return combined.ifEmpty { "OPR1" }
    }

    private fun normalizePolishChars(input: String): String {
        val map = mapOf(
            'Ą' to 'A', 'Ć' to 'C', 'Ę' to 'E', 'Ł' to 'L', 'Ń' to 'N',
            'Ó' to 'O', 'Ś' to 'S', 'Ź' to 'Z', 'Ż' to 'Z',
            'ą' to 'a', 'ć' to 'c', 'ę' to 'e', 'ł' to 'l', 'ń' to 'n',
            'ó' to 'o', 'ś' to 's', 'ź' to 'z', 'ż' to 'z'
        )
        return buildString(input.length) {
            for (ch in input) append(map[ch] ?: ch)
        }
    }

    fun listInstalledPackages(context: Context): List<FtakPackageSummary> {
        val dir = getPackagesDir(context)
        val files = dir.listFiles { f ->
            f.isFile && (f.name.endsWith(".ftak", ignoreCase = true) || f.name.endsWith(".zip", ignoreCase = true))
        }?.sortedByDescending { it.lastModified() } ?: emptyList()

        return files.mapNotNull { file ->
            runCatching { parseFtakArchive(file) }.getOrNull()
        }
    }

    fun deletePackage(context: Context, fileName: String) {
        val file = File(getPackagesDir(context), fileName)
        if (file.exists()) file.delete()
        val currentActive = getActivePackageFileName(context)
        if (currentActive == fileName) {
            val remaining = listInstalledPackages(context)
            setActivePackageFileName(context, remaining.firstOrNull()?.fileName ?: "")
        }
    }

    suspend fun importFromUri(context: Context, uri: Uri, suggestedName: String? = null): FtakPackageSummary? =
        withContext(Dispatchers.IO) {
            val fileName = (suggestedName ?: uri.lastPathSegment?.substringAfterLast('/') ?: "operacja_${System.currentTimeMillis()}.ftak")
                .let { if (it.endsWith(".ftak", true) || it.endsWith(".zip", true)) it else "$it.ftak" }
            val destFile = File(getPackagesDir(context), fileName)
            context.contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(destFile).use { output ->
                    input.copyTo(output)
                }
            }
            val parsed = parseFtakArchive(destFile)
            setActivePackageFileName(context, destFile.name)
            parsed
        }

    suspend fun handleScannedQrPayload(
        context: Context,
        rawQr: String,
        onProgress: (Int, String) -> Unit
    ): FtakPackageSummary = withContext(Dispatchers.IO) {
        val trimmed = rawQr.trim()

        // 1. Obsluga deep-linku fieldtak://provision?packageUrl=...
        val resolvedUrl = if (trimmed.startsWith("fieldtak://", ignoreCase = true)) {
            val uri = Uri.parse(trimmed)
            uri.getQueryParameter("packageUrl")
                ?: uri.getQueryParameter("url")
                ?: throw IllegalArgumentException("Brak parametru packageUrl w linku fieldtak://")
        } else if (trimmed.startsWith("http://", ignoreCase = true) || trimmed.startsWith("https://", ignoreCase = true)) {
            trimmed
        } else {
            null
        }

        // 2. Jezeli QR zawiera URL (LAN Wi-Fi :8766 lub chmura HTTPS) -> pobierz plik .ftak
        if (resolvedUrl != null) {
            onProgress(5, "Łączenie z serwerem dystrybucji: $resolvedUrl")
            val urlObj = URL(resolvedUrl)
            val conn = (urlObj.openConnection() as HttpURLConnection).apply {
                connectTimeout = 12000
                readTimeout = 20000
                requestMethod = "GET"
                connect()
            }
            if (conn.responseCode !in 200..299) {
                throw IllegalStateException("Błąd HTTP ${conn.responseCode} podczas pobierania paczki .ftak")
            }

            val totalBytes = conn.contentLengthLong.coerceAtLeast(1L)
            val rawName = Uri.parse(resolvedUrl).lastPathSegment?.substringAfterLast('/')
                ?.takeIf { it.isNotBlank() } ?: "paczka_${System.currentTimeMillis()}.ftak"
            val safeName = if (rawName.endsWith(".ftak", true) || rawName.endsWith(".zip", true)) rawName else "$rawName.ftak"
            val targetFile = File(getPackagesDir(context), safeName)

            conn.inputStream.use { input ->
                FileOutputStream(targetFile).use { output ->
                    val buffer = ByteArray(16 * 1024)
                    var downloaded = 0L
                    var read: Int
                    while (input.read(buffer).also { read = it } != -1) {
                        output.write(buffer, 0, read)
                        downloaded += read
                        val pct = ((downloaded * 100L) / totalBytes).toInt().coerceIn(5, 98)
                        onProgress(pct, "Pobrano ${downloaded / 1024} KB...")
                    }
                }
            }
            onProgress(100, "Zapisano paczkę: $safeName")
            val summary = parseFtakArchive(targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        // 3. Jezeli QR jest bezposrednim ladunkiem Offline JSON z Buildera -> utworz lokalna paczke .ftak
        if (trimmed.startsWith("{") && trimmed.endsWith("}")) {
            onProgress(50, "Przetwarzanie kodu QR Offline JSON...")
            val json = JSONObject(trimmed)
            val faction = json.optString("FactionName", json.optString("faction", "BLUFOR"))
            val eventName = json.optString("EventName", json.optString("operation", "OPERACJA_OFFLINE"))
            val fileName = "${eventName}_${faction}_OFFLINE.ftak".replace(Regex("[^a-zA-Z0-9._-]"), "_")
            val targetFile = File(getPackagesDir(context), fileName)

            ZipOutputStream(FileOutputStream(targetFile)).use { zos ->
                zos.putNextEntry(ZipEntry("config.json"))
                zos.write(json.toString(2).toByteArray(Charsets.UTF_8))
                zos.closeEntry()
            }
            onProgress(100, "Utworzono paczkę Offline: $fileName")
            val summary = parseFtakArchive(targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        throw IllegalArgumentException("Nieznany format kodu QR. Zeskanuj kod QR paczki .ftak (HTTP/LAN, fieldtak:// lub Offline JSON).")
    }

    fun parseFtakArchive(file: File): FtakPackageSummary {
        var configJsonStr: String? = null
        var manifestJsonStr: String? = null
        var opordJsonStr: String? = null
        var hasOpordHtml = false
        val zipEntries = mutableSetOf<String>()

        ZipInputStream(BufferedInputStream(FileInputStream(file))).use { zis ->
            var entry: ZipEntry?
            while (zis.nextEntry.also { entry = it } != null) {
                val entryName = entry!!.name.replace('\\', '/')
                zipEntries.add(entryName)
                val shortName = entryName.substringAfterLast('/')
                when {
                    shortName.equals("config.json", ignoreCase = true) && configJsonStr == null -> {
                        configJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    shortName.equals("manifest.json", ignoreCase = true) && manifestJsonStr == null -> {
                        manifestJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    shortName.equals("opord.json", ignoreCase = true) && opordJsonStr == null -> {
                        opordJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    shortName.equals("opord.html", ignoreCase = true) -> {
                        hasOpordHtml = true
                    }
                }
                zis.closeEntry()
            }
        }

        val root = when {
            !configJsonStr.isNullOrBlank() -> JSONObject(configJsonStr!!)
            !manifestJsonStr.isNullOrBlank() -> JSONObject(manifestJsonStr!!)
            else -> JSONObject()
        }

        val eventName = root.optString("EventName", root.optString("eventName", file.nameWithoutExtension))
        val factionName = root.optString("FactionName", root.optString("factionName", "BLUFOR"))
        val colorHex = root.optString("ColorHex", root.optString("colorHex", "#1FE0C5"))
        val otsHost = root.optString("OtsHost", root.optString("otsHost", "Tryb lokalny / Off-Grid"))
        val enrollmentUri = root.optString("EnrollmentUri", root.optString("enrollmentUri", ""))
        val channelUrl = root.optString("ChannelUrl", root.optString("channelUrl", ""))
        val loraRegion = root.optString("LoraRegion", root.optString("region", "EU_868"))
        val loraPreset = root.optString("LoraPreset", root.optString("preset", "SHORT_FAST"))
        val loraChannelName = root.optString("LoraChannelName", "$factionName-MESH")
        val emergencyPhone = root.optString("EmergencyPhone", root.optString("emergencyPhone", "112"))
        val pliStrategy = root.optString("PliStrategy", "Constant")
        val pliConstLteSec = root.optInt("PliConstLteSec", 0)
        val pliConstMeshSec = root.optInt("PliConstMeshSec", 15)
        val expirationHours = root.optInt("ExpirationHours", 0)

        val requiredList = mutableListOf<RequiredApkItem>()
        val reqArr: JSONArray? = root.optJSONArray("RequiredPackages") ?: root.optJSONArray("requiredPackages")
        if (reqArr != null) {
            for (i in 0 until reqArr.length()) {
                val obj = reqArr.optJSONObject(i) ?: continue
                requiredList.add(
                    RequiredApkItem(
                        role = obj.optString("role", "APK"),
                        displayName = obj.optString("displayName", obj.optString("role", "Pakiet APK")),
                        packageName = obj.optString("packageName", ""),
                        requiredVersion = obj.optString("requiredVersion", "1.0"),
                        bundledZipPath = obj.optString("bundledZipPath", "")
                    )
                )
            }
        }

        if (requiredList.none { it.packageName == "com.atakmap.app.civ" }) {
            val bundledAtak = zipEntries.firstOrNull { it.contains("atak", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(0, RequiredApkItem("ATAK-CIV", "ATAK-CIV (Taktyczna Mapa)", "com.atakmap.app.civ", "5.0.0", bundledAtak))
        }
        if (requiredList.none { it.packageName == "com.geeksville.mesh" }) {
            val bundledMesh = zipEntries.firstOrNull { it.contains("mesh", true) && !it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(1, RequiredApkItem("MESHTASTIC", "Meshtastic (Komunikator LoRa)", "com.geeksville.mesh", "2.3.0", bundledMesh))
        }
        if (requiredList.none { it.packageName == "com.atakmap.android.meshtastic.plugin" }) {
            val bundledPlugin = zipEntries.firstOrNull { it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(RequiredApkItem("PLUGIN", "Wtyczka ATAK Meshtastic", "com.atakmap.android.meshtastic.plugin", "1.0.0", bundledPlugin))
        }

        val voiceList = mutableListOf<VoiceChannelItem>()
        val voiceArr = root.optJSONArray("VoiceChannels") ?: root.optJSONArray("voiceChannels")
        if (voiceArr != null) {
            for (i in 0 until voiceArr.length()) {
                val v = voiceArr.optJSONObject(i) ?: continue
                voiceList.add(
                    VoiceChannelItem(
                        role = v.optString("Role", v.optString("role", "Kanał ${i + 1}")),
                        band = v.optString("Band", v.optString("band", "PMR")),
                        freqOrChannel = v.optString("FreqOrChannel", v.optString("freq", "CH 1")),
                        ctcssOrTg = v.optString("CtcssOrTg", v.optString("ctcss", "Brak tonu"))
                    )
                )
            }
        }
        if (voiceList.isEmpty()) {
            voiceList.add(VoiceChannelItem("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"))
            voiceList.add(VoiceChannelItem("Taktyczny Alfa", "VHF/UHF", "145.500 MHz", "CTCSS 88.5 Hz"))
        }

        val callsigns = mutableListOf<String>()
        if (!opordJsonStr.isNullOrBlank()) {
            runCatching {
                val opObj = JSONObject(opordJsonStr!!)
                val roster = opObj.optJSONArray("SquadRoster")
                if (roster != null) {
                    for (i in 0 until roster.length()) {
                        val r = roster.optJSONObject(i)
                        val cs = r?.optString("OperatorOrTeam", "")?.trim() ?: ""
                        if (cs.isNotEmpty()) callsigns.add(cs)
                    }
                }
            }
        }

        return FtakPackageSummary(
            id = file.name,
            fileName = file.name,
            localFilePath = file.absolutePath,
            eventName = eventName,
            factionName = factionName,
            colorHex = colorHex,
            otsHost = otsHost,
            enrollmentUri = enrollmentUri,
            meshtasticChannelUrl = channelUrl,
            loraRegion = loraRegion,
            loraPreset = loraPreset,
            loraChannelName = loraChannelName,
            emergencyPhone = emergencyPhone,
            pliStrategy = pliStrategy,
            pliConstLteSec = pliConstLteSec,
            pliConstMeshSec = pliConstMeshSec,
            expirationHours = expirationHours,
            sizeBytes = file.length(),
            requiredPackages = requiredList,
            voiceChannels = voiceList,
            squadCallsigns = callsigns.distinct(),
            hasEmbeddedOpordHtml = hasOpordHtml
        )
    }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/data/ApkCompatibilityEngine.kt===
package com.fieldtak.mobile.data

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream

object ApkCompatibilityEngine {

    fun checkCompatibility(context: Context, pkg: FtakPackageSummary): List<InstalledApkStatus> {
        val pm = context.packageManager
        val zipFile = File(pkg.localFilePath)
        val zipEntryNames = mutableSetOf<String>()
        if (zipFile.exists()) {
            runCatching {
                ZipInputStream(BufferedInputStream(FileInputStream(zipFile))).use { zis ->
                    var e: ZipEntry?
                    while (zis.nextEntry.also { e = it } != null) {
                        zipEntryNames.add(e!!.name.replace('\\', '/'))
                        zis.closeEntry()
                    }
                }
            }
        }

        return pkg.requiredPackages.map { req ->
            val installedVer = getInstalledVersionName(pm, req.packageName)
            val isInstalled = installedVer != null
            val isUpToDate = isInstalled && compareVersions(installedVer!!, req.requiredVersion) >= 0
            val hasBundled = req.bundledZipPath.isNotBlank() && zipEntryNames.any {
                it.equals(req.bundledZipPath.replace('\\', '/'), ignoreCase = true) ||
                it.substringAfterLast('/').equals(req.bundledZipPath.substringAfterLast('/'), ignoreCase = true)
            }

            InstalledApkStatus(
                item = req,
                installedVersion = installedVer,
                isInstalled = isInstalled,
                isUpToDate = isUpToDate,
                hasBundledApkInFtak = hasBundled
            )
        }
    }

    private fun getInstalledVersionName(pm: PackageManager, packageName: String): String? {
        if (packageName.isBlank()) return null
        return try {
            val info = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                pm.getPackageInfo(packageName, PackageManager.PackageInfoFlags.of(0))
            } else {
                @Suppress("DEPRECATION")
                pm.getPackageInfo(packageName, 0)
            }
            info.versionName ?: "1.0"
        } catch (_: PackageManager.NameNotFoundException) {
            null
        }
    }

    fun compareVersions(installed: String, required: String): Int {
        val cleanInst = installed.substringBefore('-').split('.').map { it.filter { c -> c.isDigit() }.toIntOrNull() ?: 0 }
        val cleanReq = required.substringBefore('-').split('.').map { it.filter { c -> c.isDigit() }.toIntOrNull() ?: 0 }
        val maxLen = maxOf(cleanInst.size, cleanReq.size)
        for (i in 0 until maxLen) {
            val v1 = cleanInst.getOrElse(i) { 0 }
            val v2 = cleanReq.getOrElse(i) { 0 }
            if (v1 != v2) return v1.compareTo(v2)
        }
        return 0
    }

    suspend fun extractAndInstallBundledApk(
        context: Context,
        pkg: FtakPackageSummary,
        req: RequiredApkItem
    ): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val ftakFile = File(pkg.localFilePath)
            require(ftakFile.exists()) { "Plik paczki .ftak nie istnieje: ${pkg.localFilePath}" }

            val outApkFile = File(context.cacheDir, "${req.role}_${req.packageName}.apk")
            var extracted = false

            ZipInputStream(BufferedInputStream(FileInputStream(ftakFile))).use { zis ->
                var entry: ZipEntry?
                while (zis.nextEntry.also { entry = it } != null) {
                    val entryPath = entry!!.name.replace('\\', '/')
                    val matchesExact = req.bundledZipPath.isNotBlank() &&
                        (entryPath.equals(req.bundledZipPath.replace('\\', '/'), ignoreCase = true) ||
                         entryPath.substringAfterLast('/').equals(req.bundledZipPath.substringAfterLast('/'), ignoreCase = true))
                    val matchesFallback = entryPath.endsWith(".apk", ignoreCase = true) &&
                        ((req.role.contains("ATAK", true) && entryPath.contains("atak", true) && !entryPath.contains("plugin", true)) ||
                         (req.role.contains("MESH", true) && entryPath.contains("mesh", true) && !entryPath.contains("plugin", true)) ||
                         (req.role.contains("PLUGIN", true) && entryPath.contains("plugin", true)))

                    if (matchesExact || matchesFallback) {
                        FileOutputStream(outApkFile).use { fos ->
                            zis.copyTo(fos)
                        }
                        extracted = true
                        break
                    }
                    zis.closeEntry()
                }
            }

            if (!extracted) {
                throw IllegalStateException("Brak instalatora ${req.displayName} (.apk) wewnątrz tej paczki .ftak.")
            }

            val apkUri = FileProvider.getUriForFile(
                context,
                "${context.packageName}.fileprovider",
                outApkFile
            )

            val installIntent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(apkUri, "application/vnd.android.package-archive")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(installIntent)
        }
    }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/ui/components/TacticalShellComponents.kt===
package com.fieldtak.mobile.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.data.FtakPackageSummary
import com.fieldtak.mobile.ui.theme.*

enum class TacticalTab(val label: String) {
    SCANNER("SKANER"),
    PACKAGES("PACZKI"),
    OPORD("OPORD"),
    CHEATSHEET("ŚCIĄGA")
}

@Composable
fun TacticalTopBar(activePackage: FtakPackageSummary?, onOpenPermissions: () -> Unit) {
    Surface(
        color = TacticalCard,
        shape = RoundedCornerShape(8.dp),
        border = BorderStroke(1.2.dp, RadarCyan),
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 10.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Canvas(modifier = Modifier.size(28.dp)) {
                    val stroke = 2.dp.toPx()
                    drawCircle(color = RadarCyan, radius = size.minDimension / 2.2f, style = Stroke(stroke))
                    drawCircle(color = RadarCyan, radius = size.minDimension / 4.2f, style = Stroke(stroke * 0.8f))
                    drawLine(RadarCyan, Offset(size.width / 2, 0f), Offset(size.width / 2, size.height), stroke * 0.7f)
                    drawLine(RadarCyan, Offset(0f, size.height / 2), Offset(size.width, size.height / 2), stroke * 0.7f)
                }
                Spacer(modifier = Modifier.width(10.dp))
                Column {
                    Text(
                        text = "FIELD TAK HUB",
                        color = TextWhite,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Text(
                        text = "connect · deploy · operate",
                        color = RadarCyan,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }

            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Surface(
                    color = InputTrench,
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.dp, if (activePackage != null) MilSimGreen else MutedGrid),
                    modifier = Modifier.clickable { onOpenPermissions() }
                ) {
                    Text(
                        text = activePackage?.factionName ?: "BRAK PACZKI",
                        color = if (activePackage != null) MilSimGreen else TextMuted,
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
                    )
                }
            }
        }
    }
}

@Composable
fun TacticalBottomBar(currentTab: TacticalTab, onSelectTab: (TacticalTab) -> Unit) {
    Surface(
        color = TacticalCard,
        border = BorderStroke(1.dp, MutedGrid),
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 8.dp, horizontal = 6.dp),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            TacticalTab.values().forEach { tab ->
                val selected = tab == currentTab
                val tint = if (selected) RadarCyan else TextMuted
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    modifier = Modifier
                        .weight(1f)
                        .clickable { onSelectTab(tab) }
                        .padding(vertical = 4.dp)
                ) {
                    TacticalTabVectorIcon(tab = tab, color = tint)
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = tab.label,
                        color = tint,
                        fontSize = 10.sp,
                        fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }
        }
    }
}

@Composable
private fun TacticalTabVectorIcon(tab: TacticalTab, color: Color) {
    Canvas(modifier = Modifier.size(20.dp)) {
        val s = 1.8.dp.toPx()
        val w = size.width
        val h = size.height
        when (tab) {
            TacticalTab.SCANNER -> {
                val len = w * 0.28f
                drawLine(color, Offset(0f, 0f), Offset(len, 0f), s)
                drawLine(color, Offset(0f, 0f), Offset(0f, len), s)
                drawLine(color, Offset(w, 0f), Offset(w - len, 0f), s)
                drawLine(color, Offset(w, 0f), Offset(w, len), s)
                drawLine(color, Offset(0f, h), Offset(len, h), s)
                drawLine(color, Offset(0f, h), Offset(0f, h - len), s)
                drawLine(color, Offset(w, h), Offset(w - len, h), s)
                drawLine(color, Offset(w, h), Offset(w, h - len), s)
                drawLine(color, Offset(w * 0.15f, h / 2f), Offset(w * 0.85f, h / 2f), s)
            }
            TacticalTab.PACKAGES -> {
                drawRect(color = color, topLeft = Offset(w * 0.1f, h * 0.2f), size = Size(w * 0.8f, h * 0.65f), style = Stroke(s))
                drawLine(color, Offset(w * 0.1f, h * 0.42f), Offset(w * 0.9f, h * 0.42f), s)
                drawLine(color, Offset(w * 0.35f, h * 0.08f), Offset(w * 0.65f, h * 0.08f), s)
            }
            TacticalTab.OPORD -> {
                drawRect(color = color, topLeft = Offset(w * 0.15f, h * 0.1f), size = Size(w * 0.7f, h * 0.8f), style = Stroke(s))
                drawLine(color, Offset(w * 0.3f, h * 0.35f), Offset(w * 0.7f, h * 0.35f), s)
                drawLine(color, Offset(w * 0.3f, h * 0.55f), Offset(w * 0.7f, h * 0.55f), s)
                drawLine(color, Offset(w * 0.3f, h * 0.75f), Offset(w * 0.55f, h * 0.75f), s)
            }
            TacticalTab.CHEATSHEET -> {
                drawCircle(color = color, radius = w * 0.38f, style = Stroke(s))
                drawCircle(color = color, radius = w * 0.12f)
                drawLine(color, Offset(w / 2f, 0f), Offset(w / 2f, h * 0.22f), s)
                drawLine(color, Offset(w / 2f, h * 0.78f), Offset(w / 2f, h), s)
                drawLine(color, Offset(0f, h / 2f), Offset(w * 0.22f, h / 2f), s)
                drawLine(color, Offset(w * 0.78f, h / 2f), Offset(w, h / 2f), s)
            }
        }
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
import com.fieldtak.mobile.ui.theme.*

data class TacticalPermissionState(
    val id: String,
    val title: String,
    val desc: String,
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
        TacticalPermissionState("GPS", "Lokalizacja GPS (MGRS)", "Wyliczanie kwadratu MGRS i pozycji do meldunku SOS.", hasGps),
        TacticalPermissionState("CAM", "Dostęp do aparatu (QR)", "Błyskawiczny odczyt kodów QR paczek .ftak z chmury i LAN.", hasCamera),
        TacticalPermissionState("BT", "Łączność Bluetooth & LoRa", "Wstrzykiwanie kanałów, PSK oraz ksywy do Meshtastic.", hasBt),
        TacticalPermissionState("FILES", "Dostęp do plików (.ftak)", "Zapisywanie map .mbtiles/.kmz oraz paczek operacji.", hasStorage),
        TacticalPermissionState("APK", "Instalacja pakietów (.apk)", "Automatyczna instalacja ATAK-CIV, Meshtastic i pluginów.", hasInstallApks)
    )
}

@Composable
fun OnboardingScreen(onContinueToApp: () -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    var permissions by remember { mutableStateOf(evaluatePermissions(context)) }

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
                    color = RadarCyan,
                    fontSize = 11.sp,
                    fontFamily = FontFamily.Monospace
                )
                Text(
                    text = "Onboarding & Permissions ($grantedCount/${permissions.size})",
                    color = TextWhite,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(top = 2.dp, bottom = 12.dp)
                )

                LazyColumn(
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    items(permissions.size) { index ->
                        val perm = permissions[index]
                        Card(
                            colors = CardDefaults.cardColors(containerColor = TacticalCard),
                            shape = RoundedCornerShape(6.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .border(1.2.dp, if (perm.isGranted) MilSimGreen else MutedGrid, RoundedCornerShape(6.dp))
                        ) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(12.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column(modifier = Modifier.weight(1f).padding(end = 8.dp)) {
                                    Text(text = perm.title, color = TextWhite, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                    Text(text = perm.desc, color = TextMuted, fontSize = 11.sp)
                                }
                                Button(
                                    onClick = { requestSinglePermission(perm.id) },
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = if (perm.isGranted) InputTrench else RadarCyan
                                    ),
                                    border = BorderStroke(1.dp, if (perm.isGranted) MilSimGreen else RadarCyan),
                                    shape = RoundedCornerShape(4.dp),
                                    contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp),
                                    modifier = Modifier.height(32.dp)
                                ) {
                                    Text(
                                        text = if (perm.isGranted) "✓ OK" else "NADAJ",
                                        color = if (perm.isGranted) MilSimGreen else AbyssalSlate,
                                        fontSize = 11.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                }
                            }
                        }
                    }
                }
            }

            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                if (grantedCount < permissions.size) {
                    OutlinedButton(
                        onClick = {
                            val firstMissing = permissions.firstOrNull { !it.isGranted }
                            if (firstMissing != null) requestSinglePermission(firstMissing.id)
                        },
                        border = BorderStroke(1.2.dp, RadarCyan),
                        shape = RoundedCornerShape(6.dp),
                        modifier = Modifier.fillMaxWidth().height(44.dp)
                    ) {
                        Text(
                            text = "⚡ NADAJ KOLEJNE BRAKUJĄCE UPRAWNIENIE",
                            color = RadarCyan,
                            fontWeight = FontWeight.Bold,
                            fontSize = 12.sp
                        )
                    }
                }

                Button(
                    onClick = onContinueToApp,
                    colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier.fillMaxWidth().height(48.dp)
                ) {
                    Text(
                        text = "PRZEJDŹ DO SKANERA QR I GRY ->",
                        color = AbyssalSlate,
                        fontWeight = FontWeight.Bold,
                        fontSize = 13.sp
                    )
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

    val filePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val summary = FtakPackageRepository.importFromUri(context, uri)
                if (summary != null) onPackageLoaded(summary)
            }
        }
    }

    var isDownloading by remember { mutableStateOf(false) }
    var progressPct by remember { mutableStateOf(0) }
    var statusMessage by remember { mutableStateOf("Skieruj celownik na kod QR paczki .ftak lub wybierz plik z pamięci.") }

    val savedCallsigns = remember { FtakPackageRepository.loadOperatorCallsign(context) }
    var fullCallsign by remember { mutableStateOf(savedCallsigns.first) }
    var shortCallsign by remember { mutableStateOf(savedCallsigns.second) }

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
                statusMessage = "Pobrano i zweryfikowano paczkę: ${summary.fileName}"
                onPackageLoaded(summary)
                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
            }.onFailure { err ->
                isDownloading = false
                statusMessage = "Błąd QR: ${err.message}"
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
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(8.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.5.dp, RadarCyan, RoundedCornerShape(8.dp))
        ) {
            Column(
                modifier = Modifier.padding(10.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(195.dp)
                        .background(InputTrench, RoundedCornerShape(6.dp))
                        .border(1.dp, MutedGrid, RoundedCornerShape(6.dp)),
                    contentAlignment = Alignment.Center
                ) {
                    if (hasCameraPerm) {
                        CameraQrPreview(onQrScanned = { onQrDetected(it) })
                    } else {
                        Button(
                            onClick = { camPermLauncher.launch(Manifest.permission.CAMERA) },
                            colors = ButtonDefaults.buttonColors(containerColor = RadarCyan)
                        ) {
                            Text("WŁĄCZ APARAT SKANERA QR", color = AbyssalSlate, fontWeight = FontWeight.Bold)
                        }
                    }

                    Box(
                        modifier = Modifier
                            .fillMaxWidth(0.78f)
                            .height(2.dp)
                            .background(RadarCyan)
                    )
                }

                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = statusMessage,
                    color = if (statusMessage.startsWith("Błąd")) DangerCrimson else RadarCyan,
                    fontSize = 11.sp,
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

                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = { filePickerLauncher.launch("*/*") },
                        border = BorderStroke(1.dp, RadarCyan),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(4.dp)
                    ) {
                        Text("📂 WCZYTAJ PLIK .FTAK", color = RadarCyan, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                    OutlinedButton(
                        onClick = {
                            if (activePackage != null) {
                                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, activePackage)
                                statusMessage = "Odświeżono wersje pakietów APK."
                            }
                        },
                        border = BorderStroke(1.dp, MutedGrid),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(4.dp)
                    ) {
                        Text("🔄 SPRAWDŹ WERSJE APK", color = TextWhite, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = "Status integracji (Weryfikacja wersji z paczki .ftak)",
                    color = TextWhite,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Bold
                )

                if (activePackage == null) {
                    Text(
                        text = "Zeskanuj kod QR lub wybierz paczkę .ftak, aby porównać wersje ATAK-CIV, Meshtastic oraz pluginów.",
                        color = TextMuted,
                        fontSize = 11.sp
                    )
                } else {
                    apkStatuses.forEach { st ->
                        val statusColor = when {
                            st.isUpToDate -> MilSimGreen
                            st.isInstalled -> CautionAmber
                            else -> DangerCrimson
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = "● ${st.item.displayName}",
                                    color = statusColor,
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Bold
                                )
                                Text(
                                    text = if (st.isInstalled)
                                        "Zainstalowano: v${st.installedVersion} (Wymagana: v${st.item.requiredVersion})"
                                    else
                                        "Brak w telefonie (Wymagana: v${st.item.requiredVersion})",
                                    color = TextMuted,
                                    fontSize = 10.sp,
                                    fontFamily = FontFamily.Monospace
                                )
                            }

                            if (!st.isUpToDate) {
                                Button(
                                    onClick = {
                                        scope.launch {
                                            val res = ApkCompatibilityEngine.extractAndInstallBundledApk(context, activePackage, st.item)
                                            res.onFailure { err -> statusMessage = err.message ?: "Błąd instalacji APK" }
                                        }
                                    },
                                    colors = ButtonDefaults.buttonColors(containerColor = CautionAmber),
                                    shape = RoundedCornerShape(4.dp),
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp),
                                    modifier = Modifier.height(28.dp)
                                ) {
                                    Text(
                                        text = if (st.isInstalled) "AKTUALIZUJ" else "ZAINSTALUJ",
                                        color = AbyssalSlate,
                                        fontSize = 10.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                }
                            } else {
                                Text(
                                    text = "(OK)",
                                    color = MilSimGreen,
                                    fontSize = 11.sp,
                                    fontWeight = FontWeight.Bold,
                                    fontFamily = FontFamily.Monospace
                                )
                            }
                        }
                    }
                }
            }
        }

        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    text = "ZNAK WYWOŁAWCZY (Callsign ATAK & Skrót Meshtastic)",
                    color = RadarCyan,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedTextField(
                        value = fullCallsign,
                        onValueChange = { newVal ->
                            fullCallsign = newVal
                            shortCallsign = FtakPackageRepository.generateMeshtasticShortName(newVal)
                            FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                        },
                        label = { Text("Callsign ATAK", fontSize = 10.sp) },
                        singleLine = true,
                        modifier = Modifier.weight(1.8f)
                    )

                    OutlinedTextField(
                        value = shortCallsign,
                        onValueChange = { newShort ->
                            shortCallsign = newShort.take(4).uppercase()
                            FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                        },
                        label = { Text("Skrót LoRa (4 zn.)", fontSize = 10.sp) },
                        singleLine = true,
                        modifier = Modifier.weight(1f)
                    )
                }

                Text(
                    text = "Automated preview: [$fullCallsign] -> Meshtastic ShortName: [$shortCallsign]",
                    color = TextMuted,
                    fontSize = 10.sp,
                    fontFamily = FontFamily.Monospace
                )

                Button(
                    onClick = {
                        FtakPackageRepository.saveOperatorCallsign(context, fullCallsign, shortCallsign)
                        statusMessage = "Gotowe do wstrzyknięcia: $fullCallsign ($shortCallsign) [Etap 3]"
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(48.dp)
                ) {
                    Text(
                        text = "↓↑ WSTRZYKNIJ DO ATAK + MESHTASTIC",
                        color = AbyssalSlate,
                        fontWeight = FontWeight.Bold,
                        fontSize = 13.sp
                    )
                }
            }
        }
    }
}

@Composable
@androidx.annotation.OptIn(androidx.camera.core.ExperimentalGetImage::class)
private fun CameraQrPreview(onQrScanned: (String) -> Unit) {
    val context = LocalContext.current
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

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/ui/packages/PackageManagerScreen.kt===
package com.fieldtak.mobile.ui.packages

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
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.data.FtakPackageRepository
import com.fieldtak.mobile.data.FtakPackageSummary
import com.fieldtak.mobile.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun PackageManagerScreen(
    activePackage: FtakPackageSummary?,
    onActivePackageChanged: (FtakPackageSummary?) -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var packages by remember { mutableStateOf(FtakPackageRepository.listInstalledPackages(context)) }

    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val imported = FtakPackageRepository.importFromUri(context, uri)
                packages = FtakPackageRepository.listInstalledPackages(context)
                if (imported != null) onActivePackageChanged(imported)
            }
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(14.dp)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Text(
                text = "MENEDŻER PACZEK .FTAK (${packages.size})",
                color = RadarCyan,
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(bottom = 10.dp)
            )

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(
                    onClick = { filePicker.launch("*/*") },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, RadarCyan),
                    modifier = Modifier.weight(1f).height(38.dp)
                ) {
                    Text(text = "📁 WCZYTAJ Z PLIKU", color = RadarCyan, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
                Button(
                    onClick = { },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, MilSimGreen),
                    modifier = Modifier.weight(1f).height(38.dp)
                ) {
                    Text(text = "📡 HOTSPOT WI-FI", color = MilSimGreen, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }

            if (packages.isEmpty()) {
                Card(
                    colors = CardDefaults.cardColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .border(1.dp, MutedGrid, RoundedCornerShape(6.dp))
                ) {
                    Text(
                        text = "Brak zapisanych paczek .ftak w pamięci telefonu. Zeskanuj kod QR w zakładce SKANER lub kliknij 'Wczytaj z pliku'.",
                        color = TextMuted,
                        fontSize = 12.sp,
                        modifier = Modifier.padding(16.dp)
                    )
                }
            } else {
                LazyColumn(
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.weight(1f)
                ) {
                    items(packages.size) { index ->
                        val pkg = packages[index]
                        val isCurrent = activePackage?.fileName == pkg.fileName
                        Card(
                            colors = CardDefaults.cardColors(containerColor = TacticalCard),
                            shape = RoundedCornerShape(6.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .border(1.3.dp, if (isCurrent) RadarCyan else MutedGrid, RoundedCornerShape(6.dp))
                                .clickable {
                                    FtakPackageRepository.setActivePackageFileName(context, pkg.fileName)
                                    onActivePackageChanged(pkg)
                                }
                        ) {
                            Column(modifier = Modifier.padding(12.dp)) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Column(modifier = Modifier.weight(1f)) {
                                        Text(
                                            text = "${pkg.eventName} — ${pkg.factionName}",
                                            color = TextWhite,
                                            fontSize = 13.sp,
                                            fontWeight = FontWeight.Bold
                                        )
                                        Text(
                                            text = "Plik: ${pkg.fileName} (${pkg.formattedSize})",
                                            color = TextMuted,
                                            fontSize = 10.sp,
                                            fontFamily = FontFamily.Monospace
                                        )
                                    }

                                    Row(
                                        verticalAlignment = Alignment.CenterVertically,
                                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                                    ) {
                                        if (isCurrent) {
                                            Surface(
                                                color = InputTrench,
                                                shape = RoundedCornerShape(4.dp),
                                                border = BorderStroke(1.dp, MilSimGreen)
                                            ) {
                                                Text(
                                                    text = "AKTYWNA",
                                                    color = MilSimGreen,
                                                    fontSize = 9.sp,
                                                    fontWeight = FontWeight.Bold,
                                                    modifier = Modifier.padding(horizontal = 6.dp, vertical = 3.dp)
                                                )
                                            }
                                        }

                                        Surface(
                                            color = InputTrench,
                                            shape = RoundedCornerShape(4.dp),
                                            border = BorderStroke(1.dp, DangerCrimson),
                                            modifier = Modifier
                                                .size(28.dp)
                                                .clickable {
                                                    FtakPackageRepository.deletePackage(context, pkg.fileName)
                                                    packages = FtakPackageRepository.listInstalledPackages(context)
                                                    onActivePackageChanged(packages.firstOrNull())
                                                }
                                        ) {
                                            Box(contentAlignment = Alignment.Center) {
                                                Text(
                                                    text = "−",
                                                    color = DangerCrimson,
                                                    fontSize = 15.sp,
                                                    fontWeight = FontWeight.Bold
                                                )
                                            }
                                        }
                                    }
                                }

                                Spacer(modifier = Modifier.height(4.dp))
                                Text(
                                    text = "OTS: ${pkg.otsHost} | LoRa: ${pkg.loraRegion} (${pkg.loraPreset})",
                                    color = RadarCyan,
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
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtak/mobile/MainActivity.kt===
package com.fieldtak.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.data.FtakPackageRepository
import com.fieldtak.mobile.ui.components.TacticalBottomBar
import com.fieldtak.mobile.ui.components.TacticalTab
import com.fieldtak.mobile.ui.components.TacticalTopBar
import com.fieldtak.mobile.ui.onboarding.OnboardingScreen
import com.fieldtak.mobile.ui.onboarding.evaluatePermissions
import com.fieldtak.mobile.ui.packages.PackageManagerScreen
import com.fieldtak.mobile.ui.scanner.ScannerScreen
import com.fieldtak.mobile.ui.theme.AbyssalSlate
import com.fieldtak.mobile.ui.theme.FieldTakHubTheme
import com.fieldtak.mobile.ui.theme.RadarCyan

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FieldTakHubTheme {
                val context = LocalContext.current
                val initialPerms = remember { evaluatePermissions(context) }
                var showOnboarding by remember {
                    mutableStateOf(initialPerms.any { !it.isGranted })
                }

                var currentTab by remember { mutableStateOf(TacticalTab.SCANNER) }
                var activePackage by remember {
                    val all = FtakPackageRepository.listInstalledPackages(context)
                    val savedName = FtakPackageRepository.getActivePackageFileName(context)
                    mutableStateOf(all.firstOrNull { it.fileName == savedName } ?: all.firstOrNull())
                }

                if (showOnboarding) {
                    OnboardingScreen(onContinueToApp = { showOnboarding = false })
                } else {
                    Scaffold(
                        containerColor = AbyssalSlate,
                        topBar = {
                            TacticalTopBar(
                                activePackage = activePackage,
                                onOpenPermissions = { showOnboarding = true }
                            )
                        },
                        bottomBar = {
                            TacticalBottomBar(
                                currentTab = currentTab,
                                onSelectTab = { currentTab = it }
                            )
                        }
                    ) { innerPadding ->
                        Box(
                            modifier = Modifier
                                .fillMaxSize()
                                .background(AbyssalSlate)
                                .padding(innerPadding)
                        ) {
                            when (currentTab) {
                                TacticalTab.SCANNER -> {
                                    ScannerScreen(
                                        activePackage = activePackage,
                                        onPackageLoaded = { loaded -> activePackage = loaded }
                                    )
                                }
                                TacticalTab.PACKAGES -> {
                                    PackageManagerScreen(
                                        activePackage = activePackage,
                                        onActivePackageChanged = { updated -> activePackage = updated }
                                    )
                                }
                                TacticalTab.OPORD -> {
                                    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
                                        Text("Moduł Czytnika OPORD (Dni + Mapa Załącznik 3) — Etap 4", color = RadarCyan, fontSize = 13.sp)
                                    }
                                }
                                TacticalTab.CHEATSHEET -> {
                                    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
                                        Text("Moduł Ściągi Taktycznej & Alarmu SOS MGRS — Etap 4", color = RadarCyan, fontSize = 13.sp)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
//===END_FILE===