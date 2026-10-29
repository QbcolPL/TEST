#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FieldTakHub.Builder.Core;

namespace FieldTakHub.Builder.Views
{
    /// <summary>
    /// Moduł 2 (Część A): Generator Rozkazu Bojowego NATO / MilSim (OPORD v4.0) napisany od zera.
    /// Zawiera górny boks z przyciskiem synchronizacji na samej górze, 100% niezależne zakładki dzienne,
    /// przyciski wyboru dni do druku ("Generuj wszystkie dni" / "Generuj tylko aktywny dzień"),
    /// moduł pogodowo-astronomiczny METOC z twardą walidacją AO oraz sekcje I, II, III i Tabelę D Ról.
    /// </summary>
    public partial class OpordGeneratorView : UserControl
    {
        private readonly ScrollViewer _rootScroll;
        private readonly StackPanel _mainStack;

        public OpordGeneratorView()
        {
            _mainStack = new StackPanel { Margin = new Thickness(24) };
            _rootScroll = new ScrollViewer
            {
                Content = _mainStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalUiFactory.BgMainBrush
            };
            Content = _rootScroll;
            RebuildView();
        }

        partial void BuildSectionsPartB(StackPanel parentStack, OpordDocumentModel doc, OpordDayPlanModel activeDay);
        partial void OnExportA4Clicked();
        partial void OnExportDiscordClicked();
        partial void OnSaveToFtakClicked();

        partial void RebuildView()
        {
            _mainStack.Children.Clear();

            var store = ProjectStore.Instance;
            var doc = store.OpordDocument;
            if (doc.Days.Count == 0)
            {
                doc.Days.Add(store.CreateDefaultOpordDay(1));
            }

            doc.ActiveDayIndex = Math.Clamp(doc.ActiveDayIndex, 0, doc.Days.Count - 1);
            var activeDay = doc.Days[doc.ActiveDayIndex];

            // Automatyczna synchronizacja z Generatorem Stron i Mapą 1:1
            store.SyncGeneratorsToOpord();

            // =========================================================================
            // 1. GÓRNY BOKS NAGŁÓWKOWY OPORD (Z PRZYCISKIEM SYNCHRONIZACJI NA SAMEJ GÓRZE)
            // =========================================================================
            var topCard = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentCyanBrush);
            var topStack = new StackPanel();

            topStack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.Get("OPORD_HEADER"),
                TacticalUiFactory.AccentCyanBrush, 15.5));

            // Przycisk "🔄 Pobierz z Generatora Stron" umieszczony na stałe na samej górze!
            var topSyncBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("OPORD_SYNC_TOP_BTN"),
                new SolidColorBrush(Color.FromRgb(14, 52, 64)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush,
                38,
                new Thickness(0, 2, 0, 12));
            topSyncBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            topSyncBtn.Click += (_, __) =>
            {
                store.SyncGeneratorsToOpord();
                RebuildView();
            };
            topStack.Children.Add(topSyncBtn);

            // Wiersz ustawień głównych rozkazu: Kryptonim Operacji, Strona Konfliktu, Klauzula Tajności
            var metaGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            // Kryptonim Operacji / Wydarzenia
            var spEvent = new StackPanel();
            spEvent.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Kryptonim Operacji / MilSima:", "Operation / MilSim Codename:")));
            var eventBox = TacticalUiFactory.CreateTextBox(doc.EventName);
            eventBox.TextChanged += (_, __) => doc.EventName = eventBox.Text;
            spEvent.Children.Add(eventBox);
            Grid.SetColumn(spEvent, 0); metaGrid.Children.Add(spEvent);

            // Wybór Strony Konfliktu (czysta lista z ProjectStore.Factions — bez PLI / GeoChat)
            var spFaction = new StackPanel();
            spFaction.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Docelowa Strona Konfliktu (z Generatora Stron):", "Target Faction (from Faction Generator):")));
            var cbFaction = new ComboBox { Height = 32 };
            foreach (var f in store.Factions)
            {
                cbFaction.Items.Add(f.Name);
            }
            cbFaction.SelectedItem = doc.TargetFaction;
            if (cbFaction.SelectedItem == null && cbFaction.Items.Count > 0)
            {
                cbFaction.SelectedIndex = 0;
            }
            cbFaction.SelectionChanged += (_, __) =>
            {
                if (cbFaction.SelectedItem != null)
                {
                    doc.TargetFaction = cbFaction.SelectedItem.ToString();
                    store.SyncGeneratorsToOpord();
                    RebuildView();
                }
            };
            spFaction.Children.Add(cbFaction);
            Grid.SetColumn(spFaction, 2); metaGrid.Children.Add(spFaction);

            // Przełącznik Klauzuli 🔓 JAWNE / 🔒 TAJNE
            var spClass = new StackPanel();
            spClass.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Klauzula Tajności Rozkazu:", "Security Classification:")));
            var cbClass = new ComboBox { Height = 32 };
            cbClass.Items.Add("🔓 JAWNE (UNCLASSIFIED)");
            cbClass.Items.Add("🔒 TAJNE — TYLKO DLA DOWÓDCÓW (SECRET)");
            cbClass.Items.Add("🔒 POUFNE — WEWNĄTRZ STRONY (CONFIDENTIAL)");
            cbClass.SelectedItem = activeDay.Classification;
            if (cbClass.SelectedItem == null) cbClass.SelectedIndex = 0;
            cbClass.SelectionChanged += (_, __) =>
            {
                if (cbClass.SelectedItem != null)
                    activeDay.Classification = cbClass.SelectedItem.ToString();
            };
            spClass.Children.Add(cbClass);
            Grid.SetColumn(spClass, 4); metaGrid.Children.Add(spClass);

            topStack.Children.Add(metaGrid);

            // Przyciski eksportu rozkazu (A4 HTML/PDF, Discord Markdown, Zapis do .FTAK)
            var exportWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            var btnPrintA4 = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("OPORD_PRINT_A4"),
                TacticalUiFactory.AccentCyanBrush,
                new SolidColorBrush(Color.FromRgb(5, 16, 20)));
            btnPrintA4.Click += (_, __) => OnExportA4Clicked();

            var btnDiscord = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("OPORD_COPY_DISCORD"),
                new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                TacticalUiFactory.AccentGoldBrush,
                TacticalUiFactory.AccentGoldBrush);
            btnDiscord.Click += (_, __) => OnExportDiscordClicked();

            var btnSaveFtak = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("OPORD_SAVE_FTAK"),
                new SolidColorBrush(Color.FromRgb(22, 110, 65)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush);
            btnSaveFtak.Click += (_, __) => OnSaveToFtakClicked();

            exportWrap.Children.Add(btnPrintA4);
            exportWrap.Children.Add(btnDiscord);
            exportWrap.Children.Add(btnSaveFtak);
            topStack.Children.Add(exportWrap);

            topCard.Child = topStack;
            _mainStack.Children.Add(topCard);

            // =========================================================================
            // 2. NIEZALEŻNE ZAKŁADKI DZIENNE (DZIEŃ 1, DZIEŃ 2...) + WYBÓR DNI DO DRUKU
            // =========================================================================
            var daysCard = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentGoldBrush);
            var daysStack = new StackPanel();

            var daysTopGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            daysTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            daysTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var daysTitle = TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("📅 ZAKŁADKI DZIENNE OPERACJI (100% NIEZALEŻNE PLANY DZIENNE)", "📅 OPERATION DAILY TABS (100% INDEPENDENT DAY PLANS)"),
                TacticalUiFactory.AccentGoldBrush, 13.5);
            Grid.SetColumn(daysTitle, 0); daysTopGrid.Children.Add(daysTitle);

            // Przyciski zakresu eksportu: "☑️ Generuj wszystkie dni" oraz naprawiony "☑️ Generuj tylko aktywny dzień"
            var scopeBtnsWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            var btnAllDays = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("OPORD_ALL_DAYS_BTN"),
                new SolidColorBrush(Color.FromRgb(16, 46, 58)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush,
                32,
                new Thickness(0, 0, 8, 0));
            btnAllDays.Click += (_, __) =>
            {
                foreach (var d in doc.Days) d.IncludeInExport = true;
                RebuildView();
            };

            var btnActiveDayOnly = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("OPORD_ACTIVE_DAY_BTN"),
                new SolidColorBrush(Color.FromRgb(16, 46, 58)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush,
                32,
                new Thickness(0));
            btnActiveDayOnly.Click += (_, __) =>
            {
                for (int i = 0; i < doc.Days.Count; i++)
                {
                    doc.Days[i].IncludeInExport = (i == doc.ActiveDayIndex);
                }
                RebuildView();
            };

            scopeBtnsWrap.Children.Add(btnAllDays);
            scopeBtnsWrap.Children.Add(btnActiveDayOnly);
            Grid.SetColumn(scopeBtnsWrap, 1); daysTopGrid.Children.Add(scopeBtnsWrap);
            daysStack.Children.Add(daysTopGrid);

            // Pasek zakładek dziennych z ptaszkiem [✓] do druku oraz przyciskiem "+ Dodaj dzień"
            var tabsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < doc.Days.Count; i++)
            {
                int capturedIdx = i;
                var dayItem = doc.Days[i];
                bool isCurrent = (i == doc.ActiveDayIndex);

                var tabBorder = new Border
                {
                    Background = isCurrent ? new SolidColorBrush(Color.FromRgb(22, 60, 74)) : new SolidColorBrush(Color.FromRgb(10, 22, 29)),
                    BorderBrush = isCurrent ? TacticalUiFactory.AccentCyanBrush : TacticalUiFactory.BorderCardBrush,
                    BorderThickness = new Thickness(isCurrent ? 2 : 1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 8, 6),
                    Cursor = Cursors.Hand
                };

                var tabPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var exportChk = new CheckBox
                {
                    IsChecked = dayItem.IncludeInExport,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = LocalizationManager.T("Uwzględnij ten dzień w wydruku A4 i eksporcie Discord", "Include this day in A4 print & Discord export")
                };
                exportChk.Checked += (_, __) => dayItem.IncludeInExport = true;
                exportChk.Unchecked += (_, __) => dayItem.IncludeInExport = false;
                tabPanel.Children.Add(exportChk);

                var tabLabel = new TextBlock
                {
                    Text = dayItem.DayTitle + " (" + dayItem.DateIso + ")",
                    Foreground = isCurrent ? TacticalUiFactory.AccentCyanBrush : TacticalUiFactory.TextPrimaryBrush,
                    FontWeight = isCurrent ? FontWeights.Bold : FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                tabLabel.MouseLeftButtonDown += (_, __) =>
                {
                    doc.ActiveDayIndex = capturedIdx;
                    RebuildView();
                };
                tabPanel.Children.Add(tabLabel);
                tabBorder.Child = tabPanel;
                tabsWrap.Children.Add(tabBorder);
            }

            var addDayBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.T("➕ Dodaj kolejny dzień", "➕ Add Next Day"),
                new SolidColorBrush(Color.FromRgb(22, 110, 65)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush,
                34,
                new Thickness(0, 0, 8, 6));
            addDayBtn.Click += (_, __) =>
            {
                var newDay = store.CreateDefaultOpordDay(doc.Days.Count + 1);
                doc.Days.Add(newDay);
                doc.ActiveDayIndex = doc.Days.Count - 1;
                store.SyncGeneratorsToOpord();
                RebuildView();
            };
            tabsWrap.Children.Add(addDayBtn);

            if (doc.Days.Count > 1)
            {
                var delDayBtn = TacticalUiFactory.CreateActionButton(
                    LocalizationManager.T("🗑️ Usuń aktywny dzień", "🗑️ Delete Active Day"),
                    new SolidColorBrush(Color.FromRgb(85, 22, 22)),
                    Brushes.White,
                    TacticalUiFactory.AccentRedBrush,
                    34,
                    new Thickness(0, 0, 0, 6));
                delDayBtn.Click += (_, __) =>
                {
                    doc.Days.RemoveAt(doc.ActiveDayIndex);
                    doc.ActiveDayIndex = Math.Clamp(doc.ActiveDayIndex, 0, doc.Days.Count - 1);
                    RebuildView();
                };
                tabsWrap.Children.Add(delDayBtn);
            }

            daysStack.Children.Add(tabsWrap);

            // Pola edycji nazwy aktywnego dnia, daty oraz dowództwa wydającego
            var dayMetaGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            dayMetaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dayMetaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            dayMetaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dayMetaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            dayMetaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dayMetaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            dayMetaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            var spDayTitle = new StackPanel();
            spDayTitle.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Tytuł zakładki dnia:", "Day Tab Title:")));
            var dayTitleBox = TacticalUiFactory.CreateTextBox(activeDay.DayTitle);
            dayTitleBox.TextChanged += (_, __) => activeDay.DayTitle = dayTitleBox.Text;
            spDayTitle.Children.Add(dayTitleBox);
            Grid.SetColumn(spDayTitle, 0); dayMetaGrid.Children.Add(spDayTitle);

            var spDate = new StackPanel();
            spDate.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Data operacji (RRRR-MM-DD):", "Operation Date (YYYY-MM-DD):")));
            var dateBox = TacticalUiFactory.CreateTextBox(activeDay.DateIso, false, 32, true);
            dateBox.TextChanged += (_, __) => activeDay.DateIso = dateBox.Text;
            spDate.Children.Add(dateBox);
            Grid.SetColumn(spDate, 2); dayMetaGrid.Children.Add(spDate);

            var spOpNum = new StackPanel();
            spOpNum.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Numer rozkazu (OPORD):", "Order Number (OPORD):")));
            var opNumBox = TacticalUiFactory.CreateTextBox(activeDay.OperationNumber);
            opNumBox.TextChanged += (_, __) => activeDay.OperationNumber = opNumBox.Text;
            spOpNum.Children.Add(opNumBox);
            Grid.SetColumn(spOpNum, 4); dayMetaGrid.Children.Add(spOpNum);

            var spHq = new StackPanel();
            spHq.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Dowództwo wydające (HQ):", "Issuing Headquarters (HQ):")));
            var hqBox = TacticalUiFactory.CreateTextBox(activeDay.IssuingHq);
            hqBox.TextChanged += (_, __) => activeDay.IssuingHq = hqBox.Text;
            spHq.Children.Add(hqBox);
            Grid.SetColumn(spHq, 6); dayMetaGrid.Children.Add(spHq);

            daysStack.Children.Add(dayMetaGrid);
            daysCard.Child = daysStack;
            _mainStack.Children.Add(daysCard);

            // =========================================================================
            // 3. SEKCJA I. SYTUACJA ORAZ MODUŁ POGODOWO-ASTRONOMICZNY B.2. METOC
            // =========================================================================
            var sec1Card = TacticalUiFactory.CreateCardBorder();
            var sec1Stack = new StackPanel();
            sec1Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("I. SYTUACJA (SIŁY PRZECIWNIKA, SIŁY WŁASNE, METOC, REJON AO)", "I. SITUATION (ENEMY FORCES, FRIENDLY FORCES, METOC, AO REGION)")));

            // Boks B.2. METOC z twardą walidacją AO / Miejscowość
            var metocBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 29)),
                BorderBrush = TacticalUiFactory.AccentCyanBrush,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var metocStack = new StackPanel();
            metocStack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("B.2. INTELIGENTNY MODUŁ POGODOWO-ASTRONOMICZNY METOC (BMNT / EENT / KSIĘŻYC NVG)", "B.2. INTELLIGENT METOC WEATHER & ASTRONOMICAL MODULE (BMNT / EENT / NVG MOON)"),
                TacticalUiFactory.AccentGoldBrush, 13));

            var metocInputsGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Jeśli nie zaznaczono AO i nie wpisano miejscowości -> wyczyść Lat/Lon
            if (!store.MapConfig.HasExplicitAoMarked && string.IsNullOrWhiteSpace(activeDay.LocationName))
            {
                activeDay.LocationLat = "";
                activeDay.LocationLon = "";
            }

            var spLocName = new StackPanel();
            spLocName.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Miejscowość (lub zaznacz AO w Generatorze Map 1:1):", "Location Name (or mark AO in 1:1 Map Generator):")));
            var locNameBox = TacticalUiFactory.CreateTextBox(activeDay.LocationName);
            locNameBox.TextChanged += (_, __) => activeDay.LocationName = locNameBox.Text;
            spLocName.Children.Add(locNameBox);
            Grid.SetColumn(spLocName, 0); metocInputsGrid.Children.Add(spLocName);

            var spMetocLat = new StackPanel();
            spMetocLat.Children.Add(TacticalUiFactory.CreateFieldLabel("AO Lat (°N):"));
            var metocLatBox = TacticalUiFactory.CreateTextBox(activeDay.LocationLat, false, 32, true);
            metocLatBox.IsReadOnly = true;
            spMetocLat.Children.Add(metocLatBox);
            Grid.SetColumn(spMetocLat, 2); metocInputsGrid.Children.Add(spMetocLat);

            var spMetocLon = new StackPanel();
            spMetocLon.Children.Add(TacticalUiFactory.CreateFieldLabel("AO Lon (°E):"));
            var metocLonBox = TacticalUiFactory.CreateTextBox(activeDay.LocationLon, false, 32, true);
            metocLonBox.IsReadOnly = true;
            spMetocLon.Children.Add(metocLonBox);
            Grid.SetColumn(spMetocLon, 4); metocInputsGrid.Children.Add(spMetocLon);

            var metocSummaryBlock = new TextBlock
            {
                Text = activeDay.MetocQuickSummary,
                Foreground = TacticalUiFactory.AccentGreenBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            TextBox terrainWeatherBox = null;

            var spFetchBtn = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            var fetchMetocBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("METOC_FETCH_BTN") + " (" + activeDay.DayTitle + ")",
                TacticalUiFactory.AccentCyanBrush,
                new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                TacticalUiFactory.AccentCyanBrush,
                32,
                new Thickness(0));
            fetchMetocBtn.Click += async (_, __) =>
            {
                fetchMetocBtn.IsEnabled = false;
                var res = await MetocWeatherService.FetchAndApplyMetocAsync(activeDay, store.MapConfig);
                fetchMetocBtn.IsEnabled = true;

                locNameBox.Text = activeDay.LocationName;
                metocLatBox.Text = activeDay.LocationLat;
                metocLonBox.Text = activeDay.LocationLon;

                if (!res.IsSuccess)
                {
                    MessageBox.Show(res.ErrorMessage, "METOC — Walidacja AO / Miejscowości", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                metocSummaryBlock.Text = activeDay.MetocQuickSummary;
                if (terrainWeatherBox != null)
                {
                    terrainWeatherBox.Text = activeDay.TerrainAndWeather;
                }
            };
            spFetchBtn.Children.Add(fetchMetocBtn);
            Grid.SetColumn(spFetchBtn, 6); metocInputsGrid.Children.Add(spFetchBtn);

            metocStack.Children.Add(metocInputsGrid);
            metocStack.Children.Add(metocSummaryBlock);
            metocBorder.Child = metocStack;
            sec1Stack.Children.Add(metocBorder);

            // Pola A. Siły Przeciwnika, B. Siły Własne, B.1. Teren i Pogoda, C. Punkty Taktyczne AO z Mapy
            sec1Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("A. Siły Przeciwnika (Skład, dyslokacja, prawdopodobny zamiar):", "A. Enemy Forces (Composition, disposition, most probable course of action):")));
            var enemyBox = TacticalUiFactory.CreateTextBox(activeDay.EnemyForces, true, 54);
            enemyBox.TextChanged += (_, __) => activeDay.EnemyForces = enemyBox.Text;
            enemyBox.Margin = new Thickness(0, 0, 0, 8);
            sec1Stack.Children.Add(enemyBox);

            sec1Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("B. Siły Własne (Struktura, sąsiedzi, wsparcie):", "B. Friendly Forces (Higher HQ mission, adjacent units, support):")));
            var friendlyBox = TacticalUiFactory.CreateTextBox(activeDay.FriendlyForces, true, 54);
            friendlyBox.TextChanged += (_, __) => activeDay.FriendlyForces = friendlyBox.Text;
            friendlyBox.Margin = new Thickness(0, 0, 0, 8);
            sec1Stack.Children.Add(friendlyBox);

            sec1Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("B.1. Teren i Pogoda (Raport METOC / OAKOC):", "B.1. Terrain and Weather (METOC / OAKOC Report):")));
            terrainWeatherBox = TacticalUiFactory.CreateTextBox(activeDay.TerrainAndWeather, true, 76, true);
            terrainWeatherBox.TextChanged += (_, __) => activeDay.TerrainAndWeather = terrainWeatherBox.Text;
            terrainWeatherBox.Margin = new Thickness(0, 0, 0, 8);
            sec1Stack.Children.Add(terrainWeatherBox);

            sec1Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("C. Obszar Operacji (AO) i Punkty Taktyczne POI (Zgodne z układem z Generatora Map 1:1):", "C. Area of Operations (AO) & Tactical POIs (Matching 1:1 Map Generator Coordinate System):")));
            var poiDescBox = TacticalUiFactory.CreateTextBox(activeDay.AreaOfInterestPoiDesc, true, 56, true);
            poiDescBox.TextChanged += (_, __) => activeDay.AreaOfInterestPoiDesc = poiDescBox.Text;
            sec1Stack.Children.Add(poiDescBox);

            sec1Card.Child = sec1Stack;
            _mainStack.Children.Add(sec1Card);

            // =========================================================================
            // 4. SEKCJA II. MISJA (5W) ORAZ III. WYKONANIE + TABELA D SKŁADU I RÓL
            // =========================================================================
            var sec23Card = TacticalUiFactory.CreateCardBorder();
            var sec23Stack = new StackPanel();

            sec23Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("II. MISJA (5W — KTO, CO, KIEDY, GDZIE, W JAKIM CELU)", "II. MISSION (5W — WHO, WHAT, WHEN, WHERE, WHY)")));
            var missionBox = TacticalUiFactory.CreateTextBox(activeDay.MissionFullStatement, true, 58);
            missionBox.TextChanged += (_, __) => activeDay.MissionFullStatement = missionBox.Text;
            missionBox.Margin = new Thickness(0, 0, 0, 14);
            sec23Stack.Children.Add(missionBox);

            sec23Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("III. WYKONANIE (ZAMIAR DOWÓDCY, HARMONOGRAM DNIA, TABELA D RÓL)", "III. EXECUTION (COMMANDER'S INTENT, TIMELINE, TABLE D SQUAD ROLES)")));

            sec23Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("A. Zamiar Dowódcy (Commander's Intent):", "A. Commander's Intent:")));
            var intentBox = TacticalUiFactory.CreateTextBox(activeDay.CommandersIntent, true, 52);
            intentBox.TextChanged += (_, __) => activeDay.CommandersIntent = intentBox.Text;
            intentBox.Margin = new Thickness(0, 0, 0, 8);
            sec23Stack.Children.Add(intentBox);

            var tasksCoordGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            tasksCoordGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tasksCoordGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            tasksCoordGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spTimeline = new StackPanel();
            spTimeline.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("B. Harmonogram i Zadania Dnia (" + activeDay.DayTitle + "):", "B. Daily Timeline & Tasks (" + activeDay.DayTitle + "):")));
            var timelineBox = TacticalUiFactory.CreateTextBox(activeDay.DaySpecificTasks, true, 78, true);
            timelineBox.TextChanged += (_, __) => activeDay.DaySpecificTasks = timelineBox.Text;
            spTimeline.Children.Add(timelineBox);
            Grid.SetColumn(spTimeline, 0); tasksCoordGrid.Children.Add(spTimeline);

            var spCoord = new StackPanel();
            spCoord.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("C. Wytyczne Koordynacyjne i Dodatkowe Zadania:", "C. Coordinating Instructions & Additional Tasks:")));
            var coordBox = TacticalUiFactory.CreateTextBox(activeDay.CoordinationMeasures + "\n" + activeDay.AdditionalTasksSummary, true, 78);
            coordBox.TextChanged += (_, __) => activeDay.CoordinationMeasures = coordBox.Text;
            spCoord.Children.Add(coordBox);
            Grid.SetColumn(spCoord, 2); tasksCoordGrid.Children.Add(spCoord);

            sec23Stack.Children.Add(tasksCoordGrid);

            // Tabela D: Skład Oddziału i Przydział Ról MilSimowych
            sec23Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("D. TABELA SKŁADU ODDZIAŁU I PRZYDZIAŁU RÓL MILSIMOWYCH (" + activeDay.DayTitle + ")", "D. SQUAD ROSTER & MILSIM ROLE ASSIGNMENT TABLE (" + activeDay.DayTitle + ")"),
                TacticalUiFactory.AccentGoldBrush, 13));

            var rosterContainer = new StackPanel();
            var roleNames = TacticalServices.GetMilsimRolesCatalog().Select(r => r.Item1).ToList();

            void RebuildRosterRows()
            {
                rosterContainer.Children.Clear();
                foreach (var member in activeDay.SquadRoster.ToList())
                {
                    var rGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var tCall = TacticalUiFactory.CreateTextBox(member.Callsign, false, 30, true);
                    tCall.TextChanged += (_, __) => member.Callsign = tCall.Text;
                    Grid.SetColumn(tCall, 0); rGrid.Children.Add(tCall);

                    var cbPrim = new ComboBox { Height = 30, Margin = new Thickness(6, 0, 0, 0), IsEditable = true };
                    foreach (string rn in roleNames) cbPrim.Items.Add(rn);
                    cbPrim.Text = member.PrimaryRole;
                    cbPrim.SelectionChanged += (_, __) => { if (cbPrim.SelectedItem != null) member.PrimaryRole = cbPrim.SelectedItem.ToString(); };
                    Grid.SetColumn(cbPrim, 1); rGrid.Children.Add(cbPrim);

                    var cbSec = new ComboBox { Height = 30, Margin = new Thickness(6, 0, 0, 0), IsEditable = true };
                    foreach (string rn in roleNames) cbSec.Items.Add(rn);
                    cbSec.Text = member.SecondaryRole;
                    cbSec.SelectionChanged += (_, __) => { if (cbSec.SelectedItem != null) member.SecondaryRole = cbSec.SelectedItem.ToString(); };
                    Grid.SetColumn(cbSec, 2); rGrid.Children.Add(cbSec);

                    var tGear = TacticalUiFactory.CreateTextBox(member.GearAndTasks, false, 30);
                    tGear.Margin = new Thickness(6, 0, 0, 0);
                    tGear.TextChanged += (_, __) => member.GearAndTasks = tGear.Text;
                    Grid.SetColumn(tGear, 3); rGrid.Children.Add(tGear);

                    var delMemberBtn = TacticalUiFactory.CreateCompactIconBtn("−", new SolidColorBrush(Color.FromRgb(85, 22, 22)), TacticalUiFactory.AccentRedBrush, 32, 30);
                    delMemberBtn.Click += (_, __) =>
                    {
                        activeDay.SquadRoster.Remove(member);
                        RebuildRosterRows();
                    };
                    Grid.SetColumn(delMemberBtn, 4); rGrid.Children.Add(delMemberBtn);

                    rosterContainer.Children.Add(rGrid);
                }
            }

            RebuildRosterRows();
            sec23Stack.Children.Add(rosterContainer);

            var addMemberBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.T("➕ Dodaj operatora do składu", "➕ Add Squad Member"),
                new SolidColorBrush(Color.FromRgb(22, 130, 75)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush,
                32,
                new Thickness(0, 4, 0, 0));
            addMemberBtn.Click += (_, __) =>
            {
                int nextIdx = activeDay.SquadRoster.Count + 1;
                activeDay.SquadRoster.Add(new SquadMemberRoleModel
                {
                    Callsign = "ALFA-" + nextIdx,
                    PrimaryRole = roleNames[nextIdx % roleNames.Count],
                    SecondaryRole = roleNames[(nextIdx + 1) % roleNames.Count],
                    GearAndTasks = "Replika + Radio PMR/VHF + ATAK"
                });
                RebuildRosterRows();
            };
            sec23Stack.Children.Add(addMemberBtn);

            sec23Card.Child = sec23Stack;
            _mainStack.Children.Add(sec23Card);

            // Punkt zaczepienia dla Części B (Sekcja V: Tabela E Łączności + Krypto SARNEG, Sekcja IV: Logistyka i Załącznik 3 Mapa)
            BuildSectionsPartB(_mainStack, doc, activeDay);
        }
    }
}
