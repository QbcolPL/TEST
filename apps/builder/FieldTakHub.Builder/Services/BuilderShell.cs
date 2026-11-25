using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FieldTakHub.Builder.Services.Shell;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Okno główne Buildera (etap 4 uproszczenia interfejsu): skróty całego okna (Ctrl+S, Ctrl+Shift+S,
    /// Ctrl+O, Ctrl+N, Alt+1…4, F1), znacznik niezapisanych zmian w pasku bocznym i na pasku tytułu,
    /// powrót do tego samego pliku projektu po ponownym uruchomieniu oraz ściągawka skrótów.
    /// </summary>
    internal static class BuilderShell
    {
        private static readonly HashSet<int> AttachedWindows = new HashSet<int>();

        /// <summary>Kolor nazwy projektu z niezapisanymi zmianami (ten sam żółty co ostrzeżenia w Builderze).</summary>
        internal static readonly SolidColorBrush UnsavedBrush = TacticalConfigPanel.CreateFrozenBrush(250, 208, 44);

        private static Window? _window;
        private static string _baseTitle = "";
        private static string _baseline = "";
        private static bool _unsaved;
        private static bool _tracking;
        private static bool _startupDone;
        private static int _rebaseGeneration;
        private static DispatcherTimer? _checkTimer;
        private static string _restoredProjectPath = "";
        private static string _restoredBaseline = "";

        /// <summary>Stan projektu różni się od zapisanego w pliku (albo od stanu zaraz po otwarciu lub założeniu projektu).</summary>
        public static bool HasUnsavedChanges => _unsaved;

        internal static bool IsEnglish => string.Equals(BuilderI18n.CurrentLang, "EN", StringComparison.OrdinalIgnoreCase);

        public static void Attach(Window win)
        {
            if (win == null || !AttachedWindows.Add(win.GetHashCode())) return;
            _window = win;
            _baseTitle = string.IsNullOrWhiteSpace(win.Title) ? "Field TAK Hub Builder" : win.Title.Trim();

            // Bąbelkowy KeyDown: pola tekstowe i generator map obsługują swoje klawisze pierwsze.
            win.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) => OnWindowKeyDown(win, e)), false);

            // Zmiany bez MarkDirty (przesunięcie mapy, nazwa wydarzenia) sprawdza się po kliknięciu, puszczeniu klawisza
            // i wyjściu z pola — z opóźnieniem, raz po serii zdarzeń.
            win.AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler((_, __) => ScheduleCheck()), true);
            win.AddHandler(UIElement.PreviewKeyUpEvent, new KeyEventHandler((_, __) => ScheduleCheck()), true);
            win.AddHandler(UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, __) => ScheduleCheck()), true);
            TacticalConfigPanel.ContentChanged += ScheduleCheck;
            BuilderI18n.LanguageChanged += _ => RefreshIndicators();

            // Po przywróceniu sesji i zbudowaniu wszystkich modułów (najniższy priorytet przed bezczynnością systemu).
            win.Dispatcher.BeginInvoke(new Action(() => StartTracking(win)), DispatcherPriority.ApplicationIdle);
        }

        // ------------------------------------------------------------------ stan projektu

        private static void StartTracking(Window win)
        {
            try
            {
                string restored = _restoredProjectPath;
                if (string.IsNullOrEmpty(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath)
                    && !string.IsNullOrWhiteSpace(restored) && File.Exists(restored))
                {
                    BuilderCoreSidebarAndProjectEngine.RestoreProjectFilePath(restored);
                }

                string current = CurrentSignature(win);
                bool hasFile = !string.IsNullOrEmpty(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath);
                _baseline = hasFile && !string.IsNullOrEmpty(_restoredBaseline) ? _restoredBaseline : current;
                _unsaved = current.Length > 0 && current != _baseline;
            }
            catch
            {
                _unsaved = false;
            }
            _startupDone = true;
            _tracking = true;
            RefreshIndicators();
        }

        private static string CurrentSignature(Window? win)
        {
            try { return ProjectSignature.Compute(TacticalConfigPanel.BuildFullWorkspaceStateJson(win)); }
            catch { return ""; }
        }

        private static void ScheduleCheck()
        {
            var win = _window;
            if (!_tracking || win == null) return;
            if (!win.Dispatcher.CheckAccess())
            {
                win.Dispatcher.BeginInvoke(new Action(ScheduleCheck));
                return;
            }
            if (_checkTimer == null)
            {
                var timer = new DispatcherTimer(DispatcherPriority.Background, win.Dispatcher) { Interval = TimeSpan.FromMilliseconds(700) };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    CheckNow();
                };
                _checkTimer = timer;
            }
            _checkTimer.Stop();
            _checkTimer.Start();
        }

        private static void CheckNow()
        {
            var win = _window;
            if (!_tracking || win == null) return;
            string current = CurrentSignature(win);
            if (current.Length == 0) return;
            bool unsaved = current != _baseline;
            if (unsaved == _unsaved) return;
            _unsaved = unsaved;
            RefreshIndicators();
        }

        /// <summary>Po zapisie do pliku: bieżący stan jest tym, co jest w pliku.</summary>
        public static void MarkSaved()
        {
            var win = _window;
            if (win == null) return;
            _rebaseGeneration++;
            string current = CurrentSignature(win);
            if (current.Length > 0) _baseline = current;
            _unsaved = false;
            _startupDone = true;
            _tracking = true;
            RefreshIndicators();
            PersistSession(win);
        }

        /// <summary>Po otwarciu albo założeniu projektu: stan odniesienia liczony dopiero, gdy moduły skończą przebudowę.</summary>
        public static void MarkProjectReplaced()
        {
            var win = _window;
            if (win == null) return;
            _tracking = false;
            _unsaved = false;
            _startupDone = true;
            RefreshIndicators();
            int generation = ++_rebaseGeneration;
            win.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != _rebaseGeneration) return;
                _baseline = CurrentSignature(win);
                _unsaved = false;
                _tracking = true;
                RefreshIndicators();
                PersistSession(win);
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>Zamykanie okna: sprawdza stan od razu, bez czekania na opóźnione sprawdzenie.</summary>
        public static bool HasUnsavedChangesNow(Window? win)
        {
            if (!_tracking) return true; // okno zamykane, zanim stan został zmierzony — pytamy jak dotąd
            string current = CurrentSignature(win ?? _window);
            if (current.Length == 0) return true;
            _unsaved = current != _baseline;
            return _unsaved;
        }

        private static void PersistSession(Window win)
        {
            try { TacticalConfigPanel.SaveSessionStateAuto(win); } catch { }
        }

        /// <summary>Autozapis sesji pamięta plik projektu i odcisk jego zapisanego stanu (dla znacznika po ponownym uruchomieniu).</summary>
        public static void WriteSessionExtras(JsonObject root)
        {
            if (root == null) return;
            string path = BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath ?? "";
            string baseline = _baseline;
            if (!_startupDone)
            {
                // Autozapis przed końcem startu okna: przywrócone wartości nie zostały jeszcze użyte — nie wolno ich zgubić.
                if (string.IsNullOrEmpty(path)) path = _restoredProjectPath;
                baseline = _restoredBaseline;
            }
            root[SessionKeys.ProjectPath] = path;
            root[SessionKeys.ProjectBaseline] = string.IsNullOrEmpty(path) ? "" : baseline;
        }

        public static void ReadSessionExtras(JsonObject root)
        {
            _restoredProjectPath = TextOf(root, SessionKeys.ProjectPath);
            _restoredBaseline = TextOf(root, SessionKeys.ProjectBaseline);
        }

        private static string TextOf(JsonObject? root, string key)
        {
            try
            {
                return root?[key] is JsonValue value && value.TryGetValue(out string? text) && text != null ? text : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Nazwa projektu w pasku bocznym i tytuł okna — po zmianie stanu, pliku albo języka.</summary>
        public static void RefreshIndicators()
        {
            try
            {
                BuilderCoreSidebarAndProjectEngine.RefreshProjectLabel();
                var win = _window;
                if (win != null)
                {
                    win.Title = ProjectTitleText.WindowTitle(
                        BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath, _unsaved, _baseTitle, IsEnglish);
                }
            }
            catch { }
        }

        /// <summary>
        /// Podpowiedź jako obiekt ToolTip, nie tekst: tłumacz interfejsu (BuilderI18n) zapamiętuje tekstowe podpowiedzi
        /// przy pierwszym przejściu i potem je przywraca, a te zmieniają się ze stanem projektu i językiem.
        /// </summary>
        internal static void SetLiveToolTip(FrameworkElement? element, string text)
        {
            if (element == null) return;
            if (element.ToolTip is ToolTip tip) tip.Content = text;
            else element.ToolTip = new ToolTip { Content = text };
        }

        // ------------------------------------------------------------------ skróty klawiszowe

        private static void OnWindowKeyDown(Window win, KeyEventArgs e)
        {
            if (e.Handled) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys modifiers = Keyboard.Modifiers;

            if (modifiers == ModifierKeys.Control && key == Key.S)
            {
                e.Handled = true;
                RunAfterKey(win, () => BuilderCoreSidebarAndProjectEngine.SaveProjectQuick(win));
            }
            else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S)
            {
                e.Handled = true;
                RunAfterKey(win, () => BuilderCoreSidebarAndProjectEngine.SaveProjectAs(win));
            }
            else if (modifiers == ModifierKeys.Control && key == Key.O)
            {
                e.Handled = true;
                RunAfterKey(win, () => BuilderCoreSidebarAndProjectEngine.OpenProjectInteractive(win));
            }
            else if (modifiers == ModifierKeys.Control && key == Key.N)
            {
                e.Handled = true;
                RunAfterKey(win, () => BuilderCoreSidebarAndProjectEngine.CreateNewZeroProjectInteractive(win));
            }
            else if (modifiers == ModifierKeys.Alt && ShellShortcuts.ModuleIndexForDigit(TopRowDigit(key)) is int module)
            {
                e.Handled = true;
                BuilderCoreSidebarAndProjectEngine.SwitchActiveModule(win, (BuilderActiveModule)module);
            }
            else if (modifiers == ModifierKeys.None && key == Key.F1)
            {
                e.Handled = true;
                RunAfterKey(win, () => ShowShortcutSheet(win));
            }
        }

        /// <summary>Okna dialogowe otwiera się po obsłudze klawisza, a nie w jej trakcie.</summary>
        private static void RunAfterKey(Window win, Action action) =>
            win.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); } catch { }
            }), DispatcherPriority.Input);

        /// <summary>Tylko górny rząd cyfr: Alt z cyframi klawiatury numerycznej wpisuje znaki (kody Alt).</summary>
        private static int TopRowDigit(Key key) => key >= Key.D0 && key <= Key.D9 ? key - Key.D0 : -1;

        // ------------------------------------------------------------------ ściągawka (F1)

        public static void ShowShortcutSheet(Window owner)
        {
            bool en = IsEnglish;
            var dialog = new Window
            {
                Title = en ? "Keyboard shortcuts" : "Skróty klawiszowe",
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Background = TacticalConfigPanel.BrushCardBg,
                Foreground = Brushes.White
            };

            var layout = new Grid { Margin = new Thickness(18, 16, 18, 14) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var windowTable = new Grid();
            windowTable.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            windowTable.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            windowTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var windowHeading = SheetHeading(en ? "Whole window" : "Całe okno");
            Grid.SetColumnSpan(windowHeading, 2);
            windowTable.Children.Add(windowHeading);
            foreach (var entry in ShellShortcuts.WindowShortcuts)
            {
                int row = windowTable.RowDefinitions.Count;
                windowTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var keys = new TextBlock
                {
                    Text = entry.Keys,
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 3, 18, 3)
                };
                var text = new TextBlock
                {
                    Text = entry.Text(en),
                    Foreground = Brushes.White,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                Grid.SetRow(keys, row);
                Grid.SetRow(text, row);
                Grid.SetColumn(text, 1);
                windowTable.Children.Add(keys);
                windowTable.Children.Add(text);
            }
            layout.Children.Add(windowTable);

            string mapHelp = TacticalMapGeneratorView.MapShortcutsHelpText;
            int firstBreak = mapHelp.IndexOf('\n');
            if (firstBreak > 0) mapHelp = mapHelp.Substring(firstBreak + 1); // pierwszy wiersz to tytuł — nagłówek kolumny go zastępuje
            int windowPart = mapHelp.IndexOf("\n\nCałe okno:", StringComparison.Ordinal);
            if (windowPart > 0) mapHelp = mapHelp.Substring(0, windowPart); // skróty całego okna są już w lewej kolumnie
            var mapColumn = new StackPanel();
            mapColumn.Children.Add(SheetHeading(en ? "Map generator" : "Generator map"));
            mapColumn.Children.Add(new TextBlock
            {
                Text = mapHelp,
                Foreground = Brushes.White,
                FontSize = 11,
                LineHeight = 17,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(mapColumn, 2);
            layout.Children.Add(mapColumn);

            var close = new Button
            {
                Content = en ? "Close" : "Zamknij",
                IsCancel = true,
                IsDefault = true,
                MinWidth = 110,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            close.Click += (_, __) => dialog.Close();
            Grid.SetRow(close, 1);
            Grid.SetColumnSpan(close, 3);
            layout.Children.Add(close);

            dialog.Content = layout;
            dialog.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.F1) return;
                e.Handled = true;
                dialog.Close();
            };
            dialog.ShowDialog();
        }

        private static TextBlock SheetHeading(string text) => new TextBlock
        {
            Text = text,
            Foreground = TacticalConfigPanel.BrushAccent,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }
}
