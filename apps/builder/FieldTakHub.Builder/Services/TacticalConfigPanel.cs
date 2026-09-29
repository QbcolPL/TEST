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
        public string FirmwareMin { get; set; } = "2.7.26";
        public string FirmwareMax { get; set; } = "2.8.0.47";
    }
    public static class TacticalConfigPanel
    {
        public static TacticalConfigModel Current { get; set; } = new TacticalConfigModel();
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
                        cb.Loaded += (_, __) => cb.Foreground = Brushes.White;
                    }
                    if (cur is TextBlock tb && tb.Text != null && tb.Text.Contains("Konfiguracja Meshtastic"))
                    {
                        var parent = VisualTreeHelper.GetParent(tb) ?? LogicalTreeHelper.GetParent(tb);
                        while (parent != null)
                        {
                            if (parent is Border cardBorder)
                            {
                                var cardContent = cardBorder.Child as Panel;
                                if (cardContent != null)
                                {
                                    InjectFirmwareFieldsIntoMeshtasticCard(cardContent);
                                }
                                break;
                            }
                            parent = VisualTreeHelper.GetParent(parent) ?? LogicalTreeHelper.GetParent(parent);
                        }
                    }
                    int cnt = VisualTreeHelper.GetChildrenCount(cur);
                    for (int i = 0; i < cnt; i++) q.Enqueue(VisualTreeHelper.GetChild(cur, i));
                }
            }
            catch { }
        }
        private static void InjectFirmwareFieldsIntoMeshtasticCard(Panel parentPanel)
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
            txtMin.TextChanged += (_, __) => Current.FirmwareMin = txtMin.Text;
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
            txtMax.TextChanged += (_, __) => Current.FirmwareMax = txtMax.Text;
            container.Children.Add(labelMin);
            container.Children.Add(txtMin);
            container.Children.Add(labelMax);
            container.Children.Add(txtMax);
            parentPanel.Children.Add(container);
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