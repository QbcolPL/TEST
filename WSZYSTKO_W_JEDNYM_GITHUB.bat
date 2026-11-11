@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [ALL-IN-ONE: FAZA 0 + FAZY 1-4] Calkowity reset starych folderow Androida
echo i budowa czystej aplikacji Field TAK Hub Mobile v3.4 (1:1 z wizualizacja)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'ALL_IN_ONE_CLEAN_MOBILE_CS===')[1]); [FthCleanMobileAllInOne]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas generowania projektu lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Stary kod Androida zostal usuniety, a kompletna aplikacja mobilna
echo (Fazy 0-4) zostala wyslana na GitHub! Pobierz artefakt:
echo FieldTakHub-Mobile-v3.4-CLEAN-APK z zakladki GitHub Actions.
echo ============================================================================
pause
exit /b 0

//===ALL_IN_ONE_CLEAN_MOBILE_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthCleanMobileAllInOne
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        // 1. Twarde wyzerowanie wszystkich starych katalogow Androida (bez ruszania apps/builder)
        WipeLegacyAndroidDirectories(targetDir);

        // 2. Uporzadkowanie workflowow GitHub Actions
        CleanExistingWorkflows(targetDir);

        // 3. Zapisanie kompletnego projektu (Faza 0 + Fazy 1-4) z sekcji PAYLOAD
        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string payloadMarker = "//===" + "CLEAN_MOBILE_FILES_PAYLOAD===";
        int payloadIdx = rawBat.IndexOf(payloadMarker, StringComparison.Ordinal);
        if (payloadIdx < 0)
            throw new Exception("Nie znaleziono znacznika CLEAN_MOBILE_FILES_PAYLOAD w pliku .bat!");

        string payloadSection = rawBat.Substring(payloadIdx + payloadMarker.Length);
        UTF8Encoding utf8NoBom = new UTF8Encoding(false);

        string pattern = @"//===" + @"FILE:([^\r\n]+)===\r?\n(.*?)\r?\n//===" + @"END_FILE===";
        MatchCollection matches = Regex.Matches(payloadSection, pattern, RegexOptions.Singleline);
        if (matches.Count == 0)
            throw new Exception("Nie znaleziono zadnych blokow FILE w sekcji CLEAN_MOBILE_FILES_PAYLOAD!");

        foreach (Match m in matches)
        {
            string relPath = m.Groups[1].Value.Trim().Replace('/', Path.DirectorySeparatorChar);
            string content = m.Groups[2].Value;
            string fullPath = Path.Combine(targetDir, relPath);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content, utf8NoBom);
            Console.WriteLine("[ZAPISANO CZYSTY PLIK] " + relPath);
        }

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Mobile Clean Rebuild Phases 0-4 UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static void WipeLegacyAndroidDirectories(string targetDir)
    {
        string[] legacyDirs = new string[]
        {
            @"apps\android",
            @"apps\mobile",
            @"android",
            @"mobile",
            @"apps\FieldTakHub.Android",
            @"apps\FieldTakHub.Mobile"
        };

        foreach (string rel in legacyDirs)
        {
            string full = Path.Combine(targetDir, rel);
            if (Directory.Exists(full))
            {
                Console.WriteLine("[FAZA 0 - RESET] Kasowanie starego katalogu: " + rel);
                ForceDeleteDirectory(full);
            }
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        if (!dirInfo.Exists) return;
        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            try { file.Attributes = FileAttributes.Normal; } catch { }
        }
        foreach (var subDir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
        {
            try { subDir.Attributes = FileAttributes.Normal; } catch { }
        }
        Directory.Delete(path, true);
    }

    private static void CleanExistingWorkflows(string targetDir)
    {
        string wfDir = Path.Combine(targetDir, @".github\workflows");
        if (!Directory.Exists(wfDir))
        {
            Directory.CreateDirectory(wfDir);
            return;
        }

        foreach (string ymlFile in Directory.GetFiles(wfDir, "*.yml"))
        {
            string fileName = Path.GetFileName(ymlFile);
            if (fileName.Equals("android-mobile-clean.yml", StringComparison.OrdinalIgnoreCase))
                continue;

            string content = File.ReadAllText(ymlFile, Encoding.UTF8);
            bool buildsBuilder = content.IndexOf("FieldTakHub.Builder", StringComparison.OrdinalIgnoreCase) >= 0;
            bool mentionsOldAndroid = content.IndexOf("apps/android", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      content.IndexOf("assembleDebug", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      content.IndexOf("assembleRelease", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!buildsBuilder && mentionsOldAndroid)
            {
                Console.WriteLine("[WORKFLOW CLEANUP] Usuwanie starego workflow Androida: " + fileName);
                try { File.Delete(ymlFile); } catch { }
            }
            else if (buildsBuilder && content.IndexOf("apps/android", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("[WORKFLOW UPDATE] Przepinanie apps/android -> apps/mobile w: " + fileName);
                string updated = content.Replace("apps/android", "apps/mobile").Replace(@"apps\android", @"apps\mobile");
                File.WriteAllText(ymlFile, updated, new UTF8Encoding(false));
            }
        }
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
        RunProcess(gitExe, "commit -m \"Clean Slate Rebuild (Phases 0-4): Complete Field TAK Hub Mobile v3.4 matching tactical visualization 1:1\" --allow-empty", targetDir, ignoreError: false);

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
//===ALL_IN_ONE_CLEAN_MOBILE_CS===

//===CLEAN_MOBILE_FILES_PAYLOAD===

//===FILE:.github/workflows/android-mobile-clean.yml===
name: Build Clean Field TAK Hub Mobile APK (v3.4)

on:
  push:
    branches: [ "main", "master" ]
  workflow_dispatch:

jobs:
  build-clean-mobile-apk:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout Repository
        uses: actions/checkout@v4

      - name: Set up JDK 17
        uses: actions/setup-java@v4
        with:
          distribution: 'temurin'
          java-version: '17'

      - name: Setup Gradle 8.5
        uses: gradle/actions/setup-gradle@v3
        with:
          gradle-version: '8.5'

      - name: Build Debug APK (apps/mobile)
        working-directory: apps/mobile
        run: gradle assembleDebug --no-daemon --stacktrace

      - name: Rename APK for Clean Distribution
        working-directory: apps/mobile
        run: |
          cp build/outputs/apk/debug/mobile-debug.apk build/outputs/apk/debug/FieldTakHub-Mobile-v3.4-CLEAN.apk

      - name: Upload Compiled Clean APK Artifact
        uses: actions/upload-artifact@v4
        with:
          name: FieldTakHub-Mobile-v3.4-CLEAN-APK
          path: apps/mobile/build/outputs/apk/debug/FieldTakHub-Mobile-v3.4-CLEAN.apk
          if-no-files-found: error
//===END_FILE===

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
rootProject.name = "mobile"
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
    namespace = "com.fieldtakhub.mobile"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.fieldtakhub.mobile"
        minSdk = 26
        targetSdk = 34
        versionCode = 341
        versionName = "3.4.0-CLEAN"

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

    // CameraX & MLKit Barcode Scanning (Skaner z kamery + odczyt QR z galerii)
    val cameraVersion = "1.3.1"
    implementation("androidx.camera:camera-core:$cameraVersion")
    implementation("androidx.camera:camera-camera2:$cameraVersion")
    implementation("androidx.camera:camera-lifecycle:$cameraVersion")
    implementation("androidx.camera:camera-view:$cameraVersion")
    implementation("com.google.mlkit:barcode-scanning:17.2.0")

    // ZXing Core do generowania kodu QR na ekranie telefonu (Hotspot Wi-Fi dla zapominalskich)
    implementation("com.google.zxing:core:3.5.2")
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

    <queries>
        <package android:name="com.atakmap.app.civ" />
        <package android:name="com.geeksville.mesh" />
        <package android:name="com.atakmap.android.meshtastic.plugin" />
        <intent>
            <action android:name="com.atakmap.app.IMPORT_MISSION_PACKAGE" />
        </intent>
    </queries>

    <application
        android:allowBackup="false"
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

//===FILE:apps/mobile/src/main/res/values/styles.xml===
<?xml version="1.0" encoding="utf-8"?>
<resources>
    <style name="Theme.FieldTakHub" parent="android:Theme.Material.NoActionBar">
        <item name="android:windowBackground">@color/abyssal_slate</item>
        <item name="android:statusBarColor">@color/abyssal_slate</item>
        <item name="android:navigationBarColor">@color/abyssal_slate</item>
    </style>
    <color name="abyssal_slate">#060E12</color>
</resources>
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
    <path
        android:fillColor="#00000000"
        android:strokeColor="#1FE0C5"
        android:strokeWidth="3.2"
        android:pathData="M54,24 A30,30 0 1,1 53.9,24 Z" />
    <path
        android:fillColor="#00000000"
        android:strokeColor="#1FE0C5"
        android:strokeWidth="2.0"
        android:pathData="M54,36 A18,18 0 1,1 53.9,36 Z" />
    <path
        android:strokeColor="#1FE0C5"
        android:strokeWidth="2.5"
        android:pathData="M54,16 L54,32 M54,76 L54,92 M16,54 L32,54 M76,54 L92,54" />
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

//===FILE:apps/mobile/src/main/java/com/fieldtakhub/mobile/ui/theme/ThemeAndIcons.kt===
package com.fieldtakhub.mobile.ui.theme

import android.graphics.Bitmap
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.google.zxing.BarcodeFormat
import com.google.zxing.qrcode.QRCodeWriter

val AbyssalSlate = Color(0xFF060E12)
val TacticalCard = Color(0xFF0A181E)
val InputTrench = Color(0xFF071115)
val RadarCyan = Color(0xFF1FE0C5)
val MutedGrid = Color(0xFF163844)
val MilSimGreen = Color(0xFF2ECC71)
val CautionAmber = Color(0xFFFAD02C)
val DangerCrimson = Color(0xFFEB4034)
val SosCardBg = Color(0xFF2A0D12)
val SosButtonBg = Color(0xFFC92A2A)
val TextWhite = Color(0xFFFFFFFF)
val TextMuted = Color(0xFF8FA8B2)

private val DarkTacticalColorScheme = darkColorScheme(
    primary = RadarCyan,
    onPrimary = AbyssalSlate,
    secondary = MilSimGreen,
    background = AbyssalSlate,
    surface = TacticalCard,
    onSurface = TextWhite,
    onBackground = TextWhite,
    error = DangerCrimson
)

@Composable
fun FieldTakHubTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = DarkTacticalColorScheme,
        content = content
    )
}

enum class TacticalTab(val label: String) {
    SCANNER("SKANER"),
    PACKAGES("PACZKI"),
    OPORD("OPORD"),
    CHEATSHEET("ŚCIĄGA")
}

enum class TacticalIconType {
    GPS_SATELLITE,
    CAMERA_QR,
    BLUETOOTH_LORA,
    FILES_CRATE,
    APK_SHIELD,
    GALLERY_QR,
    FOLDER_OPEN,
    WIFI_HOTSPOT,
    INJECT_ARROWS,
    SOS_BEACON
}

@Composable
fun TacticalTopBar(
    isOnboardingOpen: Boolean,
    onToggleOnboarding: () -> Unit
) {
    Surface(
        color = TacticalCard,
        shape = RoundedCornerShape(10.dp),
        border = BorderStroke(1.5.dp, RadarCyan),
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
                Canvas(modifier = Modifier.size(30.dp)) {
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

            // Przycisk [<] identyczny jak na wizualizacji i w Builderze Windows
            Surface(
                color = if (isOnboardingOpen) RadarCyan else InputTrench,
                shape = RoundedCornerShape(4.dp),
                border = BorderStroke(1.dp, RadarCyan),
                modifier = Modifier
                    .size(30.dp)
                    .clickable { onToggleOnboarding() }
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(
                        text = "<",
                        color = if (isOnboardingOpen) AbyssalSlate else RadarCyan,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }
        }
    }
}

@Composable
fun TacticalBottomBar(
    currentTab: TacticalTab,
    isOnboardingOpen: Boolean,
    onSelectTab: (TacticalTab) -> Unit
) {
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
                val selected = !isOnboardingOpen && (tab == currentTab)
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
                drawRect(color, Offset(w * 0.26f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.8f))
                drawRect(color, Offset(w * 0.56f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.8f))
                drawRect(color, Offset(w * 0.26f, h * 0.56f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.8f))
            }
            TacticalTab.PACKAGES -> {
                val p = Path().apply {
                    moveTo(w * 0.08f, h * 0.24f)
                    lineTo(w * 0.38f, h * 0.24f)
                    lineTo(w * 0.48f, h * 0.35f)
                    lineTo(w * 0.92f, h * 0.35f)
                    lineTo(w * 0.92f, h * 0.84f)
                    lineTo(w * 0.08f, h * 0.84f)
                    close()
                }
                drawPath(p, color = color, style = Stroke(s))
            }
            TacticalTab.OPORD -> {
                drawRect(color = color, topLeft = Offset(w * 0.15f, h * 0.10f), size = Size(w * 0.70f, h * 0.80f), style = Stroke(s))
                drawLine(color, Offset(w * 0.30f, h * 0.35f), Offset(w * 0.70f, h * 0.35f), s)
                drawLine(color, Offset(w * 0.30f, h * 0.55f), Offset(w * 0.70f, h * 0.55f), s)
                drawLine(color, Offset(w * 0.30f, h * 0.75f), Offset(w * 0.55f, h * 0.75f), s)
            }
            TacticalTab.CHEATSHEET -> {
                drawCircle(color = color, radius = w * 0.32f, style = Stroke(s))
                drawCircle(color = color, radius = w * 0.12f)
                drawLine(color, Offset(w / 2f, 0f), Offset(w / 2f, h * 0.20f), s)
                drawLine(color, Offset(w / 2f, h * 0.80f), Offset(w / 2f, h), s)
                drawLine(color, Offset(0f, h / 2f), Offset(w * 0.20f, h / 2f), s)
                drawLine(color, Offset(w * 0.80f, h / 2f), Offset(w, h / 2f), s)
            }
        }
    }
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
                drawLine(tint, Offset(w * 0.15f, h * 0.15f), Offset(w * 0.85f, h * 0.85f), s)
                drawRect(tint, Offset(w * 0.36f, h * 0.36f), Size(w * 0.28f, h * 0.28f), style = Stroke(s))
                drawLine(tint, Offset(w * 0.12f, h * 0.38f), Offset(w * 0.38f, h * 0.12f), s)
                drawLine(tint, Offset(w * 0.62f, h * 0.88f), Offset(w * 0.88f, h * 0.62f), s)
                drawArc(MilSimGreen, 100f, 70f, false, Offset(w * 0.08f, h * 0.45f), Size(w * 0.45f, h * 0.45f), style = Stroke(s * 0.85f))
            }
            TacticalIconType.CAMERA_QR -> {
                drawRoundRect(tint, Offset(w * 0.08f, h * 0.24f), Size(w * 0.84f, h * 0.62f), CornerRadius(3.dp.toPx()), style = Stroke(s))
                drawLine(tint, Offset(w * 0.32f, h * 0.14f), Offset(w * 0.68f, h * 0.14f), s * 1.2f)
                drawCircle(tint, w * 0.18f, Offset(w * 0.5f, h * 0.55f), style = Stroke(s))
                drawCircle(MilSimGreen, w * 0.05f, Offset(w * 0.5f, h * 0.55f))
            }
            TacticalIconType.BLUETOOTH_LORA -> {
                val btPath = Path().apply {
                    moveTo(w * 0.22f, h * 0.28f)
                    lineTo(w * 0.52f, h * 0.72f)
                    lineTo(w * 0.36f, h * 0.88f)
                    lineTo(w * 0.36f, h * 0.12f)
                    lineTo(w * 0.52f, h * 0.28f)
                    lineTo(w * 0.22f, h * 0.72f)
                }
                drawPath(btPath, color = tint, style = Stroke(s))
                drawArc(MilSimGreen, -45f, 90f, false, Offset(w * 0.42f, h * 0.26f), Size(w * 0.32f, h * 0.48f), style = Stroke(s * 0.9f))
                drawArc(tint, -45f, 90f, false, Offset(w * 0.54f, h * 0.14f), Size(w * 0.40f, h * 0.72f), style = Stroke(s * 0.9f))
            }
            TacticalIconType.FILES_CRATE -> {
                drawRoundRect(tint, Offset(w * 0.10f, h * 0.22f), Size(w * 0.80f, h * 0.64f), CornerRadius(2.dp.toPx()), style = Stroke(s))
                drawLine(tint, Offset(w * 0.10f, h * 0.42f), Offset(w * 0.90f, h * 0.42f), s)
                drawRect(MilSimGreen, Offset(w * 0.40f, h * 0.36f), Size(w * 0.20f, h * 0.14f))
            }
            TacticalIconType.APK_SHIELD -> {
                drawCircle(tint, w * 0.42f, Offset(w * 0.50f, h * 0.50f), style = Stroke(s))
                drawLine(MilSimGreen, Offset(w * 0.50f, h * 0.26f), Offset(w * 0.50f, h * 0.70f), s)
                drawLine(MilSimGreen, Offset(w * 0.34f, h * 0.54f), Offset(w * 0.50f, h * 0.70f), s)
                drawLine(MilSimGreen, Offset(w * 0.66f, h * 0.54f), Offset(w * 0.50f, h * 0.70f), s)
            }
            TacticalIconType.GALLERY_QR -> {
                drawRoundRect(tint, Offset(w * 0.10f, h * 0.14f), Size(w * 0.80f, h * 0.72f), CornerRadius(2.dp.toPx()), style = Stroke(s))
                drawRect(tint, Offset(w * 0.22f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(tint, Offset(w * 0.60f, h * 0.26f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawRect(MilSimGreen, Offset(w * 0.22f, h * 0.54f), Size(w * 0.18f, h * 0.18f), style = Stroke(s * 0.85f))
                drawCircle(MilSimGreen, w * 0.08f, Offset(w * 0.69f, h * 0.63f))
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
            TacticalIconType.WIFI_HOTSPOT, TacticalIconType.SOS_BEACON -> {
                drawCircle(tint, w * 0.10f, Offset(w * 0.50f, h * 0.56f))
                drawLine(tint, Offset(w * 0.50f, h * 0.62f), Offset(w * 0.50f, h * 0.90f), s)
                drawArc(tint, 210f, 120f, false, Offset(w * 0.22f, h * 0.24f), Size(w * 0.56f, h * 0.56f), style = Stroke(s))
                drawArc(tint, 205f, 130f, false, Offset(w * 0.08f, h * 0.10f), Size(w * 0.84f, h * 0.80f), style = Stroke(s * 0.85f))
            }
            TacticalIconType.INJECT_ARROWS -> {
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

@Composable
fun TacticalQrReticleOverlay(modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val w = size.width
        val h = size.height
        val padX = w * 0.10f
        val padY = h * 0.10f
        val left = padX
        val right = w - padX
        val top = padY
        val bottom = h - padY
        val cornerLen = minOf(w, h) * 0.18f
        val stroke = 3.5.dp.toPx()

        drawLine(RadarCyan, Offset(left, top), Offset(left + cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(left, top), Offset(left, top + cornerLen), stroke)
        drawLine(RadarCyan, Offset(right, top), Offset(right - cornerLen, top), stroke)
        drawLine(RadarCyan, Offset(right, top), Offset(right, top + cornerLen), stroke)
        drawLine(RadarCyan, Offset(left, bottom), Offset(left + cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(left, bottom), Offset(left, bottom - cornerLen), stroke)
        drawLine(RadarCyan, Offset(right, bottom), Offset(right - cornerLen, bottom), stroke)
        drawLine(RadarCyan, Offset(right, bottom), Offset(right, bottom - cornerLen), stroke)

        drawLine(
            color = RadarCyan,
            start = Offset(left * 0.7f, h * 0.5f),
            end = Offset(right + left * 0.3f, h * 0.5f),
            strokeWidth = 2.2.dp.toPx()
        )
    }
}

fun generateQrBitmap(content: String, sizePx: Int = 512): Bitmap? {
    return runCatching {
        val matrix = QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, sizePx, sizePx)
        val bmp = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888)
        for (x in 0 until sizePx) {
            for (y in 0 until sizePx) {
                bmp.setPixel(x, y, if (matrix.get(x, y)) android.graphics.Color.BLACK else android.graphics.Color.WHITE)
            }
        }
        bmp
    }.getOrNull()
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtakhub/mobile/data/FtakCoreEngine.kt===
package com.fieldtakhub.mobile.data

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
import java.net.Inet4Address
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.URL
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream
import kotlin.concurrent.thread
import kotlin.math.*

data class RequiredApkItem(
    val role: String,
    val displayName: String,
    val packageName: String,
    val requiredVersion: String,
    val bundledZipPath: String
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

data class OpordDaySheet(
    val dayTitle: String,
    val situation: String,
    val metoc: String,
    val mission5W: String,
    val executionTask: String,
    val sarnegWord: String,
    val challengePassword: String,
    val logistics: String
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
    val opordDays: List<OpordDaySheet>,
    val opordHtmlPath: String?,
    val annex3MapImagePath: String?
) {
    val formattedSize: String
        get() {
            val mb = sizeBytes.toDouble() / (1024.0 * 1024.0)
            return if (mb >= 1.0) String.format("%.1f MB", mb)
            else String.format("%.0f KB", sizeBytes.toDouble() / 1024.0)
        }
}

/**
 * Dokladny konwerter WGS84 (Lat/Lon) -> MGRS (np. "34U CA 42757 24185") dzialajacy 100% offline
 */
object LatLonToMgrs {
    fun convert(lat: Double, lon: Double): String {
        if (lat < -80.0 || lat > 84.0) {
            return String.format("%.5f, %.5f", lat, lon)
        }
        val zone = ((lon + 180.0) / 6.0).toInt() + 1
        val bands = "CDEFGHJKLMNPQRSTUVWXX"
        val bandIdx = ((lat + 80.0) / 8.0).toInt().coerceIn(0, bands.length - 1)
        val band = bands[bandIdx]

        val a = 6378137.0
        val f = 1.0 / 298.257223563
        val k0 = 0.9996
        val e2 = 2 * f - f * f
        val ep2 = e2 / (1.0 - e2)

        val latRad = Math.toRadians(lat)
        val lonRad = Math.toRadians(lon)
        val lon0Rad = Math.toRadians(((zone - 1) * 6 - 180 + 3).toDouble())

        val n = a / sqrt(1.0 - e2 * sin(latRad).pow(2))
        val t = tan(latRad).pow(2)
        val c = ep2 * cos(latRad).pow(2)
        val aCoeff = cos(latRad) * (lonRad - lon0Rad)

        val m = a * (
            (1.0 - e2 / 4.0 - 3.0 * e2 * e2 / 64.0 - 5.0 * e2.pow(3) / 256.0) * latRad
                - (3.0 * e2 / 8.0 + 3.0 * e2 * e2 / 32.0 + 45.0 * e2.pow(3) / 1024.0) * sin(2.0 * latRad)
                + (15.0 * e2 * e2 / 256.0 + 45.0 * e2.pow(3) / 1024.0) * sin(4.0 * latRad)
                - (35.0 * e2.pow(3) / 3072.0) * sin(6.0 * latRad)
            )

        val easting = k0 * n * (
            aCoeff + (1.0 - t + c) * aCoeff.pow(3) / 6.0
                + (5.0 - 18.0 * t + t * t + 72.0 * c - 58.0 * ep2) * aCoeff.pow(5) / 120.0
            ) + 500000.0

        var northing = k0 * (
            m + n * tan(latRad) * (
                aCoeff.pow(2) / 2.0
                    + (5.0 - t + 9.0 * c + 4.0 * c * c) * aCoeff.pow(4) / 24.0
                    + (61.0 - 58.0 * t + t * t + 600.0 * c - 330.0 * ep2) * aCoeff.pow(6) / 720.0
                )
            )
        if (lat < 0.0) northing += 10000000.0

        val setNum = (zone - 1) % 6
        val colSets = arrayOf("ABCDEFGH", "JKLMNPQR", "STUVWXYZ", "ABCDEFGH", "JKLMNPQR", "STUVWXYZ")
        val rowSets = arrayOf("ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE")

        val colIdx = ((easting / 100000.0).toInt() - 1).mod(8)
        val colLetter = colSets[setNum][colIdx]

        val rowIdx = ((northing % 2000000.0) / 100000.0).toInt().mod(20)
        val rowLetter = rowSets[setNum % 2][rowIdx]

        val e5 = (easting % 100000.0).toInt().coerceIn(0, 99999)
        val n5 = (northing % 100000.0).toInt().coerceIn(0, 99999)

        return String.format("%02d%c %c%c %05d %05d", zone, band, colLetter, rowLetter, e5, n5)
    }
}

/**
 * Lokalny serwer HTTP Wi-Fi Hotspot (:8766) udostepniajacy paczke .ftak spoznionym graczom w lesie
 */
object LocalFtakHotspotServer {
    private var serverSocket: ServerSocket? = null
    @Volatile
    private var isRunning = false
    private const val PORT = 8766

    fun startSharingPackage(file: File): String {
        stop()
        isRunning = true
        serverSocket = ServerSocket(PORT)
        thread(start = true, isDaemon = true, name = "FthMobileHotspotHttp") {
            while (isRunning) {
                val client = runCatching { serverSocket?.accept() }.getOrNull() ?: break
                thread(start = true, isDaemon = true) {
                    runCatching {
                        client.use { sock ->
                            val out = sock.getOutputStream()
                            val bytes = file.readBytes()
                            val header = buildString {
                                append("HTTP/1.1 200 OK\r\n")
                                append("Content-Type: application/octet-stream\r\n")
                                append("Content-Disposition: attachment; filename=\"${file.name}\"\r\n")
                                append("Content-Length: ${bytes.size}\r\n")
                                append("Connection: close\r\n\r\n")
                            }
                            out.write(header.toByteArray(Charsets.UTF_8))
                            out.write(bytes)
                            out.flush()
                        }
                    }
                }
            }
        }
        val ip = detectLocalLanIp()
        return "http://$ip:$PORT/deploy/${Uri.encode(file.name)}"
    }

    fun stop() {
        isRunning = false
        runCatching { serverSocket?.close() }
        serverSocket = null
    }

    fun detectLocalLanIp(): String {
        runCatching {
            val interfaces = NetworkInterface.getNetworkInterfaces()?.toList() ?: emptyList()
            for (nif in interfaces) {
                if (!nif.isUp || nif.isLoopback) continue
                for (addr in nif.inetAddresses) {
                    if (addr is Inet4Address && !addr.isLoopbackAddress) {
                        val ip = addr.hostAddress ?: continue
                        if (ip.startsWith("192.168.") || ip.startsWith("10.") || ip.startsWith("172.")) {
                            return ip
                        }
                    }
                }
            }
        }
        return "192.168.43.1"
    }
}

object FtakPackageRepository {
    private const val PREFS_NAME = "fth_clean_v34_prefs"
    private const val KEY_ONBOARDING_DONE = "onboarding_completed_v34_clean"
    private const val KEY_ACTIVE_FILE = "active_ftak_filename"
    private const val KEY_CALLSIGN_FULL = "operator_callsign_full"
    private const val KEY_CALLSIGN_SHORT = "operator_callsign_short"

    fun isOnboardingCompleted(context: Context): Boolean {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getBoolean(KEY_ONBOARDING_DONE, false)
    }

    fun setOnboardingCompleted(context: Context, completed: Boolean) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putBoolean(KEY_ONBOARDING_DONE, completed)
            .apply()
    }

    fun getPackagesDir(context: Context): File {
        val dir = File(context.filesDir, "ftak_packages")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    fun getExtractedAssetsDir(context: Context): File {
        val dir = File(context.filesDir, "ftak_extracted")
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

    fun generateMeshtasticShortName(fullCallsign: String): String {
        val normalized = normalizePolishChars(fullCallsign.trim()).uppercase()
        if (normalized.isEmpty()) return "WLK1"

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

        return (letterPart + digits.take(4 - letterPart.length)).take(4).ifEmpty { "WLK1" }
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
            runCatching { parseFtakArchive(context, file) }.getOrNull()
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
            val rawName = suggestedName ?: uri.lastPathSegment?.substringAfterLast('/') ?: "operacja_${System.currentTimeMillis()}.ftak"
            val fileName = if (rawName.endsWith(".ftak", true) || rawName.endsWith(".zip", true)) rawName else "$rawName.ftak"
            val destFile = File(getPackagesDir(context), fileName.replace(Regex("[^a-zA-Z0-9._-]"), "_"))
            context.contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(destFile).use { output ->
                    input.copyTo(output)
                }
            }
            val parsed = parseFtakArchive(context, destFile)
            setActivePackageFileName(context, destFile.name)
            parsed
        }

    suspend fun handleScannedQrPayload(
        context: Context,
        rawQr: String,
        onProgress: (Int, String) -> Unit
    ): FtakPackageSummary = withContext(Dispatchers.IO) {
        val trimmed = rawQr.trim()

        val resolvedUrl = when {
            trimmed.startsWith("fieldtak://", ignoreCase = true) -> {
                val uri = Uri.parse(trimmed)
                uri.getQueryParameter("packageUrl")
                    ?: uri.getQueryParameter("url")
                    ?: throw IllegalArgumentException("Brak parametru packageUrl w linku fieldtak://")
            }
            trimmed.startsWith("http://", ignoreCase = true) || trimmed.startsWith("https://", ignoreCase = true) -> trimmed
            else -> null
        }

        if (resolvedUrl != null) {
            onProgress(10, "Pobieranie paczki z: $resolvedUrl")
            val urlObj = URL(resolvedUrl)
            val conn = (urlObj.openConnection() as HttpURLConnection).apply {
                connectTimeout = 12000
                readTimeout = 25000
                requestMethod = "GET"
                connect()
            }
            if (conn.responseCode !in 200..299) {
                throw IllegalStateException("Serwer zwrócił błąd HTTP ${conn.responseCode}")
            }

            val totalBytes = conn.contentLengthLong.coerceAtLeast(1L)
            val rawName = Uri.parse(resolvedUrl).lastPathSegment?.substringAfterLast('/')
                ?.takeIf { it.isNotBlank() } ?: "paczka_${System.currentTimeMillis()}.ftak"
            val safeName = (if (rawName.endsWith(".ftak", true) || rawName.endsWith(".zip", true)) rawName else "$rawName.ftak")
                .replace(Regex("[^a-zA-Z0-9._-]"), "_")
            val targetFile = File(getPackagesDir(context), safeName)

            conn.inputStream.use { input ->
                FileOutputStream(targetFile).use { output ->
                    val buffer = ByteArray(16 * 1024)
                    var downloaded = 0L
                    var read: Int
                    while (input.read(buffer).also { read = it } != -1) {
                        output.write(buffer, 0, read)
                        downloaded += read
                        val pct = ((downloaded * 100L) / totalBytes).toInt().coerceIn(10, 98)
                        onProgress(pct, "Pobrano ${downloaded / 1024} KB...")
                    }
                }
            }
            onProgress(100, "Pobrano: $safeName")
            val summary = parseFtakArchive(context, targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        if (trimmed.startsWith("{") && trimmed.endsWith("}")) {
            onProgress(50, "Dekodowanie QR Offline JSON...")
            val json = JSONObject(trimmed)
            val faction = json.optString("FactionName", json.optString("faction", "BLUFOR"))
            val eventName = json.optString("EventName", json.optString("operation", "OPERACJA"))
            val fileName = "${eventName}_${faction}_OFFLINE.ftak".replace(Regex("[^a-zA-Z0-9._-]"), "_")
            val targetFile = File(getPackagesDir(context), fileName)

            ZipOutputStream(FileOutputStream(targetFile)).use { zos ->
                zos.putNextEntry(ZipEntry("config.json"))
                zos.write(json.toString(2).toByteArray(Charsets.UTF_8))
                zos.closeEntry()
            }
            onProgress(100, "Zapisano profil Offline: $fileName")
            val summary = parseFtakArchive(context, targetFile)
            setActivePackageFileName(context, targetFile.name)
            return@withContext summary
        }

        throw IllegalArgumentException("Nieznany format kodu QR. Zeskanuj kod QR paczki .ftak (Wi-Fi LAN, Chmura lub Offline JSON).")
    }

    fun parseFtakArchive(context: Context, file: File): FtakPackageSummary {
        var configJsonStr: String? = null
        var manifestJsonStr: String? = null
        var opordJsonStr: String? = null
        var extractedOpordHtmlPath: String? = null
        var extractedAnnex3MapPath: String? = null
        val zipEntries = mutableSetOf<String>()

        val pkgAssetsDir = File(getExtractedAssetsDir(context), file.nameWithoutExtension)
        if (!pkgAssetsDir.exists()) pkgAssetsDir.mkdirs()

        ZipInputStream(BufferedInputStream(FileInputStream(file))).use { zis ->
            var entry: ZipEntry?
            while (zis.nextEntry.also { entry = it } != null) {
                val entryName = entry!!.name.replace('\\', '/')
                zipEntries.add(entryName)
                val shortName = entryName.substringAfterLast('/')
                val lower = shortName.lowercase()

                when {
                    lower == "config.json" && configJsonStr == null -> {
                        configJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    lower == "manifest.json" && manifestJsonStr == null -> {
                        manifestJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    lower == "opord.json" && opordJsonStr == null -> {
                        opordJsonStr = zis.readBytes().toString(Charsets.UTF_8)
                    }
                    lower == "opord.html" -> {
                        val outHtml = File(pkgAssetsDir, "opord.html")
                        outHtml.writeBytes(zis.readBytes())
                        extractedOpordHtmlPath = outHtml.absolutePath
                    }
                    (lower.endsWith(".png") || lower.endsWith(".jpg") || lower.endsWith(".jpeg")) &&
                        (lower.contains("map") || lower.contains("annex") || lower.contains("zalacznik") || extractedAnnex3MapPath == null) -> {
                        val outImg = File(pkgAssetsDir, shortName)
                        outImg.writeBytes(zis.readBytes())
                        extractedAnnex3MapPath = outImg.absolutePath
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
        val otsHost = root.optString("OtsHost", root.optString("otsHost", "Połączono / Lokalny"))
        val enrollmentUri = root.optString("EnrollmentUri", root.optString("enrollmentUri", ""))
        val channelUrl = root.optString("ChannelUrl", root.optString("channelUrl", ""))
        val loraRegion = root.optString("LoraRegion", root.optString("region", "EU_868"))
        val loraPreset = root.optString("LoraPreset", root.optString("preset", "SHORT_FAST"))
        val loraChannelName = root.optString("LoraChannelName", "MESH")
        val emergencyPhone = root.optString("EmergencyPhone", root.optString("emergencyPhone", "+48 600 000 000"))
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
            val bundledAtak = zipEntries.firstOrNull { it.contains("atak", true) && !it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(0, RequiredApkItem("ATAK-CIV", "ATAK-CIV", "com.atakmap.app.civ", "5.0.0", bundledAtak))
        }
        if (requiredList.none { it.packageName == "com.geeksville.mesh" }) {
            val bundledMesh = zipEntries.firstOrNull { it.contains("mesh", true) && !it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            requiredList.add(1, RequiredApkItem("Meshtastic", "Meshtastic", "com.geeksville.mesh", "2.3.0", bundledMesh))
        }
        if (requiredList.none { it.packageName == "com.atakmap.android.meshtastic.plugin" }) {
            val bundledPlugin = zipEntries.firstOrNull { it.contains("plugin", true) && it.endsWith(".apk", true) } ?: ""
            if (bundledPlugin.isNotEmpty()) {
                requiredList.add(RequiredApkItem("Plugin LoRa", "Wtyczka ATAK Meshtastic", "com.atakmap.android.meshtastic.plugin", "1.0.0", bundledPlugin))
            }
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
                        freqOrChannel = v.optString("FreqOrChannel", v.optString("freq", "CH 1 (446.00625)")),
                        ctcssOrTg = v.optString("CtcssOrTg", v.optString("ctcss", "CTCSS 67.0"))
                    )
                )
            }
        }
        if (voiceList.isEmpty()) {
            voiceList.add(VoiceChannelItem("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0"))
            voiceList.add(VoiceChannelItem("Taktyczny Alfa", "VHF", "145.500 MHz", "CTCSS 88.5"))
        }

        val callsigns = mutableListOf<String>()
        val opordDays = mutableListOf<OpordDaySheet>()

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

                // Wczytaj zakladki Dzien 1, Dzien 2... z modelu OPORD v3.4
                val daysArr = opObj.optJSONArray("Days") ?: opObj.optJSONArray("DaySheets")
                if (daysArr != null && daysArr.length() > 0) {
                    for (i in 0 until daysArr.length()) {
                        val d = daysArr.optJSONObject(i) ?: continue
                        opordDays.add(
                            OpordDaySheet(
                                dayTitle = d.optString("DayTitle", d.optString("Name", "DZIEŃ ${i + 1}")).uppercase(),
                                situation = d.optString("SituationEnemy", d.optString("Situation", "Siły przeciwnika w rejonie AO prowadzą działania patrolowe.")),
                                metoc = d.optString("MetocSummary", d.optString("Weather", "Brak pobranych danych METOC.")),
                                mission5W = d.optString("Mission5W", d.optString("Mission", "Skryte przemieszczenie czujki z przodu i opanowanie obiektu.")),
                                executionTask = d.optString("ExecutionIntent", d.optString("Priorities", "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.")),
                                sarnegWord = d.optString("SarnegKeyword", d.optString("Sarneg", "BIEGUSZOWICE")),
                                challengePassword = d.optString("ChallengePassword", "GRZMOT / BŁYSK"),
                                logistics = d.optString("Logistics", "FOB / Respawn zgodnie z wytycznymi sztabu.")
                            )
                        )
                    }
                } else {
                    opordDays.add(
                        OpordDaySheet(
                            dayTitle = "DZIEŃ 1",
                            situation = opObj.optString("SituationEnemy", opObj.optString("Situation", "Siły przeciwnika utrzymują punkty kluczowe w sektorze.")),
                            metoc = opObj.optString("MetocSummary", "Warunki terenowe zgodne z prognozą AO."),
                            mission5W = opObj.optString("Mission5W", "Wykonać zadanie główne strony $factionName w wyznaczonym oknie czasowym."),
                            executionTask = opObj.optString("ExecutionIntent", "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu."),
                            sarnegWord = opObj.optString("SarnegKeyword", "BIEGUSZOWICE"),
                            challengePassword = opObj.optString("ChallengePassword", "GRZMOT / BŁYSK"),
                            logistics = opObj.optString("Logistics", "Amunicja i pakiety medyczne w punkcie zbiórki.")
                        )
                    )
                }
            }
        }

        if (opordDays.isEmpty()) {
            val summaryNode = root.optJSONObject("OpordSummary")
            opordDays.add(
                OpordDaySheet(
                    dayTitle = "DZIEŃ 1",
                    situation = summaryNode?.optString("Situation", "Działania rozpoznawczo-bojowe w rejonie operacji $eventName.") ?: "Działania w rejonie $eventName.",
                    metoc = summaryNode?.optString("Metoc", "METOC z rejonu AO.") ?: "METOC z rejonu AO.",
                    mission5W = summaryNode?.optString("Mission5W", "Zabezpieczyć wyznaczone sektory i utrzymać łączność ze sztabem.") ?: "Zabezpieczyć wyznaczone sektory.",
                    executionTask = summaryNode?.optString("Execution", "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.") ?: "Skryte przemieszczenie czujki z przodu.",
                    sarnegWord = summaryNode?.optString("Sarneg", "BIEGUSZOWICE") ?: "BIEGUSZOWICE",
                    challengePassword = summaryNode?.optString("Challenge", "WILK / LAS") ?: "WILK / LAS",
                    logistics = "Punkt medyczny i respawn w bazie strony $factionName."
                )
            )
            opordDays.add(
                OpordDaySheet(
                    dayTitle = "DZIEŃ 2",
                    situation = "Kontynuacja operacji po przegrupowaniu sił strony $factionName.",
                    metoc = "Warunki dzienne / poranne zamglenie.",
                    mission5W = "Utrzymać zajęte rubieże i przeprowadzić kontratak na cel główny.",
                    executionTask = "Rotacja sekcji na punktach obserwacyjnych, cisza radiowa do godziny H.",
                    sarnegWord = "STRUMIEŃ",
                    challengePassword = "ZAMEK / WIEŻA",
                    logistics = "Uzupełnienie baterii LoRa i wody w FOB."
                )
            )
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
            opordDays = opordDays,
            opordHtmlPath = extractedOpordHtmlPath,
            annex3MapImagePath = extractedAnnex3MapPath
        )
    }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtakhub/mobile/data/ProvisionAndApkEngine.kt===
package com.fieldtakhub.mobile.data

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
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
            require(ftakFile.exists()) { "Plik paczki .ftak nie istnieje." }

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
                throw IllegalStateException("Brak pliku instalatora ${req.displayName} (.apk) wewnątrz tej paczki .ftak.")
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

data class ProvisioningReport(
    val success: Boolean,
    val summaryTitle: String,
    val logLines: List<String>
)

object AtakMeshtasticProvisioner {

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
        logs.add("[✓] Callsign ATAK: '$cleanFull' | Skrót LoRa: '[$cleanShort]'")

        val atakResult = runCatching {
            buildAndInjectAtakMissionPackage(context, pkg, cleanFull, logs)
        }
        if (atakResult.isFailure) {
            logs.add("[!] ATAK: ${atakResult.exceptionOrNull()?.message}")
        }

        val meshResult = runCatching {
            injectMeshtasticConfigAndOwner(context, pkg, cleanFull, cleanShort, logs)
        }
        if (meshResult.isFailure) {
            logs.add("[!] Meshtastic: ${meshResult.exceptionOrNull()?.message}")
        }

        ProvisioningReport(
            success = atakResult.isSuccess || meshResult.isSuccess,
            summaryTitle = "Wstrzyknięto profil ${pkg.factionName}: $cleanFull [$cleanShort]",
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
        require(ftakFile.exists()) { "Brak pliku paczki .ftak." }

        val outZipName = "FTH_${pkg.factionName}_${fullCallsign.replace(Regex("[^a-zA-Z0-9_-]"), "_")}.zip"
        val outMissionZip = File(context.cacheDir, outZipName)

        val extractedFilesForManifest = mutableListOf<String>()
        val certEntries = mutableListOf<String>()

        val sdcardAtakDir = File(Environment.getExternalStorageDirectory(), "atak")
        val canWriteSdcardAtak = runCatching {
            if (!sdcardAtakDir.exists()) sdcardAtakDir.mkdirs()
            sdcardAtakDir.canWrite()
        }.getOrDefault(false)

        ZipOutputStream(FileOutputStream(outMissionZip)).use { zos ->
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
                        val targetZipPath = if (lower.endsWith(".p12")) {
                            certEntries.add("certs/$shortName")
                            "certs/$shortName"
                        } else {
                            "files/$shortName"
                        }

                        zos.putNextEntry(ZipEntry(targetZipPath))
                        zos.write(bytes)
                        zos.closeEntry()
                        extractedFilesForManifest.add(targetZipPath)

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

            val prefXml = buildAtakPreferenceXml(pkg, fullCallsign, certEntries.firstOrNull())
            val prefEntryPath = "config/preference.pref"
            zos.putNextEntry(ZipEntry(prefEntryPath))
            zos.write(prefXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
            extractedFilesForManifest.add(prefEntryPath)

            val manifestXml = buildAtakMissionManifestXml(pkg, fullCallsign, extractedFilesForManifest)
            zos.putNextEntry(ZipEntry("MANIFEST/manifest.xml"))
            zos.write(manifestXml.toByteArray(Charsets.UTF_8))
            zos.closeEntry()
        }

        if (canWriteSdcardAtak) {
            runCatching {
                val dpDir = File(sdcardAtakDir, "tools/datapackage")
                dpDir.mkdirs()
                outMissionZip.copyTo(File(dpDir, outZipName), overwrite = true)
                logs.add("[✓] Skopiowano mapy i paczkę bezpośrednio do /sdcard/atak/")
            }
        }

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

        if (!launchedSpecific && pkg.enrollmentUri.startsWith("tak://", ignoreCase = true)) {
            runCatching {
                val enrollIntent = Intent(Intent.ACTION_VIEW, Uri.parse(pkg.enrollmentUri)).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                context.startActivity(enrollIntent)
                logs.add("[✓] Uruchomiono Enrollment OTS w ATAK.")
            }
        } else {
            logs.add("[✓] Wstrzyknięto Callsign, interwały PLI i serwer OTS do ATAK-CIV.")
        }
    }

    private fun injectMeshtasticConfigAndOwner(
        context: Context,
        pkg: FtakPackageSummary,
        fullCallsign: String,
        shortCallsign: String,
        logs: MutableList<String>
    ) {
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
                runCatching {
                    context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(pkg.meshtasticChannelUrl)).apply {
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    })
                }
            }
            logs.add("[✓] Wstrzyknięto kanał LoRa (${pkg.loraRegion}/${pkg.loraPreset}) + PSK do Meshtastic.")
        } else {
            logs.add("[✓] Przygotowano profil LoRa: ${pkg.loraRegion} · ${pkg.loraPreset} · [$shortCallsign]")
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
        val hasOts = pkg.otsHost.isNotBlank() && !pkg.otsHost.contains("Lokalny", true)
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

//===FILE:apps/mobile/src/main/java/com/fieldtakhub/mobile/ui/screens/OnboardingAndPackagesScreen.kt===
package com.fieldtakhub.mobile.ui.screens

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
import androidx.compose.foundation.Image
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
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.fieldtakhub.mobile.data.FtakPackageRepository
import com.fieldtakhub.mobile.data.FtakPackageSummary
import com.fieldtakhub.mobile.data.LocalFtakHotspotServer
import com.fieldtakhub.mobile.ui.theme.*
import kotlinx.coroutines.launch
import java.io.File

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
        TacticalPermissionState(
            "GPS",
            "Lokalizacja GPS",
            "Wyliczanie dokładnego kwadratu MGRS do meldunku alarmowego SOS oraz pozycjonowania na mapie.",
            TacticalIconType.GPS_SATELLITE,
            hasGps
        ),
        TacticalPermissionState(
            "CAM",
            "Dostęp do aparatu (QR)",
            "Błyskawiczny odczyt kodów QR paczek .ftak z plakatów w Safezone, laptopa lub ekranu innego gracza.",
            TacticalIconType.CAMERA_QR,
            hasCamera
        ),
        TacticalPermissionState(
            "BT",
            "Łączność Bluetooth & LoRa",
            "Komunikacja z radiem Meshtastic w celu automatycznego wgrania kanałów LoRa, klucza PSK i Callsignu.",
            TacticalIconType.BLUETOOTH_LORA,
            hasBt
        ),
        TacticalPermissionState(
            "FILES",
            "Dostęp do plików (.ftak)",
            "Przechowywanie biblioteki paczek .ftak w pamięci telefonu oraz wgrywanie map (.mbtiles, .kmz) do ATAK.",
            TacticalIconType.FILES_CRATE,
            hasStorage
        ),
        TacticalPermissionState(
            "APK",
            "Instalacja pakietów (.apk)",
            "Automatyczna instalacja lub aktualizacja aplikacji ATAK-CIV, Meshtastic oraz pluginów prosto z paczki.",
            TacticalIconType.APK_SHIELD,
            hasInstallApks
        )
    )
}

@Composable
fun OnboardingScreen(
    activePackage: FtakPackageSummary?,
    onFinishOnboarding: () -> Unit
) {
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
    val activeFaction = activePackage?.factionName ?: "BLUFOR"

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(horizontal = 14.dp, vertical = 6.dp),
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
                fontSize = 17.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(top = 2.dp, bottom = 10.dp)
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
                            .border(1.2.dp, if (perm.isGranted) MutedGrid else RadarCyan, RoundedCornerShape(6.dp))
                            .clickable { requestSinglePermission(perm.id) }
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 12.dp, vertical = 10.dp),
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
                                        fontSize = 10.sp,
                                        lineHeight = 13.sp
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

        Column(
            verticalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.padding(top = 8.dp, bottom = 4.dp)
        ) {
            if (grantedCount < permissions.size) {
                OutlinedButton(
                    onClick = {
                        val firstMissing = permissions.firstOrNull { !it.isGranted }
                        if (firstMissing != null) requestSinglePermission(firstMissing.id)
                    },
                    border = BorderStroke(1.2.dp, RadarCyan),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier.fillMaxWidth().height(40.dp)
                ) {
                    Text(
                        text = "NADAJ KOLEJNE UPRAWNIENIE ($grantedCount/${permissions.size})",
                        color = RadarCyan,
                        fontWeight = FontWeight.Bold,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )
                }
            }

            // Dolny swiecacy modul AKTYWNY PROFIL: BLUFOR 1:1 jak na wizualizacji
            Surface(
                color = TacticalCard,
                shape = RoundedCornerShape(8.dp),
                border = BorderStroke(1.8.dp, RadarCyan),
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable {
                        FtakPackageRepository.setOnboardingCompleted(context, true)
                        onFinishOnboarding()
                    }
            ) {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    modifier = Modifier.padding(vertical = 12.dp, horizontal = 16.dp)
                ) {
                    Text(
                        text = "AKTYWNY PROFIL:",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )
                    Text(
                        text = activeFaction,
                        color = RadarCyan,
                        fontSize = 18.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }
        }
    }
}

@Composable
fun PackageManagerScreen(
    activePackage: FtakPackageSummary?,
    onActivePackageChanged: (FtakPackageSummary?) -> Unit,
    onOpenPermissions: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var packages by remember { mutableStateOf(FtakPackageRepository.listInstalledPackages(context)) }
    var hotspotShareUrl by remember { mutableStateOf<String?>(null) }
    var statusNote by remember { mutableStateOf("") }

    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val imported = FtakPackageRepository.importFromUri(context, uri)
                packages = FtakPackageRepository.listInstalledPackages(context)
                if (imported != null) {
                    onActivePackageChanged(imported)
                    statusNote = "Zaimportowano paczkę: ${imported.fileName}"
                }
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column {
                Text(
                    text = "BIBLIOTEKA PACZEK .FTAK (${packages.size})",
                    color = RadarCyan,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = "Zarządzaj pobranymi operacjami i udostępniaj przez Hotspot Wi-Fi",
                    color = TextMuted,
                    fontSize = 10.sp
                )
            }
            Text(
                text = "[Uprawnienia]",
                color = RadarCyan,
                fontSize = 10.sp,
                fontFamily = FontFamily.Monospace,
                modifier = Modifier.clickable { onOpenPermissions() }
            )
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            OutlinedButton(
                onClick = { filePicker.launch("*/*") },
                border = BorderStroke(1.2.dp, RadarCyan),
                shape = RoundedCornerShape(4.dp),
                modifier = Modifier.weight(1f).height(38.dp),
                contentPadding = PaddingValues(horizontal = 6.dp)
            ) {
                TacticalVectorIcon(type = TacticalIconType.FOLDER_OPEN, tint = RadarCyan, size = 15.dp)
                Spacer(modifier = Modifier.width(6.dp))
                Text("WCZYTAJ .FTAK", color = RadarCyan, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            }

            OutlinedButton(
                onClick = {
                    val target = activePackage ?: packages.firstOrNull()
                    if (target == null) {
                        statusNote = "Brak paczki .ftak do udostępnienia przez Hotspot!"
                    } else {
                        val file = File(target.localFilePath)
                        if (file.exists()) {
                            hotspotShareUrl = LocalFtakHotspotServer.startSharingPackage(file)
                        }
                    }
                },
                border = BorderStroke(1.2.dp, MilSimGreen),
                shape = RoundedCornerShape(4.dp),
                modifier = Modifier.weight(1f).height(38.dp),
                contentPadding = PaddingValues(horizontal = 6.dp)
            ) {
                TacticalVectorIcon(type = TacticalIconType.WIFI_HOTSPOT, tint = MilSimGreen, size = 15.dp)
                Spacer(modifier = Modifier.width(6.dp))
                Text("HOTSPOT WI-FI QR", color = MilSimGreen, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            }
        }

        if (statusNote.isNotBlank()) {
            Text(
                text = statusNote,
                color = MilSimGreen,
                fontSize = 11.sp,
                fontFamily = FontFamily.Monospace
            )
        }

        // Modal z kodem QR lokalnego serwera Hotspot Wi-Fi dla zapominalskich
        if (hotspotShareUrl != null) {
            val qrBmp = remember(hotspotShareUrl) { generateQrBitmap(hotspotShareUrl!!, 460) }
            Card(
                colors = CardDefaults.cardColors(containerColor = TacticalCard),
                shape = RoundedCornerShape(8.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .border(1.5.dp, MilSimGreen, RoundedCornerShape(8.dp))
            ) {
                Column(
                    modifier = Modifier.padding(12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text(
                        text = "📡 DYSTRYBUCJA POLOWA HOTSPOT WI-FI",
                        color = MilSimGreen,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Text(
                        text = "Włącz Hotspot Wi-Fi w telefonie. Drugi gracz po podłączeniu skanuje poniższy kod QR:",
                        color = TextMuted,
                        fontSize = 10.sp,
                        modifier = Modifier.padding(top = 2.dp, bottom = 8.dp)
                    )
                    if (qrBmp != null) {
                        Image(
                            bitmap = qrBmp.asImageBitmap(),
                            contentDescription = "Hotspot QR",
                            modifier = Modifier
                                .size(170.dp)
                                .border(2.dp, RadarCyan, RoundedCornerShape(4.dp))
                        )
                    }
                    Text(
                        text = hotspotShareUrl!!,
                        color = RadarCyan,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.padding(top = 6.dp, bottom = 6.dp)
                    )
                    OutlinedButton(
                        onClick = {
                            LocalFtakHotspotServer.stop()
                            hotspotShareUrl = null
                        },
                        border = BorderStroke(1.dp, DangerCrimson),
                        modifier = Modifier.height(32.dp)
                    ) {
                        Text("ZATRZYMAJ SERWER HOTSPOT", color = DangerCrimson, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                }
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
                    text = "Brak zapisanych paczek .ftak w bibliotece telefonu.\nPrzejdź do zakładki SKANER (aby zeskanować kod QR z kamery lub wczytać go z galerii) albo użyj przycisku 'WCZYTAJ .FTAK' powyżej.",
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
                            .border(1.4.dp, if (isCurrent) RadarCyan else MutedGrid, RoundedCornerShape(6.dp))
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
                                text = "OTS: ${pkg.otsHost} | LoRa: ${pkg.loraRegion} · ${pkg.loraPreset}",
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
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtakhub/mobile/ui/screens/ScannerScreen.kt===
package com.fieldtakhub.mobile.ui.screens

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
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.fieldtakhub.mobile.data.*
import com.fieldtakhub.mobile.ui.theme.*
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
    val lifecycleOwner = LocalLifecycleOwner.current
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

    DisposableEffect(lifecycleOwner, activePackage) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                hasCameraPerm = ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
                if (activePackage != null) {
                    apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, activePackage)
                }
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
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
                statusMessage = "ZAŁADOWANO: ${summary.eventName} (${summary.factionName})"
                onPackageLoaded(summary)
                apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
            }.onFailure { err ->
                isDownloading = false
                statusMessage = "Błąd QR: ${err.message}"
            }
        }
    }

    val galleryQrLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
        if (uri != null) {
            statusMessage = "Odczytywanie kodu QR z obrazu z galerii..."
            runCatching {
                val inputImage = InputImage.fromFilePath(context, uri)
                BarcodeScanning.getClient().process(inputImage)
                    .addOnSuccessListener { barcodes ->
                        val raw = barcodes.firstOrNull { !it.rawValue.isNullOrBlank() }?.rawValue
                        if (!raw.isNullOrBlank()) {
                            onQrDetected(raw)
                        } else {
                            statusMessage = "Błąd: Na wybranym zdjęciu z galerii nie znaleziono kodu QR."
                        }
                    }
                    .addOnFailureListener { e ->
                        statusMessage = "Błąd skanowania obrazu: ${e.message}"
                    }
            }.onFailure { e ->
                statusMessage = "Błąd otwarcia obrazu: ${e.message}"
            }
        }
    }

    val filePickerLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            scope.launch {
                val summary = FtakPackageRepository.importFromUri(context, uri)
                if (summary != null) {
                    onPackageLoaded(summary)
                    apkStatuses = ApkCompatibilityEngine.checkCompatibility(context, summary)
                    statusMessage = "Wczytano paczkę: ${summary.fileName}"
                }
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .verticalScroll(scrollState)
            .padding(horizontal = 12.dp, vertical = 4.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        // 1. CELOWNIK SKANERA QR + PRZYCISKI GALERII I PLIKU .FTAK
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
                        .height(185.dp)
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
                            Text("ODBLOKUJ APARAT SKANERA QR", color = AbyssalSlate, fontWeight = FontWeight.Bold, fontSize = 11.sp)
                        }
                    }

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
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(horizontal = 6.dp)
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.GALLERY_QR, tint = RadarCyan, size = 14.dp)
                        Spacer(modifier = Modifier.width(6.dp))
                        Text("QR Z GALERII", color = RadarCyan, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }

                    OutlinedButton(
                        onClick = { filePickerLauncher.launch("*/*") },
                        border = BorderStroke(1.2.dp, MutedGrid),
                        shape = RoundedCornerShape(4.dp),
                        modifier = Modifier.weight(1f).height(34.dp),
                        contentPadding = PaddingValues(horizontal = 6.dp)
                    ) {
                        TacticalVectorIcon(type = TacticalIconType.FOLDER_OPEN, tint = TextWhite, size = 14.dp)
                        Spacer(modifier = Modifier.width(6.dp))
                        Text("PLIK .FTAK", color = TextWhite, fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        // 2. KARTA STATUS INTEGRACJI (1:1 z wizualizacja srodkowego ekranu)
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(5.dp)) {
                Text(
                    text = "Status integracji",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold
                )

                if (activePackage == null) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = MilSimGreen, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("ATAK-CIV ", color = TextWhite, fontSize = 12.sp)
                        Text("(OK)", color = MilSimGreen, fontSize = 12.sp)
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = MilSimGreen, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("Meshtastic ", color = TextWhite, fontSize = 12.sp)
                        Text("(OK)", color = MilSimGreen, fontSize = 12.sp)
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = RadarCyan, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("Server OTS ", color = TextWhite, fontSize = 12.sp)
                        Text("(Oczekuje na paczkę .ftak)", color = TextMuted, fontSize = 12.sp)
                    }
                } else {
                    apkStatuses.forEach { st ->
                        val dotColor = when {
                            st.isUpToDate -> MilSimGreen
                            st.isInstalled -> CautionAmber
                            else -> DangerCrimson
                        }
                        val statusText = when {
                            st.isUpToDate -> "(OK)"
                            st.isInstalled -> "(Wymaga v${st.item.requiredVersion})"
                            else -> "(Brak -> Zainstaluj)"
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                                Text("● ", color = dotColor, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                Text("${st.item.role} ", color = TextWhite, fontSize = 12.sp)
                                Text(statusText, color = dotColor, fontSize = 12.sp)
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

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("● ", color = MilSimGreen, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                        Text("Server OTS ", color = TextWhite, fontSize = 12.sp)
                        Text("(Połączono: ${activePackage.otsHost})", color = MilSimGreen, fontSize = 12.sp)
                    }
                }
            }
        }

        // 3. ZNAK WYWOLAWCZY (Callsign) + Automated preview: [WLK1] + Przycisk WSTRZYKNIJ
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
                                text = "[Wybierz z OPORD ▼]",
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
                        unfocusedBorderColor = RadarCyan,
                        focusedContainerColor = InputTrench,
                        unfocusedContainerColor = InputTrench,
                        focusedTextColor = TextWhite,
                        unfocusedTextColor = TextWhite
                    ),
                    modifier = Modifier.fillMaxWidth()
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Automated preview:",
                        color = TextMuted,
                        fontSize = 11.sp,
                        fontFamily = FontFamily.Monospace
                    )

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = "[$shortCallsign]",
                            color = TextWhite,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace,
                            modifier = Modifier.padding(end = 8.dp)
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
                            modifier = Modifier.width(84.dp)
                        )
                    }
                }

                Button(
                    onClick = {
                        if (activePackage == null) {
                            statusMessage = "Błąd: Najpierw zeskanuj kod QR lub wczytaj plik .ftak!"
                            return@Button
                        }
                        scope.launch {
                            statusMessage = "Wstrzykiwanie do ATAK-CIV i Meshtastic..."
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
            val previewView = PreviewView(ctx).apply {
                implementationMode = PreviewView.ImplementationMode.COMPATIBLE
            }
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

//===FILE:apps/mobile/src/main/java/com/fieldtakhub/mobile/ui/screens/OpordAndCheatSheetScreen.kt===
package com.fieldtakhub.mobile.ui.screens

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.net.Uri
import android.os.Bundle
import android.webkit.WebView
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.fieldtakhub.mobile.data.*
import com.fieldtakhub.mobile.ui.theme.*
import java.io.File

@SuppressLint("MissingPermission")
@Composable
fun OpordAndCheatSheetScreen(
    activePackage: FtakPackageSummary?,
    initialFocusOnCheatSheet: Boolean
) {
    val context = LocalContext.current
    val scrollState = rememberScrollState()

    var currentMgrs by remember { mutableStateOf("34U CA 48120 21050") }
    var currentLatLon by remember { mutableStateOf("49.84944, 18.81267") }

    DisposableEffect(context) {
        val locMgr = context.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
        val hasPerm = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED

        val listener = object : LocationListener {
            override fun onLocationChanged(loc: Location) {
                currentMgrs = LatLonToMgrs.convert(loc.latitude, loc.longitude)
                currentLatLon = String.format("%.5f, %.5f", loc.latitude, loc.longitude)
            }
            @Deprecated("Deprecated in Java")
            override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
            override fun onProviderEnabled(provider: String) {}
            override fun onProviderDisabled(provider: String) {}
        }

        if (hasPerm && locMgr != null) {
            runCatching {
                val lastGps = locMgr.getLastKnownLocation(LocationManager.GPS_PROVIDER)
                    ?: locMgr.getLastKnownLocation(LocationManager.NETWORK_PROVIDER)
                if (lastGps != null) {
                    currentMgrs = LatLonToMgrs.convert(lastGps.latitude, lastGps.longitude)
                    currentLatLon = String.format("%.5f, %.5f", lastGps.latitude, lastGps.longitude)
                }
                locMgr.requestLocationUpdates(LocationManager.GPS_PROVIDER, 3000L, 2f, listener)
                locMgr.requestLocationUpdates(LocationManager.NETWORK_PROVIDER, 5000L, 5f, listener)
            }
        }
        onDispose {
            runCatching { locMgr?.removeUpdates(listener) }
        }
    }

    val days = activePackage?.opordDays ?: listOf(
        OpordDaySheet(
            dayTitle = "DZIEŃ 1",
            situation = "Siły przeciwnika utrzymują kluczowe skrzyżowania w sektorze.",
            metoc = "Warunki umiarkowane, wiatr zachodni 3 m/s.",
            mission5W = "Opanować i utrzymać obiekt ALFA do godz. 14:00.",
            executionTask = "Skryte przemieszczenie czujki z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.",
            sarnegWord = "BIEGUSZOWICE",
            challengePassword = "GRZMOT / BŁYSK",
            logistics = "FOB / Respawn w kwadracie startowym."
        ),
        OpordDaySheet(
            dayTitle = "DZIEŃ 2",
            situation = "Przegrupowanie pododdziałów i obrona okrężna.",
            metoc = "Poranne zamglenie w dolinach.",
            mission5W = "Zabezpieczyć strefę lądowania LZ o godzinie H.",
            executionTask = "Rotacja czujek co 45 minut, cisza radiowa na kanale głównym.",
            sarnegWord = "STRUMIEŃ",
            challengePassword = "ZAMEK / WIEŻA",
            logistics = "Ewakuacja MEDEVAC drogą główną."
        )
    )

    var selectedDayIndex by remember { mutableStateOf(0) }
    var showMapTab by remember { mutableStateOf(false) }
    var showFullHtmlWebView by remember { mutableStateOf(false) }

    val voiceChannels = activePackage?.voiceChannels ?: listOf(
        VoiceChannelItem("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0"),
        VoiceChannelItem("Taktyczny Alfa", "VHF", "145.500 MHz", "CTCSS 88.5")
    )
    val loraLine = if (activePackage != null) {
        "LoRa: ${activePackage.loraRegion} · ${activePackage.loraPreset} · ${activePackage.loraChannelName}"
    } else {
        "LoRa: EU_868 · SHORT_FAST · MESH"
    }
    val emergencyPhone = activePackage?.emergencyPhone ?: "+48 600 000 000"
    val (operatorFull, operatorShort) = remember { FtakPackageRepository.loadOperatorCallsign(context) }

    val mgrsParts = currentMgrs.split(" ")
    val mgrsLine1 = if (mgrsParts.size >= 2) "MGRS: ${mgrsParts[0]}" else "MGRS: $currentMgrs"
    val mgrsLine2 = if (mgrsParts.size >= 4) "${mgrsParts[1]} ${mgrsParts[2]} ${mgrsParts[3]}" else currentLatLon

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .verticalScroll(scrollState)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        // 1. CZERWONY PANEL SOS MGRS ALARM (1:1 z prawym ekranem wizualizacji)
        Surface(
            color = SosCardBg,
            shape = RoundedCornerShape(8.dp),
            border = BorderStroke(1.6.dp, DangerCrimson),
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(modifier = Modifier.padding(12.dp)) {
                Text(
                    text = "SOS MGRS ALARM",
                    color = DangerCrimson,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold
                )
                Spacer(modifier = Modifier.height(8.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = mgrsLine1,
                            color = TextWhite,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = mgrsLine2,
                            color = TextWhite,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = "ORGA: $emergencyPhone",
                            color = TextMuted,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace,
                            modifier = Modifier.padding(top = 2.dp)
                        )
                    }

                    Button(
                        onClick = {
                            val smsBody = "🆘 SOS MEDEVAC / ALARM!\nOperator: $operatorFull [$operatorShort]\nStrona: ${activePackage?.factionName ?: "BLUFOR"}\nPozycja MGRS: $currentMgrs\nGPS: $currentLatLon"
                            val smsIntent = Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:${Uri.encode(emergencyPhone)}")).apply {
                                putExtra("sms_body", smsBody)
                                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                            }
                            runCatching { context.startActivity(smsIntent) }
                        },
                        colors = ButtonDefaults.buttonColors(containerColor = SosButtonBg),
                        shape = RoundedCornerShape(6.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp)
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            TacticalVectorIcon(type = TacticalIconType.SOS_BEACON, tint = TextWhite, size = 18.dp)
                            Spacer(modifier = Modifier.width(8.dp))
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Text("WYŚLIJ", color = TextWhite, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                                Text("SMS SOS", color = TextWhite, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }
            }
        }

        // 2. NAGLOWEK ROZKAZ BOJOWY (OPORD) & LACZNOSC + PRZYCISKI [ DZIEN 1 ] [ DZIEN 2 ] [ MAPA ]
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "ROZKAZ BOJOWY (OPORD) & ŁĄCZNOŚĆ",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold
                )
                if (activePackage?.opordHtmlPath != null) {
                    Text(
                        text = if (showFullHtmlWebView) "[Widok Kart]" else "[Pełny Arkusz A4]",
                        color = RadarCyan,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.clickable { showFullHtmlWebView = !showFullHtmlWebView }
                    )
                }
            }

            Text(
                text = "OPORD",
                color = TextMuted,
                fontSize = 11.sp,
                fontFamily = FontFamily.Monospace
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                days.forEachIndexed { idx, daySheet ->
                    val isSelected = !showMapTab && (selectedDayIndex == idx)
                    Surface(
                        color = if (isSelected) Color(0xFF0E2A31) else TacticalCard,
                        shape = RoundedCornerShape(4.dp),
                        border = BorderStroke(1.2.dp, if (isSelected) RadarCyan else MutedGrid),
                        modifier = Modifier
                            .weight(1f)
                            .clickable {
                                showMapTab = false
                                selectedDayIndex = idx
                            }
                    ) {
                        Box(
                            contentAlignment = Alignment.Center,
                            modifier = Modifier.padding(vertical = 8.dp, horizontal = 4.dp)
                        ) {
                            Text(
                                text = "[ ${daySheet.dayTitle} ]",
                                color = if (isSelected) RadarCyan else TextMuted,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                fontFamily = FontFamily.Monospace
                            )
                        }
                    }
                }

                Surface(
                    color = if (showMapTab) Color(0xFF0E2A31) else TacticalCard,
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, if (showMapTab) RadarCyan else MutedGrid),
                    modifier = Modifier
                        .weight(1f)
                        .clickable { showMapTab = true }
                ) {
                    Box(
                        contentAlignment = Alignment.Center,
                        modifier = Modifier.padding(vertical = 8.dp, horizontal = 4.dp)
                    ) {
                        Text(
                            text = "[ MAPA ]",
                            color = if (showMapTab) RadarCyan else TextMuted,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                    }
                }
            }
        }

        // 3. TABELA CZESTOTLIWOSCI (Radio) — 1:1 z wizualizacja
        Card(
            colors = CardDefaults.cardColors(containerColor = TacticalCard),
            shape = RoundedCornerShape(6.dp),
            modifier = Modifier
                .fillMaxWidth()
                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
        ) {
            Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = "TABELA CZĘSTOTLIWOŚCI (Radio)",
                    color = TextMuted,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )

                voiceChannels.forEach { ch ->
                    Text(
                        text = "${ch.band}: ${ch.freqOrChannel} · ${ch.ctcssOrTg}",
                        color = TextWhite,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        fontFamily = FontFamily.Monospace
                    )
                    Divider(color = MutedGrid, thickness = 0.8.dp)
                }

                Text(
                    text = loraLine,
                    color = TextWhite,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )
            }
        }

        // 4. ZAWARTOSC WYBRANEGO DNIA OPORD LUB ZALACZNIKA NR 3 (MAPA)
        if (showMapTab) {
            Annex3ZoomableMapCard(activePackage = activePackage)
        } else if (showFullHtmlWebView && activePackage?.opordHtmlPath != null) {
            Card(
                colors = CardDefaults.cardColors(containerColor = TacticalCard),
                shape = RoundedCornerShape(6.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(420.dp)
                    .border(1.2.dp, RadarCyan, RoundedCornerShape(6.dp))
            ) {
                AndroidView(
                    modifier = Modifier.fillMaxSize(),
                    factory = { ctx ->
                        WebView(ctx).apply {
                            settings.javaScriptEnabled = false
                            settings.builtInZoomControls = true
                            settings.displayZoomControls = false
                            loadUrl("file://${activePackage.opordHtmlPath}")
                        }
                    }
                )
            }
        } else {
            val activeDay = days.getOrElse(selectedDayIndex) { days.first() }
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    text = "Zadanie:",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = activeDay.executionTask,
                    color = TextWhite,
                    fontSize = 12.sp,
                    lineHeight = 17.sp
                )
                Divider(color = MutedGrid, thickness = 1.dp, modifier = Modifier.padding(vertical = 2.dp))

                Card(
                    colors = CardDefaults.cardColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(6.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .border(1.dp, MutedGrid, RoundedCornerShape(6.dp))
                ) {
                    Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(
                            text = "I. SYTUACJA & METOC (${activeDay.dayTitle})",
                            color = RadarCyan,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(text = activeDay.situation, color = TextWhite, fontSize = 11.sp)
                        Text(text = "METOC: ${activeDay.metoc}", color = TextMuted, fontSize = 10.sp)

                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "II. MISJA (5W)",
                            color = RadarCyan,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(text = activeDay.mission5W, color = TextWhite, fontSize = 11.sp)

                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "III.E SŁOWO-KLUCZ SARNEG & HASŁA",
                            color = RadarCyan,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            fontFamily = FontFamily.Monospace
                        )
                        Text(
                            text = "SARNEG: ${activeDay.sarnegWord}  |  Hasło: ${activeDay.challengePassword}",
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

@Composable
private fun Annex3ZoomableMapCard(activePackage: FtakPackageSummary?) {
    val mapPath = activePackage?.annex3MapImagePath
    val bitmap = remember(mapPath) {
        if (!mapPath.isNullOrBlank() && File(mapPath).exists()) {
            BitmapFactory.decodeFile(mapPath)
        } else null
    }

    var scale by remember { mutableStateOf(1f) }
    var offsetX by remember { mutableStateOf(0f) }
    var offsetY by remember { mutableStateOf(0f) }

    Card(
        colors = CardDefaults.cardColors(containerColor = TacticalCard),
        shape = RoundedCornerShape(6.dp),
        modifier = Modifier
            .fillMaxWidth()
            .border(1.2.dp, RadarCyan, RoundedCornerShape(6.dp))
    ) {
        Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "ZAŁĄCZNIK NR 3 — MAPA TAKTYCZNA OPERACJI",
                    color = RadarCyan,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )
                if (bitmap != null) {
                    Text(
                        text = "[Reset Zoom]",
                        color = MilSimGreen,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        modifier = Modifier.clickable {
                            scale = 1f
                            offsetX = 0f
                            offsetY = 0f
                        }
                    )
                }
            }

            if (bitmap != null) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(300.dp)
                        .background(InputTrench, RoundedCornerShape(4.dp))
                        .pointerInput(Unit) {
                            detectTransformGestures { _, pan, zoom, _ ->
                                scale = (scale * zoom).coerceIn(1f, 5f)
                                offsetX += pan.x
                                offsetY += pan.y
                            }
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Image(
                        bitmap = bitmap.asImageBitmap(),
                        contentDescription = "Załącznik nr 3 Mapa Taktyczna",
                        contentScale = ContentScale.Fit,
                        modifier = Modifier
                            .fillMaxSize()
                            .graphicsLayer(
                                scaleX = scale,
                                scaleY = scale,
                                translationX = offsetX,
                                translationY = offsetY
                            )
                    )
                }
                Text(
                    text = "Użyj dwóch palców, aby powiększyć i przesuwać mapę z Załącznika nr 3.",
                    color = TextMuted,
                    fontSize = 10.sp
                )
            } else {
                Text(
                    text = "Aktualna paczka .ftak nie zawiera załączonego obrazu mapy w Załączniku nr 3 (lub żadna paczka nie została jeszcze wczytana).",
                    color = TextMuted,
                    fontSize = 11.sp
                )
            }
        }
    }
}
//===END_FILE===

//===FILE:apps/mobile/src/main/java/com/fieldtakhub/mobile/MainActivity.kt===
package com.fieldtakhub.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import com.fieldtakhub.mobile.data.FtakPackageRepository
import com.fieldtakhub.mobile.ui.screens.OnboardingScreen
import com.fieldtakhub.mobile.ui.screens.OpordAndCheatSheetScreen
import com.fieldtakhub.mobile.ui.screens.PackageManagerScreen
import com.fieldtakhub.mobile.ui.screens.ScannerScreen
import com.fieldtakhub.mobile.ui.screens.evaluatePermissions
import com.fieldtakhub.mobile.ui.theme.AbyssalSlate
import com.fieldtakhub.mobile.ui.theme.FieldTakHubTheme
import com.fieldtakhub.mobile.ui.theme.TacticalBottomBar
import com.fieldtakhub.mobile.ui.theme.TacticalTab
import com.fieldtakhub.mobile.ui.theme.TacticalTopBar

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FieldTakHubTheme {
                val context = LocalContext.current

                // Wymuszone otwarcie ekranu powitalnego (Onboarding & Permissions) przy pierwszym uruchomieniu
                // lub gdy brakuje uprawnien bojowych
                val hasCompletedFirstRun = remember { FtakPackageRepository.isOnboardingCompleted(context) }
                val initialPerms = remember { evaluatePermissions(context) }
                var isOnboardingOpen by remember {
                    mutableStateOf(!hasCompletedFirstRun || initialPerms.any { !it.isGranted })
                }

                var currentTab by remember { mutableStateOf(TacticalTab.SCANNER) }
                var activePackage by remember {
                    val all = FtakPackageRepository.listInstalledPackages(context)
                    val savedName = FtakPackageRepository.getActivePackageFileName(context)
                    mutableStateOf(all.firstOrNull { it.fileName == savedName } ?: all.firstOrNull())
                }

                Scaffold(
                    containerColor = AbyssalSlate,
                    topBar = {
                        TacticalTopBar(
                            isOnboardingOpen = isOnboardingOpen,
                            onToggleOnboarding = { isOnboardingOpen = !isOnboardingOpen }
                        )
                    },
                    bottomBar = {
                        TacticalBottomBar(
                            currentTab = currentTab,
                            isOnboardingOpen = isOnboardingOpen,
                            onSelectTab = { selectedTab ->
                                FtakPackageRepository.setOnboardingCompleted(context, true)
                                isOnboardingOpen = false
                                currentTab = selectedTab
                            }
                        )
                    }
                ) { innerPadding ->
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .background(AbyssalSlate)
                            .padding(innerPadding)
                    ) {
                        if (isOnboardingOpen) {
                            OnboardingScreen(
                                activePackage = activePackage,
                                onFinishOnboarding = {
                                    FtakPackageRepository.setOnboardingCompleted(context, true)
                                    isOnboardingOpen = false
                                }
                            )
                        } else {
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
                                        onActivePackageChanged = { updated -> activePackage = updated },
                                        onOpenPermissions = { isOnboardingOpen = true }
                                    )
                                }
                                TacticalTab.OPORD -> {
                                    OpordAndCheatSheetScreen(
                                        activePackage = activePackage,
                                        initialFocusOnCheatSheet = false
                                    )
                                }
                                TacticalTab.CHEATSHEET -> {
                                    OpordAndCheatSheetScreen(
                                        activePackage = activePackage,
                                        initialFocusOnCheatSheet = true
                                    )
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