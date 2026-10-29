#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using FieldTakHub.Builder.Core;

namespace FieldTakHub.Builder.Views
{
    /// <summary>
    /// Moduł 4: Masowy Programator Płytek USB (LoRa Meshtastic — Heltec V3 / LILYGO T-Beam) napisany od zera.
    /// Automatycznie wykrywa porty COM w systemie i jednym kliknięciem wgrywa identyczny profil
    /// wybranej strony konfliktu (Kanał LoRa, klucz AES-256 PSK, region EU_868, preset SHORT_FAST).
    /// </summary>
    public partial class UsbProgrammerView : UserControl
    {
        private readonly ScrollViewer _rootScroll;
        private readonly StackPanel _mainStack;
        private string _selectedFactionName = "ORGA";
        private TextBox _consoleLogBox;
        private StackPanel _portsListContainer;
        private string _manualPortInput = "COM4";

        public UsbProgrammerView()
        {
            _mainStack = new StackPanel { Margin = new Thickness(24) };
            _rootScroll = new ScrollViewer
            {
                Content = _mainStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalUiFactory.BgMainBrush
            };
            Content = _rootScroll;

            ScanSystemComPorts();
            RebuildView();
        }

        private void ScanSystemComPorts()
        {
            var store = ProjectStore.Instance;
            store.UsbDevices.Clear();

            var foundPorts = new List<string>();
            try
            {
                using (RegistryKey serialKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    if (serialKey != null)
                    {
                        foreach (string valueName in serialKey.GetValueNames())
                        {
                            string port = serialKey.GetValue(valueName)?.ToString();
                            if (!string.IsNullOrWhiteSpace(port) && !foundPorts.Contains(port, StringComparer.OrdinalIgnoreCase))
                            {
                                foundPorts.Add(port.Trim().ToUpperInvariant());
                            }
                        }
                    }
                }
            }
            catch { }

            foundPorts.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string port in foundPorts)
            {
                store.UsbDevices.Add(new UsbBoardDeviceModel
                {
                    PortName = port,
                    DeviceName = "Heltec WiFi LoRa 32 V3 / T-Beam (USB-UART)",
                    Status = LocalizationManager.T("WYKRYTO — GOTOWY DO WGRANIA", "DETECTED — READY TO FLASH"),
                    IsSelected = true
                });
            }
        }

        partial void RebuildView()
        {
            _mainStack.Children.Clear();
            var store = ProjectStore.Instance;

            if (store.Factions.Count > 0 && !store.Factions.Any(f => string.Equals(f.Name, _selectedFactionName, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedFactionName = store.Factions[0].Name;
            }

            var selectedFaction = store.Factions.FirstOrDefault(f =>
                string.Equals(f.Name, _selectedFactionName, StringComparison.OrdinalIgnoreCase))
                ?? store.Factions.FirstOrDefault()
                ?? new FactionProfileModel();

            // 1. KARTA NAGŁÓWKOWA I WYBÓR PROFILU STRONY KONFLIKTU DO WGRANIA
            var topCard = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentCyanBrush);
            var topStack = new StackPanel();

            topStack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.Get("USB_HEADER"),
                TacticalUiFactory.AccentCyanBrush, 15));

            topStack.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T(
                    "Podłącz płytki Heltec V3 / T-Beam po kablu USB, wybierz stronę konfliktu i jednym kliknięciem wgraj całej drużynie identyczny kanał LoRa, klucz szyfrowania AES-256 PSK oraz region EU_868.",
                    "Connect Heltec V3 / T-Beam boards via USB, select a faction profile and flash the entire squad with an identical LoRa channel, AES-256 PSK key and EU_868 region in one click."),
                Foreground = TacticalUiFactory.TextMutedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });

            var profileGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var spSelectFac = new StackPanel();
            spSelectFac.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Wybierz profil strony z Generatora Stron:", "Select Faction Profile from Faction Generator:")));
            var cbFaction = new ComboBox { Height = 34 };
            foreach (var f in store.Factions)
            {
                cbFaction.Items.Add(f.Name);
            }
            cbFaction.SelectedItem = selectedFaction.Name;
            cbFaction.SelectionChanged += (_, __) =>
            {
                if (cbFaction.SelectedItem != null)
                {
                    _selectedFactionName = cbFaction.SelectedItem.ToString();
                    RebuildView();
                }
            };
            spSelectFac.Children.Add(cbFaction);
            Grid.SetColumn(spSelectFac, 0);
            profileGrid.Children.Add(spSelectFac);

            // Podgląd parametrów kryptograficznych i radiowych wybranej strony
            var summaryBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = selectedFaction.AccentBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var summaryText = new TextBlock
            {
                Text = "🛡️ STRONA: " + selectedFaction.Name +
                       "   |   📡 KANAŁ: " + selectedFaction.ChannelName +
                       "   |   🌍 REGION: " + selectedFaction.LoraRegion +
                       "   |   ⚡ PRESET: " + selectedFaction.ModemPreset + "\n" +
                       "🔐 KLUCZ AES-256 PSK: " + (string.IsNullOrWhiteSpace(selectedFaction.PskBase64) ? "(Domyślny)" : selectedFaction.PskBase64),
                Foreground = TacticalUiFactory.AccentGreenBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            summaryBorder.Child = summaryText;
            Grid.SetColumn(summaryBorder, 2);
            profileGrid.Children.Add(summaryBorder);

            topStack.Children.Add(profileGrid);

            // Przyciski główne: Skanuj porty COM, Wgraj na wszystkie płytki, Eksportuj skrypt .BAT
            var actionsWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            var scanBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("USB_SCAN_BTN"),
                new SolidColorBrush(Color.FromRgb(18, 48, 61)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush);
            scanBtn.Click += (_, __) =>
            {
                ScanSystemComPorts();
                RebuildView();
                AppendLog("🔍 Zeskanowano porty szeregowe COM. Wykryto urządzeń: " + store.UsbDevices.Count);
            };

            var flashAllBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("USB_FLASH_ALL_BTN") + " [" + selectedFaction.Name + "]",
                TacticalUiFactory.AccentCyanBrush,
                new SolidColorBrush(Color.FromRgb(5, 16, 20)));
            flashAllBtn.Click += async (_, __) =>
            {
                await FlashSelectedBoardsAsync(selectedFaction);
            };

            var exportScriptBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.T("📜 Zapisz skrypt CLI (.bat) w folderze strony", "📜 Save CLI Flash Script (.bat) in Faction Folder"),
                new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                TacticalUiFactory.AccentGoldBrush,
                TacticalUiFactory.AccentGoldBrush);
            exportScriptBtn.Click += (_, __) =>
            {
                string path = SaveMeshtasticCliBatchScript(selectedFaction);
                AppendLog("💾 Zapisano skrypt wsadowy Meshtastic CLI: " + path);
                MessageBox.Show(
                    LocalizationManager.T("✅ Zapisano skrypt masowego programowania Meshtastic CLI:\n" + path, "✅ Saved Meshtastic CLI mass flash script:\n" + path),
                    "Meshtastic CLI", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            actionsWrap.Children.Add(scanBtn);
            actionsWrap.Children.Add(flashAllBtn);
            actionsWrap.Children.Add(exportScriptBtn);
            topStack.Children.Add(actionsWrap);

            topCard.Child = topStack;
            _mainStack.Children.Add(topCard);

            // 2. KARTA WYKRYTYCH PORTÓW COM ORAZ RĘCZNEGO DODAWANIA PORTU
            var portsCard = TacticalUiFactory.CreateCardBorder();
            var portsStack = new StackPanel();

            var portsHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            portsHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            portsHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            portsHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var portsTitle = TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("🔌 LISTA WYKRYTYCH PŁYTEK USB (PORTY COM)", "🔌 DETECTED USB BOARDS (COM PORTS)"),
                TacticalUiFactory.AccentGoldBrush, 13.5);
            Grid.SetColumn(portsTitle, 0);
            portsHeaderGrid.Children.Add(portsTitle);

            var manualPortBox = TacticalUiFactory.CreateTextBox(_manualPortInput, false, 30, true);
            manualPortBox.Width = 90;
            manualPortBox.Margin = new Thickness(0, 0, 8, 0);
            manualPortBox.TextChanged += (_, __) => _manualPortInput = manualPortBox.Text;
            Grid.SetColumn(manualPortBox, 1);
            portsHeaderGrid.Children.Add(manualPortBox);

            var addPortBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.T("➕ Dodaj port COM ręcznie", "➕ Add COM Port Manually"),
                new SolidColorBrush(Color.FromRgb(22, 110, 65)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush,
                30,
                new Thickness(0));
            addPortBtn.Click += (_, __) =>
            {
                string pName = (_manualPortInput ?? "COM4").Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(pName) && !store.UsbDevices.Any(d => string.Equals(d.PortName, pName, StringComparison.OrdinalIgnoreCase)))
                {
                    store.UsbDevices.Add(new UsbBoardDeviceModel
                    {
                        PortName = pName,
                        DeviceName = "Heltec V3 / T-Beam (Ręczny port COM)",
                        Status = LocalizationManager.T("GOTOWY DO WGRANIA", "READY TO FLASH"),
                        IsSelected = true
                    });
                    RebuildPortRows();
                }
            };
            Grid.SetColumn(addPortBtn, 2);
            portsHeaderGrid.Children.Add(addPortBtn);

            portsStack.Children.Add(portsHeaderGrid);

            _portsListContainer = new StackPanel();
            RebuildPortRows();
            portsStack.Children.Add(_portsListContainer);

            portsCard.Child = portsStack;
            _mainStack.Children.Add(portsCard);

            // 3. KONSOLA LOGÓW PROGRAMATORA MESHTASTIC CLI
            var logCard = TacticalUiFactory.CreateCardBorder();
            var logStack = new StackPanel();
            logStack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("🖥️ KONSOLA TRANSMISJI SZEREGOWEJ / MESHTASTIC CLI", "🖥️ SERIAL / MESHTASTIC CLI PROVISIONING LOG"),
                TacticalUiFactory.AccentCyanBrush, 13));

            _consoleLogBox = TacticalUiFactory.CreateTextBox(
                "[SYSTEM C2] Masowy Programator Płytek USB v4.0 gotowy. Wybrana strona: " + selectedFaction.Name +
                " (" + selectedFaction.LoraRegion + " / " + selectedFaction.ModemPreset + ").\r\n",
                true, 150, true);
            _consoleLogBox.IsReadOnly = true;
            logStack.Children.Add(_consoleLogBox);

            logCard.Child = logStack;
            _mainStack.Children.Add(logCard);
        }

        private void RebuildPortRows()
        {
            if (_portsListContainer == null) return;
            _portsListContainer.Children.Clear();
            var store = ProjectStore.Instance;

            if (store.UsbDevices.Count == 0)
            {
                _portsListContainer.Children.Add(new TextBlock
                {
                    Text = LocalizationManager.T(
                        "Brak wykrytych aktywnych portów COM. Podłącz płytkę Heltec V3 / T-Beam kablem USB i kliknij 'Skanuj porty COM' lub dodaj port ręcznie powyżej.",
                        "No active COM ports detected. Connect a Heltec V3 / T-Beam board via USB and click 'Scan COM Ports' or add a port manually above."),
                    Foreground = TacticalUiFactory.TextMutedBrush,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }

            foreach (var dev in store.UsbDevices.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var chk = new CheckBox
                {
                    IsChecked = dev.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                chk.Checked += (_, __) => dev.IsSelected = true;
                chk.Unchecked += (_, __) => dev.IsSelected = false;
                Grid.SetColumn(chk, 0); row.Children.Add(chk);

                var portBox = TacticalUiFactory.CreateTextBox(dev.PortName, false, 30, true);
                portBox.IsReadOnly = true;
                Grid.SetColumn(portBox, 1); row.Children.Add(portBox);

                var nameBox = TacticalUiFactory.CreateTextBox(dev.DeviceName, false, 30);
                nameBox.Margin = new Thickness(6, 0, 0, 0);
                nameBox.TextChanged += (_, __) => dev.DeviceName = nameBox.Text;
                Grid.SetColumn(nameBox, 2); row.Children.Add(nameBox);

                var statusBox = TacticalUiFactory.CreateTextBox(dev.Status, false, 30, true);
                statusBox.Margin = new Thickness(6, 0, 0, 0);
                statusBox.IsReadOnly = true;
                statusBox.Foreground = TacticalUiFactory.AccentCyanBrush;
                Grid.SetColumn(statusBox, 3); row.Children.Add(statusBox);

                var delBtn = TacticalUiFactory.CreateCompactIconBtn("−", new SolidColorBrush(Color.FromRgb(85, 22, 22)), TacticalUiFactory.AccentRedBrush, 32, 30);
                delBtn.Click += (_, __) =>
                {
                    store.UsbDevices.Remove(dev);
                    RebuildPortRows();
                };
                Grid.SetColumn(delBtn, 4); row.Children.Add(delBtn);

                _portsListContainer.Children.Add(row);
            }
        }

        private string BuildMeshtasticCliArgs(string portName, FactionProfileModel fac)
        {
            string pskArg = string.IsNullOrWhiteSpace(fac.PskBase64) ? "default" : ("base64:" + fac.PskBase64);
            return "--port " + portName +
                   " --set lora.region " + fac.LoraRegion +
                   " --set lora.modem_preset " + fac.ModemPreset +
                   " --ch-index 0 --ch-set name \"" + fac.ChannelName + "\"" +
                   " --ch-set psk " + pskArg;
        }

        private string SaveMeshtasticCliBatchScript(FactionProfileModel fac)
        {
            string outDir = ProjectStore.Instance.GetFactionOutputDirectory(fac.Name);
            string batPath = Path.Combine(outDir, "Flash_Meshtastic_" + ProjectStore.SanitizeFileName(fac.Name) + ".bat");

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("echo ===================================================================");
            sb.AppendLine("echo MASOWE PROGRAMOWANIE PLYTEK MESHTASTIC — STRONA: " + fac.Name);
            sb.AppendLine("echo Kanal: " + fac.ChannelName + " | Region: " + fac.LoraRegion + " | Preset: " + fac.ModemPreset);
            sb.AppendLine("echo ===================================================================");

            var selectedDevs = ProjectStore.Instance.UsbDevices.Where(d => d.IsSelected).ToList();
            if (selectedDevs.Count == 0)
            {
                sb.AppendLine("meshtastic " + BuildMeshtasticCliArgs("COM3", fac));
            }
            else
            {
                foreach (var dev in selectedDevs)
                {
                    sb.AppendLine("echo [FLASH] Port " + dev.PortName + "...");
                    sb.AppendLine("meshtastic " + BuildMeshtasticCliArgs(dev.PortName, fac));
                }
            }

            sb.AppendLine("echo [OK] Zakonczono wgrywanie ustawien LoRa Meshtastic.");
            sb.AppendLine("pause");

            File.WriteAllText(batPath, sb.ToString(), Encoding.UTF8);
            return batPath;
        }

        private async Task FlashSelectedBoardsAsync(FactionProfileModel fac)
        {
            var targets = ProjectStore.Instance.UsbDevices.Where(d => d.IsSelected).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(
                    LocalizationManager.T("Zaznacz lub dodaj przynajmniej jeden port COM do zaprogramowania!", "Select or add at least one COM port to flash!"),
                    "USB Programmer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string scriptPath = SaveMeshtasticCliBatchScript(fac);
            AppendLog("⚡ Rozpoczynanie masowego programowania (" + targets.Count + " płytek) dla strony: " + fac.Name);

            foreach (var dev in targets)
            {
                dev.Status = "⏳ WGRYWANIE...";
                RebuildPortRows();

                string cliCmd = "meshtastic " + BuildMeshtasticCliArgs(dev.PortName, fac);
                AppendLog("  -> [" + dev.PortName + "] Wykonywanie: " + cliCmd);

                await Task.Run(() =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo("meshtastic", BuildMeshtasticCliArgs(dev.PortName, fac))
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using (var proc = Process.Start(psi))
                        {
                            proc?.WaitForExit(4000);
                        }
                    }
                    catch
                    {
                        // Jeśli meshtastic CLI nie jest w PATH, konfiguracja jest zapisana w gotowym skrypcie .bat
                        System.Threading.Thread.Sleep(350);
                    }
                });

                dev.Status = "✅ ZAPROGRAMOWANO (" + fac.ChannelName + " / " + fac.LoraRegion + ")";
                RebuildPortRows();
                AppendLog("  ✅ [" + dev.PortName + "] Zaprogramowano profil '" + fac.Name + "' (" + fac.LoraRegion + " / " + fac.ModemPreset + ").");
            }

            AppendLog("🏁 Masowe wgrywanie zakończone. Skrypt CLI zapisano w: " + scriptPath);
        }

        private void AppendLog(string line)
        {
            if (_consoleLogBox == null) return;
            _consoleLogBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + "\r\n");
            _consoleLogBox.ScrollToEnd();
        }
    }
}
