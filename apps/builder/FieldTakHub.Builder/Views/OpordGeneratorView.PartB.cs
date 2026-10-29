#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using FieldTakHub.Builder.Core;

namespace FieldTakHub.Builder.Views
{
    /// <summary>
    /// Moduł 2 (Część B): Sekcja V (Tabela E Łączności bez 3 starych pól + opcjonalna tabela na ptaszek,
    /// Generator Krypto SARNEG 0..9 i Hasła) oraz Sekcja IV (Logistyka, MEDEVAC, Szpejlista i Załączniki 1–3).
    /// </summary>
    public partial class OpordGeneratorView
    {
        partial void BuildSectionsPartB(StackPanel parentStack, OpordDocumentModel doc, OpordDayPlanModel activeDay)
        {
            // =========================================================================
            // 5. SEKCJA V. DOWODZENIE I ŁĄCZNOŚĆ (TABELA E + GENERATOR KRYPTO SARNEG)
            // =========================================================================
            var sec5Card = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentCyanBrush);
            var sec5Stack = new StackPanel();

            sec5Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("V. DOWODZENIE I ŁĄCZNOŚĆ (TABELA E ŁĄCZNOŚCI, KRYPTO SARNEG, ŁAŃCUCH DOWODZENIA)", "V. COMMAND & SIGNAL (TABLE E COMMS MATRIX, SARNEG CRYPTO, SUCCESSION OF COMMAND)"),
                TacticalUiFactory.AccentCyanBrush, 14.5));

            // E. Tabela łączności — bezpośrednio pod nagłówkiem wyświetla automatyczną tabelę z Generatora Stron (bez 3 starych pól ręcznych!)
            sec5Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.Get("SEC_E_HEADER") + " [" + activeDay.DayTitle + "]",
                TacticalUiFactory.AccentGoldBrush, 13));

            var autoCommsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 24, 20)),
                BorderBrush = TacticalUiFactory.AccentGreenBrush,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var autoCommsBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary)
                    ? "Brak zsynchronizowanych kanałów — kliknij górny przycisk 'Pobierz z Generatora Stron'."
                    : activeDay.SyncedCommsMatrixSummary,
                Foreground = TacticalUiFactory.AccentGreenBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            autoCommsBorder.Child = autoCommsBlock;
            sec5Stack.Children.Add(autoCommsBorder);

            // E.1. Opcjonalna tabela "na ptaszek" dla dodatkowych kanałów wpisywanych ręcznie w OPORD
            var customCommsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 20, 27)),
                BorderBrush = TacticalUiFactory.AccentCyanBrush,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var customCommsStack = new StackPanel();

            var customHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            customHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            customHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var customChk = new CheckBox
            {
                Content = LocalizationManager.Get("SEC_E_CUSTOM_CHK") + " (" + activeDay.DayTitle + ")",
                IsChecked = activeDay.IncludeCustomChannelsTable,
                Foreground = TacticalUiFactory.AccentCyanBrush,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            Grid.SetColumn(customChk, 0);
            customHeaderGrid.Children.Add(customChk);

            var addCustomChBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("BTN_ADD_CHANNEL"),
                new SolidColorBrush(Color.FromRgb(22, 130, 75)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush,
                30,
                new Thickness(0));
            addCustomChBtn.Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(addCustomChBtn, 1);
            customHeaderGrid.Children.Add(addCustomChBtn);

            customCommsStack.Children.Add(customHeaderGrid);

            var customRowsPanel = new StackPanel
            {
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed
            };

            void RebuildCustomChannelRows()
            {
                customRowsPanel.Children.Clear();
                foreach (var ch in activeDay.CustomOpordChannels.ToList())
                {
                    var rGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var tRole = TacticalUiFactory.CreateTextBox(ch.Role, false, 30);
                    tRole.TextChanged += (_, __) => ch.Role = tRole.Text;
                    Grid.SetColumn(tRole, 0); rGrid.Children.Add(tRole);

                    var tBand = TacticalUiFactory.CreateTextBox(ch.Band, false, 30, true);
                    tBand.Margin = new Thickness(6, 0, 0, 0);
                    tBand.TextChanged += (_, __) => ch.Band = tBand.Text;
                    Grid.SetColumn(tBand, 1); rGrid.Children.Add(tBand);

                    var tFreq = TacticalUiFactory.CreateTextBox(ch.Frequency, false, 30, true);
                    tFreq.Margin = new Thickness(6, 0, 0, 0);
                    tFreq.TextChanged += (_, __) => ch.Frequency = tFreq.Text;
                    Grid.SetColumn(tFreq, 2); rGrid.Children.Add(tFreq);

                    var tTone = TacticalUiFactory.CreateTextBox(ch.Tone, false, 30, true);
                    tTone.Margin = new Thickness(6, 0, 0, 0);
                    tTone.TextChanged += (_, __) => ch.Tone = tTone.Text;
                    Grid.SetColumn(tTone, 3); rGrid.Children.Add(tTone);

                    // Estetyczny przycisk usuwania wiersza "−" (Padding = 0, nigdy nie ucina znaku do ":")
                    var delBtn = TacticalUiFactory.CreateCompactIconBtn("−", new SolidColorBrush(Color.FromRgb(85, 22, 22)), TacticalUiFactory.AccentRedBrush, 32, 30);
                    delBtn.Click += (_, __) =>
                    {
                        activeDay.CustomOpordChannels.Remove(ch);
                        RebuildCustomChannelRows();
                    };
                    Grid.SetColumn(delBtn, 4); rGrid.Children.Add(delBtn);

                    customRowsPanel.Children.Add(rGrid);
                }
            }

            RebuildCustomChannelRows();

            addCustomChBtn.Click += (_, __) =>
            {
                activeDay.CustomOpordChannels.Add(new VoiceChannelModel
                {
                    Role = "Kanał dodatkowy OPORD",
                    Band = "PMR",
                    Frequency = "CH 6 (446.06875 MHz)",
                    Tone = "CTCSS 110.9 Hz"
                });
                RebuildCustomChannelRows();
            };

            customChk.Checked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = true;
                customRowsPanel.Visibility = Visibility.Visible;
                addCustomChBtn.Visibility = Visibility.Visible;
                if (activeDay.CustomOpordChannels.Count == 0)
                {
                    activeDay.CustomOpordChannels.Add(new VoiceChannelModel());
                    RebuildCustomChannelRows();
                }
            };
            customChk.Unchecked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = false;
                customRowsPanel.Visibility = Visibility.Collapsed;
                addCustomChBtn.Visibility = Visibility.Collapsed;
            };

            customCommsStack.Children.Add(customRowsPanel);
            customCommsBorder.Child = customCommsStack;
            sec5Stack.Children.Add(customCommsBorder);

            // F.1. Generator Tabeli Krypto SARNEG (jednym przyciskiem!) + Wizualna Siatka 0..9
            sec5Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("F.1. GENERATOR TABELI KRYPTO SARNEG (SZYFROWANIE WSPÓŁRZĘDNYCH 0–9) I HASŁA", "F.1. SARNEG CRYPTO TABLE GENERATOR (0–9 COORDINATE CIPHER) & PASSWORDS"),
                TacticalUiFactory.AccentGoldBrush, 13));

            var sarnegTopGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            sarnegTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sarnegTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            sarnegTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sarnegBox = TacticalUiFactory.CreateTextBox(activeDay.SarnegKeyWord10, false, 34, true);
            sarnegBox.MaxLength = 10;
            Grid.SetColumn(sarnegBox, 0);
            sarnegTopGrid.Children.Add(sarnegBox);

            var sarnegMatrixGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (int c = 0; c < 10; c++)
            {
                sarnegMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            void RenderSarnegVisualCells(string rawWord)
            {
                sarnegMatrixGrid.Children.Clear();
                string clean10 = TacticalServices.NormalizePolishAscii10(rawWord);
                for (int i = 0; i < 10; i++)
                {
                    var cellBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(10, 26, 34)),
                        BorderBrush = TacticalUiFactory.BorderCardBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(4, 6, 4, 6),
                        Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0)
                    };
                    var cellStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    cellStack.Children.Add(new TextBlock
                    {
                        Text = i.ToString(),
                        Foreground = TacticalUiFactory.AccentCyanBrush,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    cellStack.Children.Add(new TextBlock
                    {
                        Text = clean10[i].ToString(),
                        Foreground = TacticalUiFactory.AccentGoldBrush,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    cellBorder.Child = cellStack;
                    Grid.SetColumn(cellBorder, i);
                    sarnegMatrixGrid.Children.Add(cellBorder);
                }
            }

            sarnegBox.TextChanged += (_, __) =>
            {
                activeDay.SarnegKeyWord10 = sarnegBox.Text.Trim().ToUpperInvariant();
                RenderSarnegVisualCells(activeDay.SarnegKeyWord10);
            };

            var genSarnegBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.T("🎲 Generuj SARNEG (10 liter bez powtórzeń)", "🎲 Generate SARNEG (10 distinct letters)"),
                new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                TacticalUiFactory.AccentGoldBrush,
                TacticalUiFactory.AccentGoldBrush,
                34,
                new Thickness(0));
            genSarnegBtn.Click += (_, __) =>
            {
                string nextWord = TacticalServices.GetRandomSarnegWord10(activeDay.SarnegKeyWord10);
                activeDay.SarnegKeyWord10 = nextWord;
                sarnegBox.Text = nextWord;
                RenderSarnegVisualCells(nextWord);
            };
            Grid.SetColumn(genSarnegBtn, 2);
            sarnegTopGrid.Children.Add(genSarnegBtn);

            sec5Stack.Children.Add(sarnegTopGrid);
            RenderSarnegVisualCells(activeDay.SarnegKeyWord10);
            sec5Stack.Children.Add(sarnegMatrixGrid);

            // Hasła rozpoznawcze, Kryptonimy oraz Sukcesja Dowodzenia
            var passGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            passGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            passGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            passGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spPass = new StackPanel();
            spPass.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Hasło / Odzew i Liczba kontrolna:", "Challenge / Password & Running Number:")));
            var passBox = TacticalUiFactory.CreateTextBox(activeDay.ChallengeAndPassword);
            passBox.TextChanged += (_, __) => activeDay.ChallengeAndPassword = passBox.Text;
            spPass.Children.Add(passBox);
            Grid.SetColumn(spPass, 0); passGrid.Children.Add(spPass);

            var spCodes = new StackPanel();
            spCodes.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Kryptonimy Obiektów i Punktów:", "Target & Waypoint Codewords:")));
            var codesBox = TacticalUiFactory.CreateTextBox(activeDay.TargetCodeNames);
            codesBox.TextChanged += (_, __) => activeDay.TargetCodeNames = codesBox.Text;
            spCodes.Children.Add(codesBox);
            Grid.SetColumn(spCodes, 2); passGrid.Children.Add(spCodes);

            sec5Stack.Children.Add(passGrid);

            sec5Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Łańcuch Dowodzenia (Succession of Command):", "Succession of Command:")));
            var succBox = TacticalUiFactory.CreateTextBox(activeDay.SuccessionOfCommand);
            succBox.TextChanged += (_, __) => activeDay.SuccessionOfCommand = succBox.Text;
            sec5Stack.Children.Add(succBox);

            sec5Card.Child = sec5Stack;
            parentStack.Children.Add(sec5Card);

            // =========================================================================
            // 6. SEKCJA IV. LOGISTYKA, MEDEVAC, SZPEJLISTA ORAZ ZAŁĄCZNIKI 1, 2 i 3 (MAPA)
            // =========================================================================
            var sec4Card = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentGoldBrush);
            var sec4Stack = new StackPanel();

            sec4Stack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("IV. LOGISTYKA, ZASADY MEDEVAC, SZPEJLISTA ORAZ ZAŁĄCZNIKI (1–3)", "IV. SERVICE SUPPORT, MEDEVAC, GEAR CHECKLIST & ANNEXES (1–3)"),
                TacticalUiFactory.AccentGoldBrush, 14.5));

            sec4Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("A. Zasady Ewakuacji Medycznej (MEDEVAC / Respawn):", "A. Medical Evacuation & Respawn Rules (MEDEVAC):")));
            var medBox = TacticalUiFactory.CreateTextBox(activeDay.MedevacRules, true, 54);
            medBox.TextChanged += (_, __) => activeDay.MedevacRules = medBox.Text;
            medBox.Margin = new Thickness(0, 0, 0, 8);
            sec4Stack.Children.Add(medBox);

            sec4Stack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("B. Obowiązkowe Wyposażenie Indywidualne i Zespołowe (Szpejlista):", "B. Mandatory Individual & Squad Equipment (Gear Checklist):")));
            var gearBox = TacticalUiFactory.CreateTextBox(activeDay.SquadGearChecklist, true, 54);
            gearBox.TextChanged += (_, __) => activeDay.SquadGearChecklist = gearBox.Text;
            gearBox.Margin = new Thickness(0, 0, 0, 12);
            sec4Stack.Children.Add(gearBox);

            // Załączniki 1 i 2
            var chkAnnex1 = new CheckBox
            {
                Content = LocalizationManager.T("☑️ Załącznik 1: Dołącz katalog ról MilSimowych i obowiązków w sekcji", "☑️ Annex 1: Attach MilSim Squad Roles & Duties Catalog"),
                IsChecked = activeDay.IncludeAnnex1Roles,
                Foreground = TacticalUiFactory.TextPrimaryBrush,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };
            chkAnnex1.Checked += (_, __) => activeDay.IncludeAnnex1Roles = true;
            chkAnnex1.Unchecked += (_, __) => activeDay.IncludeAnnex1Roles = false;
            sec4Stack.Children.Add(chkAnnex1);

            var chkAnnex2 = new CheckBox
            {
                Content = LocalizationManager.T("☑️ Załącznik 2: Dołącz tabelę szyfrowania współrzędnych SARNEG", "☑️ Annex 2: Attach SARNEG Coordinate Cipher Table"),
                IsChecked = activeDay.IncludeAnnex2Sarneg,
                Foreground = TacticalUiFactory.TextPrimaryBrush,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };
            chkAnnex2.Checked += (_, __) => activeDay.IncludeAnnex2Sarneg = true;
            chkAnnex2.Unchecked += (_, __) => activeDay.IncludeAnnex2Sarneg = false;
            sec4Stack.Children.Add(chkAnnex2);

            // Załącznik nr 3 — Mapa Operacyjna / Szkic AO ("na ptaszek")
            var annex3Border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = TacticalUiFactory.AccentCyanBrush,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 2)
            };
            var annex3Stack = new StackPanel();

            var chkAnnex3 = new CheckBox
            {
                Content = LocalizationManager.Get("ANNEX_3_CHK") + " (" + activeDay.DayTitle + ")",
                IsChecked = activeDay.IncludeAnnex3Map,
                Foreground = TacticalUiFactory.AccentCyanBrush,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            annex3Stack.Children.Add(chkAnnex3);

            var annex3DetailsPanel = new StackPanel
            {
                Visibility = activeDay.IncludeAnnex3Map ? Visibility.Visible : Visibility.Collapsed
            };

            var mapPickGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            mapPickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mapPickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            mapPickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mapPathBox = TacticalUiFactory.CreateTextBox(activeDay.Annex3MapImagePath, false, 32, true);
            mapPathBox.TextChanged += (_, __) => activeDay.Annex3MapImagePath = mapPathBox.Text;
            Grid.SetColumn(mapPathBox, 0);
            mapPickGrid.Children.Add(mapPathBox);

            var browseMapBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.T("📂 Wybierz plik mapy (.PNG / .JPG)", "📂 Select Map Image (.PNG / .JPG)"),
                new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                TacticalUiFactory.AccentGoldBrush,
                TacticalUiFactory.AccentGoldBrush,
                32,
                new Thickness(0));
            browseMapBtn.Click += (_, __) =>
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Wszystkie pliki (*.*)|*.*"
                };
                if (ofd.ShowDialog(Application.Current?.MainWindow) == true)
                {
                    mapPathBox.Text = ofd.FileName;
                    activeDay.Annex3MapImagePath = ofd.FileName;
                }
            };
            Grid.SetColumn(browseMapBtn, 2);
            mapPickGrid.Children.Add(browseMapBtn);
            annex3DetailsPanel.Children.Add(mapPickGrid);

            annex3DetailsPanel.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Opis / Uwagi do Załącznika nr 3 (Mapa):", "Annex 3 Map Notes & Legend:")));
            var mapNotesBox = TacticalUiFactory.CreateTextBox(activeDay.Annex3MapNotes);
            mapNotesBox.TextChanged += (_, __) => activeDay.Annex3MapNotes = mapNotesBox.Text;
            annex3DetailsPanel.Children.Add(mapNotesBox);

            chkAnnex3.Checked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = true;
                annex3DetailsPanel.Visibility = Visibility.Visible;
            };
            chkAnnex3.Unchecked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = false;
                annex3DetailsPanel.Visibility = Visibility.Collapsed;
            };

            annex3Stack.Children.Add(annex3DetailsPanel);
            annex3Border.Child = annex3Stack;
            sec4Stack.Children.Add(annex3Border);

            sec4Card.Child = sec4Stack;
            parentStack.Children.Add(sec4Card);
        }

        partial void OnExportA4Clicked()
        {
            var doc = ProjectStore.Instance.OpordDocument;
            string htmlPath = OpordExporters.ExportPrintableA4Html(doc, true);
            MessageBox.Show(
                LocalizationManager.T(
                    "✅ Wygenerowano rozkaz bojowy OPORD do druku A4 (HTML / PDF):\n" + htmlPath,
                    "✅ Generated printable A4 OPORD (HTML / PDF):\n" + htmlPath),
                "OPORD A4", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        partial void OnExportDiscordClicked()
        {
            var doc = ProjectStore.Instance.OpordDocument;
            string markdown = OpordExporters.BuildCombinedDiscordMarkdown(doc);
            var chunks = OpordExporters.BuildCondensedDiscordChunks(doc);
            if (!string.IsNullOrWhiteSpace(markdown))
            {
                Clipboard.SetText(markdown);
                MessageBox.Show(
                    LocalizationManager.T(
                        "✅ Skopiowano skondensowany rozkaz OPORD bezpośrednio do schowka!\nLiczba wiadomości Discord (<1900 zn.): " + chunks.Count + "\n\nWklej na kanał Discord kombinacją Ctrl + V.",
                        "✅ Copied condensed OPORD Markdown directly to clipboard!\nDiscord message chunks (<1900 chars): " + chunks.Count + "\n\nPaste in Discord using Ctrl + V."),
                    "💬 Discord Markdown", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        partial void OnSaveToFtakClicked()
        {
            var doc = ProjectStore.Instance.OpordDocument;
            string htmlPath = OpordExporters.ExportPrintableA4Html(doc, false);
            ProjectStore.Instance.BuildAllActiveFactionPackages();
            MessageBox.Show(
                LocalizationManager.T(
                    "💾 Zapisano rozkaz OPORD oraz zaktualizowano paczkę .FTAK dla strony '" + doc.TargetFaction + "':\n" + htmlPath,
                    "💾 Saved OPORD & updated .FTAK package for faction '" + doc.TargetFaction + "':\n" + htmlPath),
                "OPORD -> .FTAK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
