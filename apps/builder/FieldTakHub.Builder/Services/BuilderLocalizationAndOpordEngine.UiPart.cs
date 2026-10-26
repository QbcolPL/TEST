#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FieldTakHub.Builder.Services
{
    internal static partial class OpordBuilderManager
    {
        // =========================================================================
        // 6. WIDOK GENERATORA OPORD v3.2 (NIEZALEŻNE DNI, WYBÓR DNI DO GENEROWANIA, JEDEN PRZYCISK SARNEG, PEŁNE PL/ENG)
        // =========================================================================
        public static Border CreateOrGetOpordOverlayView(Window win)
        {
            _lastActiveWindowRef = win;
            if (_cachedOpordOverlay != null)
            {
                Grid.SetColumn(_cachedOpordOverlay, 1);
                Grid.SetColumnSpan(_cachedOpordOverlay, 1);
                return _cachedOpordOverlay;
            }

            var rootBorder = new Border
            {
                Tag = "INJECTED_OPORD_OVERLAY",
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Padding = new Thickness(18, 14, 18, 18),
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(rootBorder, 1);
            Grid.SetColumnSpan(rootBorder, 1);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var mainStack = new StackPanel();
            scroll.Content = mainStack;
            rootBorder.Child = scroll;

            void RebuildOpordForm()
            {
                mainStack.Children.Clear();
                var configuredFactions = TacticalConfigPanel.GetConfiguredFactions();
                if (configuredFactions.Count > 0 && !configuredFactions.Any(f => f.Name.Equals(_activeOpordFaction, StringComparison.OrdinalIgnoreCase)))
                {
                    _activeOpordFaction = configuredFactions[0].Name;
                }

                var model = GetOrCreateModel(_activeOpordFaction);
                model.ActiveDayIndex = Math.Clamp(model.ActiveDayIndex, 0, Math.Max(0, model.Days.Count - 1));
                var activeDay = model.Days[model.ActiveDayIndex];

                string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);
                SyncModelRadioChannelsFromFactionGenerator(win, _activeOpordFaction, model);

                // PASEK NAGŁÓWKOWY GENERATORA OPORD v3.2
                var topCard = new Border
                {
                    Background = TacticalConfigPanel.BrushCardBg,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var topStack = new StackPanel();
                topStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "📋 GENERATOR ROZKAZU BOJOWEGO MILSIM / NATO (OPORD v3.2 + SARNEG + METOC) — " + eventName,
                        "📋 MILSIM / NATO OPORD v3.2 BUILDER (SARNEG + METOC + SQUAD ROSTER) — " + eventName),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14.5,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                topStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "Wzór drużynowy MilSim i NATO: przełącznik JAWNE / TAJNE, niezależne zakładki dzienne (+ / -) z wyborem dni do wygenerowania, inteligentny moduł pogodowy METOC dla każdego dnia osobno, Tabela Składu (+ / -), generator słów SARNEG oraz opcjonalny Załącznik 1 Ról.",
                        "Complete MilSim & NATO OPORD suite: UNCLASSIFIED / SECRET toggle, independent daily tabs (+ / -) with day selection for export, per-day smart METOC weather, Squad Roster (+ / -), SARNEG word generator, and optional editable Roles Annex."),
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var actionBar = new Grid();
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });

                var facCombo = new ComboBox
                {
                    Height = 32,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = "INJECTED"
                };
                TacticalConfigPanel.ApplyUnifiedComboBoxTheme(facCombo, win);
                foreach (var cf in configuredFactions)
                    facCombo.Items.Add(cf.Name);
                if (facCombo.Items.Count == 0) facCombo.Items.Add("BLUFOR");
                facCombo.SelectedItem = _activeOpordFaction;
                facCombo.SelectionChanged += (_, __) =>
                {
                    if (facCombo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s) && s != _activeOpordFaction)
                    {
                        _activeOpordFaction = s;
                        RebuildOpordForm();
                    }
                };
                Grid.SetColumn(facCombo, 0);
                actionBar.Children.Add(facCombo);

                var btnExportHtml = new Button
                {
                    Content = BuilderI18n.T("🖨️ Generuj OPORD A4 (Wybrane dni)", "🖨️ Generate A4 OPORD (Selected Days)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = TacticalConfigPanel.BrushButtonAccent,
                    Foreground = TacticalConfigPanel.BrushDarkFg,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnExportHtml.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    string html = GeneratePrintableOpordHtml(win, _activeOpordFaction, model);
                    string outPath = Path.Combine(Path.GetTempPath(), "OPORD_" + TacticalConfigPanel.SanitizeFileName(eventName) + "_" + TacticalConfigPanel.SanitizeFileName(_activeOpordFaction) + ".html");
                    File.WriteAllText(outPath, html, Encoding.UTF8);
                    try { Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true }); } catch { }
                };
                Grid.SetColumn(btnExportHtml, 1);
                actionBar.Children.Add(btnExportHtml);

                var btnShareLanQr = new Button
                {
                    Content = BuilderI18n.T("📡 Udostępnij OPORD (Wi-Fi / LAN QR)", "📡 Share OPORD (Wi-Fi / LAN QR)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.4),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnShareLanQr.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    ShowOpordLanDistributionModal(win, _activeOpordFaction, model);
                };
                Grid.SetColumn(btnShareLanQr, 2);
                actionBar.Children.Add(btnShareLanQr);

                var btnCopyDiscord = new Button
                {
                    Content = BuilderI18n.T("💬 Kopiuj na Discorda (Wybrane dni)", "💬 Copy for Discord (Selected Days)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = TacticalConfigPanel.BrushItemBg,
                    Foreground = Brushes.White,
                    BorderBrush = TacticalConfigPanel.BrushCardBorder,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnCopyDiscord.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    string md = GenerateDiscordMarkdownOpord(win, _activeOpordFaction, model);
                    Clipboard.SetText(md);
                    MessageBox.Show(
                        win,
                        BuilderI18n.T(
                            "Skopiowano rozkaz OPORD (dla zaznaczonych dni) strony " + _activeOpordFaction + " do schowka! Możesz wkleić go na Discordzie (Ctrl+V).",
                            "Copied OPORD (for selected days) of " + _activeOpordFaction + " to clipboard!"),
                        "OPORD -> Discord Markdown",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };
                Grid.SetColumn(btnCopyDiscord, 3);
                actionBar.Children.Add(btnCopyDiscord);

                var btnSaveToFtak = new Button
                {
                    Content = BuilderI18n.T("📦 Zapisz w .FTAK", "📦 Save to .FTAK"),
                    Height = 32,
                    Background = TacticalConfigPanel.BrushItemBg,
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnSaveToFtak.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    MessageBox.Show(
                        win,
                        BuilderI18n.T(
                            "Zapisano rozkaz OPORD_" + _activeOpordFaction + " w katalogu wyjściowym frakcji!",
                            "Saved OPORD_" + _activeOpordFaction + " to the faction output folder!"),
                        BuilderI18n.T("Zapisano OPORD", "OPORD Saved"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };
                Grid.SetColumn(btnSaveToFtak, 4);
                actionBar.Children.Add(btnSaveToFtak);

                topStack.Children.Add(actionBar);
                topCard.Child = topStack;
                mainStack.Children.Add(topCard);

                // =================================================================
                // PASEK NIEZALEŻNYCH ZAKŁADEK DZIENNYCH + WYBÓR DNI DO GENEROWANIA OPORD
                // =================================================================
                var daysBarBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(28, 62, 74)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var daysOuterStack = new StackPanel();
                var daysTopRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

                daysTopRow.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "📅 NIEZALEŻNE DNI OPERACJI (Zaznacz [✓] dni, które mają zostać ujęte przy generowaniu OPORD):",
                        "📅 INDEPENDENT OPERATION DAYS (Check [✓] days to include when generating OPORD):"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var quickSelectWrap = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(quickSelectWrap, Dock.Right);

                var btnSelectAllDays = new Button
                {
                    Content = BuilderI18n.T("☑️ Generuj wszystkie dni", "☑️ Export All Days"),
                    Height = 24,
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromRgb(16, 42, 52)),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand
                };
                btnSelectAllDays.Click += (_, __) =>
                {
                    foreach (var d in model.Days) d.IncludeInExport = true;
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                };
                quickSelectWrap.Children.Add(btnSelectAllDays);

                var btnSelectOnlyActiveDay = new Button
                {
                    Content = BuilderI18n.T("1️⃣ Tylko aktywny dzień", "1️⃣ Only Active Day"),
                    Height = 24,
                    Padding = new Thickness(8, 1, 8, 1),
                    Background = new SolidColorBrush(Color.FromRgb(16, 42, 52)),
                    Foreground = Brushes.White,
                    BorderBrush = TacticalConfigPanel.BrushInputBorder,
                    BorderThickness = new Thickness(1),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand
                };
                btnSelectOnlyActiveDay.Click += (_, __) =>
                {
                    for (int i = 0; i < model.Days.Count; i++)
                        model.Days[i].IncludeInExport = (i == model.ActiveDayIndex);
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                };
                quickSelectWrap.Children.Add(btnSelectOnlyActiveDay);
                daysTopRow.Children.Add(quickSelectWrap);
                daysOuterStack.Children.Add(daysTopRow);

                var daysWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                for (int dIdx = 0; dIdx < model.Days.Count; dIdx++)
                {
                    int capturedIdx = dIdx;
                    var dayObj = model.Days[dIdx];
                    bool isSel = (dIdx == model.ActiveDayIndex);

                    var tabChipBorder = new Border
                    {
                        Background = isSel ? new SolidColorBrush(Color.FromRgb(18, 60, 72)) : TacticalConfigPanel.BrushInputBg,
                        BorderBrush = isSel ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder,
                        BorderThickness = new Thickness(isSel ? 1.8 : 1.0),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6, 2, 4, 2),
                        Margin = new Thickness(0, 0, 8, 4)
                    };

                    var chipRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                    var exportCheck = new CheckBox
                    {
                        IsChecked = dayObj.IncludeInExport,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 5, 0),
                        ToolTip = BuilderI18n.T(
                            "Uwzględnij '" + dayObj.DayTitle + "' przy generowaniu dokumentu OPORD",
                            "Include '" + dayObj.DayTitle + "' when generating OPORD document"),
                        Cursor = Cursors.Hand
                    };
                    exportCheck.Checked += (_, __) => { dayObj.IncludeInExport = true; PersistModelToAppData(model); };
                    exportCheck.Unchecked += (_, __) => { dayObj.IncludeInExport = false; PersistModelToAppData(model); };
                    chipRow.Children.Add(exportCheck);

                    var dayTabBtn = new Button
                    {
                        Content = "📆 " + dayObj.DayTitle + " (" + dayObj.DateIso + ")",
                        Height = 26,
                        Padding = new Thickness(8, 1, 8, 1),
                        Background = isSel ? TacticalConfigPanel.BrushButtonAccent : Brushes.Transparent,
                        Foreground = isSel ? TacticalConfigPanel.BrushDarkFg : Brushes.White,
                        BorderThickness = new Thickness(0),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        Cursor = Cursors.Hand
                    };
                    dayTabBtn.Click += (_, __) =>
                    {
                        model.ActiveDayIndex = capturedIdx;
                        RebuildOpordForm();
                    };
                    chipRow.Children.Add(dayTabBtn);

                    tabChipBorder.Child = chipRow;
                    daysWrap.Children.Add(tabChipBorder);
                }

                var btnAddDayPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj kolejny niezależny dzień operacji (+)", "Add next independent operation day (+)"), () =>
                {
                    DateTime lastDt = DateTime.Today;
                    string prevLocName = activeDay.LocationName ?? "";
                    string prevLat = string.IsNullOrWhiteSpace(activeDay.LocationLat) ? "49.8494" : activeDay.LocationLat;
                    string prevLon = string.IsNullOrWhiteSpace(activeDay.LocationLon) ? "18.8127" : activeDay.LocationLon;

                    if (model.Days.Count > 0 && DateTime.TryParseExact(model.Days.Last().DateIso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                    {
                        lastDt = parsed.AddDays(1);
                    }
                    int nextNum = model.Days.Count + 1;
                    model.Days.Add(new OpordDayPlanModel
                    {
                        DayTitle = BuilderI18n.T("Dzień " + nextNum, "Day " + nextNum),
                        IncludeInExport = true,
                        DateIso = lastDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        LocationName = prevLocName,
                        LocationLat = prevLat,
                        LocationLon = prevLon,
                        TerrainAndWeather = BuilderI18n.T(
                            "Analiza terenu i pogody dla Dnia " + nextNum + " (kliknij przycisk METOC poniżej, aby pobrać niezależną prognozę na ten dzień).",
                            "Terrain & weather analysis for Day " + nextNum + " (click METOC button below to fetch independent forecast for this day)."),
                        MetocCondition = BuilderI18n.T("Brak pobranego raportu dla Dnia " + nextNum, "No METOC report fetched for Day " + nextNum),
                        DaySpecificTasks = BuilderI18n.T("• Zadania i harmonogram dla Dnia " + nextNum + "...", "• Tasks and schedule for Day " + nextNum + "...")
                    });
                    model.ActiveDayIndex = model.Days.Count - 1;
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                daysWrap.Children.Add(btnAddDayPlus);

                var btnRemoveDayMinus = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń aktywny dzień operacji (-)", "Remove active operation day (-)"), () =>
                {
                    if (model.Days.Count <= 1) return;
                    model.Days.RemoveAt(model.ActiveDayIndex);
                    for (int i = 0; i < model.Days.Count; i++)
                    {
                        model.Days[i].DayTitle = BuilderI18n.T("Dzień " + (i + 1), "Day " + (i + 1));
                    }
                    model.ActiveDayIndex = Math.Clamp(model.ActiveDayIndex - 1, 0, model.Days.Count - 1);
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                daysWrap.Children.Add(btnRemoveDayMinus);

                daysOuterStack.Children.Add(daysWrap);
                daysBarBorder.Child = daysOuterStack;
                mainStack.Children.Add(daysBarBorder);

                // =================================================================
                // 0. NAGŁÓWEK ROZKAZU + PRZEŁĄCZNIK KLAUZULI "JAWNE / TAJNE"
                // =================================================================
                var metaCard = CreateSectionCard(BuilderI18n.T(
                    "0. NAGŁÓWEK ROZKAZU, KLAUZULA TAJNOŚCI (JAWNE / TAJNE) ORAZ HARMONOGRAM: " + activeDay.DayTitle.ToUpperInvariant(),
                    "0. ORDER HEADER, CLASSIFICATION (UNCLASSIFIED / SECRET) & SCHEDULE: " + activeDay.DayTitle.ToUpperInvariant()));
                var metaStack = (StackPanel)metaCard.Child;

                var classToggleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                classToggleRow.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T("Klauzula dokumentu OPORD:", "OPORD Security Classification:"),
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });

                TextBox classificationTextBoxRef = null;
                var btnJawne = new Button
                {
                    Content = BuilderI18n.T("🔓 JAWNE (UNCLASSIFIED)", "🔓 UNCLASSIFIED (JAWNE)"),
                    Height = 28,
                    Padding = new Thickness(14, 2, 14, 2),
                    Margin = new Thickness(0, 0, 6, 0),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                var btnTajne = new Button
                {
                    Content = BuilderI18n.T("🔒 TAJNE (SECRET — TYLKO DLA STRONY)", "🔒 SECRET (TAJNE — FACTION EYES ONLY)"),
                    Height = 28,
                    Padding = new Thickness(14, 2, 14, 2),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };

                void UpdateSecretToggleVisuals()
                {
                    bool isSec = model.IsSecretClassification || model.Classification.Contains("TAJNE", StringComparison.OrdinalIgnoreCase) || model.Classification.Contains("SECRET", StringComparison.OrdinalIgnoreCase);
                    model.IsSecretClassification = isSec;

                    btnJawne.Background = !isSec ? new SolidColorBrush(Color.FromRgb(22, 92, 54)) : TacticalConfigPanel.BrushInputBg;
                    btnJawne.Foreground = Brushes.White;
                    btnJawne.BorderBrush = !isSec ? new SolidColorBrush(Color.FromRgb(46, 204, 113)) : TacticalConfigPanel.BrushInputBorder;
                    btnJawne.BorderThickness = new Thickness(1.5);

                    btnTajne.Background = isSec ? new SolidColorBrush(Color.FromRgb(115, 24, 24)) : TacticalConfigPanel.BrushInputBg;
                    btnTajne.Foreground = Brushes.White;
                    btnTajne.BorderBrush = isSec ? new SolidColorBrush(Color.FromRgb(235, 64, 52)) : TacticalConfigPanel.BrushInputBorder;
                    btnTajne.BorderThickness = new Thickness(1.5);
                }

                btnJawne.Click += (_, __) =>
                {
                    model.IsSecretClassification = false;
                    model.Classification = BuilderI18n.T(
                        "JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD",
                        "UNCLASSIFIED / JAWNE — MILSIM SQUAD OPORD");
                    if (classificationTextBoxRef != null) classificationTextBoxRef.Text = model.Classification;
                    UpdateSecretToggleVisuals();
                    PersistModelToAppData(model);
                };

                btnTajne.Click += (_, __) =>
                {
                    model.IsSecretClassification = true;
                    model.Classification = BuilderI18n.T(
                        "TAJNE / SECRET — TYLKO DO WIADOMOŚCI STRONY " + _activeOpordFaction.ToUpperInvariant(),
                        "SECRET / TAJNE — " + _activeOpordFaction.ToUpperInvariant() + " EYES ONLY");
                    if (classificationTextBoxRef != null) classificationTextBoxRef.Text = model.Classification;
                    UpdateSecretToggleVisuals();
                    PersistModelToAppData(model);
                };

                var toggleBtnsWrap = new StackPanel { Orientation = Orientation.Horizontal };
                toggleBtnsWrap.Children.Add(btnJawne);
                toggleBtnsWrap.Children.Add(btnTajne);
                classToggleRow.Children.Add(toggleBtnsWrap);
                metaStack.Children.Add(classToggleRow);

                var metaGrid = new UniformGrid { Columns = 4 };
                var classBoxPanel = CreateLabeledTextBox(BuilderI18n.T("Nagłówek Klasyfikacji", "Classification Banner"), model.Classification, v => { model.Classification = v; UpdateSecretToggleVisuals(); PersistModelToAppData(model); });
                classificationTextBoxRef = FindFirstDescendant<TextBox>(classBoxPanel);
                UpdateSecretToggleVisuals();

                metaGrid.Children.Add(classBoxPanel);
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Nr Rozkazu / Strzelanki", "Order / Event No."), model.OperationNumber, v => { model.OperationNumber = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Grupa Data-Czas (DTG)", "Date-Time Group (DTG)"), model.DtgIssued, v => { model.DtgIssued = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Sztab wydający (HQ)", "Issuing HQ"), model.IssuingHq, v => { model.IssuingHq = v; PersistModelToAppData(model); }));
                metaStack.Children.Add(metaGrid);

                metaStack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T(
                        "Harmonogram i zadania specyficzne dla wybranego dnia (" + activeDay.DayTitle + " — " + activeDay.DateIso + "):",
                        "Schedule & specific tasks for selected day (" + activeDay.DayTitle + " — " + activeDay.DateIso + "):"),
                    activeDay.DaySpecificTasks, 54, v => { activeDay.DaySpecificTasks = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(metaCard);

                // =================================================================
                // I. SYTUACJA (NIEZALEŻNE DANE METOC I TERENU DLA KAŻDEGO DNIA!)
                // =================================================================
                var sec1 = CreateSectionCard(BuilderI18n.T(
                    "I. SYTUACJA — " + activeDay.DayTitle.ToUpperInvariant() + " (OBSZAR ZAINTERESOWANIA, TEREN, NIEZALEŻNY METOC DNIA, WRÓG, SOJUSZNICY)",
                    "I. SITUATION — " + activeDay.DayTitle.ToUpperInvariant() + " (AOI, TERRAIN, INDEPENDENT DAY METOC, ENEMY, FRIENDLY FORCES)"));
                var sec1Stack = (StackPanel)sec1.Child;

                var aoiBoxPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Obszar zainteresowania (1. Mapa terenu gry, 2. Opis punktów na mapie POI, 3. Pliki punktów KML / GPX):", "A. Area of Interest (1. Game Map, 2. Map Points / POI description, 3. KML / GPX files):"),
                    model.AreaOfInterestPoiDesc, 50, v => { model.AreaOfInterestPoiDesc = v; PersistModelToAppData(model); });
                var aoiTextBox = FindFirstDescendant<TextBox>(aoiBoxPanel);
                sec1Stack.Children.Add(aoiBoxPanel);

                var metocBar = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(12, 31, 38)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 4, 0, 8)
                };
                var metocStack = new StackPanel();
                metocStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "🌦️ B.2. PROGNOZA POGODY, SŁOŃCE I FAZA KSIĘŻYCA DLA: " + activeDay.DayTitle.ToUpperInvariant() + " (" + activeDay.DateIso + ")",
                        "🌦️ B.2. WEATHER, SUNRISE/SUNSET & MOON PHASE FOR: " + activeDay.DayTitle.ToUpperInvariant() + " (" + activeDay.DateIso + ")"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var metocInputsGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

                var dateBoxPanel = CreateLabeledTextBox(
                    BuilderI18n.T("Data (" + activeDay.DayTitle + ")", "Date (" + activeDay.DayTitle + ")"),
                    activeDay.DateIso,
                    v =>
                    {
                        activeDay.DateIso = v;
                        PersistModelToAppData(model);
                    });
                Grid.SetColumn(dateBoxPanel, 0);
                metocInputsGrid.Children.Add(dateBoxPanel);

                var cityBoxPanel = CreateLabeledTextBox(
                    BuilderI18n.T("Miejscowość (" + activeDay.DayTitle + " — puste = AO z Mapy)", "Town (" + activeDay.DayTitle + " — blank = Map AO)"),
                    activeDay.LocationName,
                    v => { activeDay.LocationName = v; PersistModelToAppData(model); });
                var cityTextBox = FindFirstDescendant<TextBox>(cityBoxPanel);
                Grid.SetColumn(cityBoxPanel, 1);
                metocInputsGrid.Children.Add(cityBoxPanel);

                var latBoxPanel = CreateLabeledTextBox("Lat (°N)", activeDay.LocationLat, v => { activeDay.LocationLat = v; PersistModelToAppData(model); });
                var latTextBox = FindFirstDescendant<TextBox>(latBoxPanel);
                Grid.SetColumn(latBoxPanel, 2);
                metocInputsGrid.Children.Add(latBoxPanel);

                var lonBoxPanel = CreateLabeledTextBox("Lon (°E)", activeDay.LocationLon, v => { activeDay.LocationLon = v; PersistModelToAppData(model); });
                var lonTextBox = FindFirstDescendant<TextBox>(lonBoxPanel);
                Grid.SetColumn(lonBoxPanel, 3);
                metocInputsGrid.Children.Add(lonBoxPanel);

                TextBox terrainWeatherTextBox = null;
                var metocStatusText = new TextBlock
                {
                    Text = BuilderI18n.T(
                        "☀️ Słońce (" + activeDay.DayTitle + "): " + activeDay.MetocSunTimes + "   |   🌙 Księżyc: " + activeDay.MetocMoonPhase + "\n🌡️ Pogoda: " + activeDay.MetocCondition + "   |   💨 Wiatr: " + activeDay.MetocWind,
                        "☀️ Sun (" + activeDay.DayTitle + "): " + activeDay.MetocSunTimes + "   |   🌙 Moon: " + activeDay.MetocMoonPhase + "\n🌡️ Weather: " + activeDay.MetocCondition + "   |   💨 Wind: " + activeDay.MetocWind),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                var btnFetchUnifiedMetoc = new Button
                {
                    Content = BuilderI18n.T("🌦️ Pobierz METOC dla " + activeDay.DayTitle, "🌦️ Fetch METOC for " + activeDay.DayTitle),
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = TacticalConfigPanel.BrushButtonAccent,
                    Foreground = TacticalConfigPanel.BrushDarkFg,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };

                btnFetchUnifiedMetoc.Click += async (_, __) =>
                {
                    btnFetchUnifiedMetoc.IsEnabled = false;
                    try
                    {
                        double lat = 49.8494;
                        double lon = 18.8127;
                        double.TryParse((activeDay.LocationLat ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out lat);
                        double.TryParse((activeDay.LocationLon ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out lon);

                        string trimmedCity = (activeDay.LocationName ?? "").Trim();
                        bool isAutoMapLabel =
                            string.IsNullOrWhiteSpace(trimmedCity) ||
                            trimmedCity.StartsWith("Obszar AO z", StringComparison.OrdinalIgnoreCase) ||
                            trimmedCity.StartsWith("Map AO", StringComparison.OrdinalIgnoreCase);

                        if (!isAutoMapLabel)
                        {
                            var geo = await OpordMetocWeatherService.GeocodeLocationAsync(trimmedCity);
                            if (geo.Ok)
                            {
                                lat = geo.Lat;
                                lon = geo.Lon;
                                activeDay.LocationLat = lat.ToString("0.0000", CultureInfo.InvariantCulture);
                                activeDay.LocationLon = lon.ToString("0.0000", CultureInfo.InvariantCulture);
                                activeDay.LocationName = geo.DisplayName;
                                if (latTextBox != null) latTextBox.Text = activeDay.LocationLat;
                                if (lonTextBox != null) lonTextBox.Text = activeDay.LocationLon;
                                if (cityTextBox != null) cityTextBox.Text = activeDay.LocationName;
                            }
                        }
                        else
                        {
                            var aoRes = OpordMetocWeatherService.TryExtractAoCenterFromMapStudio(win);
                            if (aoRes.Found)
                            {
                                lat = aoRes.Lat;
                                lon = aoRes.Lon;
                                activeDay.LocationLat = lat.ToString("0.0000", CultureInfo.InvariantCulture);
                                activeDay.LocationLon = lon.ToString("0.0000", CultureInfo.InvariantCulture);
                                activeDay.LocationName = aoRes.SourceDescription;
                                if (latTextBox != null) latTextBox.Text = activeDay.LocationLat;
                                if (lonTextBox != null) lonTextBox.Text = activeDay.LocationLon;
                                if (cityTextBox != null) cityTextBox.Text = activeDay.LocationName;

                                if (!string.IsNullOrWhiteSpace(aoRes.ExtractedPoiList) && aoiTextBox != null && !model.AreaOfInterestPoiDesc.Contains(aoRes.ExtractedPoiList))
                                {
                                    model.AreaOfInterestPoiDesc = model.AreaOfInterestPoiDesc.TrimEnd() + "\n" + aoRes.ExtractedPoiList;
                                    aoiTextBox.Text = model.AreaOfInterestPoiDesc;
                                }
                            }
                        }

                        var rep = await OpordMetocWeatherService.FetchMetocReportAsync(lat, lon, activeDay.DateIso, activeDay.LocationName);
                        activeDay.MetocCondition = rep.ConditionText;
                        activeDay.MetocTemperature = rep.TempRangeText;
                        activeDay.MetocWind = rep.WindText;
                        activeDay.MetocHumidityPrecip = rep.HumidityAndPrecipText;
                        activeDay.MetocSunTimes = rep.SunTimesText;
                        activeDay.MetocMoonPhase = rep.MoonPhaseText;

                        metocStatusText.Text =
                            "☀️ " + rep.SunTimesText + "\n" +
                            "🌙 " + rep.MoonPhaseText + "\n" +
                            "🌡️ Temp: " + rep.TempRangeText + "  |  " + rep.HumidityAndPrecipText + "\n" +
                            "💨 " + rep.WindText;

                        string currentDayTerrain = (activeDay.TerrainAndWeather ?? "").Trim();
                        int oldIdx = currentDayTerrain.IndexOf("🌦️ [", StringComparison.Ordinal);
                        if (oldIdx >= 0)
                        {
                            currentDayTerrain = currentDayTerrain.Substring(0, oldIdx).TrimEnd();
                        }
                        activeDay.TerrainAndWeather = string.IsNullOrWhiteSpace(currentDayTerrain)
                            ? rep.FullTacticalSummary
                            : (currentDayTerrain + "\n\n" + rep.FullTacticalSummary);

                        if (terrainWeatherTextBox != null)
                        {
                            terrainWeatherTextBox.Text = activeDay.TerrainAndWeather;
                        }
                        PersistModelToAppData(model);
                    }
                    finally
                    {
                        btnFetchUnifiedMetoc.IsEnabled = true;
                    }
                };
                Grid.SetColumn(btnFetchUnifiedMetoc, 4);
                metocInputsGrid.Children.Add(btnFetchUnifiedMetoc);

                metocStack.Children.Add(metocInputsGrid);
                metocStack.Children.Add(metocStatusText);
                metocBar.Child = metocStack;
                sec1Stack.Children.Add(metocBar);

                var terrainBoxPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T(
                        "B.1 & B.2. Obszar działań (" + activeDay.DayTitle + ") — Analiza terenu i pogoda METOC dla tego dnia:",
                        "B.1 & B.2. Area of Operations (" + activeDay.DayTitle + ") — Terrain analysis & METOC weather for this day:"),
                    activeDay.TerrainAndWeather, 82, v => { activeDay.TerrainAndWeather = v; PersistModelToAppData(model); });
                terrainWeatherTextBox = FindFirstDescendant<TextBox>(terrainBoxPanel);
                sec1Stack.Children.Add(terrainBoxPanel);

                sec1Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Siły wroga (1. Skład, dyspozycja i siła, 2. Lokalizacje i możliwości, 3. Kursy działań wroga — COA):", "C. Enemy Forces (1. Composition & strength, 2. Locations & capabilities, 3. Courses of Action — COA):"),
                    model.EnemyForces, 60, v => { model.EnemyForces = v; PersistModelToAppData(model); }));

                sec1Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("D. Siły przyjazne (1. Misja i zamiar wyższego dowództwa HQ, 2. Misja sąsiednich jednostek):", "D. Friendly Forces (1. Higher HQ mission & intent, 2. Adjacent units mission):"),
                    model.FriendlyForces, 50, v => { model.FriendlyForces = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec1);

                // =================================================================
                // II. MISJA
                // =================================================================
                var sec2 = CreateSectionCard(BuilderI18n.T("II. MISJA (2. MISSION — OPIS FABULARNY, GŁÓWNE ZADANIE 5W, ZADANIA DODATKOWE)", "II. MISSION (LORE, PRIMARY 5W TASK, ADDITIONAL TASKS)"));
                var sec2Stack = (StackPanel)sec2.Child;

                sec2Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Opis fabularny (nieobowiązkowy):", "A. Story / Lore Background (optional):"),
                    model.MissionLoreDesc, 42, v => { model.MissionLoreDesc = v; PersistModelToAppData(model); }));

                var w5Grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 6) };
                TextBox fullMissionBox = null;

                void AutoUpdate5W()
                {
                    model.MissionFullStatement = Compose5WMissionText(model);
                    if (fullMissionBox != null) fullMissionBox.Text = model.MissionFullStatement;
                    PersistModelToAppData(model);
                }

                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KTO (Oddział)", "WHO (Unit)"), model.MissionWho, v => { model.MissionWho = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("CO (Główne zadanie)", "WHAT (Primary Task)"), model.MissionWhat, v => { model.MissionWhat = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KIEDY (Czas)", "WHEN (Time)"), model.MissionWhen, v => { model.MissionWhen = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("GDZIE (Sektor / MGRS)", "WHERE (Grid / AO)"), model.MissionWhere, v => { model.MissionWhere = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("DLACZEGO (Cel)", "WHY (Purpose)"), model.MissionWhy, v => { model.MissionWhy = v; AutoUpdate5W(); }));
                sec2Stack.Children.Add(w5Grid);

                var fullMissionPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Główne zadanie (np. zbieranie kodów / utrzymanie sektora):", "B. Primary Mission Statement:"),
                    model.MissionFullStatement, 44, v => { model.MissionFullStatement = v; PersistModelToAppData(model); });
                fullMissionBox = FindFirstDescendant<TextBox>(fullMissionPanel);
                sec2Stack.Children.Add(fullMissionPanel);

                sec2Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Dodatkowe zadania (lista zadań — szczegółowa analiza w Załączniku 2 na dole):", "C. Additional Tasks (task list — detailed analysis in Annex 2 below):"),
                    model.AdditionalTasksSummary, 48, v => { model.AdditionalTasksSummary = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec2);

                // =================================================================
                // III. WYKONANIE + JEDEN PRZYCISK "🎲 GENERUJ SARNEG" Z LISTY TXT
                // =================================================================
                var sec3 = CreateSectionCard(BuilderI18n.T("III. WYKONANIE (3. EXECUTION — ZAMIAR, STYL DZIAŁANIA, TABELA SKŁADU I RÓL, ŁĄCZNOŚĆ, GENERATOR SARNEG)", "III. EXECUTION (INTENT, STYLE OF ACTION, SQUAD ROSTER TABLE, COMMS, SARNEG GENERATOR)"));
                var sec3Stack = (StackPanel)sec3.Child;

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Zamiar dowódcy (Cel wyjazdu: wygrana / dobra zabawa / trening taktyczny / priorytet):", "A. Commander's Intent (Primary mindset & goal of the operation):"),
                    model.CommandersIntent, 46, v => { model.CommandersIntent = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Priorytety i styl działania (Czy unikamy kontaktu i zrywamy, czy walczymy? Warunki podjęcia walki):", "B. Priorities & Style of Action (Avoid contact & break vs engage; engagement criteria):"),
                    model.ConceptOfOperations, 50, v => { model.ConceptOfOperations = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("C. Łańcuch dowodzenia (Dowódca -> Zastępca 1 -> Zastępca 2):", "C. Chain of Command (Commander -> Deputy 1 -> Deputy 2):"),
                    model.SuccessionOfCommand, v => { model.SuccessionOfCommand = v; PersistModelToAppData(model); }));

                // III.D TABELA SKŁADU ODDZIAŁU
                var rosterCard = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 6, 0, 10)
                };
                var rosterOuterStack = new StackPanel();

                var rosterHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rosterToggleCheck = new CheckBox
                {
                    IsChecked = model.EnableSquadRosterTable,
                    Content = BuilderI18n.T(
                        "  D. Aktywuj Tabelę Składu Oddziału, Podziału Ról i Sprzętu (Operator/Zespół | Rola 1 | Rola 2 | Broń/Sprzęt)",
                        "  D. Enable Squad Roster, Roles & Equipment Table (Operator/Team | Role 1 | Role 2 | Weapon/Gear)"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                rosterHeaderDock.Children.Add(rosterToggleCheck);

                var rosterButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(rosterButtonsPanel, Dock.Right);

                var btnAddOperatorPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj wiersz Operatora / Zespołu (+)", "Add Operator / Team row (+)"), () =>
                {
                    int nextNum = model.SquadRoster.Count + 1;
                    model.SquadRoster.Add(new OpordRosterRow
                    {
                        OperatorOrTeam = "ALFA-" + nextNum,
                        PrimaryRole = "Czujka",
                        SecondaryRole = "Tragarz",
                        WeaponAndGear = "Karabinek szturmowy + Radio"
                    });
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                rosterButtonsPanel.Children.Add(btnAddOperatorPlus);

                var btnRemoveLastOperatorMinus = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń ostatni wiersz tabeli (-)", "Remove last table row (-)"), () =>
                {
                    if (model.SquadRoster.Count > 0)
                    {
                        model.SquadRoster.RemoveAt(model.SquadRoster.Count - 1);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    }
                });
                rosterButtonsPanel.Children.Add(btnRemoveLastOperatorMinus);
                rosterHeaderDock.Children.Add(rosterButtonsPanel);
                rosterOuterStack.Children.Add(rosterHeaderDock);

                var rosterTableContainer = new StackPanel
                {
                    Visibility = model.EnableSquadRosterTable ? Visibility.Visible : Visibility.Collapsed
                };

                rosterToggleCheck.Checked += (_, __) =>
                {
                    model.EnableSquadRosterTable = true;
                    rosterTableContainer.Visibility = Visibility.Visible;
                    PersistModelToAppData(model);
                };
                rosterToggleCheck.Unchecked += (_, __) =>
                {
                    model.EnableSquadRosterTable = false;
                    rosterTableContainer.Visibility = Visibility.Collapsed;
                    PersistModelToAppData(model);
                };

                var colHdrGrid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                string[] colNames = new[]
                {
                    BuilderI18n.T("Operator / Zespół (Ksywa)", "Operator / Team (Callsign)"),
                    BuilderI18n.T("Rola 1 (Załącznik 1)", "Primary Role (Annex 1)"),
                    BuilderI18n.T("Rola 2 (Opcjonalnie)", "Secondary Role (Optional)"),
                    BuilderI18n.T("Typ broni i sprzęt (Szturm / DMR / Snajper)", "Weapon Type & Equipment"),
                    ""
                };
                for (int c = 0; c < colNames.Length; c++)
                {
                    var tb = new TextBlock
                    {
                        Text = colNames[c],
                        Foreground = TacticalConfigPanel.BrushMutedText,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    };
                    Grid.SetColumn(tb, c);
                    colHdrGrid.Children.Add(tb);
                }
                rosterTableContainer.Children.Add(colHdrGrid);

                string[] roleOptions = model.CustomRolesAnnex.Select(r => r.RoleName).Concat(new[] { "Szturmowiec", "Strzelec wyborowy (DMR)", "Snajper", "Wsparcie (KM)", "Brak" }).Distinct().ToArray();

                for (int rIdx = 0; rIdx < model.SquadRoster.Count; rIdx++)
                {
                    int capturedRowIdx = rIdx;
                    var rowModel = model.SquadRoster[rIdx];

                    var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                    var tbOp = CreateCompactGridTextBox(rowModel.OperatorOrTeam, v => { rowModel.OperatorOrTeam = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbOp, 0);
                    rowGrid.Children.Add(tbOp);

                    var cbRole1 = CreateEditableRoleComboBox(win, roleOptions, rowModel.PrimaryRole, v => { rowModel.PrimaryRole = v; PersistModelToAppData(model); });
                    Grid.SetColumn(cbRole1, 1);
                    rowGrid.Children.Add(cbRole1);

                    var cbRole2 = CreateEditableRoleComboBox(win, roleOptions, rowModel.SecondaryRole, v => { rowModel.SecondaryRole = v; PersistModelToAppData(model); });
                    Grid.SetColumn(cbRole2, 2);
                    rowGrid.Children.Add(cbRole2);

                    var tbGear = CreateCompactGridTextBox(rowModel.WeaponAndGear, v => { rowModel.WeaponAndGear = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbGear, 3);
                    rowGrid.Children.Add(tbGear);

                    var btnDelRow = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń ten wiersz (-)", "Remove this row (-)"), () =>
                    {
                        model.SquadRoster.RemoveAt(capturedRowIdx);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    });
                    Grid.SetColumn(btnDelRow, 4);
                    rowGrid.Children.Add(btnDelRow);

                    rosterTableContainer.Children.Add(rowGrid);
                }

                rosterOuterStack.Children.Add(rosterTableContainer);
                rosterCard.Child = rosterOuterStack;
                sec3Stack.Children.Add(rosterCard);

                // III.E TABELA ŁĄCZNOŚCI
                var commsHeaderDock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                commsHeaderDock.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "E. Tabela łączności (Kanał główny, zapasowy, HQ + automatyczna lista z Generatora Stron):",
                        "E. Communications Table (Primary, Backup, HQ + live channels from Faction Generator):"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var btnSyncRadioNow = new Button
                {
                    Content = BuilderI18n.T("🔄 Odśwież częstotliwości z Generatora Stron", "🔄 Sync Frequencies from Faction Generator"),
                    Height = 26,
                    Padding = new Thickness(10, 2, 10, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Background = new SolidColorBrush(Color.FromRgb(18, 48, 58)),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10,
                    Cursor = Cursors.Hand
                };
                DockPanel.SetDock(btnSyncRadioNow, Dock.Right);
                btnSyncRadioNow.Click += (_, __) =>
                {
                    SyncModelRadioChannelsFromFactionGenerator(win, _activeOpordFaction, model, forceOverwrite: true);
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                };
                commsHeaderDock.Children.Add(btnSyncRadioNow);
                sec3Stack.Children.Add(commsHeaderDock);

                var radio3Grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał główny", "Primary Channel"), model.PrimaryRadioFreq, v => { model.PrimaryRadioFreq = v; PersistModelToAppData(model); }));
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał zapasowy", "Backup Channel"), model.BackupRadioFreq, v => { model.BackupRadioFreq = v; PersistModelToAppData(model); }));
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał HQ (Sztab)", "HQ Channel"), model.HqRadioFreq, v => { model.HqRadioFreq = v; PersistModelToAppData(model); }));
                sec3Stack.Children.Add(radio3Grid);

                var liveSignalInfo = new Border
                {
                    Background = TacticalConfigPanel.BrushInputBg,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                liveSignalInfo.Child = new TextBlock
                {
                    Text = BuildLiveSignalSummaryText(win, _activeOpordFaction),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap
                };
                sec3Stack.Children.Add(liveSignalInfo);

                // III.F SYGNAŁY + JEDEN PRZYCISK "🎲 GENERUJ SARNEG" Z LISTY TXT
                sec3Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("F.1. Kryptonimy celów i punktów na mapie:", "F.1. Objective & Waypoint Code Names:"),
                    model.TargetCodeNames, v => { model.TargetCodeNames = v; PersistModelToAppData(model); }));

                var sarnegCard = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 4, 0, 8)
                };
                var sarnegStack = new StackPanel();
                sarnegStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "🔐 F.2. TABELA KRYPTO SARNEG (SZYFROWANIE WSPÓŁRZĘDNYCH 0–9) + LOSOWANIE Z LISTY SŁÓW 10-LITEROWYCH",
                        "🔐 F.2. SARNEG CRYPTO TABLE (0–9 GRID ENCRYPTION) + 10-LETTER WORD GENERATOR"),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var sarnegPreviewLabel = new TextBlock
                {
                    Text = FormatSarnegMatrixPreview(model.SarnegKeyWord10),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 4)
                };

                var sarnegInputRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                sarnegInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
                sarnegInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

                TextBox sarnegKeyTextBox = null;
                var sarnegKeyBoxPanel = CreateLabeledTextBox(
                    BuilderI18n.T("10-literowe słowo-klucz SARNEG (wpisz własne lub kliknij 'Generuj SARNEG'):", "10-letter SARNEG Key Word (type custom or click 'Generate SARNEG'):"),
                    model.SarnegKeyWord10,
                    v =>
                    {
                        model.SarnegKeyWord10 = v.ToUpperInvariant();
                        sarnegPreviewLabel.Text = FormatSarnegMatrixPreview(model.SarnegKeyWord10);
                        PersistModelToAppData(model);
                    });
                sarnegKeyTextBox = FindFirstDescendant<TextBox>(sarnegKeyBoxPanel);
                Grid.SetColumn(sarnegKeyBoxPanel, 0);
                sarnegInputRow.Children.Add(sarnegKeyBoxPanel);

                var btnGenerateSarneg = new Button
                {
                    Content = BuilderI18n.T("🎲 Generuj SARNEG", "🎲 Generate SARNEG"),
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(6, 0, 0, 4),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderThickness = new Thickness(1.4),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Cursor = Cursors.Hand
                };
                btnGenerateSarneg.Click += (_, __) =>
                {
                    string drawn = SarnegDictionaryService.DrawRandomSarnegWord();
                    model.SarnegKeyWord10 = drawn;
                    if (sarnegKeyTextBox != null) sarnegKeyTextBox.Text = drawn;
                    sarnegPreviewLabel.Text = FormatSarnegMatrixPreview(drawn);
                    PersistModelToAppData(model);
                };
                Grid.SetColumn(btnGenerateSarneg, 1);
                sarnegInputRow.Children.Add(btnGenerateSarneg);

                sarnegStack.Children.Add(sarnegInputRow);
                sarnegStack.Children.Add(sarnegPreviewLabel);
                sarnegCard.Child = sarnegStack;
                sec3Stack.Children.Add(sarnegCard);

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("F.3. Hasła rozpoznawcze — a) werbalne (wywołanie - odpowiedź - potwierdzenie), b) niewerbalne (wywołanie - odzew):", "F.3. Challenge & Password — a) verbal (challenge - reply - confirmation), b) non-verbal:"),
                    model.ChallengeAndPassword, 48, v => { model.ChallengeAndPassword = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("G. Leczenie (sposób leczenia, ilość i działanie respów, zasady MEDEVAC / przycisk SOS MGRS):", "G. Medical & Respawn Rules (treatment method, respawn count & rules, SOS MGRS button):"),
                    model.MedevacRules, 50, v => { model.MedevacRules = v; PersistModelToAppData(model); }));

                mainStack.Children.Add(sec3);

                // =================================================================
                // IV. LOGISTYKA + OPCJONALNY, EDYTOWALNY ZAŁĄCZNIK 1 (PODZIAŁ RÓL)
                // =================================================================
                var sec4 = CreateSectionCard(BuilderI18n.T("IV. LOGISTYKA ORAZ ZAŁĄCZNIKI (STYL WYPOSAŻENIA, SZPEJLISTA, DOJAZD, OPCJONALNY ZAŁĄCZNIK 1 RÓL)", "IV. LOGISTICS & ANNEXES (LOADOUT, GEAR CHECKLIST, TRAVEL, OPTIONAL EDITABLE ROLES ANNEX 1)"));
                var sec4Stack = (StackPanel)sec4.Child;

                sec4Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("A. Styl wyposażenia (na lekko / na 12h w terenie / na nocleg w terenie / inne):", "A. Loadout Style (light / 12h field / overnight / other):"),
                    model.RespawnAndSupply, v => { model.RespawnAndSupply = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Wyposażenie oddziału (SZPEJLISTA obowiązkowa):", "B. Squad Equipment Checklist (SZPEJLISTA):"),
                    model.SquadGearChecklist, 48, v => { model.SquadGearChecklist = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Wyposażenie specjalne (np. RPG przydzielone ze sztabu, przedmioty fabularne do przygotowania):", "C. Special Equipment (e.g., HQ-assigned RPG, mission props):"),
                    model.SpecialEquipment, 44, v => { model.SpecialEquipment = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("D. Dojazd (1. Parking, 2. Odległość z parkingu na miejsce gry, 3. Transport: kto z kim):", "D. Travel & Parking (1. Parking location, 2. Distance to AO, 3. Vehicle allocation):"),
                    model.TravelAndParking, 50, v => { model.TravelAndParking = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("E. Regulamin gry (link):", "E. Game Rules (URL link):"),
                    model.GameRulesLink, v => { model.GameRulesLink = v; PersistModelToAppData(model); }));

                // OPCJONALNE ROZSZERZENIE: ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH (EDYTOWALNY + / -)
                var annex1Card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderThickness = new Thickness(1.4),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 8, 0, 8)
                };
                var annex1Outer = new StackPanel();
                var annex1HdrDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

                var annex1Check = new CheckBox
                {
                    IsChecked = model.EnableRolesAnnex,
                    Content = BuilderI18n.T(
                        "  Załącznik 1 (Opcjonalne rozszerzenie): Dołącz i edytuj tabelę opisu ról drużynowych (Rola | Opis obowiązków | Zajęcie)",
                        "  Annex 1 (Optional Extension): Include & edit Squad Role Definitions Table (Role | Duties Description | Mode)"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                annex1HdrDock.Children.Add(annex1Check);

                var annex1Btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(annex1Btns, Dock.Right);

                var btnAddRoleDefPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj nową definicję roli (+)", "Add new role definition (+)"), () =>
                {
                    model.EnableRolesAnnex = true;
                    model.CustomRolesAnnex.Add(new OpordRoleDefinitionRow
                    {
                        RoleName = BuilderI18n.T("Nowa Rola", "New Role"),
                        Description = BuilderI18n.T("Opis obowiązków w drużynie...", "Squad duties description..."),
                        DutyMode = BuilderI18n.T("Sytuacyjne", "Situational")
                    });
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                annex1Btns.Children.Add(btnAddRoleDefPlus);

                var btnRemoveRoleDefMinus = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń ostatnią definicję roli (-)", "Remove last role definition (-)"), () =>
                {
                    if (model.CustomRolesAnnex.Count > 0)
                    {
                        model.CustomRolesAnnex.RemoveAt(model.CustomRolesAnnex.Count - 1);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    }
                });
                annex1Btns.Children.Add(btnRemoveRoleDefMinus);
                annex1HdrDock.Children.Add(annex1Btns);
                annex1Outer.Children.Add(annex1HdrDock);

                var annex1TableContainer = new StackPanel
                {
                    Visibility = model.EnableRolesAnnex ? Visibility.Visible : Visibility.Collapsed
                };

                annex1Check.Checked += (_, __) =>
                {
                    model.EnableRolesAnnex = true;
                    annex1TableContainer.Visibility = Visibility.Visible;
                    PersistModelToAppData(model);
                };
                annex1Check.Unchecked += (_, __) =>
                {
                    model.EnableRolesAnnex = false;
                    annex1TableContainer.Visibility = Visibility.Collapsed;
                    PersistModelToAppData(model);
                };

                var a1HdrGrid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                string[] a1Cols = new[]
                {
                    BuilderI18n.T("Rola drużynowa", "Squad Role"),
                    BuilderI18n.T("Opis obowiązków (edytowalny)", "Duties Description (editable)"),
                    BuilderI18n.T("Zajęcie (Tryb)", "Duty Mode"),
                    ""
                };
                for (int c = 0; c < a1Cols.Length; c++)
                {
                    var tb = new TextBlock { Text = a1Cols[c], Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, FontWeight = FontWeights.Bold };
                    Grid.SetColumn(tb, c);
                    a1HdrGrid.Children.Add(tb);
                }
                annex1TableContainer.Children.Add(a1HdrGrid);

                for (int i = 0; i < model.CustomRolesAnnex.Count; i++)
                {
                    int capIdx = i;
                    var roleDef = model.CustomRolesAnnex[i];

                    var rGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                    var tbRName = CreateCompactGridTextBox(roleDef.RoleName, v => { roleDef.RoleName = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbRName, 0);
                    rGrid.Children.Add(tbRName);

                    var tbRDesc = CreateCompactGridTextBox(roleDef.Description, v => { roleDef.Description = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbRDesc, 1);
                    rGrid.Children.Add(tbRDesc);

                    var tbRMode = CreateCompactGridTextBox(roleDef.DutyMode, v => { roleDef.DutyMode = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbRMode, 2);
                    rGrid.Children.Add(tbRMode);

                    var btnDelRole = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń tę rolę (-)", "Remove this role (-)"), () =>
                    {
                        model.CustomRolesAnnex.RemoveAt(capIdx);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    });
                    Grid.SetColumn(btnDelRole, 3);
                    rGrid.Children.Add(btnDelRole);

                    annex1TableContainer.Children.Add(rGrid);
                }

                annex1Outer.Children.Add(annex1TableContainer);
                annex1Card.Child = annex1Outer;
                sec4Stack.Children.Add(annex1Card);

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("Załącznik 2. Szczegółowe opracowanie zadań dodatkowych:", "Annex 2. Detailed Mission Tasks Breakdown:"),
                    model.Annex2TasksAnalysis, 50, v => { model.Annex2TasksAnalysis = v; PersistModelToAppData(model); }));

                mainStack.Children.Add(sec4);
            }

            RebuildOpordForm();
            BuilderI18n.LanguageChanged += _ => RebuildOpordForm();
            _cachedOpordOverlay = rootBorder;
            return rootBorder;
        }

        private static void SyncModelRadioChannelsFromFactionGenerator(Window win, string factionName, OpordParagraphModel model, bool forceOverwrite = false)
        {
            var facJson = TryGetFactionJsonObject(win, factionName);
            if (facJson == null) return;

            var vArr = (facJson["VoiceChannels"] ?? facJson["voiceChannels"]) as JsonArray;
            if (vArr != null && vArr.Count > 0)
            {
                for (int i = 0; i < vArr.Count; i++)
                {
                    if (vArr[i] is JsonObject vo)
                    {
                        string role = vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString() ?? "";
                        string band = vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString() ?? "PMR";
                        string freq = vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString() ?? "";
                        string tone = vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString() ?? "";
                        string formatted = string.Format("{0} [{1}] ({2})", freq, band, tone);

                        if (i == 0 && (forceOverwrite || model.PrimaryRadioFreq.Contains("446.00625")))
                            model.PrimaryRadioFreq = role + ": " + formatted;
                        else if (i == 1 && (forceOverwrite || model.BackupRadioFreq.Contains("446.03125")))
                            model.BackupRadioFreq = role + ": " + formatted;
                        else if (i == 2 && (forceOverwrite || model.HqRadioFreq.Contains("446.09375")))
                            model.HqRadioFreq = role + ": " + formatted;
                    }
                }
            }
        }

        private static string FormatSarnegMatrixPreview(string key10)
        {
            string normalized = SarnegDictionaryService.NormalizePolishChars(key10 ?? "PRZEDMIOTY").Trim();
            string padded = normalized.PadRight(10, 'X').Substring(0, 10);
            bool isIso = SarnegDictionaryService.Has10UniqueLetters(padded);

            var sbTop = new StringBuilder(BuilderI18n.T("LITERA SARNEG:  ", "SARNEG LETTER:  "));
            var sbBot = new StringBuilder(BuilderI18n.T("CYFRA (0 - 9):  ", "DIGIT (0 - 9):  "));
            for (int i = 0; i < 10; i++)
            {
                sbTop.Append("| ").Append(padded[i]).Append(' ');
                sbBot.Append("| ").Append(i).Append(' ');
            }
            sbTop.Append("|   ");
            sbTop.Append(isIso
                ? BuilderI18n.T("✓ [10 UNIKALNYCH LITER — KLUCZ PRAWIDŁOWY]", "✓ [10 UNIQUE LETTERS — VALID KEY]")
                : BuilderI18n.T("⚠️ [UWAGA: POWTÓRZONE LITERY W SŁOWIE!]", "⚠️ [WARNING: DUPLICATE LETTERS IN WORD!]"));
            sbBot.Append('|');
            return sbTop + "\n" + sbBot;
        }

        private static string BuildLiveSignalSummaryText(Window win, string factionName)
        {
            var facJson = TryGetFactionJsonObject(win, factionName);
            string chName = facJson?["ChannelName"]?.ToString() ?? (factionName + "-MESH");
            string region = facJson?["LoraRegion"]?.ToString() ?? "EU_868";
            string preset = facJson?["LoraModemPreset"]?.ToString() ?? "SHORT_FAST";
            string ots = facJson?["OtsHost"]?.ToString() ?? "";
            string phone = facJson?["EmergencyPhone"]?.ToString() ?? "";

            var sb = new StringBuilder();
            sb.AppendLine(BuilderI18n.T(
                "📡 [ZACIĄGNIĘTE Z GENERATORA STRON KANAŁY RADIOWE I MESHTASTIC DLA: " + factionName + "]",
                "📡 [SYNCED RADIO & MESHTASTIC CHANNELS FROM FACTION GENERATOR FOR: " + factionName + "]"));
            sb.AppendLine(BuilderI18n.T(
                "• Meshtastic LoRa: Kanał '" + chName + "'  |  Region: " + region + "  |  Preset: " + preset + "  |  Szyfrowanie: AES-256 PSK",
                "• Meshtastic LoRa: Channel '" + chName + "'  |  Region: " + region + "  |  Preset: " + preset + "  |  Encryption: AES-256 PSK"));
            sb.AppendLine(BuilderI18n.T(
                "• Serwer ATAK (OpenTAKServer): " + (string.IsNullOrWhiteSpace(ots) ? "Tryb lokalny / Off-Grid Mesh" : ots),
                "• ATAK Server (OpenTAKServer): " + (string.IsNullOrWhiteSpace(ots) ? "Local / Off-Grid Mesh" : ots)));
            sb.AppendLine(BuilderI18n.T(
                "• Telefon alarmowy ORGA / MEDEVAC (SOS MGRS): " + (string.IsNullOrWhiteSpace(phone) ? "Brak / 112" : phone),
                "• Emergency Phone ORGA / MEDEVAC (SOS MGRS): " + (string.IsNullOrWhiteSpace(phone) ? "None / 112" : phone)));

            var vArr = (facJson?["VoiceChannels"] ?? facJson?["voiceChannels"]) as JsonArray;
            if (vArr != null && vArr.Count > 0)
            {
                sb.AppendLine(BuilderI18n.T("• Wszystkie kanały głosowe zdefiniowane w Generatorze Stron:", "• All voice channels defined in Faction Generator:"));
                foreach (var vo in vArr.OfType<JsonObject>())
                {
                    string r = vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString() ?? "Kanał";
                    string b = vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString() ?? "PMR";
                    string f = vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString() ?? "-";
                    string t = vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString() ?? "-";
                    sb.AppendLine("   - " + r + " [" + b + "]: " + f + "  (" + t + ")");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static void ShowOpordLanDistributionModal(Window owner, string factionName, OpordParagraphModel model)
        {
            string baseUrl = EnsureOpordLanServerStarted(owner);
            string safeFac = Uri.EscapeDataString(factionName);
            string htmlUrl = baseUrl + "/opord/" + safeFac + ".html";

            var dlg = new Window
            {
                Title = BuilderI18n.T(
                    "📡 Dystrybucja OPORD przez Wi-Fi / LAN (" + factionName + ")",
                    "📡 Wi-Fi / LAN OPORD Distribution (" + factionName + ")"),
                Width = 480,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = TacticalConfigPanel.BrushCardBg,
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            root.Children.Add(new TextBlock
            {
                Text = BuilderI18n.T(
                    "📋 UDOSTĘPNIANIE ROZKAZU OPORD (" + factionName + ") PO WI-FI / LAN",
                    "📋 SHARE OPORD (" + factionName + ") OVER WI-FI / LAN"),
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            root.Children.Add(new TextBlock
            {
                Text = BuilderI18n.T(
                    "Zeskanuj poniższy kod QR w aplikacji Field TAK Hub (lub aparatem telefonu), aby natychmiast otworzyć i zaimportować rozkaz OPORD (dla zaznaczonych dni) przez Wi-Fi / LAN:",
                    "Scan this QR code in Field TAK Hub (or phone camera) to view and import this OPORD (for selected days) over Wi-Fi / LAN:"),
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 10)
            });

            var qrFrame = new Border
            {
                Width = 270,
                Height = 270,
                Background = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var qrImg = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(qrImg, BitmapScalingMode.NearestNeighbor);
            qrFrame.Child = qrImg;
            root.Children.Add(qrFrame);

            var urlBox = new TextBox
            {
                Text = htmlUrl,
                IsReadOnly = true,
                Height = 26,
                FontSize = 10.5,
                FontFamily = new FontFamily("Consolas"),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(urlBox);

            _ = Task.Run(async () =>
            {
                try
                {
                    string api = "https://api.qrserver.com/v1/create-qr-code/?size=400x400&margin=10&data=" + Uri.EscapeDataString(htmlUrl);
                    byte[] bytes = await GeoportalLidarEngine.FastHttp.GetByteArrayAsync(api);
                    if (GeoportalLidarEngine.IsValidImageBytes(bytes) && Application.Current != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var bmp = new BitmapImage();
                            using var ms = new MemoryStream(bytes);
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            qrImg.Source = bmp;
                        });
                    }
                }
                catch { }
            });

            var btns = new UniformGrid { Columns = 2 };
            var btnCopyLink = new Button
            {
                Content = BuilderI18n.T("📋 Kopiuj link LAN OPORD", "📋 Copy LAN OPORD Link"),
                Height = 32,
                Margin = new Thickness(0, 0, 5, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnCopyLink.Click += (_, __) => Clipboard.SetText(htmlUrl);

            var btnClose = new Button
            {
                Content = BuilderI18n.T("Zamknij", "Close"),
                Height = 32,
                Margin = new Thickness(5, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (_, __) => dlg.Close();

            btns.Children.Add(btnCopyLink);
            btns.Children.Add(btnClose);
            root.Children.Add(btns);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        // =========================================================================
        // 7. WEKTOROWE PRZYCISKI "+" I "-" Z WŁASNYM CONTROLTEMPLATE (ODPORNE NA STYLE WPF)
        // =========================================================================
        private static Button CreateTacticalPlusMinusButton(bool isPlusGreen, string toolTipText, Action onClick)
        {
            Color fillCol = isPlusGreen ? Color.FromRgb(18, 75, 45) : Color.FromRgb(88, 22, 22);
            Color strokeCol = isPlusGreen ? Color.FromRgb(46, 204, 113) : Color.FromRgb(255, 82, 82);

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(fillCol));
            borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(strokeCol));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1.8));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            var canvasFactory = new FrameworkElementFactory(typeof(Canvas));
            canvasFactory.SetValue(FrameworkElement.WidthProperty, 14.0);
            canvasFactory.SetValue(FrameworkElement.HeightProperty, 14.0);
            canvasFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            canvasFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var hLineFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Line));
            hLineFactory.SetValue(System.Windows.Shapes.Line.X1Property, 2.0);
            hLineFactory.SetValue(System.Windows.Shapes.Line.Y1Property, 7.0);
            hLineFactory.SetValue(System.Windows.Shapes.Line.X2Property, 12.0);
            hLineFactory.SetValue(System.Windows.Shapes.Line.Y2Property, 7.0);
            hLineFactory.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
            hLineFactory.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.6);
            canvasFactory.AppendChild(hLineFactory);

            if (isPlusGreen)
            {
                var vLineFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Line));
                vLineFactory.SetValue(System.Windows.Shapes.Line.X1Property, 7.0);
                vLineFactory.SetValue(System.Windows.Shapes.Line.Y1Property, 2.0);
                vLineFactory.SetValue(System.Windows.Shapes.Line.X2Property, 7.0);
                vLineFactory.SetValue(System.Windows.Shapes.Line.Y2Property, 12.0);
                vLineFactory.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
                vLineFactory.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.6);
                canvasFactory.AppendChild(vLineFactory);
            }

            borderFactory.AppendChild(canvasFactory);
            template.VisualTree = borderFactory;

            var btn = new Button
            {
                Template = template,
                ToolTip = toolTipText,
                Width = 28,
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, __) => onClick?.Invoke();
            return btn;
        }

        private static TextBox CreateCompactGridTextBox(string initialValue, Action<string> onChanged)
        {
            var tb = new TextBox
            {
                Text = initialValue ?? "",
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                CaretBrush = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11
            };
            tb.TextChanged += (_, __) => onChanged?.Invoke(tb.Text);
            return tb;
        }

        private static ComboBox CreateEditableRoleComboBox(Window win, string[] options, string initialValue, Action<string> onChanged)
        {
            var cb = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                FontSize = 11,
                IsEditable = false,
                Tag = "INJECTED"
            };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(cb, win);
            foreach (string opt in options)
                cb.Items.Add(opt);
            if (!string.IsNullOrWhiteSpace(initialValue) && !cb.Items.Contains(initialValue))
                cb.Items.Insert(0, initialValue);
            cb.SelectedItem = string.IsNullOrWhiteSpace(initialValue) ? options.FirstOrDefault() : initialValue;
            cb.SelectionChanged += (_, __) =>
            {
                if (cb.SelectedItem is string s) onChanged?.Invoke(s);
            };
            return cb;
        }

        private static Border CreateSectionCard(string title)
        {
            var border = new Border
            {
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 8)
            });
            border.Child = stack;
            return border;
        }

        private static StackPanel CreateLabeledTextBox(string label, string initialValue, Action<string> onChanged)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 6, 4) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 3)
            });
            var tb = new TextBox
            {
                Text = initialValue ?? "",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                CaretBrush = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11
            };
            tb.TextChanged += (_, __) => onChanged?.Invoke(tb.Text);
            sp.Children.Add(tb);
            return sp;
        }

        private static StackPanel CreateLabeledMultilineBox(string label, string initialValue, double height, Action<string> onChanged)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            var tb = new TextBox
            {
                Text = initialValue ?? "",
                Height = height,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                CaretBrush = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 11
            };
            tb.TextChanged += (_, __) => onChanged?.Invoke(tb.Text);
            sp.Children.Add(tb);
            return sp;
        }

        private static T FindFirstDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindFirstDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
