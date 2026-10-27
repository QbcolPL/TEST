using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderPackageAndUiPatcher
    {
        private static bool _sharedFoldersInitialized;
        private static HttpListener _hotspotListener;
        private static int _hotspotPort = 8769;

        public static string GetWorkspaceRootDir()
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            Directory.CreateDirectory(baseDir);
            return baseDir;
        }

        public static string EnsureSharedFolderStructure()
        {
            string root = GetWorkspaceRootDir();
            string wspolneDir = Path.Combine(root, "wspolne");
            Directory.CreateDirectory(Path.Combine(wspolneDir, "apk"));
            Directory.CreateDirectory(Path.Combine(wspolneDir, "plugins"));
            Directory.CreateDirectory(Path.Combine(wspolneDir, "maps"));
            Directory.CreateDirectory(Path.Combine(root, "out"));

            if (!_sharedFoldersInitialized)
            {
                _sharedFoldersInitialized = true;
                string readmePath = Path.Combine(wspolneDir, "INSTRUKCJA_FOLDERU_WSPOLNE.txt");
                if (!File.Exists(readmePath))
                {
                    File.WriteAllText(readmePath,
                        "=== FIELD TAK HUB — STRUKTURA FOLDERU WSPÓLNE ===\r\n" +
                        "1. apk\\     -> Umieść tutaj instalatory .apk aplikacji ATAK-CIV oraz Meshtastic.\r\n" +
                        "2. plugins\\ -> Umieść tutaj wtyczki .apk do aplikacji ATAK.\r\n" +
                        "3. maps\\    -> Umieść tutaj wspólne mapy (.kmz, .gpx, .mbtiles, .sqlitedb).\r\n",
                        Encoding.UTF8);
                }
            }
            return wspolneDir;
        }

        public static string EnsureActiveFactionOutputFolder(Window win)
        {
            EnsureSharedFolderStructure();
            string factionName = GetCurrentSelectedFactionName(win);
            string safeName = SanitizeFolderName(factionName);
            string outDir = Path.Combine(GetWorkspaceRootDir(), "out", safeName);
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "BLUFOR";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            string res = sb.ToString().Trim();
            return string.IsNullOrEmpty(res) ? "BLUFOR" : res;
        }

        private static string GetCurrentSelectedFactionName(Window win)
        {
            try
            {
                var all = BuilderCoreSidebarAndProjectEngine.GetAllVisualDescendants(win);
                foreach (var tb in all.OfType<TextBox>())
                {
                    string tag = tb.Tag?.ToString() ?? "";
                    if (tag.Contains("FactionName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tb.Text))
                        return tb.Text.Trim();
                }
                foreach (var cb in all.OfType<ComboBox>())
                {
                    if (cb.SelectedItem != null)
                    {
                        string val = (cb.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cb.SelectedItem.ToString()) ?? "";
                        if (!string.IsNullOrWhiteSpace(val) &&
                            !val.Contains("EU_", StringComparison.OrdinalIgnoreCase) &&
                            !val.Contains("FAST", StringComparison.OrdinalIgnoreCase) &&
                            !val.Contains("SLOW", StringComparison.OrdinalIgnoreCase) &&
                            !val.Contains("1:", StringComparison.OrdinalIgnoreCase) &&
                            !val.Contains("Tryb", StringComparison.OrdinalIgnoreCase))
                        {
                            return val.Trim();
                        }
                    }
                }
            }
            catch { }
            return "BLUFOR";
        }

        public static void ApplyEnhancements(Window win, List<DependencyObject> allElements)
        {
            if (win == null || allElements == null) return;
            EnsureSharedFolderStructure();

            try
            {
                // 1. Skalowanie dla mniejszych ekranów (np. laptopy 1366x768 / 1536x864)
                if (win.MinWidth > 900) win.MinWidth = 900;
                if (win.MinHeight > 580) win.MinHeight = 580;

                // 2. Usuń pozostałość po starym górnym pasku (puste paski z cyjanową ramką nad widokiem)
                CollapseEmptyLegacyTopBars(allElements);

                // 3. Obsłuż bezterminową ważność paczki / kodu QR przy wpisaniu "0"
                PatchZeroExpirationInputs(allElements);

                // 4. Odblokuj przycisk "Pliki strony" oraz napraw przycisk "Plakat stron + QR" (by nie zamykał aplikacji)
                PatchFactionFilesAndPosterButtons(win, allElements);

                // 5. Przebuduj kartę "GENERATOR PACZEK .FTAK v3.0" dokładnie do 3 przycisków
                PatchFtakGeneratorCardToThreeButtons(win, allElements);
            }
            catch { }
        }

        private static void CollapseEmptyLegacyTopBars(List<DependencyObject> allElements)
        {
            foreach (var border in allElements.OfType<Border>())
            {
                if (border.Visibility != Visibility.Visible) continue;
                if (border.Tag?.ToString() == "FTH_CLEANED_BAR") continue;

                // Sprawdź czy Border ma obramowanie i czy wszystkie jego dzieci są ukryte (Collapsed) lub puste
                if (border.BorderThickness.Top > 0 || border.BorderThickness.Bottom > 0)
                {
                    if (border.Child == null)
                    {
                        if (border.ActualHeight > 0 && border.ActualHeight < 65 && border.ActualWidth > 300)
                        {
                            border.Visibility = Visibility.Collapsed;
                            border.Height = 0;
                            border.Margin = new Thickness(0);
                            border.Padding = new Thickness(0);
                            border.BorderThickness = new Thickness(0);
                        }
                    }
                    else if (border.Child is Panel p)
                    {
                        bool anyVisibleContent = false;
                        foreach (UIElement ch in p.Children)
                        {
                            if (ch.Visibility == Visibility.Visible)
                            {
                                if (ch is Panel subP && subP.Children.Cast<UIElement>().All(x => x.Visibility != Visibility.Visible))
                                    continue;
                                if (ch is TextBlock tb && string.IsNullOrWhiteSpace(tb.Text))
                                    continue;
                                anyVisibleContent = true;
                                break;
                            }
                        }

                        if (!anyVisibleContent)
                        {
                            border.Visibility = Visibility.Collapsed;
                            border.Height = 0;
                            border.Margin = new Thickness(0);
                            border.Padding = new Thickness(0);
                            border.BorderThickness = new Thickness(0);
                        }
                    }
                }
            }
        }

        private static void PatchZeroExpirationInputs(List<DependencyObject> allElements)
        {
            foreach (var tb in allElements.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? "").Trim();
                if ((txt.Contains("Ważność", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Waznosc", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Expiration", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("TTL", StringComparison.OrdinalIgnoreCase)) &&
                    !txt.Contains("0 = bezterminowo", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = txt.TrimEnd(':') + " (0 = bezterminowo):";
                }
            }

            foreach (var box in allElements.OfType<TextBox>())
            {
                if (box.Tag?.ToString() == "FTH_ZERO_EXP_PATCHED") continue;
                var parent = VisualTreeHelper.GetParent(box);
                if (parent is Panel p)
                {
                    bool isExpBox = p.Children.OfType<TextBlock>().Any(t =>
                        (t.Text ?? "").Contains("Ważność", StringComparison.OrdinalIgnoreCase) ||
                        (t.Text ?? "").Contains("Expiration", StringComparison.OrdinalIgnoreCase) ||
                        (t.Text ?? "").Contains("bezterminowo", StringComparison.OrdinalIgnoreCase));

                    if (isExpBox)
                    {
                        box.Tag = "FTH_ZERO_EXP_PATCHED";
                        box.ToolTip = "Wpisz 0, aby paczka .ftak oraz kod QR były ważne bezterminowo.";
                    }
                }
            }
        }

        private static void PatchFactionFilesAndPosterButtons(Window win, List<DependencyObject> allElements)
        {
            foreach (var btn in allElements.OfType<Button>())
            {
                string contentStr = ExtractButtonText(btn);
                if (string.IsNullOrWhiteSpace(contentStr)) continue;

                // A. Odblokuj i podłącz przycisk "Pliki strony"
                if (contentStr.Contains("Pliki strony", StringComparison.OrdinalIgnoreCase) ||
                    contentStr.Contains("Faction files", StringComparison.OrdinalIgnoreCase) ||
                    contentStr.Contains("Folder strony", StringComparison.OrdinalIgnoreCase))
                {
                    btn.IsEnabled = true;
                    btn.Visibility = Visibility.Visible;
                    if (btn.Tag?.ToString() != "FTH_FACTION_FILES_PATCHED")
                    {
                        btn.Tag = "FTH_FACTION_FILES_PATCHED";
                        btn.Content = BuilderI18n.T("📂 Pliki strony", "📂 Faction Files");
                        btn.ToolTip = BuilderI18n.T(
                            "Otwiera dedykowany folder wyjściowy out\\<STRONA>\\ dla aktywnej strony konfliktu",
                            "Opens the dedicated out\\<FACTION>\\ folder for the active faction");

                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            try
                            {
                                string dir = EnsureActiveFactionOutputFolder(win);
                                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Nie udało się otworzyć folderu strony: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };
                    }
                }

                // B. Napraw przycisk "Plakat stron + QR" / "Plakat A4 Safezone", aby nigdy nie zamykał aplikacji
                if (contentStr.Contains("Plakat", StringComparison.OrdinalIgnoreCase) ||
                    contentStr.Contains("Safezone", StringComparison.OrdinalIgnoreCase) ||
                    (contentStr.Contains("Poster", StringComparison.OrdinalIgnoreCase) && contentStr.Contains("QR", StringComparison.OrdinalIgnoreCase)))
                {
                    btn.IsEnabled = true;
                    if (btn.Tag?.ToString() != "FTH_SAFE_POSTER_PATCHED")
                    {
                        btn.Tag = "FTH_SAFE_POSTER_PATCHED";
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            GenerateAndOpenSafezonePosterHtml(win);
                        };
                    }
                }
            }
        }

        private static void PatchFtakGeneratorCardToThreeButtons(Window win, List<DependencyObject> allElements)
        {
            foreach (var tb in allElements.OfType<TextBlock>())
            {
                string t = (tb.Text ?? "").Trim();
                if (t.Contains("GENERATOR PACZEK .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("PACKAGE BUILDER .FTAK", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject parent = VisualTreeHelper.GetParent(tb);
                    while (parent != null && !(parent is StackPanel || parent is Grid))
                    {
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                    if (parent is Panel cardPanel && cardPanel.Tag?.ToString() != "FTH_3BTN_CARD_PATCHED")
                    {
                        cardPanel.Tag = "FTH_3BTN_CARD_PATCHED";
                        tb.Text = BuilderI18n.T(
                            "🎯 GENERATOR PACZEK .FTAK (FOLDER out\\<STRONA>\\ + WSPÓLNY FOLDER APK / PLUGINY / MAPY)",
                            "🎯 .FTAK PACKAGE GENERATOR (out\\<FACTION>\\ + SHARED APK / PLUGINS / MAPS)");

                        // Ukryj dotychczasowe wiersze przycisków i ComboBox wyboru pojedynczej strony wewnątrz tej karty
                        var descendants = BuilderCoreSidebarAndProjectEngine.GetAllVisualDescendants(cardPanel).ToList();
                        foreach (var cb in descendants.OfType<ComboBox>())
                        {
                            cb.Visibility = Visibility.Collapsed;
                            cb.Width = 0;
                            cb.Height = 0;
                        }

                        Button existingBuildAllBtn = null;
                        foreach (var b in descendants.OfType<Button>())
                        {
                            string bTxt = ExtractButtonText(b);
                            if (bTxt.Contains("WSZYSTKIE", StringComparison.OrdinalIgnoreCase) ||
                                bTxt.Contains("ALL", StringComparison.OrdinalIgnoreCase))
                            {
                                existingBuildAllBtn = b;
                            }
                            b.Visibility = Visibility.Collapsed;
                            b.Height = 0;
                            b.Margin = new Thickness(0);
                        }

                        // Zbuduj nowy, czysty pasek z dokładnie 3 przyciskami
                        var threeBtnGrid = new WrapPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 8, 0, 6)
                        };

                        var btnBuildAllActive = new Button
                        {
                            Content = BuilderI18n.T("⚡ Buduj wszystkie aktywne strony", "⚡ Build All Active Factions"),
                            Height = 38,
                            MinWidth = 240,
                            Padding = new Thickness(16, 6, 16, 6),
                            Margin = new Thickness(0, 4, 10, 4),
                            Background = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            Foreground = new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand
                        };
                        btnBuildAllActive.Click += (_, __) =>
                        {
                            try
                            {
                                EnsureSharedFolderStructure();
                                if (existingBuildAllBtn != null)
                                {
                                    existingBuildAllBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                }
                                else
                                {
                                    BuildAllFactionsFallback(win);
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Błąd podczas budowania paczek: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };

                        var btnOpenShared = new Button
                        {
                            Content = BuilderI18n.T("📂 Otwórz wspólny folder (APK / Pluginy / Mapy)", "📂 Open Shared Folder (APK / Plugins / Maps)"),
                            Height = 38,
                            MinWidth = 240,
                            Padding = new Thickness(16, 6, 16, 6),
                            Margin = new Thickness(0, 4, 10, 4),
                            Background = new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                            Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                            BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand
                        };
                        btnOpenShared.Click += (_, __) =>
                        {
                            try
                            {
                                string sharedDir = EnsureSharedFolderStructure();
                                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + sharedDir + "\"") { UseShellExecute = true });
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Nie udało się otworzyć wspólnego folderu: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };

                        var btnHotspotDist = new Button
                        {
                            Content = BuilderI18n.T("📡 Dystrybucja przez HOTSPOT Wi-Fi (Paczki / OPORD)", "📡 Distribute via Wi-Fi HOTSPOT (Packages / OPORD)"),
                            Height = 38,
                            MinWidth = 250,
                            Padding = new Thickness(16, 6, 16, 6),
                            Margin = new Thickness(0, 4, 0, 4),
                            Background = new SolidColorBrush(Color.FromRgb(14, 42, 54)),
                            Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand
                        };
                        btnHotspotDist.Click += (_, __) =>
                        {
                            ShowHotspotDistributionModal(win);
                        };

                        threeBtnGrid.Children.Add(btnBuildAllActive);
                        threeBtnGrid.Children.Add(btnOpenShared);
                        threeBtnGrid.Children.Add(btnHotspotDist);

                        cardPanel.Children.Add(threeBtnGrid);
                    }
                }
            }
        }

        private static void BuildAllFactionsFallback(Window win)
        {
            string outRoot = Path.Combine(GetWorkspaceRootDir(), "out");
            Directory.CreateDirectory(outRoot);
            var snapshot = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win);
            string facDir = Path.Combine(outRoot, SanitizeFolderName(snapshot.FactionName));
            Directory.CreateDirectory(facDir);
            string jsonPath = Path.Combine(facDir, SanitizeFolderName(snapshot.FactionName) + "_ftak_profile.json");
            File.WriteAllText(jsonPath, snapshot.ToQrJson(), Encoding.UTF8);

            MessageBox.Show(
                BuilderI18n.T(
                    $"Zbudowano paczkę strony '{snapshot.FactionName}' w katalogu:\n{facDir}\n\nWażność paczki: {snapshot.ExpirationLabel}",
                    $"Built package for faction '{snapshot.FactionName}' in:\n{facDir}\n\nExpiration: {snapshot.ExpirationLabel}"),
                "Field TAK Hub — Generator .FTAK",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        public static void GenerateAndOpenSafezonePosterHtml(Window win)
        {
            try
            {
                var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win);
                string url = StartLocalHotspotServer(snap);
                string qrPayloadEncoded = Uri.EscapeDataString(snap.ToQrJson());
                string outDir = EnsureActiveFactionOutputFolder(win);
                string htmlPath = Path.Combine(outDir, $"Plakat_Safezone_{SanitizeFolderName(snap.FactionName)}.html");

                var rowsSb = new StringBuilder();
                foreach (var vc in snap.VoiceChannels)
                {
                    rowsSb.AppendLine($"<tr><td><b>{WebUtility.HtmlEncode(vc.Role)}</b></td><td>{WebUtility.HtmlEncode(vc.Band)}</td><td><b>{WebUtility.HtmlEncode(vc.Freq)}</b></td><td>{WebUtility.HtmlEncode(vc.Tone)}</td></tr>");
                }

                string html = $@"<!DOCTYPE html>
<html lang='pl'>
<head>
<meta charset='utf-8'/>
<title>Plakat Safezone A4 — {WebUtility.HtmlEncode(snap.FactionName)}</title>
<style>
  @page {{ size: A4 portrait; margin: 12mm; }}
  body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #071115; color: #fff; margin: 0; padding: 18px; }}
  .sheet {{ max-width: 190mm; margin: 0 auto; border: 3px solid {WebUtility.HtmlEncode(snap.ColorHex)}; border-radius: 12px; padding: 18px; background: #0A181E; }}
  h1 {{ margin: 0 0 6px 0; color: #1FE0C5; font-size: 22px; text-transform: uppercase; }}
  .sub {{ color: #FAD02C; font-weight: bold; font-size: 13px; margin-bottom: 14px; }}
  .qr-box {{ text-align: center; background: #fff; color: #000; padding: 16px; border-radius: 10px; margin: 14px auto; width: 240px; }}
  table {{ width: 100%; border-collapse: collapse; margin-top: 12px; font-size: 12.5px; }}
  th, td {{ border: 1px solid #224A59; padding: 7px 9px; text-align: left; }}
  th {{ background: #122C36; color: #1FE0C5; }}
  .meta {{ margin-top: 12px; font-family: Consolas, monospace; font-size: 12px; background: #071115; padding: 10px; border-radius: 8px; border: 1px solid #193844; }}
  @media print {{ body {{ background: #fff; color: #000; }} .sheet {{ background: #fff; color: #000; }} th {{ background: #eee; color: #000; }} .meta {{ background: #f7f7f7; color: #000; }} }}
</style>
</head>
<body>
<div class='sheet'>
  <h1>🛡️ KARTA WDROŻENIA STRONY: {WebUtility.HtmlEncode(snap.FactionName)}</h1>
  <div class='sub'>Operacja: {WebUtility.HtmlEncode(snap.EventName)} &nbsp;|&nbsp; Ważność paczki / QR: {WebUtility.HtmlEncode(snap.ExpirationLabel)}</div>
  <div class='qr-box'>
    <img src='https://api.qrserver.com/v1/create-qr-code/?size=220x220&data={qrPayloadEncoded}' width='220' height='220' alt='QR Code'/>
    <div style='font-size:11px;font-weight:bold;margin-top:6px;'>ZESKANUJ W FIELD TAK HUB ANDROID<br/>(Podaj Callsign np. Wilk-1 &rarr; Wlk1)</div>
  </div>
  <div class='meta'>
    <b>📡 LoRa Meshtastic:</b> Kanał '{WebUtility.HtmlEncode(snap.ChannelName)}' | Region: {WebUtility.HtmlEncode(snap.Region)} | Preset: {WebUtility.HtmlEncode(snap.ModemPreset)}<br/>
    <b>🌐 Serwer OpenTAK (OTS):</b> {WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(snap.OtsHost) ? "Tryb Off-Grid Mesh" : snap.OtsHost)}<br/>
    <b>📶 Adres Hotspot Wi-Fi (Paczka / OPORD):</b> {WebUtility.HtmlEncode(url)}<br/>
    <b>🚨 Telefon alarmowy ORGA / MEDEVAC:</b> {WebUtility.HtmlEncode(snap.EmergencyPhone)}
  </div>
  <h3 style='color:#1FE0C5;margin-bottom:4px;'>📻 TABELA KANAŁÓW GŁOSOWYCH (ŚCIĄGA OPERACYJNA)</h3>
  <table>
    <thead><tr><th>Rola / Przeznaczenie</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton CTCSS / Kod DMR</th></tr></thead>
    <tbody>{rowsSb}</tbody>
  </table>
</div>
</body>
</html>";

                File.WriteAllText(htmlPath, html, Encoding.UTF8);
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd podczas generowania plakatu QR: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public static void ShowHotspotDistributionModal(Window win)
        {
            try
            {
                var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win);
                string url = StartLocalHotspotServer(snap);

                var dlg = new Window
                {
                    Title = BuilderI18n.T("📡 Dystrybucja przez HOTSPOT Wi-Fi (Paczka .FTAK + OPORD)", "📡 Wi-Fi HOTSPOT Distribution (.FTAK + OPORD)"),
                    Width = 540,
                    Height = 360,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = win,
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    Foreground = Brushes.White
                };

                var stack = new StackPanel { Margin = new Thickness(18) };
                stack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        $"📡 AKTYWNY SERWER HOTSPOT WI-FI — STRONA: {snap.FactionName}",
                        $"📡 ACTIVE WI-FI HOTSPOT SERVER — FACTION: {snap.FactionName}"),
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                stack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        $"Ważność paczki / kodu QR: {snap.ExpirationLabel}\nPodłącz telefony z Androidem do tej samej sieci Wi-Fi lub Hotspotu z laptopa i wpisz / zeskanuj poniższy adres w aplikacji Field TAK Hub Companion:",
                        $"Expiration: {snap.ExpirationLabel}\nConnect Android phones to the same Wi-Fi / Laptop Hotspot and scan or open the URL below:"),
                    Foreground = new SolidColorBrush(Color.FromRgb(210, 228, 235)),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                var urlBox = new TextBox
                {
                    Text = url,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(10),
                    Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                stack.Children.Add(urlBox);

                var btnRow = new WrapPanel { Orientation = Orientation.Horizontal };
                var btnCopy = new Button
                {
                    Content = BuilderI18n.T("📋 Kopiuj link Hotspotu", "📋 Copy Hotspot URL"),
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(0, 0, 10, 0),
                    Background = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    Foreground = new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnCopy.Click += (_, __) =>
                {
                    Clipboard.SetText(url);
                    MessageBox.Show("Skopiowano adres: " + url, "Hotspot Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Information);
                };

                var btnPoster = new Button
                {
                    Content = BuilderI18n.T("🖨️ Otwórz Plakat A4 z Kodem QR", "🖨️ Open A4 Poster with QR Code"),
                    Padding = new Thickness(14, 8, 14, 8),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderThickness = new Thickness(1.5),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnPoster.Click += (_, __) => GenerateAndOpenSafezonePosterHtml(win);

                btnRow.Children.Add(btnCopy);
                btnRow.Children.Add(btnPoster);
                stack.Children.Add(btnRow);

                dlg.Content = stack;
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd uruchamiania dystrybucji Hotspot: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string StartLocalHotspotServer(FactionSnapshotData snap)
        {
            string localIp = "127.0.0.1";
            try
            {
                foreach (var ni in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ni.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ni))
                    {
                        localIp = ni.ToString();
                        break;
                    }
                }
            }
            catch { }

            try
            {
                if (_hotspotListener == null || !_hotspotListener.IsListening)
                {
                    _hotspotListener = new HttpListener();
                    _hotspotListener.Prefixes.Add($"http://+:{_hotspotPort}/");
                    _hotspotListener.Start();
                    _hotspotListener.BeginGetContext(OnHotspotHttpRequest, null);
                }
            }
            catch
            {
                try
                {
                    _hotspotListener = new HttpListener();
                    _hotspotListener.Prefixes.Add($"http://localhost:{_hotspotPort}/");
                    _hotspotListener.Start();
                    _hotspotListener.BeginGetContext(OnHotspotHttpRequest, null);
                }
                catch { }
            }

            LatestHotspotJsonPayload = snap.ToQrJson();
            return $"http://{localIp}:{_hotspotPort}/ftak.json";
        }

        public static string LatestHotspotJsonPayload = "{}";

        private static void OnHotspotHttpRequest(IAsyncResult ar)
        {
            try
            {
                if (_hotspotListener == null || !_hotspotListener.IsListening) return;
                var ctx = _hotspotListener.EndGetContext(ar);
                _hotspotListener.BeginGetContext(OnHotspotHttpRequest, null);

                byte[] buf = Encoding.UTF8.GetBytes(LatestHotspotJsonPayload ?? "{}");
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        private static string ExtractButtonText(Button btn)
        {
            if (btn.Content is string s) return s;
            if (btn.Content is TextBlock tb) return tb.Text ?? "";
            if (btn.Content is Panel p)
            {
                var texts = p.Children.OfType<TextBlock>().Select(x => x.Text);
                return string.Join(" ", texts);
            }
            return btn.Content?.ToString() ?? "";
        }
    }

    internal sealed class FactionVoiceRowData
    {
        public string Role { get; set; } = "Dowodzenie HQ";
        public string Band { get; set; } = "PMR";
        public string Freq { get; set; } = "CH 1 (446.00625 MHz)";
        public string Tone { get; set; } = "CTCSS 67.0 Hz";
    }

    internal sealed class FactionSnapshotData
    {
        public string EventName { get; set; } = "MILSIM 2026";
        public string FactionName { get; set; } = "BLUFOR";
        public string ColorHex { get; set; } = "#2684FF";
        public string ChannelName { get; set; } = "BLUFOR-MESH";
        public string Psk { get; set; } = "";
        public string Region { get; set; } = "EU_868";
        public string ModemPreset { get; set; } = "SHORT_FAST";
        public string OtsHost { get; set; } = "";
        public string EmergencyPhone { get; set; } = "+48 600 000 000";
        public int ExpirationHours { get; set; } = 0;
        public string ExpirationLabel => ExpirationHours <= 0
            ? BuilderI18n.T("Bezterminowo (0)", "Permanent / No Expiry (0)")
            : $"{ExpirationHours} h";
        public List<FactionVoiceRowData> VoiceChannels { get; set; } = new List<FactionVoiceRowData>();

        public string ToQrJson()
        {
            var vcJson = new StringBuilder("[");
            for (int i = 0; i < VoiceChannels.Count; i++)
            {
                var v = VoiceChannels[i];
                if (i > 0) vcJson.Append(",");
                vcJson.Append($"{{\"roleName\":\"{Esc(v.Role)}\",\"bandType\":\"{Esc(v.Band)}\",\"freqMhz\":\"{Esc(v.Freq)}\",\"toneOrCode\":\"{Esc(v.Tone)}\"}}");
            }
            vcJson.Append("]");

            return $"{{\"schemaVersion\":\"3.5\",\"eventName\":\"{Esc(EventName)}\",\"Name\":\"{Esc(FactionName)}\",\"ColorHex\":\"{Esc(ColorHex)}\",\"ChannelName\":\"{Esc(ChannelName)}\",\"Psk\":\"{Esc(Psk)}\",\"LoraRegion\":\"{Esc(Region)}\",\"LoraModemPreset\":\"{Esc(ModemPreset)}\",\"OtsHost\":\"{Esc(OtsHost)}\",\"EmergencyPhone\":\"{Esc(EmergencyPhone)}\",\"ExpirationHours\":{ExpirationHours},\"IsPermanent\":{(ExpirationHours <= 0 ? "true" : "false")},\"VoiceChannels\":{vcJson}}}";
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    internal static class FactionLiveSyncBridge
    {
        public static List<string> GetAllFactionNamesFromUi(Window win)
        {
            var result = new List<string>();
            if (win == null) return new List<string> { "BLUFOR", "OPFOR" };

            try
            {
                var all = BuilderCoreSidebarAndProjectEngine.GetAllVisualDescendants(win);
                foreach (var lb in all.OfType<ListBox>())
                {
                    foreach (var item in lb.Items)
                    {
                        string name = ExtractFactionItemName(item);
                        if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
                            result.Add(name);
                    }
                }
                foreach (var tb in all.OfType<TextBox>())
                {
                    var parent = VisualTreeHelper.GetParent(tb) as Panel;
                    if (parent != null && parent.Children.OfType<TextBlock>().Any(l =>
                        (l.Text ?? "").Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase) ||
                        (l.Text ?? "").Contains("Faction", StringComparison.OrdinalIgnoreCase)))
                    {
                        string val = (tb.Text ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(val) && !result.Contains(val, StringComparer.OrdinalIgnoreCase))
                            result.Insert(0, val);
                    }
                }
            }
            catch { }

            if (result.Count == 0)
            {
                result.Add("BLUFOR");
                result.Add("OPFOR");
            }
            return result;
        }

        private static string ExtractFactionItemName(object item)
        {
            if (item == null) return "";
            var prop = item.GetType().GetProperty("Name") ?? item.GetType().GetProperty("FactionName");
            if (prop != null) return prop.GetValue(item)?.ToString() ?? "";
            return item.ToString() ?? "";
        }

        public static FactionSnapshotData CaptureCurrentFactionSnapshot(Window win, string preferredFactionName = null)
        {
            var snap = new FactionSnapshotData();
            if (!string.IsNullOrWhiteSpace(preferredFactionName))
                snap.FactionName = preferredFactionName.Trim();

            if (win == null) return snap;

            try
            {
                var all = BuilderCoreSidebarAndProjectEngine.GetAllVisualDescendants(win);

                // Zczytaj pola tekstowe z Generatora Paczek na podstawie sąsiadujących etykiet
                foreach (var tb in all.OfType<TextBox>())
                {
                    string val = (tb.Text ?? "").Trim();
                    string label = FindNearbyLabelText(tb);
                    if (string.IsNullOrWhiteSpace(label)) continue;

                    if (label.Contains("wydarzenia", StringComparison.OrdinalIgnoreCase) || label.Contains("Event", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(val)) snap.EventName = val;
                    }
                    else if (label.Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase) || label.Contains("Faction", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(preferredFactionName) && !string.IsNullOrWhiteSpace(val))
                            snap.FactionName = val;
                    }
                    else if (label.Contains("Kolor", StringComparison.OrdinalIgnoreCase) || label.Contains("HEX", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(val)) snap.ColorHex = val;
                    }
                    else if (label.Contains("kanału Meshtastic", StringComparison.OrdinalIgnoreCase) || label.Contains("Channel Name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(val)) snap.ChannelName = val;
                    }
                    else if (label.Contains("PSK", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(val)) snap.Psk = val;
                    }
                    else if (label.Contains("OpenTAK", StringComparison.OrdinalIgnoreCase) || label.Contains("OTS Host", StringComparison.OrdinalIgnoreCase))
                    {
                        snap.OtsHost = val;
                    }
                    else if (label.Contains("alarmowy", StringComparison.OrdinalIgnoreCase) || label.Contains("Emergency", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(val)) snap.EmergencyPhone = val;
                    }
                    else if (label.Contains("Ważność", StringComparison.OrdinalIgnoreCase) || label.Contains("Expiration", StringComparison.OrdinalIgnoreCase) || label.Contains("bezterminowo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, out int hours))
                            snap.ExpirationHours = hours <= 0 ? 0 : hours;
                    }
                }

                // Zczytaj wiersze kanałów głosowych z DataGrid / ItemsControl lub obiektów w pamięci
                foreach (var dg in all.OfType<DataGrid>())
                {
                    foreach (var rowItem in dg.Items)
                    {
                        if (rowItem == null) continue;
                        var t = rowItem.GetType();
                        var pRole = t.GetProperty("Role") ?? t.GetProperty("RoleName") ?? t.GetProperty("Name") ?? t.GetProperty("ChannelRole");
                        var pBand = t.GetProperty("Band") ?? t.GetProperty("BandType");
                        var pFreq = t.GetProperty("Frequency") ?? t.GetProperty("FreqMhz") ?? t.GetProperty("Freq") ?? t.GetProperty("Channel");
                        var pTone = t.GetProperty("Tone") ?? t.GetProperty("ToneOrCode") ?? t.GetProperty("Ctcss");

                        if (pRole != null || pFreq != null)
                        {
                            string rStr = pRole?.GetValue(rowItem)?.ToString() ?? "";
                            string bStr = pBand?.GetValue(rowItem)?.ToString() ?? "PMR";
                            string fStr = pFreq?.GetValue(rowItem)?.ToString() ?? "";
                            string tStr = pTone?.GetValue(rowItem)?.ToString() ?? "-";
                            if (!string.IsNullOrWhiteSpace(rStr) || !string.IsNullOrWhiteSpace(fStr))
                            {
                                snap.VoiceChannels.Add(new FactionVoiceRowData
                                {
                                    Role = string.IsNullOrWhiteSpace(rStr) ? "Kanał głosowy" : rStr,
                                    Band = bStr,
                                    Freq = fStr,
                                    Tone = tStr
                                });
                            }
                        }
                    }
                }

                // Sprawdź również dynamiczne wiersze TextBoxów w sekcji Kanałów Głosowych
                if (snap.VoiceChannels.Count == 0)
                {
                    foreach (var panel in all.OfType<Grid>())
                    {
                        var boxes = panel.Children.OfType<TextBox>().ToList();
                        var combos = panel.Children.OfType<ComboBox>().ToList();
                        if (boxes.Count >= 2 && boxes.Count <= 4 && panel.ColumnDefinitions.Count >= 3)
                        {
                            string b0 = (boxes[0].Text ?? "").Trim();
                            string b1 = (boxes[1].Text ?? "").Trim();
                            string b2 = boxes.Count >= 3 ? (boxes[2].Text ?? "").Trim() : "-";
                            string band = combos.Count > 0 ? (combos[0].SelectedItem?.ToString() ?? "PMR") : "PMR";

                            if ((b1.Contains("446") || b1.Contains("MHz") || b1.Contains("CH", StringComparison.OrdinalIgnoreCase) || b1.Contains("145") || b1.Contains("433")) &&
                                !string.IsNullOrWhiteSpace(b0))
                            {
                                snap.VoiceChannels.Add(new FactionVoiceRowData
                                {
                                    Role = b0,
                                    Band = band,
                                    Freq = b1,
                                    Tone = b2
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            if (snap.VoiceChannels.Count == 0)
            {
                snap.VoiceChannels.Add(new FactionVoiceRowData { Role = "Dowodzenie HQ", Band = "PMR", Freq = "CH 1 (446.00625 MHz)", Tone = "CTCSS 67.0 Hz" });
                snap.VoiceChannels.Add(new FactionVoiceRowData { Role = "Taktyczny (Zapasowy)", Band = "PMR", Freq = "CH 3 (446.03125 MHz)", Tone = "CTCSS 88.5 Hz" });
                snap.VoiceChannels.Add(new FactionVoiceRowData { Role = "Ratunkowy ORGA / HQ", Band = "PMR", Freq = "CH 8 (446.09375 MHz)", Tone = "Brak tonu" });
            }

            return snap;
        }

        private static string FindNearbyLabelText(DependencyObject control)
        {
            var parent = VisualTreeHelper.GetParent(control) as Panel;
            if (parent == null) return "";
            var tbs = parent.Children.OfType<TextBlock>().ToList();
            if (tbs.Count > 0) return string.Join(" ", tbs.Select(x => x.Text));
            return "";
        }
    }
}
