#nullable disable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FieldTakHub.Builder.Core;
using FieldTakHub.Builder.Views;

namespace FieldTakHub.Builder
{
    /// <summary>
    /// Główna powłoka aplikacji Field TAK Hub v4.0 napisana od zera.
    /// Zapewnia natychmiastowe przełączanie 4 głównych modułów (0 ms),
    /// zwijany pasek boczny z wyśrodkowanymi ikonami oraz przełącznik języka PL / EN.
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isSidebarCollapsed;
        private int _activeModuleIndex;

        private FactionPackageBuilderView _packagesView;
        private TacticalMapStudioView _mapView;
        private OpordGeneratorView _opordView;
        private UsbProgrammerView _usbView;

        public MainWindow()
        {
            InitializeComponent();
            InitializeViewsAndBindings();
        }

        private void InitializeViewsAndBindings()
        {
            ProjectStore.Instance.EnsureWorkspaceFolders();

            _packagesView = new FactionPackageBuilderView();
            _mapView = new TacticalMapStudioView();
            _opordView = new OpordGeneratorView();
            _usbView = new UsbProgrammerView();

            ModuleHostGrid.Children.Add(_packagesView);
            ModuleHostGrid.Children.Add(_mapView);
            ModuleHostGrid.Children.Add(_opordView);
            ModuleHostGrid.Children.Add(_usbView);

            LocalizationManager.Instance.LanguageChanged += ApplyLocalizedLabels;
            ApplyLocalizedLabels();
            SwitchToModule(0);
        }

        public void SwitchToModule(int moduleIndex)
        {
            _activeModuleIndex = moduleIndex;

            // Przy przełączeniu na Generator OPORD (indeks 2) automatycznie odśwież dane z Generatora Stron i Mapy!
            if (moduleIndex == 2)
            {
                ProjectStore.Instance.SyncGeneratorsToOpord();
                _opordView.RefreshFromStore();
            }
            else if (moduleIndex == 3)
            {
                _usbView.RefreshFromStore();
            }

            _packagesView.Visibility = (moduleIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            _mapView.Visibility = (moduleIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            _opordView.Visibility = (moduleIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            _usbView.Visibility = (moduleIndex == 3) ? Visibility.Visible : Visibility.Collapsed;

            UpdateNavButtonStyle(NavBtnPackages, moduleIndex == 0);
            UpdateNavButtonStyle(NavBtnMap, moduleIndex == 1);
            UpdateNavButtonStyle(NavBtnOpord, moduleIndex == 2);
            UpdateNavButtonStyle(NavBtnUsb, moduleIndex == 3);
        }

        private static void UpdateNavButtonStyle(Button btn, bool isActive)
        {
            if (btn == null) return;
            if (isActive)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(18, 48, 61));
                btn.Foreground = TacticalUiFactory.AccentCyanBrush;
                btn.BorderBrush = TacticalUiFactory.AccentCyanBrush;
                btn.BorderThickness = new Thickness(1.5);
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(13, 29, 38));
                btn.Foreground = TacticalUiFactory.TextPrimaryBrush;
                btn.BorderBrush = TacticalUiFactory.BorderCardBrush;
                btn.BorderThickness = new Thickness(1);
            }
        }

        private void OnSidebarToggleClicked(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = !_isSidebarCollapsed;
            SidebarColumn.Width = new GridLength(_isSidebarCollapsed ? 60 : 265);
            SidebarToggleBtn.Content = _isSidebarCollapsed ? "❯" : "❮";

            Visibility textVis = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            BrandHeaderPanel.Visibility = textVis;
            NavLblPackages.Visibility = textVis;
            NavLblMap.Visibility = textVis;
            NavLblOpord.Visibility = textVis;
            NavLblUsb.Visibility = textVis;
            LangBtnText.Visibility = textVis;
            OpenSharedQuickText.Visibility = textVis;
        }

        private void OnLanguageToggleClicked(object sender, RoutedEventArgs e)
        {
            LocalizationManager.Instance.ToggleLanguage();
        }

        private void ApplyLocalizedLabels()
        {
            Title = LocalizationManager.Get("APP_TITLE");
            NavLblPackages.Text = LocalizationManager.Get("NAV_PACKAGES");
            NavLblMap.Text = LocalizationManager.Get("NAV_MAP");
            NavLblOpord.Text = LocalizationManager.Get("NAV_OPORD");
            NavLblUsb.Text = LocalizationManager.Get("NAV_USB");

            NavBtnPackages.ToolTip = LocalizationManager.Get("NAV_PACKAGES");
            NavBtnMap.ToolTip = LocalizationManager.Get("NAV_MAP");
            NavBtnOpord.ToolTip = LocalizationManager.Get("NAV_OPORD");
            NavBtnUsb.ToolTip = LocalizationManager.Get("NAV_USB");

            LangFlagIcon.Text = LocalizationManager.Instance.IsPolish ? "🇵🇱" : "🇬🇧";
            LangBtnText.Text = LocalizationManager.Instance.IsPolish ? "JĘZYK: PL (ZMIEŃ NA EN)" : "LANG: EN (SWITCH TO PL)";
            OpenSharedQuickText.Text = LocalizationManager.T("Folder wspolne/ i out/", "Shared & out/ Folders");

            _packagesView?.OnLanguageChanged();
            _mapView?.OnLanguageChanged();
            _opordView?.OnLanguageChanged();
            _usbView?.OnLanguageChanged();
        }

        private void OnNavPackagesClicked(object sender, RoutedEventArgs e) => SwitchToModule(0);
        private void OnNavMapClicked(object sender, RoutedEventArgs e) => SwitchToModule(1);
        private void OnNavOpordClicked(object sender, RoutedEventArgs e) => SwitchToModule(2);
        private void OnNavUsbClicked(object sender, RoutedEventArgs e) => SwitchToModule(3);

        private void OnOpenSharedFolderClicked(object sender, RoutedEventArgs e)
        {
            ProjectStore.Instance.OpenSharedFolderInExplorer();
        }
    }
}
