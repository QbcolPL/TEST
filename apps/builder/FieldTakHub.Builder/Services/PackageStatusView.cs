using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FieldTakHub.Builder.Services.Shell;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Prawa kolumna generatora paczek (etap 4): karta „Status paczki” pokazuje, co naprawdę trafi do paczek
    /// (zamiast stałych „✓ Skonfigurowany”), a karta „Ostatnie paczki .ftak” — ostatnio zbudowany plik z folderu out\.
    /// Odświeżane przy każdym przeglądzie folderu source (co kilka sekund). Od etapu 1 modułu paczek karta statusu
    /// pokazuje też uwagi do ustawień stron (klucz kanału, adres serwera TAK, link rejestracji, powtórzone nazwy).
    /// </summary>
    internal static class PackageStatusView
    {
        private const string NoI18n = "DYNAMIC_NO_I18N";
        private static string _lastStatusKey = "";
        private static string _lastPackageFolder = "";

        public static void Update(Window win, IReadOnlyList<SourceFileEntry> files, IReadOnlyList<BuiltPackageEntry> packages,
            IReadOnlyList<FactionStatusEntry> factions)
        {
            if (win == null) return;
            try
            {
                bool english = BuilderShell.IsEnglish;
                var status = PackageStatusSpec.Build(files, factions, english, CultureInfo.CurrentCulture);
                RenderStatus(win, status, english);
                RenderLatestPackages(win, LatestPackagesSpec.Describe(packages, DateTime.Now, english, CultureInfo.CurrentCulture), packages);
            }
            catch { }
        }

        private static void RenderStatus(Window win, PackageStatus status, bool english)
        {
            // Ten sam stan co poprzednio — bez przebudowy wierszy (odświeżanie co kilka sekund).
            var key = new StringBuilder(english ? "EN|" : "PL|").Append(status.Level).Append('|').Append(status.Headline).Append('|').Append(status.Hint);
            foreach (var row in status.Rows) key.Append('|').Append(row.Level).Append(row.Label).Append(row.Value).Append(row.Detail);
            string statusKey = key.ToString();
            if (statusKey == _lastStatusKey) return;

            if (win.FindName("PackageStatusIcon") is TextBlock icon)
            {
                icon.Text = status.Level == StatusLevel.Ok ? "✓" : "!";
                icon.Foreground = LevelBrush(win, status.Level);
            }
            if (win.FindName("PackageStatusHeadline") is TextBlock headline) headline.Text = status.Headline;
            if (win.FindName("PackageStatusHint") is TextBlock hint) hint.Text = status.Hint;
            if (win.FindName("PackageStatusRows") is not Panel rows) return;

            rows.Children.Clear();
            Brush text = win.TryFindResource("Text") as Brush ?? Brushes.White;
            Brush muted = win.TryFindResource("Muted") as Brush ?? Brushes.Gray;
            foreach (var row in status.Rows)
            {
                var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
                var glyph = new TextBlock
                {
                    Tag = NoI18n,
                    Text = Glyph(row.Level),
                    Width = 20,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = LevelBrush(win, row.Level),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(glyph, Dock.Left);
                var value = new TextBlock
                {
                    Tag = NoI18n,
                    Text = row.Value,
                    FontSize = 12,
                    Foreground = row.Level == StatusLevel.Warning ? LevelBrush(win, StatusLevel.Warning) : muted,
                    MaxWidth = 150,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(value, Dock.Right);
                var label = new TextBlock
                {
                    Tag = NoI18n,
                    Text = row.Label,
                    FontSize = 12,
                    Foreground = text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                line.Children.Add(glyph);
                line.Children.Add(value);
                line.Children.Add(label);
                BuilderShell.SetLiveToolTip(line, row.Label + ": " + row.Value + (string.IsNullOrWhiteSpace(row.Detail) ? "" : "\n" + row.Detail));
                rows.Children.Add(line);
            }
            _lastStatusKey = statusKey;
        }

        private static void RenderLatestPackages(Window win, LatestPackagesText text, IReadOnlyList<BuiltPackageEntry> packages)
        {
            if (win.FindName("GeneratedFileNameText") is TextBlock name) SetDynamicText(name, text.Title);
            if (win.FindName("GeneratedFilePathText") is TextBlock folder) SetDynamicText(folder, text.Folder);
            if (win.FindName("GeneratedFileSizeText") is TextBlock detail) SetDynamicText(detail, text.Detail);
            _lastPackageFolder = packages != null && packages.Count > 0 ? text.Folder : "";
        }

        private static void SetDynamicText(TextBlock block, string value)
        {
            block.Tag = NoI18n;
            if (!string.Equals(block.Text, value, StringComparison.Ordinal)) block.Text = value;
        }

        /// <summary>Przycisk „Otwórz folder paczek”: folder ostatniej paczki, a bez paczek — out\ obok folderu source.</summary>
        public static void OpenPackagesFolder(Window win)
        {
            try
            {
                string folder = _lastPackageFolder;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                    string outBase = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                    folder = Path.Combine(outBase, "out");
                    Directory.CreateDirectory(folder);
                }
                TacticalConfigPanel.TryOpenFolderInExplorer(folder);
            }
            catch { }
        }

        private static string Glyph(StatusLevel level) => level switch
        {
            StatusLevel.Ok => "✓",
            StatusLevel.Warning => "!",
            _ => "○"
        };

        private static Brush LevelBrush(Window win, StatusLevel level) => level switch
        {
            StatusLevel.Ok => win.TryFindResource("Good") as Brush ?? Brushes.LimeGreen,
            StatusLevel.Warning => BuilderShell.UnsavedBrush,
            _ => win.TryFindResource("Muted") as Brush ?? Brushes.Gray
        };
    }
}
