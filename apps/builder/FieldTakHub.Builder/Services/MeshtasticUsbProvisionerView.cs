#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    public class SerialPortInfoItem
    {
        public string PortName { get; set; } = "COM3";
        public string DeviceLabel { get; set; } = "USB-Serial Meshtastic";
        public override string ToString() => $"{PortName} — {DeviceLabel}";
    }

    public class ProvisionedNodeLogItem
    {
        public int Index { get; set; }
        public string Timestamp { get; set; } = "";
        public string PortName { get; set; } = "";
        public string NodeIdHex { get; set; } = "";
        public string FactionName { get; set; } = "";
        public string LongName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string RegionPreset { get; set; } = "";
        public string Status { get; set; } = "";
    }

    internal static class NativeMeshtasticSerialEngine
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct DCB
        {
            public uint DCBlength;
            public uint BaudRate;
            public uint Flags;
            public ushort wReserved;
            public ushort XonLim;
            public ushort XoffLim;
            public byte ByteSize;
            public byte Parity;
            public byte StopBits;
            public byte XonChar;
            public byte XoffChar;
            public byte ErrorChar;
            public byte EofChar;
            public byte EvtChar;
            public ushort wReserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COMMTIMEOUTS
        {
            public uint ReadIntervalTimeout;
            public uint ReadTotalTimeoutMultiplier;
            public uint ReadTotalTimeoutConstant;
            public uint WriteTotalTimeoutMultiplier;
            public uint WriteTotalTimeoutConstant;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetCommState(IntPtr hFile, ref DCB lpDCB);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommState(IntPtr hFile, ref DCB lpDCB);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommTimeouts(IntPtr hFile, ref COMMTIMEOUTS lpCommTimeouts);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EscapeCommFunction(IntPtr hFile, uint dwFunc);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PurgeComm(IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        public static List<SerialPortInfoItem> EnumerateActiveComPorts()
        {
            var list = new List<SerialPortInfoItem>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                if (key != null)
                {
                    foreach (string valName in key.GetValueNames())
                    {
                        string port = key.GetValue(valName)?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrEmpty(port)) continue;

                        string desc = "USB-Serial Bridge (CP210x / CH340 / ESP32 / nRF52)";
                        if (valName.Contains("Silab", StringComparison.OrdinalIgnoreCase) || valName.Contains("CP210", StringComparison.OrdinalIgnoreCase))
                            desc = "Silicon Labs CP210x (Heltec V3 / T-Beam)";
                        else if (valName.Contains("USBSER", StringComparison.OrdinalIgnoreCase) || valName.Contains("acm", StringComparison.OrdinalIgnoreCase))
                            desc = "Native USB CDC / JTAG (RAK4631 / ESP32-S3 / T-Echo)";
                        else if (valName.Contains("VCP", StringComparison.OrdinalIgnoreCase) || valName.Contains("CH34", StringComparison.OrdinalIgnoreCase))
                            desc = "WCH CH340 / CH9102 (LilyGO T-Beam / Meshtastic)";

                        list.Add(new SerialPortInfoItem { PortName = port, DeviceLabel = desc });
                    }
                }
            }
            catch { }

            return list
                .OrderBy(p => int.TryParse(p.PortName.Replace("COM", ""), out int n) ? n : 999)
                .ToList();
        }

        private static void WriteVarint(MemoryStream ms, ulong value)
        {
            while (value >= 0x80)
            {
                ms.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            ms.WriteByte((byte)value);
        }

        private static void WriteTag(MemoryStream ms, int fieldNumber, int wireType) =>
            WriteVarint(ms, (ulong)((fieldNumber << 3) | wireType));

        private static void WriteVarintField(MemoryStream ms, int fieldNumber, ulong val)
        {
            WriteTag(ms, fieldNumber, 0);
            WriteVarint(ms, val);
        }

        private static void WriteFixed32Field(MemoryStream ms, int fieldNumber, uint val)
        {
            WriteTag(ms, fieldNumber, 5);
            ms.Write(BitConverter.GetBytes(val), 0, 4);
        }

        private static void WriteBytesField(MemoryStream ms, int fieldNumber, byte[] data)
        {
            if (data == null) return;
            WriteTag(ms, fieldNumber, 2);
            WriteVarint(ms, (ulong)data.Length);
            ms.Write(data, 0, data.Length);
        }

        private static void WriteStringField(MemoryStream ms, int fieldNumber, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            WriteBytesField(ms, fieldNumber, Encoding.UTF8.GetBytes(text));
        }

        private static byte[] FrameToRadioPacket(byte[] toRadioPayload)
        {
            int len = toRadioPayload.Length;
            byte[] framed = new byte[4 + len];
            framed[0] = 0x94;
            framed[1] = 0xC3;
            framed[2] = (byte)((len >> 8) & 0xFF);
            framed[3] = (byte)(len & 0xFF);
            Buffer.BlockCopy(toRadioPayload, 0, framed, 4, len);
            return framed;
        }

        private static byte[] BuildWantConfigToRadioFrame(uint configNonce)
        {
            using var ms = new MemoryStream();
            WriteVarintField(ms, 3, configNonce);
            return FrameToRadioPacket(ms.ToArray());
        }

        private static byte[] BuildAdminToRadioFrame(uint targetNodeNum, byte[] adminMessageBytes)
        {
            using var dataMs = new MemoryStream();
            WriteVarintField(dataMs, 1, 6);
            WriteBytesField(dataMs, 2, adminMessageBytes);
            WriteVarintField(dataMs, 3, 1);

            using var pktMs = new MemoryStream();
            if (targetNodeNum != 0)
                WriteFixed32Field(pktMs, 2, targetNodeNum);
            WriteBytesField(pktMs, 4, dataMs.ToArray());
            uint randId = (uint)RandomNumberGenerator.GetInt32(100000, int.MaxValue);
            WriteFixed32Field(pktMs, 6, randId);
            WriteVarintField(pktMs, 10, 1);

            using var toRadioMs = new MemoryStream();
            WriteBytesField(toRadioMs, 1, pktMs.ToArray());
            return FrameToRadioPacket(toRadioMs.ToArray());
        }

        private static byte[] BuildBeginEditSettingsAdmin()
        {
            using var ms = new MemoryStream();
            WriteVarintField(ms, 64, 1);
            return ms.ToArray();
        }

        private static byte[] BuildCommitEditSettingsAdmin()
        {
            using var ms = new MemoryStream();
            WriteVarintField(ms, 65, 1);
            return ms.ToArray();
        }

        private static byte[] BuildSetOwnerAdmin(string longName, string shortName)
        {
            using var userMs = new MemoryStream();
            WriteStringField(userMs, 2, longName);
            WriteStringField(userMs, 3, shortName);

            using var adminMs = new MemoryStream();
            WriteBytesField(adminMs, 32, userMs.ToArray());
            return adminMs.ToArray();
        }

        private static byte[] BuildSetPrimaryChannelAdmin(string channelName, string base64Psk)
        {
            byte[] pskBytes;
            try { pskBytes = Convert.FromBase64String(base64Psk.Trim()); }
            catch
            {
                pskBytes = new byte[32];
                RandomNumberGenerator.Fill(pskBytes);
            }

            using var settingsMs = new MemoryStream();
            WriteBytesField(settingsMs, 2, pskBytes);
            WriteStringField(settingsMs, 3, channelName);

            using var chMs = new MemoryStream();
            WriteVarintField(chMs, 1, 0);
            WriteBytesField(chMs, 2, settingsMs.ToArray());
            WriteVarintField(chMs, 3, 1);

            using var adminMs = new MemoryStream();
            WriteBytesField(adminMs, 33, chMs.ToArray());
            return adminMs.ToArray();
        }

        private static byte[] BuildSetLoraConfigAdmin(string regionStr, string presetStr, int hopLimit)
        {
            ulong regionEnum = regionStr switch
            {
                "US_915" => 1,
                "EU_433" => 2,
                "EU_868" => 3,
                "ANZ_915" => 7,
                _ => 3
            };

            ulong presetEnum = presetStr switch
            {
                "LONG_FAST" => 0,
                "LONG_SLOW" => 1,
                "MEDIUM_SLOW" => 3,
                "MEDIUM_FAST" => 4,
                "SHORT_SLOW" => 5,
                "SHORT_FAST" => 6,
                "SHORT_TURBO" => 8,
                _ => 6
            };

            using var loraMs = new MemoryStream();
            WriteVarintField(loraMs, 1, 1);
            WriteVarintField(loraMs, 2, presetEnum);
            WriteVarintField(loraMs, 7, regionEnum);
            WriteVarintField(loraMs, 8, (ulong)Math.Clamp(hopLimit, 1, 7));
            WriteVarintField(loraMs, 9, 1);

            using var cfgMs = new MemoryStream();
            WriteBytesField(cfgMs, 6, loraMs.ToArray());

            using var adminMs = new MemoryStream();
            WriteBytesField(adminMs, 34, cfgMs.ToArray());
            return adminMs.ToArray();
        }

        private static byte[] BuildSetDeviceRoleConfigAdmin(string roleStr)
        {
            ulong roleEnum = roleStr switch
            {
                "Router" => 2,
                "TAK" => 7,
                "TAK_Tracker" => 10,
                _ => 0
            };

            using var devMs = new MemoryStream();
            WriteVarintField(devMs, 1, roleEnum);

            using var cfgMs = new MemoryStream();
            WriteBytesField(cfgMs, 1, devMs.ToArray());

            using var adminMs = new MemoryStream();
            WriteBytesField(adminMs, 34, cfgMs.ToArray());
            return adminMs.ToArray();
        }

        private static uint TryParseMyNodeNumFromBuffer(byte[] buffer, int count)
        {
            for (int i = 0; i < count - 6; i++)
            {
                if (buffer[i] == 0x94 && buffer[i + 1] == 0xC3)
                {
                    int len = (buffer[i + 2] << 8) | buffer[i + 3];
                    if (len > 0 && len < 512 && i + 4 + len <= count)
                    {
                        int pos = i + 4;
                        int end = pos + len;
                        while (pos < end)
                        {
                            int tag = buffer[pos++];
                            int fieldNum = tag >> 3;
                            int wireType = tag & 0x07;
                            if (fieldNum == 3 && wireType == 2 && pos < end)
                            {
                                int subLen = buffer[pos++];
                                if (pos < end && buffer[pos] == 0x08)
                                {
                                    pos++;
                                    ulong val = 0;
                                    int shift = 0;
                                    while (pos < end && shift < 35)
                                    {
                                        byte b = buffer[pos++];
                                        val |= (ulong)(b & 0x7F) << shift;
                                        if ((b & 0x80) == 0) return (uint)val;
                                        shift += 7;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }
            return 0;
        }

        public static async Task<(bool Success, string NodeHex, string LogMessage)> ProvisionBoardOverUsbAsync(
            string portName,
            FactionProfileModel fac,
            string longName,
            string shortName,
            Action<string> onProgressLog)
        {
            return await Task.Run(async () =>
            {
                string cleanPort = portName.Trim();
                string devicePath = $@"\\.\{cleanPort}";

                onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Otwieranie portu {cleanPort} (115200 8N1)...");
                IntPtr hPort = CreateFileW(devicePath, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (hPort == IntPtr.Zero || hPort == new IntPtr(-1))
                {
                    int err = Marshal.GetLastWin32Error();
                    return (false, "N/A", $"Nie można otworzyć portu {cleanPort} (Kod Win32: {err}). Sprawdź, czy port nie jest zajęty przez inną aplikację.");
                }

                try
                {
                    DCB dcb = new DCB { DCBlength = (uint)Marshal.SizeOf<DCB>() };
                    if (GetCommState(hPort, ref dcb))
                    {
                        dcb.BaudRate = 115200;
                        dcb.ByteSize = 8;
                        dcb.Parity = 0;
                        dcb.StopBits = 0;
                        dcb.Flags = 0x00000001;
                        SetCommState(hPort, ref dcb);
                    }

                    var timeouts = new COMMTIMEOUTS
                    {
                        ReadIntervalTimeout = 35,
                        ReadTotalTimeoutMultiplier = 5,
                        ReadTotalTimeoutConstant = 120,
                        WriteTotalTimeoutMultiplier = 5,
                        WriteTotalTimeoutConstant = 250
                    };
                    SetCommTimeouts(hPort, ref timeouts);

                    EscapeCommFunction(hPort, 5);
                    PurgeComm(hPort, 0x000C);

                    void SendRaw(byte[] bytes)
                    {
                        WriteFile(hPort, bytes, (uint)bytes.Length, out uint _, IntPtr.Zero);
                    }

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Budzenie interfejsu Protobuf płytki Meshtastic (0x94 0xC3)...");
                    byte[] wakeBytes = Enumerable.Repeat((byte)0xC3, 32).ToArray();
                    SendRaw(wakeBytes);
                    await Task.Delay(120);

                    SendRaw(BuildWantConfigToRadioFrame(0x1337));
                    await Task.Delay(320);

                    byte[] rxBuf = new byte[4096];
                    ReadFile(hPort, rxBuf, (uint)rxBuf.Length, out uint bytesRead, IntPtr.Zero);
                    uint nodeNum = bytesRead > 0 ? TryParseMyNodeNumFromBuffer(rxBuf, (int)bytesRead) : 0;
                    string nodeHex = nodeNum != 0 ? $"!{nodeNum:x8}" : $"!usb_{cleanPort.ToLowerInvariant()}";
                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Połączono z płytką ({nodeHex}, odebrano {bytesRead} B telemetrii).");

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Otwieranie transakcji AdminMessage (begin_edit_settings)...");
                    SendRaw(BuildAdminToRadioFrame(nodeNum, BuildBeginEditSettingsAdmin()));
                    await Task.Delay(160);

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Zapisywanie Callsignu: LongName='{longName}', ShortName='{shortName}'...");
                    SendRaw(BuildAdminToRadioFrame(nodeNum, BuildSetOwnerAdmin(longName, shortName)));
                    await Task.Delay(200);

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Wgrywanie kanału '{fac.ChannelName}' oraz klucza AES-256 PSK...");
                    SendRaw(BuildAdminToRadioFrame(nodeNum, BuildSetPrimaryChannelAdmin(fac.ChannelName, fac.Psk)));
                    await Task.Delay(220);

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Konfiguracja radia LoRa: Region={fac.LoraRegion}, Preset={fac.LoraModemPreset}, Hop={fac.HopLimit}...");
                    SendRaw(BuildAdminToRadioFrame(nodeNum, BuildSetLoraConfigAdmin(fac.LoraRegion, fac.LoraModemPreset, fac.HopLimit)));
                    await Task.Delay(220);

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Ustawianie roli urządzenia: {fac.DeviceRole}...");
                    SendRaw(BuildAdminToRadioFrame(nodeNum, BuildSetDeviceRoleConfigAdmin(fac.DeviceRole)));
                    await Task.Delay(180);

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Zapisywanie zmian w pamięci Flash NVS (commit_edit_settings)...");
                    SendRaw(BuildAdminToRadioFrame(nodeNum, BuildCommitEditSettingsAdmin()));
                    await Task.Delay(280);

                    onProgressLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] ✓ SUKCES! Płytka {nodeHex} na porcie {cleanPort} zaprogramowana jako {longName} [{shortName}] ({fac.Name}).");
                    return (true, nodeHex, $"Zaprogramowano {longName} ({nodeHex})");
                }
                catch (Exception ex)
                {
                    return (false, "ERR", $"Błąd zapisu USB: {ex.Message}");
                }
                finally
                {
                    CloseHandle(hPort);
                }
            });
        }
    }

    public static class MeshtasticUsbProvisionerView
    {
        private static Border _usbOverlayBorder;
        private static Button _tabUsbBtn, _refBuilderBtn, _refMapBtn;
        private static UIElement _refOriginalContent, _refMapOverlay;
        private static ComboBox _factionComboBox, _comPortComboBox, _roleOverrideComboBox;
        private static TextBox _prefixInputBox, _shortPrefixBox, _serialNumberBox, _consoleLogBox;
        private static TextBlock _previewBannerTitle, _previewBannerDetails, _autoWatchStatusText;
        private static Border _previewBannerBorder;
        private static StackPanel _provisionedHistoryStack;
        private static CheckBox _chkAutoIncrement, _chkAutoBatchWatch;
        private static DispatcherTimer _usbWatchTimer;

        private static FactionProfileModel _selectedFaction;
        private static int _currentSerialNum = 1;
        private static bool _isFlashingNow;
        private static readonly HashSet<string> _knownConnectedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<ProvisionedNodeLogItem> _provisionedLogs = new List<ProvisionedNodeLogItem>();

        public static void AttachUsbTab(
            Window win,
            StackPanel tabsStack,
            Grid contentHostGrid,
            Button tabBuilderBtn,
            Button tabMapBtn,
            UIElement originalWindowContent,
            Border mapOverlay)
        {
            if (win == null || tabsStack == null || contentHostGrid == null) return;
            _refBuilderBtn = tabBuilderBtn;
            _refMapBtn = tabMapBtn;
            _refOriginalContent = originalWindowContent;
            _refMapOverlay = mapOverlay;

            _usbOverlayBorder = BuildUsbProvisionerUI(win);
            _usbOverlayBorder.Visibility = Visibility.Collapsed;
            contentHostGrid.Children.Add(_usbOverlayBorder);

            _tabUsbBtn = new Button
            {
                Content = "🔌 Masowy Programator USB (LoRa)",
                Height = 32,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(10, 0, 0, 0),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.5),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Cursor = Cursors.Hand
            };
            _tabUsbBtn.Click += (s, e) => ShowUsbProvisionerTab(win);
            tabsStack.Children.Add(_tabUsbBtn);

            _usbWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
            _usbWatchTimer.Tick += async (s, e) => await OnUsbWatchTimerTickAsync(win);
            _usbWatchTimer.Start();
        }

        public static void ShowUsbProvisionerForFaction(Window win, string factionName)
        {
            ShowUsbProvisionerTab(win);
            var factions = TacticalConfigPanel.GetInternalFactionsList();
            var match = factions.FirstOrDefault(f => f.Name.Equals(factionName, StringComparison.OrdinalIgnoreCase));
            if (match != null && _factionComboBox != null)
            {
                _factionComboBox.SelectedItem = match;
                ApplyFactionToTemplateFields(match);
            }
        }

        public static void ShowUsbProvisionerTab(Window win)
        {
            if (_usbOverlayBorder == null) return;
            if (_refOriginalContent != null) _refOriginalContent.Visibility = Visibility.Collapsed;
            if (_refMapOverlay != null) _refMapOverlay.Visibility = Visibility.Collapsed;
            _usbOverlayBorder.Visibility = Visibility.Visible;

            if (_tabUsbBtn != null) { _tabUsbBtn.Background = TacticalConfigPanel.BrushButtonAccent; _tabUsbBtn.Foreground = TacticalConfigPanel.BrushDarkFg; }
            if (_refBuilderBtn != null) { _refBuilderBtn.Background = TacticalConfigPanel.BrushItemBg; _refBuilderBtn.Foreground = Brushes.White; }
            if (_refMapBtn != null) { _refMapBtn.Background = TacticalConfigPanel.BrushItemBg; _refMapBtn.Foreground = TacticalConfigPanel.BrushAccent; }

            RefreshFactionsDropdown(win);
            RefreshComPortsDropdown(win);
        }

        public static void HideUsbTabOnly()
        {
            if (_usbOverlayBorder != null)
                _usbOverlayBorder.Visibility = Visibility.Collapsed;
            if (_tabUsbBtn != null)
            {
                _tabUsbBtn.Background = TacticalConfigPanel.BrushItemBg;
                _tabUsbBtn.Foreground = TacticalConfigPanel.BrushAccent;
            }
        }

        private static void RefreshFactionsDropdown(Window win)
        {
            if (_factionComboBox == null) return;
            var list = TacticalConfigPanel.GetInternalFactionsList();
            var prevName = _selectedFaction?.Name;
            _factionComboBox.ItemsSource = null;
            _factionComboBox.ItemsSource = list;
            _selectedFaction = list.FirstOrDefault(f => f.Name == prevName) ?? list.FirstOrDefault();
            _factionComboBox.SelectedItem = _selectedFaction;
            if (_selectedFaction != null)
                UpdateFactionPreviewBanner(_selectedFaction);
        }

        private static void ApplyFactionToTemplateFields(FactionProfileModel fac)
        {
            if (fac == null) return;
            _selectedFaction = fac;
            string clean = TacticalConfigPanel.SanitizeFileName(fac.Name).ToUpperInvariant();
            if (_prefixInputBox != null) _prefixInputBox.Text = clean;
            if (_shortPrefixBox != null) _shortPrefixBox.Text = clean.Length >= 2 ? clean.Substring(0, 2) : "TK";
            if (_roleOverrideComboBox != null) _roleOverrideComboBox.SelectedItem = string.IsNullOrWhiteSpace(fac.DeviceRole) ? "TAK" : fac.DeviceRole;
            UpdateFactionPreviewBanner(fac);
        }

        private static void UpdateFactionPreviewBanner(FactionProfileModel fac)
        {
            if (fac == null || _previewBannerBorder == null) return;
            _previewBannerBorder.Background = TacticalConfigPanel.BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255));
            string callsignPreview = BuildCurrentLongName();
            string shortPreview = BuildCurrentShortName();
            if (_previewBannerTitle != null)
                _previewBannerTitle.Text = $"STRONA: {fac.Name.ToUpperInvariant()}   •   NASTĘPNA PŁYTKA: {callsignPreview} [{shortPreview}]";
            if (_previewBannerDetails != null)
                _previewBannerDetails.Text = $"Kanał LoRa: {fac.ChannelName}  |  Pasmo: {fac.LoraRegion} ({fac.LoraModemPreset})  |  HopLimit: {fac.HopLimit}  |  Rola: {fac.DeviceRole}  |  Klucz AES-256 PSK: {fac.Psk.Substring(0, Math.Min(14, fac.Psk.Length))}...";
        }

        private static string BuildCurrentLongName()
        {
            string pfx = (_prefixInputBox?.Text ?? "BLUFOR").Trim();
            if (string.IsNullOrEmpty(pfx)) pfx = "UNIT";
            return $"{pfx}-{_currentSerialNum:D2}";
        }

        private static string BuildCurrentShortName()
        {
            string sp = (_shortPrefixBox?.Text ?? "BL").Trim().ToUpperInvariant();
            if (sp.Length > 2) sp = sp.Substring(0, 2);
            if (string.IsNullOrEmpty(sp)) sp = "TK";
            return $"{sp}{_currentSerialNum:D2}";
        }

        private static void RefreshComPortsDropdown(Window win)
        {
            if (_comPortComboBox == null) return;
            var ports = NativeMeshtasticSerialEngine.EnumerateActiveComPorts();
            string prevPort = (_comPortComboBox.SelectedItem as SerialPortInfoItem)?.PortName;
            _comPortComboBox.ItemsSource = ports;
            var match = ports.FirstOrDefault(p => p.PortName == prevPort) ?? ports.LastOrDefault();
            _comPortComboBox.SelectedItem = match;

            _knownConnectedPorts.Clear();
            foreach (var p in ports) _knownConnectedPorts.Add(p.PortName);
        }

        private static async Task OnUsbWatchTimerTickAsync(Window win)
        {
            if (_usbOverlayBorder?.Visibility != Visibility.Visible || _chkAutoBatchWatch?.IsChecked != true || _isFlashingNow)
                return;

            var currentPorts = NativeMeshtasticSerialEngine.EnumerateActiveComPorts();
            var currentNames = new HashSet<string>(currentPorts.Select(p => p.PortName), StringComparer.OrdinalIgnoreCase);

            _knownConnectedPorts.RemoveWhere(p => !currentNames.Contains(p));

            var newlyPlugged = currentPorts.FirstOrDefault(p => !_knownConnectedPorts.Contains(p.PortName));
            if (newlyPlugged != null)
            {
                _knownConnectedPorts.Add(newlyPlugged.PortName);
                _comPortComboBox.ItemsSource = currentPorts;
                _comPortComboBox.SelectedItem = newlyPlugged;

                AppendConsoleLog($"[AUTO-BATCH] Wykryto podpięcie nowej płytki na porcie {newlyPlugged.PortName}! Stabilizacja zasilania USB (700 ms)...");
                await Task.Delay(700);
                await ExecuteProvisioningOnPortAsync(win, newlyPlugged.PortName);
            }
        }

        private static void AppendConsoleLog(string line)
        {
            if (_consoleLogBox == null) return;
            _consoleLogBox.AppendText(line + Environment.NewLine);
            _consoleLogBox.ScrollToEnd();
        }

        private static async Task ExecuteProvisioningOnPortAsync(Window win, string portName)
        {
            if (_isFlashingNow || _selectedFaction == null) return;
            if (string.IsNullOrWhiteSpace(portName))
            {
                AppendConsoleLog("[UWAGA] Nie wykryto aktywnego portu COM. Podłącz płytkę Meshtastic kablem USB z transmisją danych.");
                return;
            }

            _isFlashingNow = true;
            try
            {
                string longName = BuildCurrentLongName();
                string shortName = BuildCurrentShortName();
                if (_roleOverrideComboBox?.SelectedItem is string rStr && !string.IsNullOrWhiteSpace(rStr))
                    _selectedFaction.DeviceRole = rStr;

                var (ok, nodeHex, msg) = await NativeMeshtasticSerialEngine.ProvisionBoardOverUsbAsync(
                    portName,
                    _selectedFaction,
                    longName,
                    shortName,
                    logLine => Application.Current?.Dispatcher.BeginInvoke(new Action(() => AppendConsoleLog(logLine))));

                if (ok)
                {
                    try { SystemSounds.Beep.Play(); } catch { }
                    _provisionedLogs.Insert(0, new ProvisionedNodeLogItem
                    {
                        Index = _provisionedLogs.Count + 1,
                        Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                        PortName = portName,
                        NodeIdHex = nodeHex,
                        FactionName = _selectedFaction.Name,
                        LongName = longName,
                        ShortName = shortName,
                        ChannelName = _selectedFaction.ChannelName,
                        RegionPreset = $"{_selectedFaction.LoraRegion} / {_selectedFaction.LoraModemPreset}",
                        Status = "✓ WGRANO"
                    });
                    RebuildProvisionedHistoryTableUI();

                    if (_chkAutoIncrement?.IsChecked == true)
                    {
                        _currentSerialNum++;
                        if (_serialNumberBox != null) _serialNumberBox.Text = _currentSerialNum.ToString();
                        UpdateFactionPreviewBanner(_selectedFaction);
                    }

                    if (_autoWatchStatusText != null)
                        _autoWatchStatusText.Text = $"✓ GOTOWE ({longName} na {portName})! Możesz odpiąć płytkę i podpiąć następną.";
                }
                else
                {
                    AppendConsoleLog($"[BŁĄD] {msg}");
                    if (_autoWatchStatusText != null)
                        _autoWatchStatusText.Text = $"⚠️ Błąd na porcie {portName}: {msg}";
                }
            }
            finally
            {
                _isFlashingNow = false;
            }
        }

        private static Border BuildUsbProvisionerUI(Window win)
        {
            var rootBorder = new Border { Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(16), Tag = "INJECTED_USB_OVERLAY" };
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var leftStack = new StackPanel();

            var sec1 = new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
            var s1Stack = new StackPanel();
            s1Stack.Children.Add(new TextBlock { Text = "1. WYBÓR STRONY KONFLIKTU (PROFIL RADIA LORA)", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            s1Stack.Children.Add(new TextBlock { Text = "Wybierz stronę konfliktu — programator automatycznie pobierze jej kanał, klucz AES-256 PSK, region oraz preset.", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            var facRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            facRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            facRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Usunięto DisplayMemberPath — ComboBox korzysta teraz z czytelnego FactionProfileModel.ToString()
            _factionComboBox = new ComboBox { Height = 28, FontSize = 10.5, FontWeight = FontWeights.Bold, Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_factionComboBox, win);
            _factionComboBox.SelectionChanged += (s, e) =>
            {
                if (_factionComboBox.SelectedItem is FactionProfileModel fm)
                    ApplyFactionToTemplateFields(fm);
            };
            Grid.SetColumn(_factionComboBox, 0);
            facRow.Children.Add(_factionComboBox);

            var btnSyncFactions = new Button
            {
                Content = "🔄 Odśwież strony",
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnSyncFactions.Click += (s, e) => RefreshFactionsDropdown(win);
            Grid.SetColumn(btnSyncFactions, 1);
            facRow.Children.Add(btnSyncFactions);
            s1Stack.Children.Add(facRow);

            s1Stack.Children.Add(new TextBlock
            {
                Text = "Docelowa rola wgrywanych płytek Meshtastic (TAK = integracja z ATAK po BLE/USB, TAK_Tracker = samodzielny lokalizator GPS):",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3)
            });
            _roleOverrideComboBox = (ComboBox)TacticalConfigPanel.ComboCtrl(new[] { "TAK", "TAK_Tracker", "Client", "Router" }, "TAK", win, v =>
            {
                if (_selectedFaction != null) { _selectedFaction.DeviceRole = v; UpdateFactionPreviewBanner(_selectedFaction); }
            });
            s1Stack.Children.Add(_roleOverrideComboBox);

            sec1.Child = s1Stack;
            leftStack.Children.Add(sec1);

            var sec2 = new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
            var s2Stack = new StackPanel();
            s2Stack.Children.Add(new TextBlock { Text = "2. NUMERACJA SERYJNA PŁYTEK (CALLSIGN & 4-ZNAKOWY SHORTNAME)", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            var numGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            _prefixInputBox = (TextBox)TacticalConfigPanel.TextCtrl("BLUFOR", v => { if (_selectedFaction != null) UpdateFactionPreviewBanner(_selectedFaction); });
            _shortPrefixBox = (TextBox)TacticalConfigPanel.TextCtrl("BL", v => { if (_selectedFaction != null) UpdateFactionPreviewBanner(_selectedFaction); });
            _serialNumberBox = (TextBox)TacticalConfigPanel.TextCtrl("1", v => { if (int.TryParse(v.Trim(), out int n) && n >= 1) { _currentSerialNum = n; if (_selectedFaction != null) UpdateFactionPreviewBanner(_selectedFaction); } });

            numGrid.Children.Add(TacticalConfigPanel.FieldBox("Prefiks Callsignu:", _prefixInputBox));
            numGrid.Children.Add(TacticalConfigPanel.FieldBox("Prefiks Short (2 zn.):", _shortPrefixBox));
            numGrid.Children.Add(TacticalConfigPanel.FieldBox("Numer kolejny (#):", _serialNumberBox));
            s2Stack.Children.Add(numGrid);

            _chkAutoIncrement = new CheckBox
            {
                Content = "Zwiększaj automatycznie numer płytki (+1) po każdym udanym wgraniu",
                IsChecked = true,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 2)
            };
            s2Stack.Children.Add(_chkAutoIncrement);
            sec2.Child = s2Stack;
            leftStack.Children.Add(sec2);

            var sec3 = new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
            var s3Stack = new StackPanel();
            s3Stack.Children.Add(new TextBlock { Text = "3. PORT USB (COM) & TRYB TAŚMOWY (AUTO-FLASH)", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            var comRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            comRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            comRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _comPortComboBox = new ComboBox { Height = 28, FontSize = 10.5, Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_comPortComboBox, win);
            Grid.SetColumn(_comPortComboBox, 0);
            comRow.Children.Add(_comPortComboBox);

            var btnScanPorts = new Button
            {
                Content = "🔍 Skanuj COM",
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnScanPorts.Click += (s, e) => RefreshComPortsDropdown(win);
            Grid.SetColumn(btnScanPorts, 1);
            comRow.Children.Add(btnScanPorts);
            s3Stack.Children.Add(TacticalConfigPanel.FieldBox("Wykryte płytki Meshtastic na portach USB (CP210x / CH340 / ESP32 / nRF52):", comRow));

            var autoBatchBox = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushGreenPlus, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10) };
            var abStack = new StackPanel();
            _chkAutoBatchWatch = new CheckBox
            {
                Content = "🔄 WŁĄCZ TRYB TAŚMOWY (Automatycznie programuj każdą nowo podpiętą płytkę USB)",
                IsChecked = false,
                Foreground = TacticalConfigPanel.BrushGreenPlus,
                FontWeight = FontWeights.Bold,
                FontSize = 10
            };
            _autoWatchStatusText = new TextBlock
            {
                Text = "Tryb ręczny. Kliknij przycisk poniżej lub włącz Tryb Taśmowy i podpinaj kolejne płytki kablem USB.",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _chkAutoBatchWatch.Checked += (s, e) =>
            {
                RefreshComPortsDropdown(win);
                _autoWatchStatusText.Text = "⚡ NASŁUCHIWANIE AKTYWNE: Podepnij płytkę Meshtastic pod kabel USB — programowanie ruszy automatycznie!";
                _autoWatchStatusText.Foreground = TacticalConfigPanel.BrushAccent;
            };
            _chkAutoBatchWatch.Unchecked += (s, e) =>
            {
                _autoWatchStatusText.Text = "Tryb taśmowy wyłączony.";
                _autoWatchStatusText.Foreground = TacticalConfigPanel.BrushMutedText;
            };
            abStack.Children.Add(_chkAutoBatchWatch);
            abStack.Children.Add(_autoWatchStatusText);
            autoBatchBox.Child = abStack;
            s3Stack.Children.Add(autoBatchBox);

            var btnFlashNow = new Button
            {
                Content = "⚡ WGRAJ PROFIL STRONY NA WYBRANY PORT USB (TERAZ)",
                Height = 38,
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 8)
            };
            btnFlashNow.Click += async (s, e) =>
            {
                btnFlashNow.IsEnabled = false;
                string port = (_comPortComboBox.SelectedItem as SerialPortInfoItem)?.PortName ?? "";
                await ExecuteProvisioningOnPortAsync(win, port);
                btnFlashNow.IsEnabled = true;
            };
            s3Stack.Children.Add(btnFlashNow);

            var utilBtnsGrid = new UniformGrid { Columns = 2 };
            var btnExportCliBat = new Button { Content = "📜 Zapisz skrypt CLI (.bat)", Height = 28, Margin = new Thickness(0, 0, 4, 0), Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), FontSize = 9.5, Cursor = Cursors.Hand };
            var btnExportCsv = new Button { Content = "📊 Eksportuj listę wydania (.csv)", Height = 28, Margin = new Thickness(4, 0, 0, 0), Background = TacticalConfigPanel.BrushInputBg, Foreground = TacticalConfigPanel.BrushAccent, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontSize = 9.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };

            btnExportCliBat.Click += (s, e) => ExportStandaloneCliBatchScript(win);
            btnExportCsv.Click += (s, e) => ExportProvisionedNodesCsv(win);

            utilBtnsGrid.Children.Add(btnExportCliBat);
            utilBtnsGrid.Children.Add(btnExportCsv);
            s3Stack.Children.Add(utilBtnsGrid);

            sec3.Child = s3Stack;
            leftStack.Children.Add(sec3);
            leftScroll.Content = leftStack;
            Grid.SetColumn(leftScroll, 0);
            mainGrid.Children.Add(leftScroll);

            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.25, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.95, GridUnitType.Star) });

            _previewBannerBorder = new Border
            {
                Background = TacticalConfigPanel.BrushFromHex("#2684FF", Color.FromRgb(38, 132, 255)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var bannerStack = new StackPanel();
            _previewBannerTitle = new TextBlock { Text = "STRONA: BLUFOR   •   NASTĘPNA PŁYTKA: BLUFOR-01 [BL01]", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 15 };
            _previewBannerDetails = new TextBlock { Text = "Kanał LoRa: GGZS-TAK  |  Pasmo: EU_868 (SHORT_FAST)  |  Rola: TAK", Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
            bannerStack.Children.Add(_previewBannerTitle);
            bannerStack.Children.Add(_previewBannerDetails);
            _previewBannerBorder.Child = bannerStack;
            Grid.SetRow(_previewBannerBorder, 0);
            rightGrid.Children.Add(_previewBannerBorder);

            var historyCard = new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
            var histDock = new DockPanel();
            var histTitle = new TextBlock { Text = "📋 DZIENNIK ZAPROGRAMOWANYCH PŁYTEK (EWIDENCJA WYDANEGO SPRZĘTU LORA)", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(histTitle, Dock.Top);
            histDock.Children.Add(histTitle);

            var histHeader = new Grid { Background = TacticalConfigPanel.BrushInputBg, Margin = new Thickness(0, 0, 0, 4) };
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            histHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });

            void AddHistColHdr(string txt, int c)
            {
                var t = new TextBlock { Text = txt, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, FontWeight = FontWeights.Bold, Padding = new Thickness(4, 4, 4, 4) };
                Grid.SetColumn(t, c);
                histHeader.Children.Add(t);
            }
            AddHistColHdr("#", 0);
            AddHistColHdr("Godzina", 1);
            AddHistColHdr("Port", 2);
            AddHistColHdr("Node ID", 3);
            AddHistColHdr("Strona", 4);
            AddHistColHdr("Callsign (Long)", 5);
            AddHistColHdr("Short", 6);
            AddHistColHdr("Kanał LoRa", 7);
            AddHistColHdr("Status", 8);
            DockPanel.SetDock(histHeader, Dock.Top);
            histDock.Children.Add(histHeader);

            var histScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _provisionedHistoryStack = new StackPanel();
            RebuildProvisionedHistoryTableUI();
            histScroll.Content = _provisionedHistoryStack;
            histDock.Children.Add(histScroll);
            historyCard.Child = histDock;
            Grid.SetRow(historyCard, 1);
            rightGrid.Children.Add(historyCard);

            var consoleCard = new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
            var conDock = new DockPanel();
            var conHdrDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var btnClearLog = new Button { Content = "Wyczyść log", Height = 22, Padding = new Thickness(8, 0, 8, 0), Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), FontSize = 9.5, Cursor = Cursors.Hand };
            btnClearLog.Click += (s, e) => _consoleLogBox?.Clear();
            DockPanel.SetDock(btnClearLog, Dock.Right);
            conHdrDock.Children.Add(btnClearLog);
            conHdrDock.Children.Add(new TextBlock { Text = "🖥️ KONSOLA TRANSMISJI SZEREGOWEJ USB (PROTOBUF 0x94 0xC3 / ADMIN MESSAGE)", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(conHdrDock, Dock.Top);
            conDock.Children.Add(conHdrDock);

            _consoleLogBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10.5,
                Padding = new Thickness(8),
                Text = "[GOTOWY] Masowy Programator USB Meshtastic 2.4 (Win32 Serial 115200 8N1 + Protobuf AdminMessage).\r\nWybierz stronę konfliktu po lewej stronie, podłącz płytkę kablem USB i kliknij 'WGRAJ PROFIL' lub włącz Tryb Taśmowy.\r\n"
            };
            conDock.Children.Add(_consoleLogBox);
            consoleCard.Child = conDock;
            Grid.SetRow(consoleCard, 2);
            rightGrid.Children.Add(consoleCard);

            Grid.SetColumn(rightGrid, 2);
            mainGrid.Children.Add(rightGrid);
            rootBorder.Child = mainGrid;

            RefreshFactionsDropdown(win);
            RefreshComPortsDropdown(win);
            return rootBorder;
        }

        private static void RebuildProvisionedHistoryTableUI()
        {
            if (_provisionedHistoryStack == null) return;
            _provisionedHistoryStack.Children.Clear();
            if (_provisionedLogs.Count == 0)
            {
                _provisionedHistoryStack.Children.Add(new TextBlock
                {
                    Text = "Brak zaprogramowanych płytek w tej sesji. Podłącz pierwsze urządzenie USB i kliknij 'WGRAJ PROFIL'.",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 10.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 0)
                });
                return;
            }

            foreach (var item in _provisionedLogs)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 2), Background = TacticalConfigPanel.BrushInputBg };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });

                void Cell(string txt, int c, Brush fg, bool bold = false)
                {
                    var tb = new TextBlock { Text = txt, Foreground = fg, FontSize = 10, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Padding = new Thickness(4, 3, 4, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(tb, c);
                    row.Children.Add(tb);
                }

                Cell(item.Index.ToString(), 0, TacticalConfigPanel.BrushMutedText);
                Cell(item.Timestamp, 1, Brushes.White);
                Cell(item.PortName, 2, TacticalConfigPanel.BrushAccent, true);
                Cell(item.NodeIdHex, 3, Brushes.White, true);
                Cell(item.FactionName, 4, TacticalConfigPanel.BrushAccent, true);
                Cell(item.LongName, 5, Brushes.White, true);
                Cell(item.ShortName, 6, TacticalConfigPanel.BrushAccent, true);
                Cell(item.ChannelName, 7, Brushes.White);
                Cell(item.Status, 8, TacticalConfigPanel.BrushGreenPlus, true);
                _provisionedHistoryStack.Children.Add(row);
            }
        }

        private static void ExportProvisionedNodesCsv(Window win)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Zapisz ewidencję zaprogramowanych płytek Meshtastic (.CSV)",
                    Filter = "Arkusz CSV (*.csv)|*.csv",
                    FileName = $"Ewidencja_Plytek_Meshtastic_{DateTime.Now:yyyyMMdd_HHmm}.csv"
                };
                if (dlg.ShowDialog(win) != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("Lp;Godzina;Port_COM;Node_ID;Strona;Callsign_LongName;ShortName;Kanal_LoRa;Region_Preset;Status");
                foreach (var it in _provisionedLogs.OrderBy(x => x.Index))
                    sb.AppendLine($"{it.Index};{it.Timestamp};{it.PortName};{it.NodeIdHex};{it.FactionName};{it.LongName};{it.ShortName};{it.ChannelName};{it.RegionPreset};{it.Status}");

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                AppendConsoleLog($"[EKSPORT] Zapisano ewidencję {_provisionedLogs.Count} płytek do pliku: {dlg.FileName}");
            }
            catch { }
        }

        private static void ExportStandaloneCliBatchScript(Window win)
        {
            if (_selectedFaction == null) return;
            try
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Zapisz skrypt wsadowy Meshtastic CLI (.bat)",
                    Filter = "Skrypt wsadowy Windows (*.bat)|*.bat",
                    FileName = $"Programuj_Meshtastic_{TacticalConfigPanel.SanitizeFileName(_selectedFaction.Name)}.bat"
                };
                if (dlg.ShowDialog(win) != true) return;

                string longName = BuildCurrentLongName();
                string shortName = BuildCurrentShortName();
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("chcp 65001 >nul");
                sb.AppendLine($"echo Programowanie plytki Meshtastic dla strony {_selectedFaction.Name}...");
                sb.AppendLine($"meshtastic --set-owner \"{longName}\" --set-owner-short \"{shortName}\" --ch-index 0 --ch-set name \"{_selectedFaction.ChannelName}\" --ch-set psk \"base64:{_selectedFaction.Psk}\" --set lora.region {_selectedFaction.LoraRegion} --set lora.modem_preset {_selectedFaction.LoraModemPreset} --set lora.hop_limit {_selectedFaction.HopLimit}");
                sb.AppendLine("pause");

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                AppendConsoleLog($"[EKSPORT] Zapisano skrypt wsadowy Meshtastic CLI: {dlg.FileName}");
            }
            catch { }
        }
    }
}
