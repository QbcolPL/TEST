#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System.Text.RegularExpressions;
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
        // 6. WIDOK GENERATORA OPORD v3.3 — KAŻDA ZAKŁADKA DNIA POSIADA WŁASNY,
        //    CAŁKOWICIE NIEZALEŻNY STAN WSZYSTKICH SEKCJI (0, I, II, III, IV, ZAŁ. 1 i 2)
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
                TranslateOpordModelToCurrentLanguage(model);
                model.ActiveDayIndex = Math.Clamp(model.ActiveDayIndex, 0, Math.Max(0, model.Days.Count - 1));
                var activeDay = model.Days[model.ActiveDayIndex];
                EnsureDayDefaultsInitialized(activeDay, _activeOpordFaction);

                string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);
                SyncDayRadioChannelsFromFactionGenerator(win, _activeOpordFaction, activeDay);

                // PASEK NAGŁÓWKOWY GENERATORA OPORD v3.3
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
                        "📋 GENERATOR ROZKAZU BOJOWEGO MILSIM / NATO (OPORD v3.3 — PEŁNA NIEZALEŻNOŚĆ DNI) — " + eventName,
                        "📋 MILSIM / NATO OPORD v3.3 BUILDER (FULLY INDEPENDENT DAY TABS) — " + eventName),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14.5,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                topStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "Każda zakładka dnia (Dzień 1, Dzień 2...) jest w 100% niezależnym arkuszem rozkazu: posiada własną Sytuację, METOC, Misję 5W, Tabelę Składu i Ról, własne słowo-klucz SARNEG, własne hasła rozpoznawcze oraz Logistykę. Zaznacz [✓], które dni wygenerować.",
                        "Each day tab (Day 1, Day 2...) is a 100% independent OPORD sheet: own Situation, METOC, 5W Mission, Squad Roster, own SARNEG keyword, own challenge/passwords, and Logistics. Check [✓] which days to export."),
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
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outPath) { UseShellExecute = true }); } catch { }
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
                // PASEK NIEZALEŻNYCH ZAKŁADEK DZIENNYCH + WYBÓR DNI DO GENEROWANIA
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
                        "📅 NIEZALEŻNE ZAKŁADKI DZIENNE (Każdy dzień ma własny Skład, SARNEG, Misję i Sytuację — zaznacz [✓] dni do wygenerowania):",
                        "📅 INDEPENDENT DAY TABS (Each day has its own Roster, SARNEG, Mission & Situation — check [✓] days to generate):"),
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
                    Content = BuilderI18n.T("☑️ Generuj tylko aktywny dzień", "1️⃣ Only Active Day"),
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

                Button activeDayTabButtonRef = null;
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
                        Tag = "DYNAMIC_NO_I18N",
                        Content = "📆 " + dayObj.DayTitle + " (" + dayObj.DateIso + ") [SARNEG: " + dayObj.SarnegKeyWord10 + "]",
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
                    if (isSel) activeDayTabButtonRef = dayTabBtn;
                    chipRow.Children.Add(dayTabBtn);

                    tabChipBorder.Child = chipRow;
                    daysWrap.Children.Add(tabChipBorder);
                }

                var btnAddDayPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj kolejny niezależny dzień operacji (+)", "Add next independent operation day (+)"), () =>
                {
                    int nextNum = model.Days.Count + 1;
                    var newIndependentDay = CreateIndependentNextDay(activeDay, nextNum, _activeOpordFaction);
                    model.Days.Add(newIndependentDay);
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
                // 0. NAGŁÓWEK ROZKAZU + PRZEŁĄCZNIK KLAUZULI "JAWNE / TAJNE" (DLA ACTIVE DAY)
                // =================================================================
                var metaCard = CreateSectionCard(BuilderI18n.T(
                    "0. NAGŁÓWEK ROZKAZU, KLAUZULA TAJNOŚCI (JAWNE / TAJNE) ORAZ HARMONOGRAM — " + activeDay.DayTitle.ToUpperInvariant(),
                    "0. ORDER HEADER, CLASSIFICATION (UNCLASSIFIED / SECRET) & SCHEDULE — " + activeDay.DayTitle.ToUpperInvariant()));
                var metaStack = (StackPanel)metaCard.Child;

                var classToggleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                classToggleRow.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T("Klauzula dokumentu (" + activeDay.DayTitle + "):", "Security Classification (" + activeDay.DayTitle + "):"),
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
                    bool isSec = activeDay.IsSecretClassification || activeDay.Classification.Contains("TAJNE", StringComparison.OrdinalIgnoreCase) || activeDay.Classification.Contains("SECRET", StringComparison.OrdinalIgnoreCase);
                    activeDay.IsSecretClassification = isSec;

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
                    activeDay.IsSecretClassification = false;
                    activeDay.Classification = BuilderI18n.T(
                        "JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD",
                        "UNCLASSIFIED / JAWNE — MILSIM SQUAD OPORD");
                    if (classificationTextBoxRef != null) classificationTextBoxRef.Text = activeDay.Classification;
                    UpdateSecretToggleVisuals();
                    PersistModelToAppData(model);
                };

                btnTajne.Click += (_, __) =>
                {
                    activeDay.IsSecretClassification = true;
                    activeDay.Classification = BuilderI18n.T(
                        "TAJNE / SECRET — TYLKO DO WIADOMOŚCI STRONY " + _activeOpordFaction.ToUpperInvariant(),
                        "SECRET / TAJNE — " + _activeOpordFaction.ToUpperInvariant() + " EYES ONLY");
                    if (classificationTextBoxRef != null) classificationTextBoxRef.Text = activeDay.Classification;
                    UpdateSecretToggleVisuals();
                    PersistModelToAppData(model);
                };

                var toggleBtnsWrap = new StackPanel { Orientation = Orientation.Horizontal };
                toggleBtnsWrap.Children.Add(btnJawne);
                toggleBtnsWrap.Children.Add(btnTajne);
                classToggleRow.Children.Add(toggleBtnsWrap);
                metaStack.Children.Add(classToggleRow);

                var metaGrid = new UniformGrid { Columns = 4 };
                var classBoxPanel = CreateLabeledTextBox(BuilderI18n.T("Nagłówek Klasyfikacji (" + activeDay.DayTitle + ")", "Classification Banner (" + activeDay.DayTitle + ")"), activeDay.Classification, v => { activeDay.Classification = v; UpdateSecretToggleVisuals(); PersistModelToAppData(model); });
                classificationTextBoxRef = FindFirstDescendant<TextBox>(classBoxPanel);
                UpdateSecretToggleVisuals();

                metaGrid.Children.Add(classBoxPanel);
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Nr Rozkazu (" + activeDay.DayTitle + ")", "Order No. (" + activeDay.DayTitle + ")"), activeDay.OperationNumber, v => { activeDay.OperationNumber = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Grupa Data-Czas (DTG)", "Date-Time Group (DTG)"), activeDay.DtgIssued, v => { activeDay.DtgIssued = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Sztab wydający (HQ)", "Issuing HQ"), activeDay.IssuingHq, v => { activeDay.IssuingHq = v; PersistModelToAppData(model); }));
                metaStack.Children.Add(metaGrid);

                metaStack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T(
                        "Harmonogram i zadania specyficzne dla wybranego dnia (" + activeDay.DayTitle + " — " + activeDay.DateIso + "):",
                        "Schedule & specific tasks for selected day (" + activeDay.DayTitle + " — " + activeDay.DateIso + "):"),
                    activeDay.DaySpecificTasks, 54, v => { activeDay.DaySpecificTasks = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(metaCard);

                // =================================================================
                // I. SYTUACJA (NIEZALEŻNA SYTUACJA, POI, METOC, WRÓG I SOJUSZNICY DLA ACTIVE DAY)
                // =================================================================
                var sec1 = CreateSectionCard(BuilderI18n.T(
                    "I. SYTUACJA — " + activeDay.DayTitle.ToUpperInvariant() + " (OBSZAR ZAINTERESOWANIA, TEREN, METOC, WRÓG, SOJUSZNICY)",
                    "I. SITUATION — " + activeDay.DayTitle.ToUpperInvariant() + " (AOI, TERRAIN, METOC, ENEMY, FRIENDLY FORCES)"));
                var sec1Stack = (StackPanel)sec1.Child;

                var aoiBoxPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Obszar zainteresowania (" + activeDay.DayTitle + " — 1. Mapa terenu gry, 2. Opis punktów POI, 3. Pliki KML / GPX):", "A. Area of Interest (" + activeDay.DayTitle + " — 1. Game Map, 2. POI description, 3. KML / GPX files):"),
                    activeDay.AreaOfInterestPoiDesc, 50, v => { activeDay.AreaOfInterestPoiDesc = v; PersistModelToAppData(model); });
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
                    v => { activeDay.DateIso = v; PersistModelToAppData(model); });
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
                    Tag = "DYNAMIC_NO_I18N",
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

                                if (!string.IsNullOrWhiteSpace(aoRes.ExtractedPoiList) && aoiTextBox != null && !activeDay.AreaOfInterestPoiDesc.Contains(aoRes.ExtractedPoiList))
                                {
                                    activeDay.AreaOfInterestPoiDesc = activeDay.AreaOfInterestPoiDesc.TrimEnd() + "\n" + aoRes.ExtractedPoiList;
                                    aoiTextBox.Text = activeDay.AreaOfInterestPoiDesc;
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
                            BuilderI18n.T("🌡️ Temp: ", "🌡️ Temp: ") + rep.TempRangeText + "  |  " + rep.HumidityAndPrecipText + "\n" +
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
                    BuilderI18n.T("C. Siły wroga (" + activeDay.DayTitle + " — 1. Skład i siła, 2. Lokalizacje, 3. Kursy działań wroga COA):", "C. Enemy Forces (" + activeDay.DayTitle + " — 1. Composition & strength, 2. Locations, 3. COA):"),
                    activeDay.EnemyForces, 60, v => { activeDay.EnemyForces = v; PersistModelToAppData(model); }));

                sec1Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("D. Siły przyjazne (" + activeDay.DayTitle + " — 1. Misja i zamiar wyższego dowództwa HQ, 2. Misja sąsiednich jednostek):", "D. Friendly Forces (" + activeDay.DayTitle + " — 1. Higher HQ mission & intent, 2. Adjacent units):"),
                    activeDay.FriendlyForces, 50, v => { activeDay.FriendlyForces = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec1);

                // =================================================================
                // II. MISJA (NIEZALEŻNE CELE I MISJA 5W DLA ACTIVE DAY)
                // =================================================================
                var sec2 = CreateSectionCard(BuilderI18n.T(
                    "II. MISJA — " + activeDay.DayTitle.ToUpperInvariant() + " (OPIS FABULARNY, GŁÓWNE ZADANIE 5W DNIA, ZADANIA DODATKOWE DNIA)",
                    "II. MISSION — " + activeDay.DayTitle.ToUpperInvariant() + " (LORE, PRIMARY 5W TASK OF THE DAY, ADDITIONAL DAY TASKS)"));
                var sec2Stack = (StackPanel)sec2.Child;

                sec2Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Opis fabularny (" + activeDay.DayTitle + "):", "A. Story / Lore Background (" + activeDay.DayTitle + "):"),
                    activeDay.MissionLoreDesc, 42, v => { activeDay.MissionLoreDesc = v; PersistModelToAppData(model); }));

                var w5Grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 6) };
                TextBox fullMissionBox = null;

                void AutoUpdate5W()
                {
                    activeDay.MissionFullStatement = Compose5WMissionTextForDay(activeDay);
                    if (fullMissionBox != null) fullMissionBox.Text = activeDay.MissionFullStatement;
                    PersistModelToAppData(model);
                }

                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KTO (Oddział)", "WHO (Unit)"), activeDay.MissionWho, v => { activeDay.MissionWho = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("CO (Zadanie — " + activeDay.DayTitle + ")", "WHAT (Task — " + activeDay.DayTitle + ")"), activeDay.MissionWhat, v => { activeDay.MissionWhat = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KIEDY (Czas)", "WHEN (Time)"), activeDay.MissionWhen, v => { activeDay.MissionWhen = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("GDZIE (Sektor / MGRS)", "WHERE (Grid / AO)"), activeDay.MissionWhere, v => { activeDay.MissionWhere = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("DLACZEGO (Cel)", "WHY (Purpose)"), activeDay.MissionWhy, v => { activeDay.MissionWhy = v; AutoUpdate5W(); }));
                sec2Stack.Children.Add(w5Grid);

                var fullMissionPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Główne zadanie na " + activeDay.DayTitle + ":", "B. Primary Mission Statement for " + activeDay.DayTitle + ":"),
                    activeDay.MissionFullStatement, 44, v => { activeDay.MissionFullStatement = v; PersistModelToAppData(model); });
                fullMissionBox = FindFirstDescendant<TextBox>(fullMissionPanel);
                sec2Stack.Children.Add(fullMissionPanel);

                sec2Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Dodatkowe zadania na " + activeDay.DayTitle + ":", "C. Additional Tasks for " + activeDay.DayTitle + ":"),
                    activeDay.AdditionalTasksSummary, 48, v => { activeDay.AdditionalTasksSummary = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec2);

                // =================================================================
                // III. WYKONANIE (NIEZALEŻNY SKŁAD, ŁĄCZNOŚĆ, SARNEG I HASŁA DLA ACTIVE DAY)
                // =================================================================
                var sec3 = CreateSectionCard(BuilderI18n.T(
                    "III. WYKONANIE — " + activeDay.DayTitle.ToUpperInvariant() + " (ZAMIAR, TABELA SKŁADU I RÓL DNIA, ŁĄCZNOŚĆ, SARNEG DNIA, HASŁA DNIA)",
                    "III. EXECUTION — " + activeDay.DayTitle.ToUpperInvariant() + " (INTENT, DAY SQUAD ROSTER, COMMS, DAY SARNEG, DAY PASSWORDS)"));
                var sec3Stack = (StackPanel)sec3.Child;

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Zamiar dowódcy (" + activeDay.DayTitle + "):", "A. Commander's Intent (" + activeDay.DayTitle + "):"),
                    activeDay.CommandersIntent, 46, v => { activeDay.CommandersIntent = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Priorytety i styl działania (" + activeDay.DayTitle + "):", "B. Priorities & Style of Action (" + activeDay.DayTitle + "):"),
                    activeDay.ConceptOfOperations, 50, v => { activeDay.ConceptOfOperations = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("C. Łańcuch dowodzenia (" + activeDay.DayTitle + "):", "C. Chain of Command (" + activeDay.DayTitle + "):"),
                    activeDay.SuccessionOfCommand, v => { activeDay.SuccessionOfCommand = v; PersistModelToAppData(model); }));

                // III.D NIEZALEŻNA TABELA SKŁADU ODDZIAŁU DLA ACTIVE DAY
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
                    IsChecked = activeDay.EnableSquadRosterTable,
                    Content = BuilderI18n.T(
                        "  D. Tabela Składu Oddziału, Podziału Ról i Sprzętu dla: " + activeDay.DayTitle,
                        "  D. Squad Roster, Roles & Equipment Table for: " + activeDay.DayTitle),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                rosterHeaderDock.Children.Add(rosterToggleCheck);

                var rosterButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(rosterButtonsPanel, Dock.Right);

                var btnAddOperatorPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj wiersz Operatora / Zespołu w " + activeDay.DayTitle + " (+)", "Add Operator / Team row in " + activeDay.DayTitle + " (+)"), () =>
                {
                    int nextNum = activeDay.SquadRoster.Count + 1;
                    activeDay.SquadRoster.Add(new OpordRosterRow
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

                var btnRemoveLastOperatorMinus = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń ostatni wiersz tabeli w " + activeDay.DayTitle + " (-)", "Remove last table row in " + activeDay.DayTitle + " (-)"), () =>
                {
                    if (activeDay.SquadRoster.Count > 0)
                    {
                        activeDay.SquadRoster.RemoveAt(activeDay.SquadRoster.Count - 1);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    }
                });
                rosterButtonsPanel.Children.Add(btnRemoveLastOperatorMinus);
                rosterHeaderDock.Children.Add(rosterButtonsPanel);
                rosterOuterStack.Children.Add(rosterHeaderDock);

                var rosterTableContainer = new StackPanel
                {
                    Visibility = activeDay.EnableSquadRosterTable ? Visibility.Visible : Visibility.Collapsed
                };

                rosterToggleCheck.Checked += (_, __) =>
                {
                    activeDay.EnableSquadRosterTable = true;
                    rosterTableContainer.Visibility = Visibility.Visible;
                    PersistModelToAppData(model);
                };
                rosterToggleCheck.Unchecked += (_, __) =>
                {
                    activeDay.EnableSquadRosterTable = false;
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
                    BuilderI18n.T("Rola 1 (" + activeDay.DayTitle + ")", "Primary Role (" + activeDay.DayTitle + ")"),
                    BuilderI18n.T("Rola 2 (" + activeDay.DayTitle + ")", "Secondary Role (" + activeDay.DayTitle + ")"),
                    BuilderI18n.T("Typ broni i sprzęt / Zadanie", "Weapon Type & Equipment / Task"),
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

                string[] roleOptions = activeDay.CustomRolesAnnex.Select(r => r.RoleName).Concat(new[] { "Szturmowiec", "Strzelec wyborowy (DMR)", "Snajper", "Wsparcie (KM)", "Brak" }).Distinct().ToArray();

                for (int rIdx = 0; rIdx < activeDay.SquadRoster.Count; rIdx++)
                {
                    int capturedRowIdx = rIdx;
                    var rowModel = activeDay.SquadRoster[rIdx];

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
                        activeDay.SquadRoster.RemoveAt(capturedRowIdx);
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

                // III.E TABELA ŁĄCZNOŚCI DLA ACTIVE DAY
                var commsHeaderDock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                commsHeaderDock.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "E. Tabela łączności (" + activeDay.DayTitle + " — Kanał główny, zapasowy, HQ):",
                        "E. Communications Table (" + activeDay.DayTitle + " — Primary, Backup, HQ):"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var btnSyncRadioNow = new Button
                {
                    Content = BuilderI18n.T("🔄 Pobierz z Generatora Stron", "🔄 Sync from Faction Generator"),
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
                    SyncDayRadioChannelsFromFactionGenerator(win, _activeOpordFaction, activeDay, forceOverwrite: true);
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                };
                commsHeaderDock.Children.Add(btnSyncRadioNow);
                sec3Stack.Children.Add(commsHeaderDock);

                var radio3Grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał główny (" + activeDay.DayTitle + ")", "Primary Channel (" + activeDay.DayTitle + ")"), activeDay.PrimaryRadioFreq, v => { activeDay.PrimaryRadioFreq = v; PersistModelToAppData(model); }));
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał zapasowy (" + activeDay.DayTitle + ")", "Backup Channel (" + activeDay.DayTitle + ")"), activeDay.BackupRadioFreq, v => { activeDay.BackupRadioFreq = v; PersistModelToAppData(model); }));
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał HQ (" + activeDay.DayTitle + ")", "HQ Channel (" + activeDay.DayTitle + ")"), activeDay.HqRadioFreq, v => { activeDay.HqRadioFreq = v; PersistModelToAppData(model); }));
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

                // III.F KRYPTONIMY, NIEZALEŻNY SARNEG ORAZ HASŁA DLA ACTIVE DAY
                sec3Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("F.1. Kryptonimy celów i punktów na mapie (" + activeDay.DayTitle + "):", "F.1. Objective & Waypoint Code Names (" + activeDay.DayTitle + "):"),
                    activeDay.TargetCodeNames, v => { activeDay.TargetCodeNames = v; PersistModelToAppData(model); }));

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
                        "🔐 F.2. TABELA KRYPTO SARNEG DLA: " + activeDay.DayTitle.ToUpperInvariant() + " (NIEZALEŻNE SŁOWO-KLUCZ 0–9 NA TEN DZIEŃ)",
                        "🔐 F.2. SARNEG CRYPTO TABLE FOR: " + activeDay.DayTitle.ToUpperInvariant() + " (INDEPENDENT 0–9 KEYWORD FOR THIS DAY)"),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var sarnegPreviewLabel = new TextBlock
                {
                    Tag = "DYNAMIC_NO_I18N",
                    Text = FormatSarnegMatrixPreview(activeDay.SarnegKeyWord10),
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
                bool isInternalSarnegUpdate = false;
                var sarnegKeyBoxPanel = CreateLabeledTextBox(
                    BuilderI18n.T("10-literowe słowo-klucz SARNEG (" + activeDay.DayTitle + "):", "10-letter SARNEG Key Word (" + activeDay.DayTitle + ")"),
                    activeDay.SarnegKeyWord10,
                    v =>
                    {
                        if (isInternalSarnegUpdate) return;
                        string clean = SarnegDictionaryService.NormalizePolishChars(v ?? "").Trim();
                        activeDay.SarnegKeyWord10 = clean;
                        model.SarnegKeyWord10 = clean;

                        if (sarnegKeyTextBox != null && sarnegKeyTextBox.Text != clean)
                        {
                            isInternalSarnegUpdate = true;
                            int caret = sarnegKeyTextBox.CaretIndex;
                            sarnegKeyTextBox.Text = clean;
                            sarnegKeyTextBox.CaretIndex = Math.Clamp(caret, 0, clean.Length);
                            isInternalSarnegUpdate = false;
                        }

                        sarnegPreviewLabel.Text = FormatSarnegMatrixPreview(clean);
                        if (activeDayTabButtonRef != null)
                        {
                            activeDayTabButtonRef.Content = "📆 " + activeDay.DayTitle + " (" + activeDay.DateIso + ") [SARNEG: " + clean + "]";
                        }
                        PersistModelToAppData(model);
                    });
                sarnegKeyTextBox = FindFirstDescendant<TextBox>(sarnegKeyBoxPanel);
                if (sarnegKeyTextBox != null)
                {
                    sarnegKeyTextBox.MaxLength = 10;
                    sarnegKeyTextBox.IsReadOnly = false;
                    sarnegKeyTextBox.IsEnabled = true;
                }
                Grid.SetColumn(sarnegKeyBoxPanel, 0);
                sarnegInputRow.Children.Add(sarnegKeyBoxPanel);

                var btnGenerateSarneg = new Button
                {
                    Content = BuilderI18n.T("🎲 Generuj SARNEG (" + activeDay.DayTitle + ")", "🎲 Generate SARNEG (" + activeDay.DayTitle + ")"),
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
                    string drawn = SarnegDictionaryService.DrawRandomSarnegWord(activeDay.SarnegKeyWord10);
                    activeDay.SarnegKeyWord10 = drawn;
                    model.SarnegKeyWord10 = drawn;
                    if (sarnegKeyTextBox != null)
                    {
                        isInternalSarnegUpdate = true;
                        sarnegKeyTextBox.Text = drawn;
                        sarnegKeyTextBox.CaretIndex = drawn.Length;
                        isInternalSarnegUpdate = false;
                    }
                    sarnegPreviewLabel.Text = FormatSarnegMatrixPreview(drawn);
                    if (activeDayTabButtonRef != null)
                    {
                        activeDayTabButtonRef.Content = "📆 " + activeDay.DayTitle + " (" + activeDay.DateIso + ") [SARNEG: " + drawn + "]";
                    }
                    PersistModelToAppData(model);
                };
                Grid.SetColumn(btnGenerateSarneg, 1);
                sarnegInputRow.Children.Add(btnGenerateSarneg);

                sarnegStack.Children.Add(sarnegInputRow);
                sarnegStack.Children.Add(sarnegPreviewLabel);
                sarnegCard.Child = sarnegStack;
                sec3Stack.Children.Add(sarnegCard);

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("F.3. Hasła rozpoznawcze na " + activeDay.DayTitle + " — a) werbalne (wywołanie - odpowiedź - potwierdzenie), b) niewerbalne:", "F.3. Challenge & Password for " + activeDay.DayTitle + " — a) verbal (challenge - reply - confirm), b) non-verbal:"),
                    activeDay.ChallengeAndPassword, 48, v => { activeDay.ChallengeAndPassword = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("G. Leczenie i zasady Respawn (" + activeDay.DayTitle + "):", "G. Medical & Respawn Rules (" + activeDay.DayTitle + "):"),
                    activeDay.MedevacRules, 50, v => { activeDay.MedevacRules = v; PersistModelToAppData(model); }));

                mainStack.Children.Add(sec3);

                // =================================================================
                // IV. LOGISTYKA ORAZ ZAŁĄCZNIKI 1 i 2 (NIEZALEŻNE DLA ACTIVE DAY)
                // =================================================================
                var sec4 = CreateSectionCard(BuilderI18n.T(
                    "IV. LOGISTYKA ORAZ ZAŁĄCZNIKI — " + activeDay.DayTitle.ToUpperInvariant() + " (WYPOSAŻENIE, SZPEJLISTA, DOJAZD, ZAŁĄCZNIK 1 i 2)",
                    "IV. LOGISTICS & ANNEXES — " + activeDay.DayTitle.ToUpperInvariant() + " (LOADOUT, CHECKLIST, TRAVEL, ANNEX 1 & 2)"));
                var sec4Stack = (StackPanel)sec4.Child;

                sec4Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("A. Styl wyposażenia (" + activeDay.DayTitle + "):", "A. Loadout Style (" + activeDay.DayTitle + "):"),
                    activeDay.RespawnAndSupply, v => { activeDay.RespawnAndSupply = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Wyposażenie oddziału — SZPEJLISTA (" + activeDay.DayTitle + "):", "B. Squad Equipment Checklist (" + activeDay.DayTitle + "):"),
                    activeDay.SquadGearChecklist, 48, v => { activeDay.SquadGearChecklist = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Wyposażenie specjalne (" + activeDay.DayTitle + "):", "C. Special Equipment (" + activeDay.DayTitle + "):"),
                    activeDay.SpecialEquipment, 44, v => { activeDay.SpecialEquipment = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("D. Dojazd i parking (" + activeDay.DayTitle + "):", "D. Travel & Parking (" + activeDay.DayTitle + "):"),
                    activeDay.TravelAndParking, 50, v => { activeDay.TravelAndParking = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("E. Regulamin gry (link):", "E. Game Rules (URL link):"),
                    activeDay.GameRulesLink, v => { activeDay.GameRulesLink = v; PersistModelToAppData(model); }));

                // ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH (NIEZALEŻNY DLA ACTIVE DAY)
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
                    IsChecked = activeDay.EnableRolesAnnex,
                    Content = BuilderI18n.T(
                        "  Załącznik 1 (" + activeDay.DayTitle + "): Dołącz i edytuj tabelę opisu ról drużynowych",
                        "  Annex 1 (" + activeDay.DayTitle + "): Include & edit Squad Role Definitions Table"),
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
                    activeDay.EnableRolesAnnex = true;
                    activeDay.CustomRolesAnnex.Add(new OpordRoleDefinitionRow
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
                    if (activeDay.CustomRolesAnnex.Count > 0)
                    {
                        activeDay.CustomRolesAnnex.RemoveAt(activeDay.CustomRolesAnnex.Count - 1);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    }
                });
                annex1Btns.Children.Add(btnRemoveRoleDefMinus);
                annex1HdrDock.Children.Add(annex1Btns);
                annex1Outer.Children.Add(annex1HdrDock);

                var annex1TableContainer = new StackPanel
                {
                    Visibility = activeDay.EnableRolesAnnex ? Visibility.Visible : Visibility.Collapsed
                };

                annex1Check.Checked += (_, __) =>
                {
                    activeDay.EnableRolesAnnex = true;
                    annex1TableContainer.Visibility = Visibility.Visible;
                    PersistModelToAppData(model);
                };
                annex1Check.Unchecked += (_, __) =>
                {
                    activeDay.EnableRolesAnnex = false;
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

                for (int i = 0; i < activeDay.CustomRolesAnnex.Count; i++)
                {
                    int capIdx = i;
                    var roleDef = activeDay.CustomRolesAnnex[i];

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
                        activeDay.CustomRolesAnnex.RemoveAt(capIdx);
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
                    BuilderI18n.T("Załącznik 2 (" + activeDay.DayTitle + "). Szczegółowe opracowanie zadań dodatkowych:", "Annex 2 (" + activeDay.DayTitle + "). Detailed Mission Tasks Breakdown:"),
                    activeDay.Annex2TasksAnalysis, 50, v => { activeDay.Annex2TasksAnalysis = v; PersistModelToAppData(model); }));

                mainStack.Children.Add(sec4);
            }

            RebuildOpordForm();
            BuilderI18n.LanguageChanged += _ => RebuildOpordForm();
            _cachedOpordOverlay = rootBorder;
            return rootBorder;
        }

        private static void SyncDayRadioChannelsFromFactionGenerator(Window win, string factionName, OpordDayPlanModel day, bool forceOverwrite = false)
        {
            if (day == null) return;
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

                        if (i == 0 && (forceOverwrite || day.PrimaryRadioFreq.Contains("446.00625")))
                            day.PrimaryRadioFreq = role + ": " + formatted;
                        else if (i == 1 && (forceOverwrite || day.BackupRadioFreq.Contains("446.03125")))
                            day.BackupRadioFreq = role + ": " + formatted;
                        else if (i == 2 && (forceOverwrite || day.HqRadioFreq.Contains("446.09375")))
                            day.HqRadioFreq = role + ": " + formatted;
                    }
                }
            }
        }

        private static string FormatSarnegMatrixPreview(string key10)
        {
            string normalized = SarnegDictionaryService.NormalizePolishChars(key10 ?? "PRZEDMIOTY").Trim();
            string padded = normalized.PadRight(10, '_').Substring(0, 10);
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
                : (normalized.Length < 10
                    ? BuilderI18n.T($"⏳ [WPISANO {normalized.Length}/10 LITER — WPISZ 10 UNIKALNYCH LITER]", $"⏳ [TYPED {normalized.Length}/10 LETTERS — ENTER 10 UNIQUE LETTERS]")
                    : (normalized.Length < 10
                    ? BuilderI18n.T($"⏳ [WPISANO {normalized.Length}/10 LITER — WPISZ 10 UNIKALNYCH LITER]", $"⏳ [TYPED {normalized.Length}/10 LETTERS — ENTER 10 UNIQUE LETTERS]")
                    : BuilderI18n.T("⚠️ [UWAGA: POWTÓRZONE LITERY W SŁOWIE!]", "⚠️ [WARNING: DUPLICATE LETTERS IN WORD!]"))));
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





// [START_V34_CLEAN_OPORD_UI_TRANSFORMER]
    public static class V34CleanOpordUiTransformer
    {
        

        public static void ApplyOpordUiRefinements(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w == null) return;

                var model = V34CleanOpordDataEngine.GetLiveOpordModel();
                if (model == null || model.Days == null || model.Days.Count == 0) return;

                int activeIdx = Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1);
                var activeDay = model.Days[activeIdx];

                var all = V34CleanFtakAndFactionEngine.ScanAllWindowRoots(w);
                var opordTitleTb = all.OfType<TextBlock>().FirstOrDefault(t =>
                    (t.Text ?? "").Contains("GENERATOR ROZKAZU BOJOWEGO", StringComparison.OrdinalIgnoreCase) ||
                    (t.Text ?? "").Contains("OPERATION ORDER GENERATOR", StringComparison.OrdinalIgnoreCase));

                if (opordTitleTb == null) return;

                DependencyObject cur = opordTitleTb;
                StackPanel mainOpordStack = null;
                while (cur != null && !(cur is Window))
                {
                    if (cur is StackPanel sp && sp.Children.Count >= 3)
                    {
                        mainOpordStack = sp;
                        break;
                    }
                    cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                }

                if (mainOpordStack == null || mainOpordStack.Tag?.ToString() == "V34_OPORD_UI_READY")
                    return;

                mainOpordStack.Tag = "V34_OPORD_UI_READY";

                var opordElements = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack);

                // 1. Naprawa wszystkich małych przycisków usuwania wiersza ("−"), aby miały Padding = 0 i nie ucinały znaku do ":"
                foreach (var btn in opordElements.OfType<Button>())
                {
                    string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(btn).Trim();
                    if ((btn.Width >= 20 && btn.Width <= 42) || bt == "-" || bt == "−" || bt == ":" || bt == "✖")
                    {
                        btn.Padding = new Thickness(0);
                        btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                        btn.VerticalContentAlignment = VerticalAlignment.Center;
                        if (bt == "-" || bt == ":") btn.Content = "−";
                    }
                }

                // 2. Pierwszy górny boks nagłówkowy OPORD:
                //    - Czysta lista stron w ComboBox (bez PLI / GeoChat)
                //    - Przycisk "🔄 Pobierz z Generatora Stron" na samej górze
                if (mainOpordStack.Children.Count > 0 &&
                    mainOpordStack.Children[0] is Border topBorder &&
                    topBorder.Child is StackPanel topHeaderStack)
                {
                    var topElems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(topHeaderStack);
                    var factionCombo = topElems.OfType<ComboBox>().FirstOrDefault();
                    if (factionCombo != null)
                    {
                        var cleanNames = V34CleanOpordDataEngine.GetCleanFactionNames(w);
                        for (int i = factionCombo.Items.Count - 1; i >= 0; i--)
                        {
                            string itemStr = factionCombo.Items[i]?.ToString() ?? "";
                            if (V34CleanOpordDataEngine.IsGarbageFactionName(itemStr))
                            {
                                factionCombo.Items.RemoveAt(i);
                            }
                        }

                        foreach (string fn in cleanNames)
                        {
                            bool exists = false;
                            foreach (var existing in factionCombo.Items)
                            {
                                if (string.Equals(existing?.ToString(), fn, StringComparison.OrdinalIgnoreCase))
                                {
                                    exists = true;
                                    break;
                                }
                            }
                            if (!exists) factionCombo.Items.Add(fn);
                        }

                        foreach (var item in factionCombo.Items)
                        {
                            if (string.Equals(item?.ToString(), model.TargetFaction, StringComparison.OrdinalIgnoreCase))
                            {
                                factionCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    bool hasTopSyncBtn = false;
                    foreach (var b in topElems.OfType<Button>())
                    {
                        string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(b);
                        if (bt.Contains("Pobierz", StringComparison.OrdinalIgnoreCase) &&
                            bt.Contains("Generatora Stron", StringComparison.OrdinalIgnoreCase))
                        {
                            hasTopSyncBtn = true;
                            b.Content = "🔄 Pobierz z Generatora Stron (Odśwież strony, kanały radiowe i współrzędne AO)";
                            b.PreviewMouseLeftButtonDown += (s, e) =>
                            {
                                e.Handled = true;
                                TriggerFullOpordSyncAndRebuild(w);
                            };
                        }
                    }

                    if (!hasTopSyncBtn)
                    {
                        var topSyncBtn = new Button
                        {
                            Content = "🔄 Pobierz z Generatora Stron (Odśwież strony, kanały radiowe i współrzędne AO)",
                            Height = 36,
                            Margin = new Thickness(0, 6, 0, 8),
                            Padding = new Thickness(16, 6, 16, 6),
                            Background = new SolidColorBrush(Color.FromRgb(14, 52, 64)),
                            Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold,
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        topSyncBtn.Click += (_, __) => TriggerFullOpordSyncAndRebuild(w);

                        int insertPos = topHeaderStack.Children.Count > 1 ? 1 : topHeaderStack.Children.Count;
                        topHeaderStack.Children.Insert(insertPos, topSyncBtn);
                    }
                }

                // 3. Naprawa przycisku "☑️ Generuj tylko aktywny dzień" (styl identyczny jak "☑️ Generuj wszystkie dni" + odznaczanie pozostałych dni)
                Button btnGenAll = null;
                Button btnActiveOnly = null;
                foreach (var btn in opordElements.OfType<Button>())
                {
                    string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(btn);
                    if (bt.Contains("Generuj wszystkie dni", StringComparison.OrdinalIgnoreCase) ||
                        bt.Contains("Generate all days", StringComparison.OrdinalIgnoreCase))
                    {
                        btnGenAll = btn;
                    }
                    else if (bt.Contains("aktywny dzień", StringComparison.OrdinalIgnoreCase) ||
                             bt.Contains("active day", StringComparison.OrdinalIgnoreCase))
                    {
                        btnActiveOnly = btn;
                    }
                }

                if (btnActiveOnly != null)
                {
                    btnActiveOnly.Content = "☑️ Generuj tylko aktywny dzień";
                    if (btnGenAll != null)
                    {
                        btnActiveOnly.Background = btnGenAll.Background;
                        btnActiveOnly.Foreground = btnGenAll.Foreground;
                        btnActiveOnly.BorderBrush = btnGenAll.BorderBrush;
                        btnActiveOnly.BorderThickness = btnGenAll.BorderThickness;
                        btnActiveOnly.Padding = btnGenAll.Padding;
                        btnActiveOnly.Height = btnGenAll.Height;
                        btnActiveOnly.FontWeight = btnGenAll.FontWeight;
                        btnActiveOnly.FontSize = btnGenAll.FontSize;
                    }

                    btnActiveOnly.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        int curDayIdx = Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1);
                        for (int i = 0; i < model.Days.Count; i++)
                        {
                            model.Days[i].IncludeInExport = (i == curDayIdx);
                        }
                        TriggerFullOpordSyncAndRebuild(w);
                    };
                }

                // 4. Sekcja B.2 (METOC): Wyczyść Lat/Lon przy braku AO i zablokuj pobieranie METOC dla pustego arkusza mapy
                RefineMetocSectionUi(w, opordElements, activeDay);

                // 5. Sekcja E (Tabela Łączności): Usuń 3 pola ręczne, pokaż automatyczną tabelę z Generatora Stron + opcjonalną tabelę na ptaszek
                RefineSectionECommsUi(opordElements, activeDay);

                // 6. Punkt rozszerzenia dla Kroku 7 (Załącznik nr 3 Mapa w Sekcji IV oraz eksport Discord)
                V34CleanAnnex3AndDiscordEngine.AttachToOpordForm(w, mainOpordStack, model, activeDay);
            }
            catch { }
        }

        public static void TriggerFullOpordSyncAndRebuild(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                V34CleanOpordDataEngine.SyncFactionsAndMapToOpordModel(w);
                var mRebuild = typeof(OpordBuilderManager).GetMethod(
                    "RebuildOpordForm",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                mRebuild?.Invoke(null, new object[] { w });
            }
            catch { }
        }

        private static void RefineMetocSectionUi(Window win, List<DependencyObject> opordElements, OpordDayPlanModel activeDay)
        {
            var metocHdr = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("B.2. PROGNOZA POGODY", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("B.2. WEATHER FORECAST", StringComparison.OrdinalIgnoreCase));

            if (metocHdr == null) return;

            DependencyObject p = LogicalTreeHelper.GetParent(metocHdr);
            while (p != null && !(p is StackPanel)) p = LogicalTreeHelper.GetParent(p);
            if (!(p is StackPanel metocStack)) return;

            var metocItems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(metocStack);
            var boxes = metocItems.OfType<TextBox>().ToList();
            TextBox locBox = boxes.Count >= 2 ? boxes[1] : null;
            TextBox latBox = boxes.Count >= 3 ? boxes[2] : null;
            TextBox lonBox = boxes.Count >= 4 ? boxes[3] : null;

            V34CleanMapStudioBridge.SyncMapStudioState(win);
            if (!V34CleanMapStudioBridge.HasExplicitAoMarked && (locBox == null || string.IsNullOrWhiteSpace(locBox.Text)))
            {
                if (latBox != null) latBox.Text = "";
                if (lonBox != null) lonBox.Text = "";
                activeDay.LocationLat = "";
                activeDay.LocationLon = "";
            }
            else if (V34CleanMapStudioBridge.HasExplicitAoMarked)
            {
                string latS = V34CleanMapStudioBridge.AoCenterLat.ToString("F4", CultureInfo.InvariantCulture);
                string lonS = V34CleanMapStudioBridge.AoCenterLon.ToString("F4", CultureInfo.InvariantCulture);
                if (latBox != null) latBox.Text = latS;
                if (lonBox != null) lonBox.Text = lonS;
                activeDay.LocationLat = latS;
                activeDay.LocationLon = lonS;
            }

            foreach (var btn in metocItems.OfType<Button>())
            {
                string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(btn);
                if (bt.Contains("Pobierz METOC", StringComparison.OrdinalIgnoreCase) ||
                    bt.Contains("Fetch METOC", StringComparison.OrdinalIgnoreCase))
                {
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        if (locBox != null) activeDay.LocationName = locBox.Text.Trim();
                        if (!V34CleanOpordDataEngine.ValidateAndPrepareMetocBeforeFetch(win))
                        {
                            e.Handled = true;
                            if (latBox != null) latBox.Text = "";
                            if (lonBox != null) lonBox.Text = "";
                        }
                        else
                        {
                            if (locBox != null) locBox.Text = activeDay.LocationName;
                            if (latBox != null) latBox.Text = activeDay.LocationLat;
                            if (lonBox != null) lonBox.Text = activeDay.LocationLon;
                        }
                    };
                }
            }
        }

        private static void RefineSectionECommsUi(List<DependencyObject> opordElements, OpordDayPlanModel activeDay)
        {
            var secEHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("E. Tabela łączności", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("E. Communications", StringComparison.OrdinalIgnoreCase));

            if (secEHeader == null) return;

            secEHeader.Text = "E. Tabela łączności (" + activeDay.DayTitle + " — Automatycznie z Generatora Stron + opcjonalna tabela ręczna):";

            if (LogicalTreeHelper.GetParent(secEHeader) is Panel hdrParent)
            {
                foreach (var b in V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(hdrParent).OfType<Button>())
                {
                    if (V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(b).Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase))
                    {
                        b.Visibility = Visibility.Collapsed;
                        b.Width = 0;
                        b.Height = 0;
                    }
                }
            }

            DependencyObject secParent = LogicalTreeHelper.GetParent(secEHeader);
            while (secParent != null && !(secParent is StackPanel))
                secParent = LogicalTreeHelper.GetParent(secParent);

            if (!(secParent is StackPanel secStack)) return;

            var secItems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(secStack);

            // 1. Usunięcie 3 sztywnych pól ręcznych (Kanał główny, Kanał zapasowy, Kanał HQ) bezpośrednio pod nagłówkiem E
            foreach (var tb in secItems.OfType<TextBox>())
            {
                if (LogicalTreeHelper.GetParent(tb) is Panel fieldPanel)
                {
                    string lblText = string.Join(" ", fieldPanel.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                    if (lblText.Contains("Kanał główny", StringComparison.OrdinalIgnoreCase) ||
                        lblText.Contains("Kanał zapasowy", StringComparison.OrdinalIgnoreCase) ||
                        lblText.Contains("Kanał HQ", StringComparison.OrdinalIgnoreCase) ||
                        lblText.Contains("Primary channel", StringComparison.OrdinalIgnoreCase) ||
                        lblText.Contains("Alternate channel", StringComparison.OrdinalIgnoreCase) ||
                        lblText.Contains("HQ channel", StringComparison.OrdinalIgnoreCase))
                    {
                        fieldPanel.Visibility = Visibility.Collapsed;
                        fieldPanel.Height = 0;
                        fieldPanel.Margin = new Thickness(0);

                        if (LogicalTreeHelper.GetParent(fieldPanel) is Grid threeColGrid && threeColGrid.ColumnDefinitions.Count == 3)
                        {
                            threeColGrid.Visibility = Visibility.Collapsed;
                            threeColGrid.Height = 0;
                            threeColGrid.Margin = new Thickness(0);
                        }
                    }
                }
            }

            // 2. Aktualizacja automatycznej tabeli zaciąganej z Generatora Stron zaraz pod nagłówkiem E
            foreach (var txt in secItems.OfType<TextBlock>())
            {
                if ((txt.Text ?? "").Contains("[ZACIĄGNIĘTE Z GENERATORA STRON", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary))
                {
                    txt.Text = activeDay.SyncedCommsMatrixSummary;
                }
            }

            // 3. Dodanie pod automatyczną tabelą opcjonalnej tabeli "na ptaszek" (☑️ Dodaj ręcznie dodatkowe kanały radiowe w OPORD)
            Border optionalManualTable = BuildOptionalManualCommsBox(activeDay);

            int insertIndex = secStack.Children.Count;
            for (int i = 0; i < secStack.Children.Count; i++)
            {
                if (secStack.Children[i] is TextBlock tb && (tb.Text ?? "").Contains("F.1.", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = i;
                    break;
                }
            }

            secStack.Children.Insert(insertIndex, optionalManualTable);
        }

        private static Border BuildOptionalManualCommsBox(OpordDayPlanModel activeDay)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 12)
            };

            var stack = new StackPanel();
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chk = new CheckBox
            {
                Content = "☑️ Dodaj ręcznie dodatkowe kanały radiowe w OPORD (" + activeDay.DayTitle + ")",
                IsChecked = activeDay.IncludeCustomChannelsTable,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetColumn(chk, 0);
            headerGrid.Children.Add(chk);

            var addRowBtn = new Button
            {
                Content = "➕ Dodaj kanał",
                Height = 30,
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(Color.FromRgb(22, 130, 75)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand,
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed
            };
            Grid.SetColumn(addRowBtn, 1);
            headerGrid.Children.Add(addRowBtn);
            stack.Children.Add(headerGrid);

            var rowsContainer = new StackPanel
            {
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed
            };

            void SaveCustomRowsToModel()
            {
                var lines = new List<string>();
                foreach (var rowGrid in rowsContainer.Children.OfType<Grid>())
                {
                    var tbs = rowGrid.Children.OfType<TextBox>().ToList();
                    if (tbs.Count >= 4)
                    {
                        string r = (tbs[0].Text ?? "").Trim();
                        string b = (tbs[1].Text ?? "").Trim();
                        string f = (tbs[2].Text ?? "").Trim();
                        string t = (tbs[3].Text ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(r) || !string.IsNullOrWhiteSpace(f))
                        {
                            lines.Add("• " + r + " [" + b + "]: " + f + " (" + t + ")");
                        }
                    }
                }
                activeDay.CustomChannelsTableText = string.Join("\n", lines);
            }

            void AddChannelRow(string role, string band, string freq, string tone)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBox MakeCell(string val, int col)
                {
                    var tb = new TextBox
                    {
                        Text = val,
                        Height = 30,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11.5
                    };
                    tb.TextChanged += (_, __) => SaveCustomRowsToModel();
                    Grid.SetColumn(tb, col);
                    return tb;
                }

                g.Children.Add(MakeCell(role, 0));
                g.Children.Add(MakeCell(band, 1));
                g.Children.Add(MakeCell(freq, 2));
                g.Children.Add(MakeCell(tone, 3));

                var delBtn = new Button
                {
                    Content = "−",
                    Width = 32,
                    Height = 30,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(85, 22, 22)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(235, 64, 52)),
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                delBtn.Click += (_, __) =>
                {
                    rowsContainer.Children.Remove(g);
                    SaveCustomRowsToModel();
                };
                Grid.SetColumn(delBtn, 4);
                g.Children.Add(delBtn);

                rowsContainer.Children.Add(g);
            }

            if (!string.IsNullOrWhiteSpace(activeDay.CustomChannelsTableText))
            {
                string[] savedLines = activeDay.CustomChannelsTableText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in savedLines)
                {
                    string clean = line.TrimStart('•', ' ', '\t');
                    Match m = Regex.Match(clean, @"^(.*?)\s*\[(.*?)\]:\s*(.*?)\s*\((.*?)\)$");
                    if (m.Success)
                        AddChannelRow(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
                    else
                        AddChannelRow(clean, "PMR", "CH 1", "CTCSS 67.0 Hz");
                }
            }
            else
            {
                AddChannelRow("Kanał główny (Ręczny)", "PMR", "CH 1 (446.00625 MHz)", "CTCSS 67.0 Hz");
                AddChannelRow("Kanał zapasowy", "PMR", "CH 3 (446.03125 MHz)", "CTCSS 88.5 Hz");
                AddChannelRow("Kanał HQ", "PMR", "CH 8 (446.09375 MHz)", "Brak tonu");
            }

            addRowBtn.Click += (_, __) =>
            {
                AddChannelRow("Kanał dodatkowy", "PMR", "CH 5 (446.05625 MHz)", "CTCSS 110.9 Hz");
                SaveCustomRowsToModel();
            };

            chk.Checked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = true;
                rowsContainer.Visibility = Visibility.Visible;
                addRowBtn.Visibility = Visibility.Visible;
                SaveCustomRowsToModel();
            };

            chk.Unchecked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = false;
                rowsContainer.Visibility = Visibility.Collapsed;
                addRowBtn.Visibility = Visibility.Collapsed;
            };

            stack.Children.Add(rowsContainer);
            border.Child = stack;
            return border;
        }
    }
    // [END_V34_CLEAN_OPORD_UI_TRANSFORMER]

// [START_V34_CLEAN_ANNEX3_DISCORD]
    public static class V34CleanAnnex3AndDiscordEngine
    {
        public static void AttachToOpordForm(Window win, StackPanel mainStack, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            if (mainStack == null || activeDay == null) return;
            try
            {
                var allElems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainStack);

                // 1. Podpięcie akcji pod przyciski eksportu rozkazu OPORD (Druk A4, Kopiuj na Discorda, Zapis do paczki .FTAK)
                foreach (var btn in allElems.OfType<Button>())
                {
                    string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(btn);
                    if (bt.Contains("Drukuj rozkaz", StringComparison.OrdinalIgnoreCase) ||
                        bt.Contains("Generuj OPORD", StringComparison.OrdinalIgnoreCase) ||
                        (bt.Contains("A4", StringComparison.OrdinalIgnoreCase) && !bt.Contains("Safezone", StringComparison.OrdinalIgnoreCase)))
                    {
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            ExportOpordA4Document(model);
                        };
                    }
                    else if (bt.Contains("Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            ExportOpordToDiscord(model);
                        };
                    }
                    else if (bt.Contains("FTAK", StringComparison.OrdinalIgnoreCase) ||
                             bt.Contains("Zapisz rozkaz", StringComparison.OrdinalIgnoreCase))
                    {
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            ExportOpordToFtakAndA4(model);
                        };
                    }
                }

                // 2. Dodanie na stałe w sekcji IV. LOGISTYKA ORAZ ZAŁĄCZNIKI opcjonalnego Załącznika nr 3 (Mapa na "ptaszek")
                var sec4Header = allElems.OfType<TextBlock>().FirstOrDefault(t =>
                    (t.Text ?? "").Contains("IV. LOGISTYKA", StringComparison.OrdinalIgnoreCase) ||
                    (t.Text ?? "").Contains("IV. SERVICE SUPPORT", StringComparison.OrdinalIgnoreCase) ||
                    (t.Text ?? "").Contains("ZAŁĄCZNIKI", StringComparison.OrdinalIgnoreCase));

                if (sec4Header != null)
                {
                    DependencyObject p = LogicalTreeHelper.GetParent(sec4Header);
                    while (p != null && !(p is StackPanel)) p = LogicalTreeHelper.GetParent(p);
                    if (p is StackPanel sec4Stack)
                    {
                        bool alreadyAdded = false;
                        foreach (var child in sec4Stack.Children)
                        {
                            if (child is Border b && b.Tag?.ToString() == "V34_ANNEX3_MAP_BOX")
                            {
                                alreadyAdded = true;
                                break;
                            }
                        }

                        if (!alreadyAdded)
                        {
                            var annex3Box = BuildAnnex3MapBoxUi(win, activeDay);
                            sec4Stack.Children.Add(annex3Box);
                        }
                    }
                }
            }
            catch { }
        }

        private static Border BuildAnnex3MapBoxUi(Window win, OpordDayPlanModel activeDay)
        {
            var cyanBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197));
            var goldBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44));

            var border = new Border
            {
                Tag = "V34_ANNEX3_MAP_BOX",
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = cyanBrush,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 10, 0, 4)
            };

            var st = new StackPanel();
            var chk = new CheckBox
            {
                Content = "☑️ Załącznik 3 (" + activeDay.DayTitle + "): Dołącz mapę operacyjną / szkic AO do rozkazu OPORD",
                IsChecked = activeDay.IncludeAnnex3Map,
                Foreground = cyanBrush,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            st.Children.Add(chk);

            var detailsStack = new StackPanel
            {
                Visibility = activeDay.IncludeAnnex3Map ? Visibility.Visible : Visibility.Collapsed
            };

            var fileRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Text = activeDay.Annex3MapImagePath ?? "",
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89))
            };
            pathBox.TextChanged += (_, __) => activeDay.Annex3MapImagePath = pathBox.Text;
            Grid.SetColumn(pathBox, 0);
            fileRow.Children.Add(pathBox);

            var pickBtn = new Button
            {
                Content = "📂 Wybierz plik mapy (.PNG / .JPG)",
                Height = 30,
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = goldBrush,
                BorderBrush = goldBrush,
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            pickBtn.Click += (_, __) =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Wszystkie pliki (*.*)|*.*"
                };
                if (ofd.ShowDialog(win ?? Application.Current?.MainWindow) == true)
                {
                    pathBox.Text = ofd.FileName;
                    activeDay.Annex3MapImagePath = ofd.FileName;
                }
            };
            Grid.SetColumn(pickBtn, 2);
            fileRow.Children.Add(pickBtn);
            detailsStack.Children.Add(fileRow);

            var notesBox = new TextBox
            {
                Text = activeDay.Annex3MapNotes ?? "Mapa taktyczna 1:5000 MGRS z granicą AO i punktami POI.",
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89))
            };
            notesBox.TextChanged += (_, __) => activeDay.Annex3MapNotes = notesBox.Text;
            detailsStack.Children.Add(notesBox);

            chk.Checked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = true;
                detailsStack.Visibility = Visibility.Visible;
            };
            chk.Unchecked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = false;
                detailsStack.Visibility = Visibility.Collapsed;
            };

            st.Children.Add(detailsStack);
            border.Child = st;
            return border;
        }

        public static List<OpordDayPlanModel> GetDaysForExport(OpordParagraphModel model)
        {
            if (model == null || model.Days == null) return new List<OpordDayPlanModel>();
            var selected = model.Days.Where(d => d.IncludeInExport).ToList();
            if (selected.Count == 0 && model.Days.Count > 0)
            {
                int idx = Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1);
                selected.Add(model.Days[idx]);
            }
            return selected;
        }

        public static string ExportOpordA4Document(OpordParagraphModel model)
        {
            if (model == null) return "";
            V34CleanOpordDataEngine.SyncFactionsAndMapToOpordModel(null);

            string outDir = V34CleanFtakAndFactionEngine.EnsureFactionOutputDir(model.TargetFaction);
            string htmlPath = Path.Combine(outDir, "OPORD_A4_" + V34CleanFtakAndFactionEngine.SanitizeFolderName(model.TargetFaction) + ".html");

            var days = GetDaysForExport(model);
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
            sb.AppendLine("<title>OPORD — " + System.Net.WebUtility.HtmlEncode(model.EventName) + " (" + System.Net.WebUtility.HtmlEncode(model.TargetFaction) + ")</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("@page { size: A4 portrait; margin: 12mm; }");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; background: #060D10; color: #E8F1F5; margin: 0; padding: 16px; font-size: 12px; line-height: 1.45; }");
            sb.AppendLine(".page { max-width: 190mm; margin: 0 auto 24px auto; background: #0A151B; border: 2px solid #1FE0C5; border-radius: 8px; padding: 16px 20px; page-break-after: always; }");
            sb.AppendLine(".banner { text-align: center; padding: 6px; font-weight: bold; background: #122B36; color: #FAD02C; border: 1px solid #FAD02C; margin-bottom: 12px; letter-spacing: 1px; }");
            sb.AppendLine("h1 { margin: 0 0 6px 0; font-size: 20px; color: #1FE0C5; }");
            sb.AppendLine("h2 { margin: 14px 0 6px 0; font-size: 14px; color: #FAD02C; border-bottom: 1px solid #1B3B4B; padding-bottom: 3px; text-transform: uppercase; }");
            sb.AppendLine("h3 { margin: 10px 0 4px 0; font-size: 12.5px; color: #1FE0C5; }");
            sb.AppendLine(".box { background: #081218; border: 1px solid #1B3B4B; border-radius: 5px; padding: 8px 10px; margin-bottom: 8px; white-space: pre-wrap; font-family: Consolas, monospace; font-size: 11.5px; }");
            sb.AppendLine(".prose-box { background: #081218; border: 1px solid #1B3B4B; border-radius: 5px; padding: 8px 10px; margin-bottom: 8px; white-space: pre-wrap; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin: 6px 0 10px 0; font-size: 11.5px; }");
            sb.AppendLine("th, td { border: 1px solid #1B3B4B; padding: 5px 8px; text-align: left; }");
            sb.AppendLine("th { background: #102530; color: #1FE0C5; }");
            sb.AppendLine("@media print { body { background: #fff; color: #000; padding: 0; } .page { background: #fff; color: #000; border: 1px solid #000; margin: 0; } .box, .prose-box { background: #f8f8f8; color: #000; border-color: #888; } th { background: #eee; color: #000; } th, td { border-color: #666; } h1, h2, h3 { color: #000; } .no-print { display: none; } }");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<div class='no-print' style='text-align:center;margin-bottom:14px;'>");
            sb.AppendLine("<button onclick='window.print()' style='padding:10px 24px;background:#1FE0C5;color:#051014;font-weight:bold;border:none;border-radius:6px;cursor:pointer;'>🖨️ DRUKUJ ROZKAZ OPORD A4 / ZAPISZ JAKO PDF</button>");
            sb.AppendLine("</div>");

            foreach (var day in days)
            {
                sb.AppendLine("<div class='page'>");
                sb.AppendLine("<div class='banner'>" + System.Net.WebUtility.HtmlEncode(day.Classification) + "</div>");
                sb.AppendLine("<h1>📋 " + System.Net.WebUtility.HtmlEncode(day.OperationNumber) + " — " + System.Net.WebUtility.HtmlEncode(model.EventName) + "</h1>");
                sb.AppendLine("<p style='margin:2px 0 10px 0;'><b>STRONA:</b> " + System.Net.WebUtility.HtmlEncode(model.TargetFaction) +
                              " &nbsp;|&nbsp; <b>DZIEŃ:</b> " + System.Net.WebUtility.HtmlEncode(day.DayTitle) + " (" + System.Net.WebUtility.HtmlEncode(day.DateIso) + ")" +
                              " &nbsp;|&nbsp; <b>HQ:</b> " + System.Net.WebUtility.HtmlEncode(day.IssuingHq) + "</p>");

                sb.AppendLine("<h2>I. SYTUACJA</h2>");
                sb.AppendLine("<h3>A. Siły Przeciwnika:</h3><div class='prose-box'>" + System.Net.WebUtility.HtmlEncode(day.EnemyForces) + "</div>");
                sb.AppendLine("<h3>B. Siły Własne:</h3><div class='prose-box'>" + System.Net.WebUtility.HtmlEncode(day.FriendlyForces) + "</div>");
                sb.AppendLine("<h3>B.1. Teren i Pogoda (METOC):</h3><div class='box'>" + System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(day.TerrainAndWeather) ? day.MetocQuickSummary : day.TerrainAndWeather) + "</div>");
                sb.AppendLine("<h3>C. Obszar Operacji (AO) i Punkty POI:</h3><div class='box'>" + System.Net.WebUtility.HtmlEncode(day.AreaOfInterestPoiDesc) + "</div>");

                sb.AppendLine("<h2>II. MISJA (5W)</h2>");
                sb.AppendLine("<div class='prose-box' style='border-color:#FAD02C;font-weight:bold;'>" + System.Net.WebUtility.HtmlEncode(day.MissionFullStatement) + "</div>");

                sb.AppendLine("<h2>III. WYKONANIE</h2>");
                sb.AppendLine("<h3>A. Zamiar Dowódcy:</h3><div class='prose-box'>" + System.Net.WebUtility.HtmlEncode(day.CommandersIntent) + "</div>");
                sb.AppendLine("<h3>B. Harmonogram (" + System.Net.WebUtility.HtmlEncode(day.DayTitle) + "):</h3><div class='box'>" + System.Net.WebUtility.HtmlEncode(day.DaySpecificTasks) + "</div>");
                sb.AppendLine("<h3>C. Koordynacja:</h3><div class='prose-box'>" + System.Net.WebUtility.HtmlEncode(day.CoordinationMeasures) + "</div>");

                if (day.SquadRoster != null && day.SquadRoster.Count > 0)
                {
                    sb.AppendLine("<h3>D. Tabela Składu Oddziału i Ról:</h3>");
                    sb.AppendLine("<table><thead><tr><th>Kryptonim</th><th>Rola Główna</th><th>Rola Zapasowa</th><th>Wyposażenie</th></tr></thead><tbody>");
                    foreach (var m in day.SquadRoster)
                    {
                        sb.AppendLine("<tr><td><b>" + System.Net.WebUtility.HtmlEncode(m.Callsign) + "</b></td><td>" +
                                      System.Net.WebUtility.HtmlEncode(m.PrimaryRole) + "</td><td>" +
                                      System.Net.WebUtility.HtmlEncode(m.SecondaryRole) + "</td><td>" +
                                      System.Net.WebUtility.HtmlEncode(m.GearAndTasks) + "</td></tr>");
                    }
                    sb.AppendLine("</tbody></table>");
                }

                sb.AppendLine("<h2>V. DOWODZENIE I ŁĄCZNOŚĆ</h2>");
                sb.AppendLine("<h3>E. Tabela Łączności (Z Generatora Stron):</h3>");
                sb.AppendLine("<div class='box'>" + System.Net.WebUtility.HtmlEncode(day.SyncedCommsMatrixSummary) + "</div>");

                if (day.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(day.CustomChannelsTableText))
                {
                    sb.AppendLine("<h3>E.1. Dodatkowe kanały wpisane ręcznie:</h3>");
                    sb.AppendLine("<div class='box'>" + System.Net.WebUtility.HtmlEncode(day.CustomChannelsTableText) + "</div>");
                }

                sb.AppendLine("<h3>F.1. Krypto SARNEG i Hasła:</h3>");
                sb.AppendLine("<div class='box'><b>Słowo SARNEG:</b> " + System.Net.WebUtility.HtmlEncode(day.SarnegKeyWord10) +
                              "\n<b>Hasła:</b> " + System.Net.WebUtility.HtmlEncode(day.ChallengeAndPassword) +
                              "\n<b>Kryptonimy:</b> " + System.Net.WebUtility.HtmlEncode(day.TargetCodeNames) +
                              "\n<b>Dowodzenie:</b> " + System.Net.WebUtility.HtmlEncode(day.SuccessionOfCommand) + "</div>");

                sb.AppendLine("<h2>IV. LOGISTYKA ORAZ ZAŁĄCZNIKI</h2>");
                sb.AppendLine("<h3>A. Zasady MEDEVAC:</h3><div class='prose-box'>" + System.Net.WebUtility.HtmlEncode(day.MedevacRules) + "</div>");
                sb.AppendLine("<h3>B. Szpejlista:</h3><div class='prose-box'>" + System.Net.WebUtility.HtmlEncode(day.SquadGearChecklist) + "</div>");

                if (day.IncludeAnnex3Map && !string.IsNullOrWhiteSpace(day.Annex3MapImagePath) && File.Exists(day.Annex3MapImagePath))
                {
                    sb.AppendLine("<h2>ZAŁĄCZNIK 3 — MAPA OPERACYJNA (" + System.Net.WebUtility.HtmlEncode(day.DayTitle) + ")</h2>");
                    if (!string.IsNullOrWhiteSpace(day.Annex3MapNotes))
                        sb.AppendLine("<div class='prose-box'>" + System.Net.WebUtility.HtmlEncode(day.Annex3MapNotes) + "</div>");
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(day.Annex3MapImagePath);
                        string ext = Path.GetExtension(day.Annex3MapImagePath).ToLowerInvariant();
                        string mime = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : "image/png";
                        sb.AppendLine("<div style='text-align:center;margin-top:8px;'><img src='data:" + mime + ";base64," +
                                      Convert.ToBase64String(bytes) + "' style='max-width:100%;max-height:200mm;border:2px solid #1FE0C5;'/></div>");
                    }
                    catch { }
                }

                sb.AppendLine("<div class='banner' style='margin-top:14px;'>" + System.Net.WebUtility.HtmlEncode(day.Classification) + "</div>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            File.WriteAllText(htmlPath, sb.ToString(), Encoding.UTF8);
            try { Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true }); } catch { }
            return htmlPath;
        }

        public static void ExportOpordToDiscord(OpordParagraphModel model)
        {
            if (model == null) return;
            V34CleanOpordDataEngine.SyncFactionsAndMapToOpordModel(null);

            var chunks = new List<string>();
            var days = GetDaysForExport(model);

            foreach (var d in days)
            {
                var p1 = new StringBuilder();
                p1.AppendLine("## 📋 " + d.OperationNumber + " | " + model.TargetFaction + " — " + d.DayTitle + " (" + d.DateIso + ")");
                p1.AppendLine("`" + d.Classification + "` | **HQ:** " + d.IssuingHq + " | **AO:** " + (string.IsNullOrWhiteSpace(d.LocationName) ? "Map AO" : d.LocationName));
                p1.AppendLine("**🌦️ METOC:** " + CompactLine(string.IsNullOrWhiteSpace(d.TerrainAndWeather) ? d.MetocQuickSummary : d.TerrainAndWeather));
                p1.AppendLine("**🎯 II. MISJA (5W):**\n> " + d.MissionFullStatement);
                p1.AppendLine("**⚡ III. ZAMIAR:** " + d.CommandersIntent);
                p1.AppendLine("**⏱️ HARMONOGRAM:** " + CompactLine(d.DaySpecificTasks));

                var p2 = new StringBuilder();
                p2.AppendLine("### 📡 ŁĄCZNOŚĆ & SARNEG — " + d.DayTitle + " (" + model.TargetFaction + ")");
                if (!string.IsNullOrWhiteSpace(d.SyncedCommsMatrixSummary))
                    p2.AppendLine("```text\n" + d.SyncedCommsMatrixSummary + "\n```");
                if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                    p2.AppendLine("**➕ Dodatkowe kanały:**\n" + d.CustomChannelsTableText);

                p2.AppendLine("**🔐 SARNEG:** `" + d.SarnegKeyWord10 + "` | **Hasła:** " + d.ChallengeAndPassword);
                p2.AppendLine("**🚑 MEDEVAC:** " + d.MedevacRules + " | **Szpej:** " + d.SquadGearChecklist);

                string s1 = p1.ToString().Trim();
                string s2 = p2.ToString().Trim();

                if (s1.Length + s2.Length + 2 <= 1850)
                {
                    chunks.Add(s1 + "\n\n" + s2);
                }
                else
                {
                    chunks.Add(s1);
                    chunks.Add(s2);
                }
            }

            string finalMd = string.Empty;
            if (chunks.Count == 1) finalMd = chunks[0];
            else
            {
                var sb = new StringBuilder();
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (i > 0) sb.AppendLine("\n--- ✂️ [WIADOMOŚĆ " + (i + 1) + "/" + chunks.Count + "] ✂️ ---\n");
                    sb.AppendLine(chunks[i]);
                }
                finalMd = sb.ToString();
            }

            if (!string.IsNullOrWhiteSpace(finalMd))
            {
                Clipboard.SetText(finalMd);
                MessageBox.Show(
                    "✅ Skopiowano skondensowany rozkaz OPORD bezpośrednio do schowka!\n" +
                    "Liczba wiadomości Discord (<1900 zn.): " + chunks.Count + "\n\n" +
                    "Wklej na kanał Discord kombinacją Ctrl + V.",
                    "💬 Kopiuj na Discorda",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        public static void ExportOpordToFtakAndA4(OpordParagraphModel model)
        {
            string htmlPath = ExportOpordA4Document(model);
            V34CleanFtakAndFactionEngine.EnsureSharedFolderStructure();
            MessageBox.Show(
                "💾 Zapisano rozkaz OPORD oraz zaktualizowano paczkę .FTAK dla strony '" + model.TargetFaction + "':\n" + htmlPath,
                "OPORD -> .FTAK",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static string CompactLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "-";
            return string.Join(" • ", text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0));
        }
    }
    // [END_V34_CLEAN_ANNEX3_DISCORD]
}
