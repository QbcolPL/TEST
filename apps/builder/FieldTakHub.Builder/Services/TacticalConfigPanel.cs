#pragma warning disable CS8600, CS8602, CS8604
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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

        private static void FindVisualChildren<T>(DependencyObject depObj, List<T> list) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T t) list.Add(t);
                    FindVisualChildren<T>(child, list);
                }
            }
        }

        public static void AttachToMainWindow(Window win)
        {
            if (Application.Current == null) return;

            try
            {
                // Wymuszanie białej czcionki we wszystkich starych ComboBoxach
                var combos = new List<ComboBox>();
                FindVisualChildren(win, combos);
                Style nativeCbStyle = null;

                foreach (var c in combos)
                {
                    if (c.Style != null && c.Tag?.ToString() != "INJECTED")
                    {
                        nativeCbStyle = c.Style;
                    }
                    if (c.Tag?.ToString() != "INJECTED")
                    {
                        c.IsEditable = true;
                        c.IsReadOnly = true;
                        c.Foreground = Brushes.White;
                    }
                }

                var tbs = new List<TextBlock>();
                FindVisualChildren(win, tbs);
                foreach (var tb in tbs)
                {
                    if (tb.Text != null && tb.Text.Contains("Konfiguracja Meshtastic"))
                    {
                        Border cardBorder = null;
                        DependencyObject p = VisualTreeHelper.GetParent(tb);
                        while (p != null)
                        {
                            if (p is Border b && b.Background != null) { cardBorder = b; break; }
                            p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p);
                        }

                        if (cardBorder != null)
                        {
                            if (!_fwFieldsAdded && cardBorder.Child is Panel innerPanel)
                            {
                                InjectFirmwareFields(innerPanel, win);
                                _fwFieldsAdded = true;
                            }

                            if (!_atakCardAdded && VisualTreeHelper.GetParent(cardBorder) is Panel parentPanel)
                            {
                                int idx = parentPanel.Children.IndexOf(cardBorder);
                                parentPanel.Children.Insert(idx, BuildAtakCard(win, nativeCbStyle));
                                _atakCardAdded = true;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void ForceSizeUpdate(Window win)
        {
            if (Application.Current == null) return;

            try
            {
                string evName = "Draft";
                var tbs = new List<TextBlock>();
                FindVisualChildren(win, tbs);
                foreach (var tb in tbs)
                {
                    if (tb.Text != null && (tb.Text.Contains("Nazwa wydarzenia") || tb.Text.Contains("Event Name")))
                    {
                        var parent = VisualTreeHelper.GetParent(tb);
                        if (parent != null)
                        {
                            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                            {
                                var sib = VisualTreeHelper.GetChild(parent, i);
                                if (sib is TextBox tbx && !string.IsNullOrWhiteSpace(tbx.Text)) { evName = tbx.Text.Trim(); break; }
                            }
                        }
                    }
                }

                var targetDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string baseD = AppDomain.CurrentDomain.BaseDirectory;
                targetDirs.Add(System.IO.Path.Combine(baseD, "payload", evName, "config"));

                if (win.DataContext != null)
                {
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
                    foreach (var prop in win.DataContext.GetType().GetProperties(flags))
                    {
                        if (prop.PropertyType == typeof(string))
                        {
                            try
                            {
                                string val = prop.GetValue(win.DataContext) as string;
                                if (!string.IsNullOrWhiteSpace(val) && val.Length > 3 && System.IO.Path.IsPathRooted(val) && Directory.Exists(val))
                                {
                                    targetDirs.Add(System.IO.Path.Combine(val, "config"));
                                    targetDirs.Add(System.IO.Path.Combine(val, "source"));
                                    if (val.EndsWith("config", StringComparison.OrdinalIgnoreCase) || val.EndsWith("source", StringComparison.OrdinalIgnoreCase)) 
                                        targetDirs.Add(val);
                                }
                            } catch { }
                        }
                    }
                }

                foreach (string dir in targetDirs)
                {
                    try 
                    {
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        WriteGeneratedConfigsIfInteractive(dir);
                    } catch { }
                }

                win.Dispatcher.BeginInvoke(new Action(() => {
                    var btns = new List<ButtonBase>();
                    FindVisualChildren(win, btns);
                    foreach(var btn in btns)
                    {
                        string c = btn.Content?.ToString()?.ToLower() ?? "";
                        string n = btn.Name?.ToLower() ?? "";
                        if (c.Contains("odśwież") || c.Contains("refresh") || c.Contains("przelicz") || c.Contains("aktualizuj") || n.Contains("refresh") || n.Contains("update"))
                        {
                            if (btn.Tag?.ToString() != "INJECTED_BTN")
                            {
                                btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                            }
                        }
                    }

                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    if (win.DataContext != null)
                    {
                        foreach (var p in win.DataContext.GetType().GetProperties(flags))
                        {
                            if (typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType))
                            {
                                string n = p.Name.ToLower();
                                if (n.Contains("size") || n.Contains("refresh") || n.Contains("update") || n.Contains("calc") || n.Contains("analyz") || n.Contains("load"))
                                {
                                    var cmd = p.GetValue(win.DataContext) as System.Windows.Input.ICommand;
                                    if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
                                }
                            }
                        }
                        foreach (var m in win.DataContext.GetType().GetMethods(flags))
                        {
                            string n = m.Name.ToLower();
                            if ((n.Contains("size") || n.Contains("update") || n.Contains("refresh") || n.Contains("calc") || n.Contains("analyz") || n.Contains("load")) && m.GetParameters().Length == 0)
                            {
                                try { m.Invoke(win.DataContext, null); } catch { }
                            }
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            catch { }
        }

        private static UIElement BuildAtakCard(Window win, Style nativeCbStyle)
        {
            var m = Current;
            var card = new Border { Background = new SolidColorBrush(Color.FromRgb(9, 20, 24)), BorderBrush = new SolidColorBrush(Color.FromRgb(26, 58, 66)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14) };
            var lStack = new StackPanel();
            lStack.Children.Add(new TextBlock { Text = "Konfiguracja ATAK-CIV (PLI i siec)", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            lStack.Children.Add(new TextBlock { Text = "Czysty profil ATAK bez wymuszania wtyczek. Zdefiniuj czestotliwosci odswiezania PLI.", Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

            var gAtak = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
            gAtak.Children.Add(FieldBox("Strategia PLI", ComboCtrl(new[] { "Constant", "Dynamic" }, m.LocationReportingStrategy, nativeCbStyle, v => { m.LocationReportingStrategy = v; })));
            gAtak.Children.Add(FieldBox("Const Rel/LTE (s)", IntCtrl(m.ConstantReportingRateReliable, v => { m.ConstantReportingRateReliable = v; })));
            gAtak.Children.Add(FieldBox("Const Unrel/Mesh (s)", IntCtrl(m.ConstantReportingRateUnreliable, v => { m.ConstantReportingRateUnreliable = v; })));
            gAtak.Children.Add(FieldBox("Dynamic Max (s)", IntCtrl(m.DynamicReportingRateMaxReliable, v => { m.DynamicReportingRateMaxReliable = v; })));
            gAtak.Children.Add(FieldBox("Dynamic Min (s)", IntCtrl(m.DynamicReportingRateMinReliable, v => { m.DynamicReportingRateMinReliable = v; })));
            gAtak.Children.Add(FieldBox("Postoj Rel (s)", IntCtrl(m.DynamicReportingRateStationaryReliable, v => { m.DynamicReportingRateStationaryReliable = v; })));
            gAtak.Children.Add(FieldBox("Postoj Mesh (s)", IntCtrl(m.DynamicReportingRateStationaryUnreliable, v => { m.DynamicReportingRateStationaryUnreliable = v; })));
            gAtak.Children.Add(FieldBox("TCP/UDP Timeout (s)", IntCtrl(m.TcpConnectTimeout, v => { m.TcpConnectTimeout = v; })));
            lStack.Children.Add(gAtak);

            var gSw = new UniformGrid { Columns = 2 };
            gSw.Children.Add(TileSwitch("Wysylaj pozycje na zewnatrz", m.DispatchLocationCotExternal, v => { m.DispatchLocationCotExternal = v; }));
            gSw.Children.Add(TileSwitch("Polaczenia bezstrumieniowe Mesh", m.EnableNonStreamingConnections, v => { m.EnableNonStreamingConnections = v; }));
            lStack.Children.Add(gSw);

            // DEDYKOWANY PRZYCISK ODŚWIEŻANIA ROZMIARU PACZKI
            var btnRefresh = new Button {
                Content = "Odśwież / Skalkuluj",
                Height = 32,
                Margin = new Thickness(0, 14, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(44, 229, 208)),
                Foreground = new SolidColorBrush(Color.FromRgb(6, 22, 24)),
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Tag = "INJECTED_BTN"
            };
            btnRefresh.Click += (s, e) => ForceSizeUpdate(win);
            lStack.Children.Add(btnRefresh);

            card.Child = lStack;
            return card;
        }

        private static void InjectFirmwareFields(Panel parentPanel, Window win)
        {
            var container = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

            var labelMin = new TextBlock { Text = "Firmware MIN:", Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var txtMin = new TextBox { Text = Current.FirmwareMin, Width = 70, Height = 24, Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
            txtMin.TextChanged += (_, __) => { Current.FirmwareMin = txtMin.Text; };

            var labelMax = new TextBlock { Text = "Firmware MAX:", Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
            var txtMax = new TextBox { Text = Current.FirmwareMax, Width = 70, Height = 24, Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
            txtMax.TextChanged += (_, __) => { Current.FirmwareMax = txtMax.Text; };

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

        private static UIElement ComboCtrl(string[] items, string init, Style nativeStyle, Action<string> onChg)
        {
            var cb = new ComboBox { ItemsSource = items, SelectedItem = init, Height = 28, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center, Tag = "INJECTED" };
            if (nativeStyle != null) cb.Style = nativeStyle;
            
            // TRIK NA BIAŁĄ CZCIONKĘ
            cb.IsEditable = true;
            cb.IsReadOnly = true;
            cb.Foreground = Brushes.White;
            cb.Background = new SolidColorBrush(Color.FromRgb(8, 19, 22));

            var cStyle = new Style(typeof(ComboBoxItem));
            cStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(15, 32, 37))));
            cStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cb.ItemContainerStyle = cStyle;
            
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string str) onChg(str); };
            return cb;
        }

        private static UIElement TileSwitch(string title, bool init, Action<bool> onChg)
        {
            var b = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 32, 37)), BorderBrush = new SolidColorBrush(Color.FromRgb(28, 64, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6) };
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
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
            Grid.SetColumn(tb, 0); Grid.SetColumn(tr, 1);
            g.Children.Add(tb); g.Children.Add(tr);
            b.Child = g;
            return b;
        }

        public static void WriteGeneratedConfigsIfInteractive(string tgt)
        {
            if (string.IsNullOrWhiteSpace(tgt)) return;
            if (Application.Current == null) return; 

            try
            {
                if (!Directory.Exists(tgt)) Directory.CreateDirectory(tgt);
                string jsonPath = System.IO.Path.Combine(tgt, "tactical_config.json");
                var root = new Dictionary<string, object>();
                if (File.Exists(jsonPath))
                {
                    string existing = File.ReadAllText(jsonPath, Encoding.UTF8);
                    root = JsonSerializer.Deserialize<Dictionary<string, object>>(existing) ?? new Dictionary<string, object>();
                }

                var a = new Dictionary<string, object>
                {
                    ["locationReportingStrategy"] = Current.LocationReportingStrategy,
                    ["constantReportingRateReliable"] = Current.ConstantReportingRateReliable,
                    ["constantReportingRateUnreliable"] = Current.ConstantReportingRateUnreliable,
                    ["dynamicReportingRateMinReliable"] = Current.DynamicReportingRateMinReliable,
                    ["dynamicReportingRateMaxReliable"] = Current.DynamicReportingRateMaxReliable,
                    ["dynamicReportingRateStationaryReliable"] = Current.DynamicReportingRateStationaryReliable,
                    ["dynamicReportingRateStationaryUnreliable"] = Current.DynamicReportingRateStationaryUnreliable,
                    ["tcpConnectTimeout"] = Current.TcpConnectTimeout,
                    ["udpNoDataTimeout"] = Current.UdpNoDataTimeout,
                    ["dispatchLocationCotExternal"] = Current.DispatchLocationCotExternal,
                    ["dispatchLocationCotExternalAtStart"] = Current.DispatchLocationCotExternalAtStart,
                    ["enableNonStreamingConnections"] = Current.EnableNonStreamingConnections,
                    ["autoDisableMeshSAWhenStreaming"] = Current.AutoDisableMeshSAWhenStreaming
                };
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