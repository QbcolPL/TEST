#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using FieldTakHub.Builder.Core;

namespace FieldTakHub.Builder.Views
{
    /// <summary>
    /// Moduł 3: Konfigurator Stron i Generator Paczek .FTAK napisany od zera w architekturze MVVM.
    /// Zapewnia pełną konfigurację Meshtastic, kluczy AES-256 PSK, tabel kanałów głosowych PMR/VHF/UHF/DMR
    /// oraz unikalnych kolorów RGB dla każdej nowej strony konfliktu.
    /// </summary>
    public partial class FactionPackageBuilderView : UserControl
    {
        private readonly ScrollViewer _rootScroll;
        private readonly StackPanel _mainStack;
        private readonly StackPanel _factionsListPanel;

        public FactionPackageBuilderView()
        {
            _mainStack = new StackPanel { Margin = new Thickness(24) };

            _factionsListPanel = new StackPanel();

            _rootScroll = new ScrollViewer
            {
                Content = _mainStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalUiFactory.BgMainBrush
            };

            Content = _rootScroll;
            RebuildView();
        }

        partial void RebuildView()
        {
            _mainStack.Children.Clear();

            // 1. Karta główna: 3 przyciski główne paczek .FTAK oraz foldery
            var topCard = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentCyanBrush);
            var topStack = new StackPanel();
            topStack.Children.Add(TacticalUiFactory.CreateSectionHeader(LocalizationManager.Get("PKG_HEADER"), TacticalUiFactory.AccentCyanBrush, 15));
            topStack.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T(
                    "Zarządzaj profilami stron konfliktu (BLUFOR / OPFOR / Inne), konfiguruj szyfrowanie AES-256 PSK, kanały LoRa Meshtastic i generuj gotowe pakiety `.ftak` oraz plakaty Safezone z kodem QR.",
                    "Manage conflict faction profiles (BLUFOR / OPFOR / Other), configure AES-256 PSK encryption, LoRa Meshtastic channels and build `.ftak` packages with QR Safezone posters."),
                Foreground = TacticalUiFactory.TextMutedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap
            });

            var btnWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var buildAllBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("BTN_BUILD_ALL"),
                TacticalUiFactory.AccentCyanBrush,
                new SolidColorBrush(Color.FromRgb(5, 16, 20)));
            buildAllBtn.Click += (_, __) =>
            {
                int count = ProjectStore.Instance.BuildAllActiveFactionPackages();
                MessageBox.Show(
                    LocalizationManager.T(
                        "✅ Pomyślnie zbudowano paczki .FTAK dla wszystkich aktywnych stron (" + count + ") w folderze:\n" + ProjectStore.Instance.OutputRootDirectory,
                        "✅ Successfully built .FTAK packages for all active factions (" + count + ") in folder:\n" + ProjectStore.Instance.OutputRootDirectory),
                    "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            var openSharedBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("BTN_OPEN_SHARED"),
                new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                TacticalUiFactory.AccentGoldBrush,
                TacticalUiFactory.AccentGoldBrush);
            openSharedBtn.Click += (_, __) => ProjectStore.Instance.OpenSharedFolderInExplorer();

            var hotspotBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("BTN_WIFI_HOTSPOT"),
                new SolidColorBrush(Color.FromRgb(14, 42, 54)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush);
            hotspotBtn.Click += (_, __) =>
            {
                var fac = ProjectStore.Instance.Factions.FirstOrDefault() ?? ProjectStore.Instance.AddNewFaction();
                string url = ProjectStore.Instance.StartOrUpdateHotspotServer(fac);
                Clipboard.SetText(url);
                MessageBox.Show(
                    LocalizationManager.T(
                        "📡 Aktywny serwer Hotspot Wi-Fi dla strony '" + fac.Name + "'!\nAdres URL został skopiowany do schowka:\n" + url,
                        "📡 Active Wi-Fi Hotspot server for faction '" + fac.Name + "'!\nURL copied to clipboard:\n" + url),
                    "📡 Wi-Fi Hotspot", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            btnWrap.Children.Add(buildAllBtn);
            btnWrap.Children.Add(openSharedBtn);
            btnWrap.Children.Add(hotspotBtn);
            topStack.Children.Add(btnWrap);
            topCard.Child = topStack;
            _mainStack.Children.Add(topCard);

            // 2. Przycisk dodawania nowej strony konfliktu (+)
            var addFactionBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("FACTION_ADD"),
                new SolidColorBrush(Color.FromRgb(22, 110, 65)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush,
                40);
            addFactionBtn.Margin = new Thickness(0, 0, 0, 16);
            addFactionBtn.Click += (_, __) =>
            {
                ProjectStore.Instance.AddNewFaction();
                RebuildView();
            };
            _mainStack.Children.Add(addFactionBtn);

            // 3. Lista kart stron konfliktu
            _factionsListPanel.Children.Clear();
            foreach (var faction in ProjectStore.Instance.Factions)
            {
                _factionsListPanel.Children.Add(BuildFactionCard(faction));
            }
            _mainStack.Children.Add(_factionsListPanel);
        }

        private Border BuildFactionCard(FactionProfileModel faction)
        {
            var card = TacticalUiFactory.CreateCardBorder(faction.AccentBrush);
            var stack = new StackPanel();

            // Nagłówek karty strony
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = LocalizationManager.T("Generator Stron — ", "Faction Profile — ") + faction.Name,
                Foreground = faction.AccentBrush,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            headerGrid.Children.Add(titleBlock);

            if (ProjectStore.Instance.Factions.Count > 1)
            {
                var delBtn = TacticalUiFactory.CreateCompactIconBtn("✖", new SolidColorBrush(Color.FromRgb(85, 22, 22)), TacticalUiFactory.AccentRedBrush);
                delBtn.ToolTip = LocalizationManager.T("Usuń tę stronę", "Delete this faction");
                delBtn.Click += (_, __) =>
                {
                    ProjectStore.Instance.Factions.Remove(faction);
                    RebuildView();
                };
                Grid.SetColumn(delBtn, 1);
                headerGrid.Children.Add(delBtn);
            }
            stack.Children.Add(headerGrid);

            // Siatka ustawień podstawowych strony (Nazwa, Kolor RGB, Kanał LoRa, Klucz PSK)
            var gridProps = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            gridProps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridProps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            gridProps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridProps.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gridProps.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gridProps.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Kolumna 0, Wiersz 0: Nazwa strony
            var spName = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            spName.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Nazwa strony konfliktu:", "Faction Name:")));
            var nameBox = TacticalUiFactory.CreateTextBox(faction.Name);
            nameBox.TextChanged += (_, __) =>
            {
                faction.Name = nameBox.Text;
                titleBlock.Text = LocalizationManager.T("Generator Stron — ", "Faction Profile — ") + faction.Name;
            };
            spName.Children.Add(nameBox);
            Grid.SetRow(spName, 0); Grid.SetColumn(spName, 0);
            gridProps.Children.Add(spName);

            // Kolumna 2, Wiersz 0: Kolor RGB
            var spColor = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            spColor.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Kolor taktyczny (RGB):", "Tactical Color (RGB):")));
            var colorBtn = TacticalUiFactory.CreateActionButton("■ " + faction.ColorHex, faction.AccentBrush, Brushes.White, faction.AccentBrush, 32);
            colorBtn.Click += (_, __) =>
            {
                string newHex = TacticalServices.GenerateUniqueFactionColorHex(ProjectStore.Instance.Factions.Select(f => f.ColorHex));
                faction.ColorHex = newHex;
                colorBtn.Content = "■ " + newHex;
                colorBtn.Background = faction.AccentBrush;
                colorBtn.BorderBrush = faction.AccentBrush;
                card.BorderBrush = faction.AccentBrush;
                titleBlock.Foreground = faction.AccentBrush;
            };
            spColor.Children.Add(colorBtn);
            Grid.SetRow(spColor, 0); Grid.SetColumn(spColor, 2);
            gridProps.Children.Add(spColor);

            // Kolumna 0, Wiersz 1: Kanał Meshtastic
            var spMesh = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            spMesh.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Nazwa kanału LoRa Meshtastic:", "LoRa Meshtastic Channel Name:")));
            var meshBox = TacticalUiFactory.CreateTextBox(faction.ChannelName);
            meshBox.TextChanged += (_, __) => faction.ChannelName = meshBox.Text;
            spMesh.Children.Add(meshBox);
            Grid.SetRow(spMesh, 1); Grid.SetColumn(spMesh, 0);
            gridProps.Children.Add(spMesh);

            // Kolumna 2, Wiersz 1: Klucz PSK (AES-256)
            var spPsk = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var pskLblGrid = new Grid();
            pskLblGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pskLblGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var pskTextLbl = TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Klucz szyfrowania AES-256 PSK (Base64):", "AES-256 PSK Encryption Key (Base64):"));
            Grid.SetColumn(pskTextLbl, 0); pskLblGrid.Children.Add(pskTextLbl);

            var genPskLink = new TextBlock
            {
                Text = LocalizationManager.T("🎲 Generuj nowy", "🎲 Generate new"),
                Foreground = TacticalUiFactory.AccentCyanBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(genPskLink, 1); pskLblGrid.Children.Add(genPskLink);
            spPsk.Children.Add(pskLblGrid);

            var pskBox = TacticalUiFactory.CreateTextBox(faction.PskBase64, false, 32, true);
            pskBox.TextChanged += (_, __) => faction.PskBase64 = pskBox.Text;
            genPskLink.MouseLeftButtonDown += (_, __) =>
            {
                string newKey = TacticalServices.GenerateAes256PskBase64();
                faction.PskBase64 = newKey;
                pskBox.Text = newKey;
            };
            spPsk.Children.Add(pskBox);
            Grid.SetRow(spPsk, 1); Grid.SetColumn(spPsk, 2);
            gridProps.Children.Add(spPsk);

            // Kolumna 0, Wiersz 2: Adres Serwera OpenTAKServer
            var spOts = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            spOts.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Serwer ATAK (OpenTAKServer Host):", "ATAK Server (OpenTAKServer Host):")));
            var otsBox = TacticalUiFactory.CreateTextBox(faction.OtsHost);
            otsBox.TextChanged += (_, __) => faction.OtsHost = otsBox.Text;
            spOts.Children.Add(otsBox);
            Grid.SetRow(spOts, 2); Grid.SetColumn(spOts, 0);
            gridProps.Children.Add(spOts);

            // Kolumna 2, Wiersz 2: Telefon alarmowy SOS oraz Ważność (0 = bezterminowo)
            var spSosExp = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            spSosExp.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Telefon alarmowy ORGA / SOS (0 = bezterminowo):", "ORGA / SOS Emergency Phone (0 = permanent):")));
            
            var sosExpGrid = new Grid();
            sosExpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            sosExpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sosExpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

            var sosBox = TacticalUiFactory.CreateTextBox(faction.EmergencyPhone);
            sosBox.TextChanged += (_, __) => faction.EmergencyPhone = sosBox.Text;
            Grid.SetColumn(sosBox, 0); sosExpGrid.Children.Add(sosBox);

            var expBox = TacticalUiFactory.CreateTextBox(faction.ExpirationHours.ToString(CultureInfo.InvariantCulture));
            expBox.ToolTip = "0 = bezterminowo";
            expBox.TextChanged += (_, __) =>
            {
                if (int.TryParse(expBox.Text, out int parsed)) faction.ExpirationHours = parsed;
            };
            Grid.SetColumn(expBox, 2); sosExpGrid.Children.Add(expBox);
            spSosExp.Children.Add(sosExpGrid);

            Grid.SetRow(spSosExp, 2); Grid.SetColumn(spSosExp, 2);
            gridProps.Children.Add(spSosExp);

            stack.Children.Add(gridProps);

            // 3. Tabela Kanałów Głosowych (PMR / VHF / DMR) & SOS
            stack.Children.Add(new Border { Height = 1, Background = TacticalUiFactory.BorderCardBrush, Margin = new Thickness(0, 8, 0, 10) });
            stack.Children.Add(TacticalUiFactory.CreateSectionHeader(LocalizationManager.Get("VOICE_TABLE_HEADER"), TacticalUiFactory.AccentGoldBrush, 13));

            var voiceListPanel = new StackPanel();

            void RebuildVoiceRows()
            {
                voiceListPanel.Children.Clear();
                foreach (var vc in faction.VoiceChannels)
                {
                    var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    TextBox Cell(string val, int col)
                    {
                        var t = TacticalUiFactory.CreateTextBox(val, false, 30);
                        Grid.SetColumn(t, col);
                        return t;
                    }

                    var tRole = Cell(vc.Role, 0);
                    tRole.TextChanged += (_, __) => vc.Role = tRole.Text;
                    rowGrid.Children.Add(tRole);

                    var tBand = Cell(vc.Band, 1);
                    tBand.TextChanged += (_, __) => vc.Band = tBand.Text;
                    rowGrid.Children.Add(tBand);

                    var tFreq = Cell(vc.Frequency, 2);
                    tFreq.TextChanged += (_, __) => vc.Frequency = tFreq.Text;
                    rowGrid.Children.Add(tFreq);

                    var tTone = Cell(vc.Tone, 3);
                    tTone.TextChanged += (_, __) => vc.Tone = tTone.Text;
                    rowGrid.Children.Add(tTone);

                    var delRowBtn = TacticalUiFactory.CreateCompactIconBtn("−", new SolidColorBrush(Color.FromRgb(85, 22, 22)), TacticalUiFactory.AccentRedBrush);
                    delRowBtn.Click += (_, __) =>
                    {
                        faction.VoiceChannels.Remove(vc);
                        RebuildVoiceRows();
                    };
                    Grid.SetColumn(delRowBtn, 4);
                    rowGrid.Children.Add(delRowBtn);

                    voiceListPanel.Children.Add(rowGrid);
                }
            }

            RebuildVoiceRows();
            stack.Children.Add(voiceListPanel);

            var addChannelBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("BTN_ADD_CHANNEL"),
                new SolidColorBrush(Color.FromRgb(22, 130, 75)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush,
                32,
                new Thickness(0, 4, 0, 12));
            addChannelBtn.Click += (_, __) =>
            {
                faction.VoiceChannels.Add(new VoiceChannelModel { Role = "Kanał dodatkowy", Band = "PMR", Frequency = "CH 2 (446.01875 MHz)", Tone = "CTCSS 79.7 Hz" });
                RebuildVoiceRows();
            };
            stack.Children.Add(addChannelBtn);

            // 4. Dolne przyciski akcji strony (Pliki strony, Plakat Safezone A4 + QR)
            var bottomActionsGrid = new Grid();
            bottomActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            bottomActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var filesBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("FACTION_FILES"),
                new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush,
                36,
                new Thickness(0));
            filesBtn.Click += (_, __) => ProjectStore.Instance.OpenFactionFolderInExplorer(faction);
            Grid.SetColumn(filesBtn, 0); bottomActionsGrid.Children.Add(filesBtn);

            var posterBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("FACTION_POSTER_QR"),
                new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                TacticalUiFactory.AccentGoldBrush,
                TacticalUiFactory.AccentGoldBrush,
                36,
                new Thickness(0));
            posterBtn.Click += (_, __) => ProjectStore.Instance.GenerateSafezonePosterHtml(faction, true);
            Grid.SetColumn(posterBtn, 2); bottomActionsGrid.Children.Add(posterBtn);

            stack.Children.Add(bottomActionsGrid);
            card.Child = stack;
            return card;
        }
    }
}
