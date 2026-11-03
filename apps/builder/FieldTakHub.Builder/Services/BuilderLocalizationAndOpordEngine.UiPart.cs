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
        public static Action RebuildActiveOpordViewAction;
        internal static string ActiveOpordFactionName => _activeOpordFaction;

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
                RebuildActiveOpordViewAction = RebuildOpordForm;
                mainStack.Children.Clear();
                // [ETAP2_SYNC_PIPELINE] ApplyToOpordForm runs synchronously at the end of RebuildOpordForm
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
    public sealed class OpordManualChannelItem
    {
        public string RoleName { get; set; } = "Kana\u0142 Taktyczny";
        public string BandType { get; set; } = "PMR";
        public string FreqMhz { get; set; } = "CH 1 (446.00625)";
        public string ToneOrCode { get; set; } = "CTCSS 67.0 Hz";
    }

    public static class V34CleanOpordCommsStore
    {
        private static readonly Dictionary<string, bool> ManualOverrideByFaction = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<OpordManualChannelItem>> ManualChannelsByFaction = new Dictionary<string, List<OpordManualChannelItem>>(StringComparer.OrdinalIgnoreCase);

        public static bool IsManualOverrideEnabled(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            return ManualOverrideByFaction.TryGetValue(key, out bool val) && val;
        }

        public static void SetManualOverrideEnabled(string factionName, bool enabled)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            ManualOverrideByFaction[key] = enabled;
            if (enabled && !ManualChannelsByFaction.ContainsKey(key))
            {
                ManualChannelsByFaction[key] = PullLiveChannelsFromFactionGenerator(key);
            }
        }

        public static List<OpordManualChannelItem> PullLiveChannelsFromFactionGenerator(string factionName)
        {
            var result = new List<OpordManualChannelItem>();
            try
            {
                var list = TacticalConfigPanel.GetInternalFactionsList();
                var fac = list.FirstOrDefault(f => string.Equals(f.Name, factionName, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
                if (fac != null && fac.VoiceChannels != null && fac.VoiceChannels.Count > 0)
                {
                    foreach (var vc in fac.VoiceChannels)
                    {
                        result.Add(new OpordManualChannelItem
                        {
                            RoleName = string.IsNullOrWhiteSpace(vc.RoleName) ? "Kana\u0142" : vc.RoleName,
                            BandType = string.IsNullOrWhiteSpace(vc.BandType) ? "PMR" : vc.BandType,
                            FreqMhz = string.IsNullOrWhiteSpace(vc.FreqMhz) ? "-" : vc.FreqMhz,
                            ToneOrCode = string.IsNullOrWhiteSpace(vc.ToneOrCode) ? "Brak tonu" : vc.ToneOrCode
                        });
                    }
                }
            }
            catch { }

            if (result.Count == 0)
            {
                result.Add(new OpordManualChannelItem { RoleName = "Dowodzenie HQ", BandType = "PMR", FreqMhz = "CH 1 (446.00625)", ToneOrCode = "CTCSS 67.0 Hz" });
            }
            return result;
        }

        public static List<OpordManualChannelItem> GetEffectiveChannelsForFaction(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            if (IsManualOverrideEnabled(key))
            {
                if (!ManualChannelsByFaction.TryGetValue(key, out var manualList) || manualList == null)
                {
                    manualList = PullLiveChannelsFromFactionGenerator(key);
                    ManualChannelsByFaction[key] = manualList;
                }
                return manualList;
            }
            return PullLiveChannelsFromFactionGenerator(key);
        }

        public static void ForceResyncFromFactionGenerator(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            ManualChannelsByFaction[key] = PullLiveChannelsFromFactionGenerator(key);
        }
    }

    internal static class V34CleanOpordUiTransformer
    {
        public static void ApplyToOpordForm(Window win, StackPanel mainOpordStack, string activeFactionName, Action rebuildCallback)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (mainOpordStack == null) return;

                var model = OpordBuilderManager.GetOrCreateModel(string.IsNullOrWhiteSpace(activeFactionName) ? "BLUFOR" : activeFactionName);
                OpordDayPlanModel activeDay = null;
                if (model != null && model.Days != null && model.Days.Count > 0)
                {
                    int idx = Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1);
                    activeDay = model.Days[idx];
                }

                // 1. Pasek gorny: Aktualizacja listy stron + dodanie przycisku "Pobierz z Generatora Stron" obok wyboru strony
                UpgradeTopActionBarWithPullButton(w, mainOpordStack, activeFactionName, activeDay, rebuildCallback);

                // 2. Sekcja METOC: Pobieranie wspolrzednych wylacznie z zaznaczonego AO lub miejscowosci (blokada domyslnego arkusza mapy)
                UpgradeMetocSectionValidation(w, mainOpordStack, activeDay);

                // 3. Sekcja III.E Tabela lacznosci: Automatyczne pobieranie kanalow z Generatora Stron + opcjonalna edycja reczna na "ptaszek"
                RebuildCommunicationsTableSection(w, mainOpordStack, activeFactionName, activeDay, rebuildCallback);
                foreach (var smBtn in V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack).OfType<Button>())
                {
                    string cStr = (smBtn.Content?.ToString() ?? "").Trim();
                    if (cStr == "-" || cStr == "\u2212" || cStr == "\u2715" || cStr == "\u2716" || (smBtn.Width >= 20 && smBtn.Width <= 38))
                    {
                        smBtn.Padding = new Thickness(0);
                        smBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
                        smBtn.VerticalContentAlignment = VerticalAlignment.Center;
                        if (cStr == "-") smBtn.Content = "\u2212";
                    }
                }

                // 4. Zalacznik 3 i integracja eksportu
                V34CleanAnnex3AndDiscordEngine.AttachToOpordForm(w, mainOpordStack, model, activeDay, activeFactionName);
            }
            catch { }
        }

        private static void UpgradeTopActionBarWithPullButton(Window win, StackPanel mainOpordStack, string activeFactionName, OpordDayPlanModel activeDay, Action rebuildCallback)
        {
            if (mainOpordStack.Children.Count == 0) return;
            var topCard = mainOpordStack.Children[0] as Border;
            if (topCard == null) return;

            var topNodes = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(topCard);
            var facCombo = topNodes.OfType<ComboBox>().FirstOrDefault();
            if (facCombo != null)
            {
                var liveFactions = TacticalConfigPanel.GetInternalFactionsList();
                var validNames = liveFactions.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n) && !V34CleanOpordDataEngine.IsGarbageFactionName(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (validNames.Count > 0)
                {
                    facCombo.Items.Clear();
                    foreach (string fn in validNames) facCombo.Items.Add(fn);
                    string targetSel = validNames.FirstOrDefault(n => string.Equals(n, activeFactionName, StringComparison.OrdinalIgnoreCase)) ?? validNames[0];
                    facCombo.SelectedItem = targetSel;
                }

                if (LogicalTreeHelper.GetParent(facCombo) is Grid actionBar && actionBar.ColumnDefinitions.Count >= 5)
                {
                    bool alreadyHasTopPullBtn = actionBar.Children.OfType<FrameworkElement>().Any(fe => fe.Tag?.ToString() == "TOP_OPORD_PULL_FACTIONS_BTN");
                    if (!alreadyHasTopPullBtn)
                    {
                        actionBar.ColumnDefinitions[0].Width = new GridLength(335);
                        actionBar.Children.Remove(facCombo);

                        var comboAndPullGrid = new Grid { Tag = "TOP_OPORD_PULL_FACTIONS_BTN", Margin = new Thickness(0, 0, 6, 0) };
                        comboAndPullGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                        comboAndPullGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        facCombo.Margin = new Thickness(0, 0, 5, 0);
                        Grid.SetColumn(facCombo, 0);
                        comboAndPullGrid.Children.Add(facCombo);

                        var btnPullTop = new Button
                        {
                            Content = "\uD83D\uDD04 Pobierz z Generatora Stron",
                            Height = 32,
                            Background = new SolidColorBrush(Color.FromRgb(18, 52, 64)),
                            Foreground = TacticalConfigPanel.BrushAccent,
                            BorderBrush = TacticalConfigPanel.BrushAccent,
                            BorderThickness = new Thickness(1.3),
                            FontWeight = FontWeights.Bold,
                            FontSize = 10.5,
                            Cursor = Cursors.Hand,
                            ToolTip = "Od\u015Bwie\u017C list\u0119 stron konfliktu, cz\u0119stotliwo\u015Bci radiowe, parametry LoRa Meshtastic oraz obszar AO"
                        };
                        btnPullTop.Click += (_, __) =>
                        {
                            V34CleanOpordCommsStore.ForceResyncFromFactionGenerator(activeFactionName);
                            SyncChannelsIntoDayModel(activeFactionName, activeDay);
                            V34CleanOpordDataEngine.SyncFactionsAndMapToOpordModel(win);
                            rebuildCallback?.Invoke();
                        };
                        Grid.SetColumn(btnPullTop, 1);
                        comboAndPullGrid.Children.Add(btnPullTop);

                        Grid.SetColumn(comboAndPullGrid, 0);
                        actionBar.Children.Add(comboAndPullGrid);
                    }
                }
            }

            // Ukryj stary przycisk "Pobierz z Generatora Stron" w dolnych sekcjach formularza
            var allOpordBtns = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack).OfType<Button>().ToList();
            foreach (var b in allOpordBtns)
            {
                if (IsDescendantOf(topCard, b)) continue;
                string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(b);
                if (bt.Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase) ||
                    bt.Contains("Pull from Faction", StringComparison.OrdinalIgnoreCase))
                {
                    b.Visibility = Visibility.Collapsed;
                    b.Width = 0;
                    b.Height = 0;
                    b.Margin = new Thickness(0);
                }
            }
        }

        private static void UpgradeMetocSectionValidation(Window win, StackPanel mainOpordStack, OpordDayPlanModel activeDay)
        {
            bool hasAo = TacticalMapGeneratorView.TryGetMarkedAoCenter(out double aoLat, out double aoLon, out string aoLabel);

            foreach (var border in mainOpordStack.Children.OfType<Border>())
            {
                var nodes = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(border);
                var texts = nodes.OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                bool isMetocCard = texts.Any(t => t.Contains("METOC", StringComparison.OrdinalIgnoreCase) || t.Contains("Prognoza pogody", StringComparison.OrdinalIgnoreCase));
                if (!isMetocCard) continue;

                var textBoxes = nodes.OfType<TextBox>().ToList();
                TextBox locNameBox = null, latBox = null, lonBox = null;

                foreach (var tb in textBoxes)
                {
                    if (LogicalTreeHelper.GetParent(tb) is Panel p)
                    {
                        string lbl = string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                        if (lbl.Contains("Miejscowo", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Rejon AO", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Location", StringComparison.OrdinalIgnoreCase))
                            locNameBox = tb;
                        else if (lbl.Contains("Szer", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Lat", StringComparison.OrdinalIgnoreCase))
                            latBox = tb;
                        else if (lbl.Contains("D\u0142", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Lon", StringComparison.OrdinalIgnoreCase))
                            lonBox = tb;
                    }
                }

                if (hasAo)
                {
                    string latS = aoLat.ToString("0.0000", CultureInfo.InvariantCulture);
                    string lonS = aoLon.ToString("0.0000", CultureInfo.InvariantCulture);
                    if (activeDay != null)
                    {
                        activeDay.LocationLat = latS;
                        activeDay.LocationLon = lonS;
                        if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                            activeDay.LocationName = aoLabel;
                    }
                    if (latBox != null) latBox.Text = latS;
                    if (lonBox != null) lonBox.Text = lonS;
                    if (locNameBox != null && string.IsNullOrWhiteSpace(locNameBox.Text))
                        locNameBox.Text = aoLabel;
                }
                else
                {
                    // Brak zaznaczonego AO na mapie -> wyczysc domyslne wspolrzedne arkusza mapy, jesli nie wpisano miejscowosci
                    bool noTownTyped = locNameBox == null || string.IsNullOrWhiteSpace(locNameBox.Text) || locNameBox.Text.Contains("Obszar AO", StringComparison.OrdinalIgnoreCase);
                    if (noTownTyped)
                    {
                        if (locNameBox != null && locNameBox.Text.Contains("Obszar AO", StringComparison.OrdinalIgnoreCase))
                            locNameBox.Text = "";
                        if (latBox != null) latBox.Text = "";
                        if (lonBox != null) lonBox.Text = "";
                        if (activeDay != null)
                        {
                            if (activeDay.LocationName != null && activeDay.LocationName.Contains("Obszar AO", StringComparison.OrdinalIgnoreCase))
                                activeDay.LocationName = "";
                            activeDay.LocationLat = "";
                            activeDay.LocationLon = "";
                        }
                    }
                }

                foreach (var btn in nodes.OfType<Button>())
                {
                    string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(btn);
                    if (bt.Contains("METOC", StringComparison.OrdinalIgnoreCase) ||
                        bt.Contains("pogod", StringComparison.OrdinalIgnoreCase) ||
                        bt.Contains("Weather", StringComparison.OrdinalIgnoreCase))
                    {
                        if (btn.Tag?.ToString() != "METOC_AO_GUARD_HOOKED")
                        {
                            btn.Tag = "METOC_AO_GUARD_HOOKED";
                            btn.PreviewMouseLeftButtonDown += (s, e) =>
                            {
                                bool currentHasAo = TacticalMapGeneratorView.TryGetMarkedAoCenter(out double cAoLat, out double cAoLon, out string cAoLbl);
                                string typedTown = locNameBox != null ? (locNameBox.Text ?? "").Trim() : (activeDay?.LocationName ?? "").Trim();

                                if (!currentHasAo && string.IsNullOrWhiteSpace(typedTown))
                                {
                                    e.Handled = true;
                                    if (latBox != null) latBox.Text = "";
                                    if (lonBox != null) lonBox.Text = "";
                                    MessageBox.Show(
                                        "Brak zaznaczonego obszaru gry (AO) oraz miejscowo\u015Bci!\n\nAby pobra\u0107 prognoz\u0119 pogody METOC:\n1. Zaznacz Granic\u0119 Terenu Gry (AO) w Generatorze Map 1:1 (dane pobior\u0105 si\u0119 automatycznie),\nLUB\n2. Wpisz nazw\u0119 miejscowo\u015Bci w polu 'Miejscowo\u015B\u0107 / Rejon AO'.",
                                        "METOC \u2014 Wymagane zaznaczenie AO lub miejscowo\u015B\u0107",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                                    return;
                                }

                                if (currentHasAo)
                                {
                                    string latStr = cAoLat.ToString("0.0000", CultureInfo.InvariantCulture);
                                    string lonStr = cAoLon.ToString("0.0000", CultureInfo.InvariantCulture);
                                    if (latBox != null) latBox.Text = latStr;
                                    if (lonBox != null) lonBox.Text = lonStr;
                                    if (locNameBox != null && string.IsNullOrWhiteSpace(locNameBox.Text))
                                        locNameBox.Text = cAoLbl;
                                    if (activeDay != null)
                                    {
                                        activeDay.LocationLat = latStr;
                                        activeDay.LocationLon = lonStr;
                                        if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                                            activeDay.LocationName = cAoLbl;
                                    }
                                }
                            };
                        }
                    }
                }
            }
        }

        public static void SyncChannelsIntoDayModel(string factionName, OpordDayPlanModel activeDay)
        {
            if (activeDay == null) return;
            var channels = V34CleanOpordCommsStore.GetEffectiveChannelsForFaction(factionName);
            if (channels.Count > 0)
            {
                var c0 = channels[0];
                activeDay.PrimaryRadioFreq = $"{c0.RoleName}: {c0.FreqMhz} [{c0.BandType}] ({c0.ToneOrCode})";
            }
            if (channels.Count > 1)
            {
                var c1 = channels[1];
                activeDay.BackupRadioFreq = $"{c1.RoleName}: {c1.FreqMhz} [{c1.BandType}] ({c1.ToneOrCode})";
            }
            if (channels.Count > 2)
            {
                var c2 = channels[2];
                activeDay.HqRadioFreq = $"{c2.RoleName}: {c2.FreqMhz} [{c2.BandType}] ({c2.ToneOrCode})";
            }
        }

        private static Border CreateConsistentOpordSquareActionBtn(string symbol, bool isPlus, string toolTipText, Brush refMinusBg, Brush refMinusBorder, Action onClick)
        {
            Brush bgBrush = isPlus
                ? new SolidColorBrush(Color.FromRgb(16, 52, 34))
                : (refMinusBg ?? new SolidColorBrush(Color.FromRgb(55, 18, 22)));
            Brush borderBrush = isPlus
                ? new SolidColorBrush(Color.FromRgb(46, 204, 113))
                : (refMinusBorder ?? new SolidColorBrush(Color.FromRgb(235, 64, 52)));

            var box = new Border
            {
                Width = 28,
                Height = 26,
                CornerRadius = new CornerRadius(4),
                Background = bgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1.3),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = toolTipText,
                Child = new TextBlock
                {
                    Text = symbol,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            };
            box.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                onClick?.Invoke();
            };
            return box;
        }

        private static void RebuildCommunicationsTableSection(Window win, StackPanel mainOpordStack, string activeFactionName, OpordDayPlanModel activeDay, Action rebuildCallback)
        {
            SyncChannelsIntoDayModel(activeFactionName, activeDay);

            // Pobierz dokladny pedzel tla i ramki przycisku "-" z Sekcji D (Tabela Skladu Oddzialu), aby zachowac 100% spojnosci UI
            Brush sectionDMinusBg = new SolidColorBrush(Color.FromRgb(55, 18, 22));
            Brush sectionDMinusBorder = new SolidColorBrush(Color.FromRgb(235, 64, 52));
            foreach (var existingBtn in V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack).OfType<Button>())
            {
                string cTxt = (existingBtn.Content?.ToString() ?? "").Trim();
                if ((cTxt == "-" || cTxt == "\u2212") && existingBtn.Background != null && existingBtn.BorderBrush != null)
                {
                    sectionDMinusBg = existingBtn.Background;
                    sectionDMinusBorder = existingBtn.BorderBrush;
                    break;
                }
            }

            foreach (var border in mainOpordStack.Children.OfType<Border>())
            {
                var nodes = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(border);
                var texts = nodes.OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                bool isExecSection = texts.Any(t => t.Contains("III. WYKONANIE", StringComparison.OrdinalIgnoreCase) || t.Contains("III. EXECUTION", StringComparison.OrdinalIgnoreCase) || t.Contains("Tabela \u0142\u0105czno\u015Bci", StringComparison.OrdinalIgnoreCase));
                if (!isExecSection) continue;

                var secStack = border.Child as StackPanel;
                if (secStack == null) continue;

                int insertIndex = -1;
                for (int i = 0; i < secStack.Children.Count; i++)
                {
                    var child = secStack.Children[i];
                    var childTexts = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(child).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                    if (childTexts.Any(t => t.Contains("E. Tabela \u0142\u0105czno\u015Bci", StringComparison.OrdinalIgnoreCase) || t.Contains("E. Communications", StringComparison.OrdinalIgnoreCase) || t.Contains("Kana\u0142 g\u0142\u00F3wny", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (insertIndex < 0) insertIndex = i;
                        if (child is FrameworkElement fe && fe.Tag?.ToString() != "REMODELED_OPORD_COMMS_CARD")
                        {
                            fe.Visibility = Visibility.Collapsed;
                            fe.Height = 0;
                            fe.Margin = new Thickness(0);
                        }
                    }
                }

                for (int i = secStack.Children.Count - 1; i >= 0; i--)
                {
                    if ((secStack.Children[i] as FrameworkElement)?.Tag?.ToString() == "REMODELED_OPORD_COMMS_CARD")
                        secStack.Children.RemoveAt(i);
                }

                var liveFactions = TacticalConfigPanel.GetInternalFactionsList();
                var facModel = liveFactions.FirstOrDefault(f => string.Equals(f.Name, activeFactionName, StringComparison.OrdinalIgnoreCase)) ?? liveFactions.FirstOrDefault();
                string meshCh = facModel?.ChannelName ?? (activeFactionName + "-MESH");
                string meshReg = facModel?.LoraRegion ?? "EU_868";
                string meshPreset = facModel?.LoraModemPreset ?? "SHORT_FAST";
                string otsHost = string.IsNullOrWhiteSpace(facModel?.OtsHost) ? "Off-Grid Mesh (Lokalnie)" : facModel.OtsHost;
                string sosPhone = string.IsNullOrWhiteSpace(facModel?.EmergencyPhone) ? "Brak / 112" : facModel.EmergencyPhone;

                bool isManual = V34CleanOpordCommsStore.IsManualOverrideEnabled(activeFactionName);
                var channels = V34CleanOpordCommsStore.GetEffectiveChannelsForFaction(activeFactionName);

                var commsCard = new Border
                {
                    Tag = "REMODELED_OPORD_COMMS_CARD",
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.3),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 6, 0, 10)
                };

                var cStack = new StackPanel();

                var hdrDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rightHeaderControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var chkManual = new CheckBox
                {
                    Content = "\u270F\uFE0F Wprowad\u017A / edytuj cz\u0119stotliwo\u015Bci r\u0119cznie",
                    IsChecked = isManual,
                    Foreground = isManual ? new SolidColorBrush(Color.FromRgb(250, 208, 44)) : Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, isManual ? 10 : 0, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = "Zaznacz ten ptaszek, je\u015Bli chcesz r\u0119cznie edytowa\u0107 lub doda\u0107 cz\u0119stotliwo\u015Bci w OPORD. Gdy odznaczony, tabela pobiera wszystkie kana\u0142y automatycznie z Generatora Stron."
                };
                chkManual.Checked += (_, __) =>
                {
                    V34CleanOpordCommsStore.SetManualOverrideEnabled(activeFactionName, true);
                    SyncChannelsIntoDayModel(activeFactionName, activeDay);
                    rebuildCallback?.Invoke();
                };
                chkManual.Unchecked += (_, __) =>
                {
                    V34CleanOpordCommsStore.SetManualOverrideEnabled(activeFactionName, false);
                    SyncChannelsIntoDayModel(activeFactionName, activeDay);
                    rebuildCallback?.Invoke();
                };
                rightHeaderControls.Children.Add(chkManual);

                if (isManual)
                {
                    var btnTopPlus = CreateConsistentOpordSquareActionBtn("+", true, "Dodaj nowy wiersz kana\u0142u", sectionDMinusBg, sectionDMinusBorder, () =>
                    {
                        channels.Add(new OpordManualChannelItem
                        {
                            RoleName = $"Kana\u0142 {channels.Count + 1}",
                            BandType = "PMR",
                            FreqMhz = $"CH {Math.Clamp(channels.Count + 1, 1, 16)}",
                            ToneOrCode = "CTCSS 67.0 Hz"
                        });
                        SyncChannelsIntoDayModel(activeFactionName, activeDay);
                        rebuildCallback?.Invoke();
                    });
                    btnTopPlus.Margin = new Thickness(0, 0, 4, 0);

                    var btnTopMinus = CreateConsistentOpordSquareActionBtn("\u2212", false, "Usu\u0144 ostatni wiersz kana\u0142u", sectionDMinusBg, sectionDMinusBorder, () =>
                    {
                        if (channels.Count > 1)
                        {
                            channels.RemoveAt(channels.Count - 1);
                            SyncChannelsIntoDayModel(activeFactionName, activeDay);
                            rebuildCallback?.Invoke();
                        }
                    });

                    rightHeaderControls.Children.Add(btnTopPlus);
                    rightHeaderControls.Children.Add(btnTopMinus);
                }

                DockPanel.SetDock(rightHeaderControls, Dock.Right);
                hdrDock.Children.Add(rightHeaderControls);

                hdrDock.Children.Add(new TextBlock
                {
                    Text = $"\uD83D\uDCFB E. TABELA \u0141\u0104CZNO\u015ACI \u2014 STRONA: {activeFactionName.ToUpperInvariant()} " + (isManual ? "[TRYB EDYCJI R\u0118CZNEJ]" : "[AUTO Z GENERATORA STRON]"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
                cStack.Children.Add(hdrDock);

                var digitalInfoBorder = new Border
                {
                    Background = TacticalConfigPanel.BrushInputBg,
                    BorderBrush = TacticalConfigPanel.BrushTileBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                digitalInfoBorder.Child = new TextBlock
                {
                    Text = $"\uD83D\uDCE1 LoRa Meshtastic: {meshCh} ({meshReg} / {meshPreset})   |   \uD83D\uDDA5\uFE0F Serwer OTS: {otsHost}   |   \uD83D\uDEA8 Alarmowy SOS / ORGA: {sosPhone}",
                    Foreground = new SolidColorBrush(Color.FromRgb(210, 232, 240)),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                };
                cStack.Children.Add(digitalInfoBorder);

                var tblHdr = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isManual ? 36 : 0) });

                void AddColTitle(string txt, int col)
                {
                    var t = new TextBlock { Text = txt, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 0, 4, 0) };
                    Grid.SetColumn(t, col);
                    tblHdr.Children.Add(t);
                }
                AddColTitle("Lp.", 0);
                AddColTitle("Rola / Przeznaczenie kana\u0142u", 1);
                AddColTitle("Pasmo", 2);
                AddColTitle("Cz\u0119stotliwo\u015B\u0107 / Kana\u0142", 3);
                AddColTitle("Ton CTCSS / DCS / Kod DMR", 4);
                cStack.Children.Add(tblHdr);

                for (int r = 0; r < channels.Count; r++)
                {
                    var chItem = channels[r];
                    int rowNum = r + 1;

                    var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isManual ? 36 : 0) });

                    var lpBadge = new Border
                    {
                        Background = TacticalConfigPanel.BrushInputBg,
                        BorderBrush = TacticalConfigPanel.BrushTileBorder,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Height = 26,
                        Margin = new Thickness(0, 0, 4, 0),
                        Child = new TextBlock { Text = $"#{rowNum}", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    };
                    Grid.SetColumn(lpBadge, 0);
                    rowGrid.Children.Add(lpBadge);

                    var roleBox = new TextBox
                    {
                        Text = chItem.RoleName,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = Brushes.White,
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    roleBox.TextChanged += (_, __) => { if (isManual) { chItem.RoleName = roleBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(roleBox, 1);
                    rowGrid.Children.Add(roleBox);

                    var bandBox = new TextBox
                    {
                        Text = chItem.BandType,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = TacticalConfigPanel.BrushAccent,
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    bandBox.TextChanged += (_, __) => { if (isManual) { chItem.BandType = bandBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(bandBox, 2);
                    rowGrid.Children.Add(bandBox);

                    var freqBox = new TextBox
                    {
                        Text = chItem.FreqMhz,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = Brushes.White,
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    freqBox.TextChanged += (_, __) => { if (isManual) { chItem.FreqMhz = freqBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(freqBox, 3);
                    rowGrid.Children.Add(freqBox);

                    var toneBox = new TextBox
                    {
                        Text = chItem.ToneOrCode,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, isManual ? 4 : 0, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 220, 228)),
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    toneBox.TextChanged += (_, __) => { if (isManual) { chItem.ToneOrCode = toneBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(toneBox, 4);
                    rowGrid.Children.Add(toneBox);

                    if (isManual)
                    {
                        var btnDel = CreateConsistentOpordSquareActionBtn("\u2212", false, $"Usu\u0144 kana\u0142: {chItem.RoleName}", sectionDMinusBg, sectionDMinusBorder, () =>
                        {
                            if (channels.Count > 1)
                            {
                                channels.Remove(chItem);
                                SyncChannelsIntoDayModel(activeFactionName, activeDay);
                                rebuildCallback?.Invoke();
                            }
                        });
                        btnDel.HorizontalAlignment = HorizontalAlignment.Right;
                        Grid.SetColumn(btnDel, 5);
                        rowGrid.Children.Add(btnDel);
                    }

                    cStack.Children.Add(rowGrid);
                }

                if (isManual)
                {
                    var manualBtnsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                    var btnAddManualCh = new Button
                    {
                        Content = "+ Dodaj kana\u0142 r\u0119czny",
                        Height = 26,
                        Padding = new Thickness(12, 0, 12, 0),
                        Margin = new Thickness(0, 0, 8, 0),
                        Background = new SolidColorBrush(Color.FromRgb(16, 52, 34)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                        BorderThickness = new Thickness(1.2),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    btnAddManualCh.Click += (_, __) =>
                    {
                        channels.Add(new OpordManualChannelItem
                        {
                            RoleName = $"Kana\u0142 {channels.Count + 1}",
                            BandType = "PMR",
                            FreqMhz = $"CH {Math.Clamp(channels.Count + 1, 1, 16)}",
                            ToneOrCode = "CTCSS 67.0 Hz"
                        });
                        SyncChannelsIntoDayModel(activeFactionName, activeDay);
                        rebuildCallback?.Invoke();
                    };

                    var btnResetFromGen = new Button
                    {
                        Content = "\uD83D\uDD04 Przywr\u00F3\u0107 kana\u0142y z Generatora Stron",
                        Height = 26,
                        Padding = new Thickness(12, 0, 12, 0),
                        Background = TacticalConfigPanel.BrushInputBg,
                        Foreground = TacticalConfigPanel.BrushAccent,
                        BorderBrush = TacticalConfigPanel.BrushAccent,
                        BorderThickness = new Thickness(1),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    btnResetFromGen.Click += (_, __) =>
                    {
                        V34CleanOpordCommsStore.ForceResyncFromFactionGenerator(activeFactionName);
                        SyncChannelsIntoDayModel(activeFactionName, activeDay);
                        rebuildCallback?.Invoke();
                    };

                    manualBtnsRow.Children.Add(btnAddManualCh);
                    manualBtnsRow.Children.Add(btnResetFromGen);
                    cStack.Children.Add(manualBtnsRow);
                }

                commsCard.Child = cStack;
                if (insertIndex >= 0 && insertIndex <= secStack.Children.Count)
                    secStack.Children.Insert(insertIndex, commsCard);
                else
                    secStack.Children.Add(commsCard);
                break;
            }
        }

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject descendant)
        {
            DependencyObject cur = descendant;
            while (cur != null)
            {
                if (cur == ancestor) return true;
                cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
            }
            return false;
        }
    }
    // [END_V34_CLEAN_OPORD_UI_TRANSFORMER]

// [START_V34_CLEAN_ANNEX3_DISCORD]
    public sealed class OpordAnnex3State
    {
        public bool Enabled { get; set; } = false;
        public string MapTitle { get; set; } = "Mapa taktyczna rejonu dzia\u0142a\u0144 (AO) \u2014 Arkusz sztabowy";
        public string MapNotes { get; set; } = "Siatka wsp\u00F3\u0142rz\u0119dnych, granica terenu gry (AO) oraz punkty charakterystyczne zgodnie z legend\u0105 mapy.";
        public string ImageBase64 { get; set; } = "";
        public string ImageMimeType { get; set; } = "image/png";
        public string SourceLabel { get; set; } = "Brak wczytanej mapy (kliknij 'Pobierz aktualn\u0105 map\u0119 z Generatora Map 1:1' lub 'Wczytaj plik mapy z dysku')";
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }

    internal static class V34CleanAnnex3AndDiscordEngine
    {
        private static readonly Dictionary<string, OpordAnnex3State> Annex3ByFaction = new Dictionary<string, OpordAnnex3State>(StringComparer.OrdinalIgnoreCase);

        public static OpordAnnex3State GetOrCreateState(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            if (!Annex3ByFaction.TryGetValue(key, out var st) || st == null)
            {
                st = new OpordAnnex3State();
                Annex3ByFaction[key] = st;
            }
            return st;
        }

        public static void AttachToOpordForm(Window win, StackPanel mainStack, OpordParagraphModel model, OpordDayPlanModel activeDay, string activeFactionName = null)
        {
            if (mainStack == null) return;
            string facName = !string.IsNullOrWhiteSpace(activeFactionName)
                ? activeFactionName
                : (!string.IsNullOrWhiteSpace(model?.TargetFaction) ? model.TargetFaction : "BLUFOR");

            var state = GetOrCreateState(facName);

            for (int i = mainStack.Children.Count - 1; i >= 0; i--)
            {
                if (mainStack.Children[i] is FrameworkElement fe && fe.Tag?.ToString() == "V34_ANNEX3_MAP_BOX")
                {
                    mainStack.Children.RemoveAt(i);
                }
            }

            var annex3Card = new Border
            {
                Tag = "V34_ANNEX3_MAP_BOX",
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = state.Enabled ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(state.Enabled ? 1.5 : 1.0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var outerStack = new StackPanel();
            var bodyStack = new StackPanel
            {
                Visibility = state.Enabled ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var chkEnableAnnex3 = new CheckBox
            {
                Content = $"\uD83D\uDDFA\uFE0F ZA\u0141\u0104CZNIK 3. MAPA TAKTYCZNA OPERACJI / SZKIC SYTUACYJNY \u2014 STRONA: {facName.ToUpperInvariant()} (Opcjonalny \u2014 zaznacz, aby do\u0142\u0105czy\u0107 map\u0119 do OPORD)",
                IsChecked = state.Enabled,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            var previewImg = new Image
            {
                MaxHeight = 320,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var previewContainer = new Border
            {
                Background = TacticalConfigPanel.BrushInputBg,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 6, 0, 6),
                Visibility = string.IsNullOrWhiteSpace(state.ImageBase64) ? Visibility.Collapsed : Visibility.Visible,
                Child = previewImg
            };

            var statusLabel = new TextBlock
            {
                Text = state.SourceLabel,
                Foreground = string.IsNullOrWhiteSpace(state.ImageBase64) ? TacticalConfigPanel.BrushMutedText : TacticalConfigPanel.BrushAccent,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };

            void RefreshPreviewFromBase64()
            {
                if (string.IsNullOrWhiteSpace(state.ImageBase64))
                {
                    previewImg.Source = null;
                    previewContainer.Visibility = Visibility.Collapsed;
                    statusLabel.Text = state.SourceLabel;
                    statusLabel.Foreground = TacticalConfigPanel.BrushMutedText;
                    return;
                }
                try
                {
                    byte[] bytes = Convert.FromBase64String(state.ImageBase64);
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(bytes))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                    }
                    state.PixelWidth = bmp.PixelWidth;
                    state.PixelHeight = bmp.PixelHeight;
                    previewImg.Source = bmp;
                    previewContainer.Visibility = Visibility.Visible;
                    statusLabel.Text = state.SourceLabel + $" ({bmp.PixelWidth}\u00D7{bmp.PixelHeight} px | {Math.Max(1, bytes.Length / 1024)} KB)";
                    statusLabel.Foreground = TacticalConfigPanel.BrushAccent;
                }
                catch
                {
                    previewImg.Source = null;
                    previewContainer.Visibility = Visibility.Collapsed;
                }
            }

            void CaptureFromMapGeneratorNow(bool silentIfEmpty)
            {
                try
                {
                    byte[] pngBytes = TacticalMapGeneratorView.CaptureCurrentMapSheetForOpordAnnex3Png(win);
                    if (pngBytes != null && pngBytes.Length > 100)
                    {
                        state.ImageBase64 = Convert.ToBase64String(pngBytes);
                        state.ImageMimeType = "image/png";
                        state.SourceLabel = "\u2713 Pobrano aktualny arkusz mapy z Generatora Map 1:1";
                        RefreshPreviewFromBase64();
                    }
                    else if (!silentIfEmpty)
                    {
                        MessageBox.Show(
                            "Nie uda\u0142o si\u0119 pobra\u0107 obrazu z Generatora Map 1:1. Otw\u00F3rz na chwil\u0119 modu\u0142 Generator Map 1:1 lub wczytaj gotowy plik mapy (.PNG / .JPG) z dysku.",
                            "Za\u0142\u0105cznik 3 \u2014 Mapa OPORD",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                catch { }
            }

            chkEnableAnnex3.Checked += (_, __) =>
            {
                state.Enabled = true;
                bodyStack.Visibility = Visibility.Visible;
                annex3Card.BorderBrush = TacticalConfigPanel.BrushAccent;
                annex3Card.BorderThickness = new Thickness(1.5);
                if (string.IsNullOrWhiteSpace(state.ImageBase64))
                {
                    CaptureFromMapGeneratorNow(silentIfEmpty: true);
                }
            };
            chkEnableAnnex3.Unchecked += (_, __) =>
            {
                state.Enabled = false;
                bodyStack.Visibility = Visibility.Collapsed;
                annex3Card.BorderBrush = TacticalConfigPanel.BrushTileBorder;
                annex3Card.BorderThickness = new Thickness(1.0);
            };
            outerStack.Children.Add(chkEnableAnnex3);

            var btnsRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            btnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            btnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            btnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });

            var btnCaptureMap = new Button
            {
                Content = "\uD83D\uDDFA\uFE0F Pobierz aktualn\u0105 map\u0119 z Generatora Map 1:1",
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnCaptureMap.Click += (_, __) => CaptureFromMapGeneratorNow(silentIfEmpty: false);

            var btnLoadDiskMap = new Button
            {
                Content = "\uD83D\uDCC2 Wczytaj plik mapy z dysku (.PNG / .JPG)...",
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(18, 48, 60)),
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnLoadDiskMap.Click += (_, __) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Wybierz plik obrazu mapy dla Za\u0142\u0105cznika nr 3 OPORD",
                    Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Wszystkie pliki (*.*)|*.*"
                };
                if (dlg.ShowDialog(win) == true && File.Exists(dlg.FileName))
                {
                    try
                    {
                        byte[] raw = File.ReadAllBytes(dlg.FileName);
                        string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                        state.ImageMimeType = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : (ext == ".webp" ? "image/webp" : (ext == ".bmp" ? "image/bmp" : "image/png"));
                        state.ImageBase64 = Convert.ToBase64String(raw);
                        state.SourceLabel = "\u2713 Wczytano plik mapy z dysku: " + Path.GetFileName(dlg.FileName);
                        RefreshPreviewFromBase64();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("B\u0142\u0105d wczytywania obrazu mapy: " + ex.Message, "Za\u0142\u0105cznik 3", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            var btnClearMap = new Button
            {
                Content = "\uD83D\uDDD1\uFE0F Usu\u0144 map\u0119",
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(55, 18, 22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(235, 64, 52)),
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnClearMap.Click += (_, __) =>
            {
                state.ImageBase64 = "";
                state.SourceLabel = "Usuni\u0119to map\u0119 z Za\u0142\u0105cznika nr 3.";
                RefreshPreviewFromBase64();
            };

            Grid.SetColumn(btnCaptureMap, 0);
            Grid.SetColumn(btnLoadDiskMap, 1);
            Grid.SetColumn(btnClearMap, 2);
            btnsRow.Children.Add(btnCaptureMap);
            btnsRow.Children.Add(btnLoadDiskMap);
            btnsRow.Children.Add(btnClearMap);
            bodyStack.Children.Add(btnsRow);

            bodyStack.Children.Add(statusLabel);

            var titleInputsGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            titleInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            var mapTitleBox = (TextBox)TacticalConfigPanel.TextCtrl(state.MapTitle, v => state.MapTitle = v);
            mapTitleBox.Margin = new Thickness(0, 0, 6, 0);
            var mapNotesBox = (TextBox)TacticalConfigPanel.TextCtrl(state.MapNotes, v => state.MapNotes = v);

            var fTitle = TacticalConfigPanel.FieldBox("Tytu\u0142 mapy w Za\u0142\u0105czniku nr 3:", mapTitleBox);
            var fNotes = TacticalConfigPanel.FieldBox("Uwagi / opis pod map\u0105 w Za\u0142\u0105czniku nr 3:", mapNotesBox);
            Grid.SetColumn(fTitle, 0);
            Grid.SetColumn(fNotes, 1);
            titleInputsGrid.Children.Add(fTitle);
            titleInputsGrid.Children.Add(fNotes);
            bodyStack.Children.Add(titleInputsGrid);

            RefreshPreviewFromBase64();
            bodyStack.Children.Add(previewContainer);

            outerStack.Children.Add(bodyStack);
            annex3Card.Child = outerStack;
            int annex2Idx = -1;
            for (int i = 0; i < mainStack.Children.Count; i++)
            {
                var chTexts = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainStack.Children[i]).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                if (chTexts.Any(t => t.Contains("ZA\u0141\u0104CZNIK 2", StringComparison.OrdinalIgnoreCase) || t.Contains("ANNEX 2", StringComparison.OrdinalIgnoreCase)))
                {
                    annex2Idx = i;
                }
            }
            if (annex2Idx >= 0 && annex2Idx + 1 <= mainStack.Children.Count)
                mainStack.Children.Insert(annex2Idx + 1, annex3Card);
            else
                mainStack.Children.Add(annex3Card);
        }

        public static string BuildAnnex3HtmlSection(Window win, string factionName, string dayTitle)
        {
            var st = GetOrCreateState(factionName);
            if (!st.Enabled) return "";

            if (string.IsNullOrWhiteSpace(st.ImageBase64))
            {
                try
                {
                    byte[] autoBytes = TacticalMapGeneratorView.CaptureCurrentMapSheetForOpordAnnex3Png(win);
                    if (autoBytes != null && autoBytes.Length > 100)
                    {
                        st.ImageBase64 = Convert.ToBase64String(autoBytes);
                        st.ImageMimeType = "image/png";
                    }
                }
                catch { }
            }

            string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
            var sb = new StringBuilder();
            sb.AppendLine("<div style='page-break-before:always;margin-top:16px;'>");
            sb.AppendLine("<h2>" + BuilderI18n.T("ZA\u0141\u0104CZNIK 3. MAPA TAKTYCZNA OPERACJI (" + (dayTitle ?? "DZIE\u0143 1").ToUpperInvariant() + ")", "ANNEX 3. TACTICAL OPERATION MAP (" + (dayTitle ?? "DAY 1").ToUpperInvariant() + ")") + "</h2>");
            if (!string.IsNullOrWhiteSpace(st.MapTitle))
            {
                sb.AppendLine("<div class='sub-p'><b>" + E(st.MapTitle) + "</b></div>");
            }
            if (!string.IsNullOrWhiteSpace(st.ImageBase64))
            {
                string mime = string.IsNullOrWhiteSpace(st.ImageMimeType) ? "image/png" : st.ImageMimeType;
                sb.AppendLine("<div style='text-align:center;margin:10px 0;'><img src='data:" + mime + ";base64," + st.ImageBase64 + "' style='max-width:100%;max-height:230mm;height:auto;border:2px solid #111;border-radius:4px;'/></div>");
            }
            else
            {
                sb.AppendLine("<div class='sub-p'><i>[Brak wczytanego obrazu mapy w Za\u0142\u0105czniku nr 3]</i></div>");
            }
            if (!string.IsNullOrWhiteSpace(st.MapNotes))
            {
                sb.AppendLine("<div class='sub-p'>" + E(st.MapNotes) + "</div>");
            }
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        public static string BuildAnnex3DiscordMarkdown(string factionName, string dayTitle)
        {
            var st = GetOrCreateState(factionName);
            if (!st.Enabled) return "";

            var sb = new StringBuilder();
            sb.AppendLine("## \uD83D\uDDFA\uFE0F " + BuilderI18n.T("ZA\u0141\u0104CZNIK 3. MAPA TAKTYCZNA OPERACJI (" + dayTitle + ")", "ANNEX 3. TACTICAL OPERATION MAP (" + dayTitle + ")"));
            if (!string.IsNullOrWhiteSpace(st.MapTitle))
                sb.AppendLine("- **Mapa:** " + st.MapTitle);
            if (!string.IsNullOrWhiteSpace(st.MapNotes))
                sb.AppendLine("- **Uwagi do mapy:** " + st.MapNotes);
            return sb.ToString().TrimEnd();
        }
    }
    // [END_V34_CLEAN_ANNEX3_DISCORD]
}
