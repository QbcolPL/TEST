@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [ETAP 1] Tworzenie struktury Android app (Kotlin + Jetpack Compose),
echo          Manifestu uprawnien, Taktycznego Motywu, Onboardingu i Menedzera .ftak...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'ETAP1_MOBILE_CS===')[1]); [FthEtap1MobileScaffold]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Etapu 1 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - ETAP 1] Szkielet aplikacji mobilnej Android zostal utworzony
echo i wyslany na GitHub (QbcolPL/TEST)! Kompilacja na GitHub Actions ruszyła.
echo ============================================================================
pause
exit /b 0

//===ETAP1_MOBILE_CS===
using System;
using System.IO;
using System.Text;
using System.Diagnostics;

public static class FthEtap1MobileScaffold
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string mobileDir = Path.Combine(targetDir, @"apps\mobile");

        // Tworzenie struktury katalogow gradle / package
        string javaPkgDir = Path.Combine(mobileDir, @"src\main\java\com\fieldtak\mobile");
        string uiPkgDir = Path.Combine(javaPkgDir, @"ui");
        string themeDir = Path.Combine(uiPkgDir, @"theme");
        string onboardingDir = Path.Combine(uiPkgDir, @"onboarding");
        string packagesDir = Path.Combine(uiPkgDir, @"packages");
        string resValuesDir = Path.Combine(mobileDir, @"src\main\res\values");

        Directory.CreateDirectory(javaPkgDir);
        Directory.CreateDirectory(themeDir);
        Directory.CreateDirectory(onboardingDir);
        Directory.CreateDirectory(packagesDir);
        Directory.CreateDirectory(resValuesDir);

        // 1. AndroidManifest.xml z pelnym zestawem uprawnien bojowych
        string manifestContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android""
    xmlns:tools=""http://schemas.android.com/tools"">

    <uses-permission android:name=""android.permission.INTERNET"" />
    <uses-permission android:name=""android.permission.ACCESS_NETWORK_STATE"" />
    <uses-permission android:name=""android.permission.ACCESS_FINE_LOCATION"" />
    <uses-permission android:name=""android.permission.ACCESS_COARSE_LOCATION"" />
    <uses-permission android:name=""android.permission.CAMERA"" />
    <uses-permission android:name=""android.permission.BLUETOOTH"" />
    <uses-permission android:name=""android.permission.BLUETOOTH_ADMIN"" />
    <uses-permission android:name=""android.permission.BLUETOOTH_CONNECT"" />
    <uses-permission android:name=""android.permission.BLUETOOTH_SCAN"" />
    <uses-permission android:name=""android.permission.READ_EXTERNAL_STORAGE"" />
    <uses-permission android:name=""android.permission.WRITE_EXTERNAL_STORAGE"" />
    <uses-permission android:name=""android.permission.MANAGE_EXTERNAL_STORAGE"" tools:ignore=""ScopedStorage"" />
    <uses-permission android:name=""android.permission.REQUEST_INSTALL_PACKAGES"" />

    <application
        android:allowBackup=""true""
        android:icon=""@mipmap/ic_launcher""
        android:label=""Field TAK Hub""
        android:roundIcon=""@mipmap/ic_launcher_round""
        android:supportsRtl=""true""
        android:theme=""@style/Theme.FieldTakHub"">
        <activity
            android:name="".MainActivity""
            android:exported=""true""
            android:theme=""@style/Theme.FieldTakHub"">
            <intent-filter>
                <action android:name=""android.intent.action.MAIN"" />
                <category android:name=""android.intent.category.LAUNCHER"" />
            </intent-filter>
        </activity>
    </application>
</manifest>
";
        File.WriteAllText(Path.Combine(mobileDir, @"src\main\AndroidManifest.xml"), manifestContent, utf8NoBom);

        // 2. Taktyczny Motyw kolorystyczny w Kotlin (Theme.kt)
        string themeContent = @"package com.fieldtak.mobile.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val AbyssalSlate = Color(0xFF060E12)
val TacticalCard = Color(0xFF0A181E)
val InputTrench = Color(0xFF071115)
val RadarCyan = Color(0xFF1FE0C5)
val MutedGrid = Color(0xFF163844)
val MilSimGreen = Color(0xFF2ECC71)
val CautionAmber = Color(0xFFFAD02C)
val DangerCrimson = Color(0xFFEB4034)
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
";
        File.WriteAllText(Path.Combine(themeDir, "Theme.kt"), themeContent, utf8NoBom);

        // 3. Ekran Onboardingu (Uprawnienia Bojowe)
        string onboardingContent = @"package com.fieldtak.mobile.ui.onboarding

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.ui.theme.*

data class PermissionItem(val title: String, val desc: String, val granted: Boolean)

@Composable
fun OnboardingScreen(onAllGranted: () -> Unit) {
    val permissions = remember {
        mutableStateListOf(
            PermissionItem(""LOKALIZACJA GPS / MGRS"", ""Wyliczanie kwadratu MGRS i pozycji do meldunku SOS."", true),
            PermissionItem(""APARAT (SKANER QR)"", ""Błyskawiczny odczyt kodów QR paczek ftak."", true),
            PermissionItem(""BLUETOOTH & LORA"", ""Wstrzykiwanie kanałów i ksywy do Meshtastic."", true),
            PermissionItem(""PAMIĘĆ I PLIKI (.FTAK)"", ""Zapisywanie map .mbtiles/.kmz oraz paczek operacji."", false),
            PermissionItem(""INSTALACJA PAKIETÓW (.APK)"", ""Automatyczna instalacja ATAK-CIV i pluginów."", false)
        )
    }

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
            Column {
                Text(
                    text = ""FIELD TAK HUB MOBILE"",
                    color = RadarCyan,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = ""connect · deploy · operate"",
                    color = TextMuted,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(bottom = 16.dp)
                )

                Text(
                    text = ""WYMAGANE UPRAWNIENIA OPERACYJNE"",
                    color = TextWhite,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(bottom = 8.dp)
                )

                LazyColumn(
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.weight(1f)
                ) {
                    items(permissions.size) { index ->
                        val perm = permissions[index]
                        Card(
                            colors = CardDefaults.cardColors(containerColor = TacticalCard),
                            shape = RoundedCornerShape(6.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .border(1.2.dp, MutedGrid, RoundedCornerShape(6.dp))
                        ) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(12.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column(modifier = Modifier.weight(1f)) {
                                    Text(text = perm.title, color = RadarCyan, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                    Text(text = perm.desc, color = TextMuted, fontSize = 11.sp)
                                }
                                Button(
                                    onClick = { },
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = if (perm.granted) MilSimGreen else InputTrench
                                    ),
                                    shape = RoundedCornerShape(4.dp),
                                    modifier = Modifier.height(32.dp)
                                ) {
                                    Text(
                                        text = if (perm.granted) ""NADANO ✓"" else ""NADAJ"",
                                        color = if (perm.granted) AbyssalSlate else RadarCyan,
                                        fontSize = 11.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                }
                            }
                        }
                    }
                }
            }

            Button(
                onClick = onAllGranted,
                colors = ButtonDefaults.buttonColors(containerColor = RadarCyan),
                shape = RoundedCornerShape(6.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(48.dp)
            ) {
                Text(
                    text = ""WEJDŹ DO GRY (START OPERACJI)"",
                    color = AbyssalSlate,
                    fontWeight = FontWeight.Bold,
                    fontSize = 13.sp
                )
            }
        }
    }
}
";
        File.WriteAllText(Path.Combine(onboardingDir, "OnboardingScreen.kt"), onboardingContent, utf8NoBom);

        // 4. Menedżer Paczek .ftak (PackageManagerScreen.kt)
        string packagesContent = @"package com.fieldtak.mobile.ui.packages

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fieldtak.mobile.ui.theme.*

data class FtakPackage(val name: String, val faction: String, val size: String, val active: Boolean)

@Composable
fun PackageManagerScreen() {
    val packages = remember {
        mutableStateListOf(
            FtakPackage(""OPERACJA AEGIS_BLUFOR.ftak"", ""BLUFOR"", ""48.2 MB"", true),
            FtakPackage(""TRENING SOBOTNI_ORGA.ftak"", ""ORGA"", ""12.4 MB"", false)
        )
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(AbyssalSlate)
            .padding(16.dp)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Text(
                text = ""MENEDŻER PACZEK .FTAK"",
                color = RadarCyan,
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(bottom = 12.dp)
            )

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(
                    onClick = { },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, RadarCyan),
                    modifier = Modifier.weight(1f).height(36.dp)
                ) {
                    Text(text = ""📁 WCZYTAJ Z PLIKU"", color = RadarCyan, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
                Button(
                    onClick = { },
                    colors = ButtonDefaults.buttonColors(containerColor = TacticalCard),
                    shape = RoundedCornerShape(4.dp),
                    border = BorderStroke(1.2.dp, MilSimGreen),
                    modifier = Modifier.weight(1f).height(36.dp)
                ) {
                    Text(text = ""📡 HOTSPOT WI-FI"", color = MilSimGreen, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }

            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.weight(1f)
            ) {
                items(packages.size) { index ->
                    val pkg = packages[index]
                    Card(
                        colors = CardDefaults.cardColors(containerColor = TacticalCard),
                        shape = RoundedCornerShape(6.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .border(1.2.dp, if (pkg.active) RadarCyan else MutedGrid, RoundedCornerShape(6.dp))
                    ) {
                        Column(modifier = Modifier.padding(12.dp)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(text = pkg.name, color = TextWhite, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                                if (pkg.active) {
                                    Surface(
                                        color = InputTrench,
                                        shape = RoundedCornerShape(4.dp),
                                        border = BorderStroke(1.dp, RadarCyan)
                                    ) {
                                        Text(
                                            text = ""AKTYWNA"",
                                            color = RadarCyan,
                                            fontSize = 9.sp,
                                            fontWeight = FontWeight.Bold,
                                            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                                        )
                                    }
                                }
                            }
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(text = ""Strona: ${pkg.faction} | Rozmiar: ${pkg.size}"", color = TextMuted, fontSize = 11.sp)
                        }
                    }
                }
            }
        }
    }
}
";
        File.WriteAllText(Path.Combine(packagesDir, "PackageManagerScreen.kt"), packagesContent, utf8NoBom);

        // 5. Główna aktywność (MainActivity.kt)
        string mainActivityContent = @"package com.fieldtak.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import com.fieldtak.mobile.ui.onboarding.OnboardingScreen
import com.fieldtak.mobile.ui.packages.PackageManagerScreen
import com.fieldtak.mobile.ui.theme.FieldTakHubTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FieldTakHubTheme {
                var showOnboarding by remember { mutableStateOf(true) }
                if (showOnboarding) {
                    OnboardingScreen(onAllGranted = { showOnboarding = false })
                } else {
                    PackageManagerScreen()
                }
            }
        }
    }
}
";
        File.WriteAllText(Path.Combine(javaPkgDir, "MainActivity.kt"), mainActivityContent, utf8NoBom);

        // 6. Podstawowy build.gradle.kts dla modułu mobile
        string buildGradleContent = @"plugins {
    id(""com.android.application"")
    id(""org.jetbrains.kotlin.android"")
}

android {
    namespace = ""com.fieldtak.mobile""
    compileSdk = 34

    defaultConfig {
        applicationId = ""com.fieldtak.mobile""
        minSdk = 26
        targetSdk = 34
        versionCode = 1
        versionName = ""3.4.0""

        testInstrumentationRunner = ""androidx.test.runner.AndroidJUnitRunner""
        vectorDrawables {
            useSupportLibrary = true
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile(""proguard-android-optimize.txt""), ""proguard-rules.pro"")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = ""17""
    }
    buildFeatures {
        compose = true
    }
    composeOptions {
        kotlinCompilerExtensionVersion = ""1.5.8""
    }
    packaging {
        resources {
            excludes += ""/META-INF/{AL2.0,LGPL2.1}""
        }
    }
}

dependencies {
    implementation(""androidx.core:core-ktx:1.12.0"")
    implementation(""androidx.lifecycle:lifecycle-runtime-ktx:2.7.0"")
    implementation(""androidx.activity:activity-compose:1.8.2"")
    implementation(platform(""androidx.compose:compose-bom:2023.08.00""))
    implementation(""androidx.compose.ui:ui"")
    implementation(""androidx.compose.ui:ui-graphics"")
    implementation(""androidx.compose.ui:ui-tooling-preview"")
    implementation(""androidx.compose.material3:material3"")
}
";
        File.WriteAllText(Path.Combine(mobileDir, "build.gradle.kts"), buildGradleContent, utf8NoBom);

        // 7. Styles.xml dla motywu
        string stylesContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<resources>
    <style name=""Theme.FieldTakHub"" parent=""android:Theme.Material.NoActionBar"">
        <item name=""android:windowBackground"">@color/abyssal_slate</item>
    </style>
    <color name=""abyssal_slate"">#060E12</color>
</resources>
";
        File.WriteAllText(Path.Combine(resValuesDir, "styles.xml"), stylesContent, utf8NoBom);

        Console.WriteLine("[OK] Wygenerowano szkielet aplikacji mobilnej w apps/mobile.");

        // Znacznik wyzwalajacy dla GitHub Actions
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Mobile App Etap 1 Scaffold UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Etap 1: Scaffold Field TAK Hub Mobile app (Jetpack Compose, tactical theme, onboarding permissions & .ftak package manager)\" --allow-empty", targetDir, ignoreError: false);

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
//===ETAP1_MOBILE_CS===