using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace FieldTakHub.Builder.Services
{
    public class TacticalConfigModel
    {
        public string LocationReportingStrategy { get; set; } = "Constant";
        public int ConstantReportingRateReliable { get; set; } = 0;
        public int ConstantReportingRateUnreliable { get; set; } = 1;
        public int DynamicReportingRateMinReliable { get; set; } = 20;
        public int DynamicReportingRateMaxReliable { get; set; } = 2;
        public int DynamicReportingRateStationaryReliable { get; set; } = 180;
        public int DynamicReportingRateStationaryUnreliable { get; set; } = 30;
        public int TcpConnectTimeout { get; set; } = 20;
        public int UdpNoDataTimeout { get; set; } = 30;
        public bool DispatchLocationCotExternal { get; set; } = true;
        public bool DispatchLocationCotExternalAtStart { get; set; } = true;
        public bool EnableNonStreamingConnections { get; set; } = true;
        public bool AutoDisableMeshSAWhenStreaming { get; set; } = false;
        public string FirmwareMin { get; set; } = "2.7.26";
        public string FirmwareMax { get; set; } = "2.8.0.47";
    }
    public static class TacticalConfigPanel
    {
        public static TacticalConfigModel Current { get; set; } = new TacticalConfigModel();
        private static bool _fwFieldsAdded = false;
        private static bool _atakCardAdded = false;
        public static void AttachToMainWindow(Window win)
        {
            try
            {
                var q = new Queue<DependencyObject>();
                q.Enqueue(win);
                while (q.Count > 0)
                {
                    var cur = q.Dequeue();
                    if (cur is ComboBox cb)
                    {
                        cb.Foreground = Brushes.White;
                        var style = new Style(typeof(ComboBoxItem));
                        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(20, 35, 40))));
                        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                        cb.ItemContainerStyle = style;
                    }
                    else if (cur is TextBox tbk) tbk.Foreground = Brushes.White;
                    if (cur is TextBlock tb && tb.Text != null && tb.Text.Contains("Konfiguracja Meshtastic"))
                    {
                        Border cardBorder = null;
                        DependencyObject parent = VisualTreeHelper.GetParent(tb);
                        while (parent != null)
                        {
                            if (parent is Border b && b.Background != null) { cardBorder = b; break; }
                            parent = VisualTreeHelper.GetParent(parent) ?? LogicalTreeHelper.GetParent(parent);
                        }
                        if (cardBorder != null)
                        {
                            if (!_fwFieldsAdded && cardBorder.Child is Panel innerPanel)
                            {
                                InjectFirmwareFieldsIntoMeshtasticCard(innerPanel, win);
                                _fwFieldsAdded = true;
                            }
                            if (!_atakCardAdded)
                            {
                                var parentPanel = VisualTreeHelper.GetParent(cardBorder) as Panel;
                                if (parentPanel != null)
                                {
                                    var atakCard = BuildAtakCard(win);
                                    int idx = parentPanel.Children.IndexOf(cardBorder);
                                    parentPanel.Children.Insert(idx, atakCard);
                                    _atakCardAdded = true;
                                }
                            }
                        }
                    }
                    int cnt = VisualTreeHelper.GetChildrenCount(cur);
                    for (int i = 0; i < cnt; i++) q.Enqueue(VisualTreeHelper.GetChild(cur, i));
                }
            }
            catch { }
        }
        private static void ForceSizeUpdate(Window win)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] dirs = { "config", "meshtastic", "payload/config", "payload/meshtastic", "DataPackages" };
                foreach (var d in dirs)
                {
                    string p = System.IO.Path.Combine(baseDir, d);
                    if (System.IO.Directory.Exists(p)) WriteGeneratedConfigsIfInteractive(p);
                }
            }
            catch { }
            try
            {
                var methods = win.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                foreach (var m in methods)
                {
                    string n = m.Name.ToLower();
                    if ((n.Contains("size") || n.Contains("update") || n.Contains("refresh") || n.Contains("calc")) && m.GetParameters().Length == 0)
                    {
                        try { m.Invoke(win, null); } catch { }
                    }
                }
            }
            catch { }
        }
        private static UIElement BuildAtakCard(Window win)
        {
            var m = Current;
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(9, 20, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 58, 66)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var lStack = new StackPanel();
            lStack.Children.Add(new TextBlock
            {
                Text = "Konfiguracja ATAK-CIV (PLI i siec)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4)
            });
            lStack.Children.Add(new TextBlock
            {
                Text = "Czysty profil ATAK bez wymuszania wtyczek. Zdefiniuj czestotliwosci odswiezania PLI.",
                Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10)
            });
            var gAtak = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
            gAtak.Children.Add(FieldBox("Strategia PLI", ComboCtrl(new[] { "Constant", "Dynamic" }, m.LocationReportingStrategy, v => { m.LocationReportingStrategy = v; ForceSizeUpdate(win); })));
            gAtak.Children.Add(FieldBox("Constant Reliable/LTE (s)", IntCtrl(m.ConstantReportingRateReliable, v => { m.ConstantReportingRateReliable = v; ForceSizeUpdate(win); })));
            gAtak.Children.Add(FieldBox("Constant Unrel/Mesh (s)", IntCtrl(m.ConstantReportingRateUnreliable, v => { m.ConstantReportingRateUnreliable = v; ForceSizeUpdate(win); })));
            gAtak.Children.Add(FieldBox("Dynamic Max (s)", IntCtrl(m.DynamicReportingRateMaxReliable, v => { m.DynamicReportingRateMaxReliable = v; ForceSizeUpdate(win); })));
            gAtak.Children.Add(FieldBox("Dynamic Min (s)", IntCtrl(m.DynamicReportingRateMinReliable, v => { m.DynamicReportingRateMinReliable = v; ForceSizeUpdate(win); })));
            gAtak.Children.Add(FieldBox("Postoj Reliable (s)", IntCtrl(m.DynamicReportingRateStationaryReliable, v => { m.DynamicReportingRateStationaryReliable = v; ForceSizeUpdate(win); })));
            gAtak.Children.Add(FieldBox("Postoj Mesh (s)", IntCtrl(m.DynamicReportingRateStationaryUnreliable, v => { m.DynamicReportingRateStationaryUnreliable = v; ForceSizeUpdate(win); })));
            gAtak.Children.Add(FieldBox("TCP / UDP Timeout (s)", IntCtrl(m.TcpConnectTimeout, v => { m.TcpConnectTimeout = v; ForceSizeUpdate(win); })));
            lStack.Children.Add(gAtak);
            var gSwitches = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            gSwitches.Children.Add(TileSwitch("Wysylaj pozycje na zewnatrz", "dispatchLocationCotExternal", m.DispatchLocationCotExternal, v => { m.DispatchLocationCotExternal = v; ForceSizeUpdate(win); }));
            gSwitches.Children.Add(TileSwitch("Wysylaj pozycje od startu", "dispatchLocationCotExternalAtStart", m.DispatchLocationCotExternalAtStart, v => { m.DispatchLocationCotExternalAtStart = v; ForceSizeUpdate(win); }));
            gSwitches.Children.Add(TileSwitch("Polaczenia bezstrumieniowe Mesh", "enableNonStreamingConnections", m.EnableNonStreamingConnections, v => { m.EnableNonStreamingConnections = v; ForceSizeUpdate(win); }));
            gSwitches.Children.Add(TileSwitch("Wylacz Mesh gdy dziala LTE", "autoDisableMeshSAWhenStreaming", m.AutoDisableMeshSAWhenStreaming, v => { m.AutoDisableMeshSAWhenStreaming = v; ForceSizeUpdate(win); }));
            lStack.Children.Add(gSwitches);
            card.Child = lStack;
            return card;
        }
        private static void InjectFirmwareFieldsIntoMeshtasticCard(Panel parentPanel, Window win)
        {
            var container = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var labelMin = new TextBlock
            {
                Text = "Firmware MIN:",
                Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var txtMin = new TextBox
            {
                Text = Current.FirmwareMin,
                Width = 70,
                Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            txtMin.TextChanged += (_, __) => { Current.FirmwareMin = txtMin.Text; ForceSizeUpdate(win); };
            var labelMax = new TextBlock
            {
                Text = "Firmware MAX:",
                Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 8, 0)
            };
            var txtMax = new TextBox
            {
                Text = Current.FirmwareMax,
                Width = 70,
                Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            txtMax.TextChanged += (_, __) => { Current.FirmwareMax = txtMax.Text; ForceSizeUpdate(win); };
            container.Children.Add(labelMin);
            container.Children.Add(txtMin);
            container.Children.Add(labelMax);
            container.Children.Add(txtMax);
            parentPanel.Children.Add(container);
        }
        private static UIElement FieldBox(string label, UIElement ctrl)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 6) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), FontSize = 10.5, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(ctrl);
            return sp;
        }
        private static UIElement TextCtrl(string init, Action<string> onChg)
        {
            var tb = new TextBox { Text = init, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), FontSize = 11, Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)), BorderThickness = new Thickness(1) };
            tb.TextChanged += (_, __) => onChg(tb.Text);
            return tb;
        }
        private static UIElement IntCtrl(int init, Action<int> onChg) => TextCtrl(init.ToString(), s => { if (int.TryParse(s.Trim(), out int v)) onChg(v); });
        private static UIElement ComboCtrl(string[] items, string init, Action<string> onChg)
        {
            var cb = new ComboBox { ItemsSource = items, SelectedItem = init, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.White };
            var cStyle = new Style(typeof(ComboBoxItem));
            cStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(20, 35, 40))));
            cStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cb.ItemContainerStyle = cStyle;
            cb.SelectionChanged += (_, __) => { if (cb.SelectedItem is string s) onChg(s); };
            return cb;
        }
        private static UIElement TileSwitch(string title, string sub, bool init, Action<bool> onChg)
        {
            var b = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 32, 37)), BorderBrush = new SolidColorBrush(Color.FromRgb(28, 64, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6) };
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 11 });
            st.Children.Add(new TextBlock { Text = sub, Foreground = new SolidColorBrush(Color.FromRgb(44, 229, 208)), FontSize = 9.5 });
            var tr = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var kn = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(2) };
            bool state = init;
            void Upd() {
                tr.Background = state ? new SolidColorBrush(Color.FromRgb(31, 224, 197)) : new SolidColorBrush(Color.FromRgb(24, 44, 50));
                kn.Background = state ? new SolidColorBrush(Color.FromRgb(6, 22, 24)) : new SolidColorBrush(Color.FromRgb(138, 168, 164));
                kn.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            Upd(); tr.Child = kn;
            tr.MouseLeftButtonDown += (_, __) => { state = !state; Upd(); onChg(state); };
            Grid.SetColumn(st, 0); Grid.SetColumn(tr, 1); g.Children.Add(st); g.Children.Add(tr); b.Child = g;
            return b;
        }
        public static void WriteGeneratedConfigsIfInteractive(string tgt)
        {
            if (Application.Current == null) return;
            if (string.IsNullOrWhiteSpace(tgt) || !Directory.Exists(tgt)) return;
            try
            {
                string norm = tgt.ToLowerInvariant();
                if (!norm.EndsWith("meshtastic") && !norm.EndsWith("config")) return;
                string jsonPath = System.IO.Path.Combine(tgt, "tactical_config.json");
                var root = new Dictionary<string, object>();
                if (File.Exists(jsonPath))
                {
                    string existing = File.ReadAllText(jsonPath, Encoding.UTF8);
                    root = JsonSerializer.Deserialize<Dictionary<string, object>>(existing) ?? new Dictionary<string, object>();
                }
                var a = new Dictionary<string, object>();
                a["locationReportingStrategy"] = Current.LocationReportingStrategy;
                a["constantReportingRateReliable"] = Current.ConstantReportingRateReliable;
                a["constantReportingRateUnreliable"] = Current.ConstantReportingRateUnreliable;
                a["dynamicReportingRateMinReliable"] = Current.DynamicReportingRateMinReliable;
                a["dynamicReportingRateMaxReliable"] = Current.DynamicReportingRateMaxReliable;
                a["dynamicReportingRateStationaryReliable"] = Current.DynamicReportingRateStationaryReliable;
                a["dynamicReportingRateStationaryUnreliable"] = Current.DynamicReportingRateStationaryUnreliable;
                a["tcpConnectTimeout"] = Current.TcpConnectTimeout;
                a["udpNoDataTimeout"] = Current.UdpNoDataTimeout;
                a["dispatchLocationCotExternal"] = Current.DispatchLocationCotExternal;
                a["dispatchLocationCotExternalAtStart"] = Current.DispatchLocationCotExternalAtStart;
                a["enableNonStreamingConnections"] = Current.EnableNonStreamingConnections;
                a["autoDisableMeshSAWhenStreaming"] = Current.AutoDisableMeshSAWhenStreaming;
                root["atak"] = a;
                if (!root.ContainsKey("meshtastic")) root["meshtastic"] = new Dictionary<string, object>();
                var meshNode = root["meshtastic"] as System.Text.Json.Nodes.JsonObject;
                if (meshNode == null && root["meshtastic"] is JsonElement el && el.ValueKind == JsonValueKind.Object)
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(el.GetRawText());
                    if (dict != null) root["meshtastic"] = dict;
                }
                if (root["meshtastic"] is Dictionary<string, object> md)
                {
                    md["firmwareMin"] = Current.FirmwareMin;
                    md["firmwareMax"] = Current.FirmwareMax;
                }
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
        }
    }
}