using System.IO;
using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FieldTakHub.Builder.Models;
using FieldTakHub.Builder.Services;
using Microsoft.Win32;

namespace FieldTakHub.Builder;

public partial class MainWindow : Window
{
    private readonly ProjectService _projects = new();
    private readonly SourceAnalyzer _analyzer = new();
    private readonly FtakPackageBuilder _builder = new();
    private readonly DistributionServer _server = new();
    private readonly QrService _qr = new();
    private readonly ServerTextConfigService _serverText = new();
    private readonly ServerDiagnosticsService _diagnostics = new();
    private readonly SigningKeyService _signing = new();
    private readonly UpdateService _updates = new();
    private readonly WorkspaceService _workspace = new();
    private readonly LegacyPackageImporter _legacy = new();
    private readonly FtakPackageImporter _ftakImporter = new();
    private readonly CloudDistributionService _cloud = new();
    private IReadOnlyList<ContentItem> _items = Array.Empty<ContentItem>();
    private string? _lastPackage;
    private byte[]? _lastQrPng;
    private string? _lastQrDeepLink;
    private bool _languageReady;
    private bool _uiReady;
    private string? _currentProjectPath;
    private bool _contentsExpanded = true;
    private bool _extraOptionsExpanded = false;

    public MainWindow()
    {
        InitializeComponent();
        // WPF raises SelectionChanged while InitializeComponent is still building the visual tree.
        // Do not touch named controls from BuilderModeBox_SelectionChanged until all fields exist.
        _uiReady = true;
        ConfigureResponsiveWindow();
        ApplyBuilderMode();
        PublisherBox.Text = Environment.UserName;
        var layout = _workspace.EnsureDefaultProject();
        SourceBox.Text = layout.SourceDirectory;
        OutputBox.Text = layout.OutputDirectory;
        _currentProjectPath = File.Exists(layout.ProjectFile) ? layout.ProjectFile : null;
        SyncNavigationServerFields();
        SyncNavigationAtakFields();
        // Do not perform cryptographic key creation/loading during the WPF constructor.
        // NSec loads native crypto components on first use; a failure there must not
        // prevent the Builder window from starting. The fingerprint is initialized
        // after the window is loaded and failures are shown as a non-fatal status.
        FingerprintRun.Text = T("FingerprintUnavailable", "not initialized");
        _languageReady = true;
        LocalizationService.Apply("pl");
        RefreshFolderCounts();
        LoadMeshtasticConfig();
        LoadEnrollmentConfig();
        SyncExtraOptionsFields();
        Loaded += async (_, _) =>
        {
            EnsureCurrentStructure(log: true);
            TryRefreshFingerprint();
            await CheckUpdatesAsync(silent: true);
        };
    }


    private void DarkCombo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox combo || !combo.IsEnabled) return;

        // The arrow button already controls IsDropDownOpen. Do not toggle it a second time.
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null && !ReferenceEquals(current, combo))
        {
            if (current is ToggleButton) return;
            current = VisualTreeHelper.GetParent(current);
        }

        combo.Focus();
        combo.IsDropDownOpen = true;
        e.Handled = true;
    }


    // Navigation no longer duplicates server/ATAK controls.
    // The left menu navigates directly to the canonical Advanced Mode controls.
    private void SyncNavigationServerFields() { }
    private void SyncNavigationAtakFields() { }

    private void SetNavigationActive(Button active)
    {
        foreach (var b in new[] { NavPackageButton, NavServerButton, NavMeshtasticButton, NavAtakButton, NavCertsButton, NavSettingsButton })
        {
            b.Background = ReferenceEquals(b, active) ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10,52,55)) : System.Windows.Media.Brushes.Transparent;
            b.BorderBrush = ReferenceEquals(b, active) ? (System.Windows.Media.Brush)FindResource("Accent") : System.Windows.Media.Brushes.Transparent;
            b.BorderThickness = ReferenceEquals(b, active) ? new Thickness(4,0,0,0) : new Thickness(0);
        }
    }

    private void NavigateAdvanced(FrameworkElement target, Button active)
    {
        BuilderModeBox.SelectedIndex = 1;
        SetNavigationActive(active);
        AppScroll.Dispatcher.BeginInvoke(new Action(() =>
        {
            AppScroll.UpdateLayout();
            target.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void NavPackage_Click(object sender, RoutedEventArgs e) { BuilderModeBox.SelectedIndex = 0; SetNavigationActive(NavPackageButton); AppScroll.ScrollToHome(); Log(T("NavPackageOpened","Package overview opened.")); }
    private void NavServer_Click(object sender, RoutedEventArgs e) { SyncNavigationServerFields(); NavigateAdvanced(AdvancedServerPanel, NavServerButton); }
    private void NavMeshtastic_Click(object sender, RoutedEventArgs e) { NavigateAdvanced(AdvancedMeshtasticPanel, NavMeshtasticButton); }
    private void NavAtak_Click(object sender, RoutedEventArgs e) { SyncNavigationAtakFields(); NavigateAdvanced(AdvancedAtakPanel, NavAtakButton); }
    private void NavCerts_Click(object sender, RoutedEventArgs e) { NavigateAdvanced(AdvancedSecurityPanel, NavCertsButton); }
    private void NavSettings_Click(object sender, RoutedEventArgs e) { NavigateAdvanced(AdvancedSettingsPanel, NavSettingsButton); Log(T("NavSettingsOpened","Application settings opened.")); }

    private void ConfigureResponsiveWindow()
    {
        // WPF uses device-independent pixels. Keep the Builder inside the usable
        // work area on Full HD and high-DPI displays instead of forcing a window
        // that is too large for the desktop.
        var work = SystemParameters.WorkArea;
        var targetWidth = Math.Min(1510, work.Width * 0.94);
        var targetHeight = Math.Min(1000, work.Height * 0.96);
        Width = Math.Max(Math.Min(targetWidth, work.Width), MinWidth);
        Height = Math.Max(Math.Min(targetHeight, work.Height), MinHeight);
        MaxWidth = work.Width;
        MaxHeight = work.Height;
    }

    private void TryRefreshFingerprint()
    {
        try
        {
            FingerprintRun.Text = ShortFingerprint(_signing.Fingerprint());
        }
        catch (Exception ex)
        {
            FingerprintRun.Text = T("FingerprintUnavailable", "not available");
            LogStartupNonFatal("Publisher fingerprint initialization failed", ex);
        }
    }

    private static void LogStartupNonFatal(string context, Exception ex)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FieldTakHub", "Builder-startup.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] NON-FATAL: {context}\r\n{ex}\r\n\r\n",
                new UTF8Encoding(false));
        }
        catch { }
    }

    private FieldTakProject FromUi() => new()
    {
        PackageId = IdBox.Text.Trim(), Name = NameBox.Text.Trim(), PackageVersion = VersionBox.Text.Trim(), PublisherName = PublisherBox.Text.Trim(),
        SourceDirectory = SourceBox.Text.Trim(), OutputDirectory = OutputBox.Text.Trim(), AtakMinVersion = AtakMinBox.Text.Trim(), AtakMaxVersion = AtakMaxBox.Text.Trim(),
        ExpiryHours = int.TryParse(PackageExpiryBox.Text, out var ph) ? Math.Max(1, ph) : 6,
        QrExpiryHours = int.TryParse(QrExpiryBox.Text, out var qh) ? Math.Max(1, qh) : 6,
        BuilderMode = (BuilderModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "simple",
        MaxDownloads = int.TryParse(MaxDownloadsBox.Text, out var md) ? Math.Max(1, md) : 50,
        Server = ServerFromUi(),
        Enrollment = new EnrollmentProfile { Enabled = IsEnrollmentUri(EnrollmentUriBox.Text), TakUri = EnrollmentUriBox.Text.Trim(), Label = T("EnrollmentDefaultLabel", "OpenTAK enrollment") },
        Meshtastic = MeshtasticFromUi()
    };


    private MeshtasticProfile MeshtasticFromUi() => new()
    {
        Enabled = true,
        Mode = (MeshtasticModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "hybrid",
        DeviceRole = (MeshtasticDeviceRoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto",
        DeviceModel = string.IsNullOrWhiteSpace(MeshtasticDeviceModelBox.Text) ? "generic_meshtastic" : MeshtasticDeviceModelBox.Text.Trim(),
        ChannelName = string.IsNullOrWhiteSpace(MeshtasticChannelNameBox.Text) ? "GGZS-TAK" : MeshtasticChannelNameBox.Text.Trim(),
        Region = string.IsNullOrWhiteSpace(MeshtasticRegionBox.Text) ? "EU_868" : MeshtasticRegionBox.Text.Trim(),
        ModemPreset = string.IsNullOrWhiteSpace(MeshtasticPresetBox.Text) ? "SHORT_FAST" : MeshtasticPresetBox.Text.Trim(),
        HopLimit = int.TryParse(MeshtasticHopBox.Text, out var hop) ? Math.Clamp(hop,1,7) : 3,
        Pli = MeshtasticPliBox.IsChecked == true, GeoChat = MeshtasticGeoChatBox.IsChecked == true,
        OtsRelay = MeshtasticOtsBox.IsChecked == true, FileTransfer = MeshtasticFileBox.IsChecked == true,
        Gateway = new MeshtasticGatewayProfile
        {
            Enabled = MeshtasticGatewayEnabledBox.IsChecked == true,
            Type = string.IsNullOrWhiteSpace(MeshtasticGatewayTypeBox.Text) ? "generic_meshtastic" : MeshtasticGatewayTypeBox.Text.Trim(),
            Role = string.IsNullOrWhiteSpace(MeshtasticGatewayRoleBox.Text) ? "ots_mqtt" : MeshtasticGatewayRoleBox.Text.Trim(),
            Transport = string.IsNullOrWhiteSpace(MeshtasticGatewayTransportBox.Text) ? "mqtt_tls" : MeshtasticGatewayTransportBox.Text.Trim(),
            MqttPort = int.TryParse(MeshtasticGatewayMqttPortBox.Text, out var mqtt) ? Math.Clamp(mqtt,1,65535) : 8883,
            RootTopic = string.IsNullOrWhiteSpace(MeshtasticGatewayRootTopicBox.Text) ? "opentakserver" : MeshtasticGatewayRootTopicBox.Text.Trim(),
            SecretsExternal = true
        }
    };

    private void MeshtasticToUi(MeshtasticProfile m)
    {
        MeshtasticModeBox.SelectedIndex = m.Mode switch { "compatible" => 1, "direct" => 2, _ => 0 };
        MeshtasticDeviceRoleBox.SelectedIndex = m.DeviceRole switch { "tak" or "field_node" => 1, "gateway" => 2, _ => 0 };
        MeshtasticDeviceModelBox.Text = string.IsNullOrWhiteSpace(m.DeviceModel) ? "generic_meshtastic" : m.DeviceModel;
        MeshtasticChannelNameBox.Text=m.ChannelName; MeshtasticRegionBox.Text=m.Region; MeshtasticPresetBox.Text=m.ModemPreset; MeshtasticHopBox.Text=m.HopLimit.ToString();
        MeshtasticPliBox.IsChecked=m.Pli; MeshtasticGeoChatBox.IsChecked=m.GeoChat; MeshtasticOtsBox.IsChecked=m.OtsRelay; MeshtasticFileBox.IsChecked=m.FileTransfer;
        MeshtasticGatewayEnabledBox.IsChecked=m.Gateway.Enabled; MeshtasticGatewayTypeBox.Text=m.Gateway.Type; MeshtasticGatewayRoleBox.Text=m.Gateway.Role; MeshtasticGatewayTransportBox.Text=m.Gateway.Transport;
        MeshtasticGatewayMqttPortBox.Text=m.Gateway.MqttPort.ToString(); MeshtasticGatewayRootTopicBox.Text=m.Gateway.RootTopic; MeshtasticGatewaySecretsExternalBox.IsChecked=true;
    }

    private ServerProfile ServerFromUi() => new()
    {
        Type = string.IsNullOrWhiteSpace(ServerTypeBox.Text) ? "OpenTAK" : ServerTypeBox.Text.Trim(),
        Name = string.IsNullOrWhiteSpace(ServerNameBox.Text) ? "TAK Server" : ServerNameBox.Text.Trim(),
        Host = HostBox.Text.Trim(), CotPort = Int(CotBox, 8089), ApiPort = Int(ApiBox, 8446), WebPort = Int(WebBox, 8443)
    };

    private static int Int(TextBox b, int d) => int.TryParse(b.Text, out var v) && v is > 0 and <= 65535 ? v : d;

    private void ToUi(FieldTakProject p)
    {
        IdBox.Text=p.PackageId; NameBox.Text=p.Name; VersionBox.Text=p.PackageVersion; PublisherBox.Text=p.PublisherName; SourceBox.Text=p.SourceDirectory; OutputBox.Text=p.OutputDirectory;
        AtakMinBox.Text=p.AtakMinVersion; AtakMaxBox.Text=p.AtakMaxVersion; PackageExpiryBox.Text=p.ExpiryHours.ToString(); QrExpiryBox.Text=p.QrExpiryHours.ToString(); MaxDownloadsBox.Text=p.MaxDownloads.ToString(); BuilderModeBox.SelectedIndex = p.BuilderMode.Equals("advanced", StringComparison.OrdinalIgnoreCase) ? 1 : 0; ServerToUi(p.Server); ApplyBuilderMode();
        LoadEnrollmentConfig();
        SyncExtraOptionsFields();
        MeshtasticToUi(p.Meshtastic);
        LoadMeshtasticConfig();
    }

    private void ServerToUi(ServerProfile s)
    {
        ServerTypeBox.Text=s.Type; ServerNameBox.Text=s.Name; HostBox.Text=s.Host; CotBox.Text=s.CotPort.ToString(); ApiBox.Text=s.ApiPort.ToString(); WebBox.Text=s.WebPort.ToString();
    }

    private void MeshtasticDeviceRoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || MeshtasticDeviceRoleBox is null || MeshtasticGatewayEnabledBox is null) return;
        var role = (MeshtasticDeviceRoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        if (role == "gateway") MeshtasticGatewayEnabledBox.IsChecked = true;
        else if (role == "tak" || role == "field_node") MeshtasticGatewayEnabledBox.IsChecked = false;
    }

    private void BuilderModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        ApplyBuilderMode();
    }

    private void ApplyBuilderMode()
    {
        if (!_uiReady || BuilderModeBox is null || AdvancedFoldersPanel is null || AdvancedMeshtasticPanel is null || AdvancedGatewayPanel is null || AdvancedEnrollmentPanel is null || AdvancedSecurityPanel is null || AdvancedSettingsPanel is null || AdvancedServerPanel is null || AdvancedAtakPanel is null || AdvancedModeHint is null)
            return;
        var advanced = (BuilderModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "advanced";
        AdvancedFoldersPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedServerPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedAtakPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedMeshtasticPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedGatewayPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedEnrollmentPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedSecurityPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedSettingsPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        AdvancedModeHint.Text = advanced ? T("AdvancedModeHint", "Advanced mode: all deployment, server, enrollment, Meshtastic and security controls are visible.") : T("SimpleModeHint", "Simple mode: only the controls normally needed to create and distribute a field package are shown.");
    }

    private void ContentsHeader_Click(object sender, RoutedEventArgs e)
    {
        _contentsExpanded = !_contentsExpanded;
        ContentsPanel.Visibility = _contentsExpanded ? Visibility.Visible : Visibility.Collapsed;
        ContentsChevron.Text = _contentsExpanded ? "⌃" : "⌄";
    }

    private void ExtraOptionsHeader_Click(object sender, RoutedEventArgs e)
    {
        _extraOptionsExpanded = !_extraOptionsExpanded;
        ExtraOptionsPanel.Visibility = _extraOptionsExpanded ? Visibility.Visible : Visibility.Collapsed;
        ExtraChevron.Text = _extraOptionsExpanded ? "⌃" : "⌄";
        if (_extraOptionsExpanded) SyncExtraOptionsFields();
    }

    private void ExtraFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        if (ExtraPackageIdBox is not null && IdBox is not null) IdBox.Text = ExtraPackageIdBox.Text;
        if (ExtraVersionBox is not null && VersionBox is not null) VersionBox.Text = ExtraVersionBox.Text;
        if (ExtraPublisherBox is not null && PublisherBox is not null) PublisherBox.Text = ExtraPublisherBox.Text;
        if (ExtraMaxDownloadsBox is not null && MaxDownloadsBox is not null) MaxDownloadsBox.Text = ExtraMaxDownloadsBox.Text;
    }

    private void SyncExtraOptionsFields()
    {
        if (ExtraPackageIdBox is null) return;
        ExtraPackageIdBox.Text = IdBox.Text;
        ExtraVersionBox.Text = VersionBox.Text;
        ExtraPublisherBox.Text = PublisherBox.Text;
        ExtraMaxDownloadsBox.Text = MaxDownloadsBox.Text;
    }

    private void RecommendedProfile_Click(object sender, RoutedEventArgs e)
    {
        NameBox.Text = T("RecommendedEventName", "MILSIM 2026");
        DescriptionBox.Text = T("RecommendedEventDescription", "Konfiguracja uczestników - MILSIM 2026");
        PackageExpiryBox.Text = "24";
        QrExpiryBox.Text = "2";
        MaxDownloadsBox.Text = "50";
        AtakMinBox.Text = "5.6";
        AtakMaxBox.Text = "5.8";
        if (AtakEnabledSwitch != null) AtakEnabledSwitch.IsChecked = true;
        if (PluginsEnabledSwitch != null) PluginsEnabledSwitch.IsChecked = true;
        if (MapsEnabledSwitch != null) MapsEnabledSwitch.IsChecked = true;
        if (OverlaysEnabledSwitch != null) OverlaysEnabledSwitch.IsChecked = true;
        if (MeshtasticEnabledSwitch != null) MeshtasticEnabledSwitch.IsChecked = true;
        if (ServerEnabledSwitch != null) ServerEnabledSwitch.IsChecked = true;
        MeshtasticModeBox.SelectedIndex = 0;
        MeshtasticDeviceRoleBox.SelectedIndex = 1;
        MeshtasticGatewayEnabledBox.IsChecked = false;
        MeshtasticPliBox.IsChecked = true;
        MeshtasticGeoChatBox.IsChecked = true;
        MeshtasticOtsBox.IsChecked = true;
        MeshtasticFileBox.IsChecked = false;
        MeshtasticRegionBox.Text = "EU_868";
        MeshtasticPresetBox.Text = "SHORT_FAST";
        MeshtasticHopBox.Text = "3";
        Log(T("LogRecommendedProfile", "Applied recommended MILSIM profile."));
    }

    private void PackageComponent_Click(object sender, RoutedEventArgs e)
    {
        var target = (sender as Button)?.Tag?.ToString() ?? "component";
        BuilderModeBox.SelectedIndex = 1;
        switch (target)
        {
            case "atak": SyncNavigationAtakFields(); NavigateAdvanced(AdvancedAtakPanel, NavAtakButton); break;
            case "meshtastic": NavigateAdvanced(AdvancedMeshtasticPanel, NavMeshtasticButton); break;
            case "server": SyncNavigationServerFields(); NavigateAdvanced(AdvancedServerPanel, NavServerButton); break;
            default:
                BuilderModeBox.SelectedIndex = 1;
                AppScroll.Dispatcher.BeginInvoke(new Action(() => AppScroll.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Loaded);
                Log(T("ConfigureComponentHint", "Advanced configuration is available below the simple view."));
                break;
        }
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e) { var p=Folder(SourceBox.Text); if(p!=null){ SourceBox.Text=p; EnsureCurrentStructure(log:false); RefreshFolderCounts(); } }
    private void BrowseOutput_Click(object sender, RoutedEventArgs e) { var p=Folder(OutputBox.Text); if(p!=null){ OutputBox.Text=p; EnsureCurrentStructure(log:false); } }
    private static string? Folder(string initial) { var d = new OpenFolderDialog { InitialDirectory = Directory.Exists(initial) ? initial : string.Empty, Multiselect = false }; return d.ShowDialog() == true ? d.FolderName : null; }

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        try { EnsureCurrentStructure(log:false); _items=_analyzer.Analyze(SourceBox.Text); ContentList.ItemsSource=_items; UpdateContentPreview(); Log(string.Format(T("LogAnalyzed","Analyzed {0} files. ATAK source: {1}; APKs: {2}; total {3}."),_items.Count,_items.Count(x=>x.Category=="ATAK Mission Package"),_items.Count(x=>x.Category=="Plugin APK"),HumanBytes(_items.Sum(x=>x.Size)))); }
        catch(Exception ex){ Error(ex); }
    }

    private void ImportFtak_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Filter = "Field TAK package (*.ftak)|*.ftak|All files (*.*)|*.*",
            InitialDirectory = _workspace.DefaultProjectsRoot
        };
        if (d.ShowDialog() != true) return;
        try
        {
            var publisher = string.IsNullOrWhiteSpace(PublisherBox.Text) ? Environment.UserName : PublisherBox.Text.Trim();
            var result = _ftakImporter.Import(d.FileName, _workspace, publisher);
            ToUi(result.Project);
            _currentProjectPath = result.ProjectFile;
            EnsureCurrentStructure(log:false);
            _items = _analyzer.Analyze(result.Project.SourceDirectory);
            ContentList.ItemsSource = _items;
            RefreshFolderCounts();
            UpdateContentPreview();
            Log(string.Format(T("LogFtakImported", "Imported .ftak for editing. Files: {0}; source size: {1}; project: {2}"), result.ImportedFiles, HumanBytes(result.ImportedBytes), result.ProjectFile));
            MessageBox.Show(string.Format(T("FtakImportSummary", "The .ftak package was imported into an editable Builder project.\n\nFiles: {0}\nSource size: {1}\nOriginal package SHA-256 was recorded in IMPORTED-FROM-FTAK.txt.\n\nYou can now modify the project and build a new signed .ftak."), result.ImportedFiles, HumanBytes(result.ImportedBytes)), T("ImportFtak", "Import .ftak"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void UpdateContentPreview()
    {
        if (_items.Count == 0)
        {
            if (ContentFilesSizeText != null) ContentFilesSizeText.Text = "0 B";
            if (ContentMapsSizeText != null) ContentMapsSizeText.Text = "0 B";
            if (ContentPluginsSizeText != null) ContentPluginsSizeText.Text = "0 B";
            if (ContentConfigSizeText != null) ContentConfigSizeText.Text = "0 B";
            if (ContentTacticalSizeText != null) ContentTacticalSizeText.Text = "0 B";
            if (ContentOtherSizeText != null) ContentOtherSizeText.Text = "0 B";
            return;
        }
        long Size(params string[] categories) => _items.Where(x => categories.Contains(x.Category, StringComparer.OrdinalIgnoreCase)).Sum(x => x.Size);
        var maps = Size("Map");
        var plugins = Size("Plugin APK");
        var config = Size("Configuration", "Certificate");
        var tactical = Size("Overlay", "ATAK Mission Package");
        var other = _items.Sum(x => x.Size) - maps - plugins - config - tactical;
        if (ContentMapsSizeText != null) ContentMapsSizeText.Text = HumanBytes(maps);
        if (ContentPluginsSizeText != null) ContentPluginsSizeText.Text = HumanBytes(plugins);
        if (ContentConfigSizeText != null) ContentConfigSizeText.Text = HumanBytes(config);
        if (ContentTacticalSizeText != null) ContentTacticalSizeText.Text = HumanBytes(tactical);
        if (ContentOtherSizeText != null) ContentOtherSizeText.Text = HumanBytes(Math.Max(0, other));
        if (ContentFilesSizeText != null) ContentFilesSizeText.Text = HumanBytes(_items.Sum(x => x.Size));
    }

    private void Build_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureCurrentStructure(log:false);
            var project=FromUi(); ServerValidator.ValidateProject(project);
            if(_items.Count==0) _items=_analyzer.Analyze(project.SourceDirectory);
            var preview = BuildPreview(project,_items);
            var caption = LocalizationService.Text("BuildPreviewTitle","Build preview");
            if(MessageBox.Show(preview+"\n\n"+LocalizationService.Text("BuildPreviewConfirm","Build this package?"),caption,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
            _lastPackage=_builder.Build(project,_items);
            CloudPackageBox.Text=_lastPackage;
            var serverTxt=ServerTextConfigService.SidecarPath(_lastPackage);
            Log(string.Format(T("LogBuilt","Built and signed:\r\n{0}\r\n\r\nserver.txt:\r\n{1}\r\n\r\nATAK server.pref is generated inside the Mission Package unless source/atak contains a custom .pref."),_lastPackage,serverTxt));
        }
        catch(Exception ex){ Error(ex); }
    }

    private void UpdateGeneratedPreview()
    {
        if (string.IsNullOrWhiteSpace(_lastPackage) || !File.Exists(_lastPackage)) return;
        var info = new FileInfo(_lastPackage);
        if (GeneratedFileNameText != null) GeneratedFileNameText.Text = info.Name;
        if (GeneratedFilePathText != null) GeneratedFilePathText.Text = info.DirectoryName ?? string.Empty;
        if (GeneratedFileSizeText != null) GeneratedFileSizeText.Text = HumanBytes(info.Length);
    }

    private string BuildPreview(FieldTakProject p,IReadOnlyList<ContentItem> items)
    {
        var total=items.Sum(x=>x.Size); var plugins=items.Count(x=>x.Category=="Plugin APK"); var maps=items.Count(x=>x.Category=="Map");
        return string.Format(T("BuildPreviewBody","{0}  v{1}\n\nATAK: {2} – {3}\nServer: {4}\nPorts: {5} / {6} / {7}\nPlugins: {8}\nMaps: {9}\nFiles: {10}\nSource size: {11}\nPackage validity: {12} h\nQR validity: {13} h\nMax downloads: {14}\nPublisher: {15}"),p.Name,p.PackageVersion,p.AtakMinVersion,p.AtakMaxVersion,p.Server.Host,p.Server.CotPort,p.Server.ApiPort,p.Server.WebPort,plugins,maps,items.Count,HumanBytes(total),p.ExpiryHours,p.QrExpiryHours,p.MaxDownloads,p.PublisherName);
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var server=ServerFromUi(); ServerValidator.Validate(server); Log(string.Format(T("LogTestingServer","Testing {0}…"),server.Host));
            var results=await _diagnostics.TestAsync(server);
            Log(T("LogServerTest","SERVER TEST")+"\r\n"+string.Join("\r\n",results.Select(r=>$"{(r.Success?"OK":"FAIL"),-4} {r.Name,-16} {r.Detail}")));
        }
        catch(Exception ex){ Error(ex); }
    }

    private void StartServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(_lastPackage) || !File.Exists(_lastPackage)) { MessageBox.Show(T("BuildFirst","Build a package first."),T("WindowTitle","Field TAK Hub Builder")); return; }
            var project=FromUi(); ServerValidator.Validate(project.Server);
            var url=_server.Start(_lastPackage, TimeSpan.FromHours(project.QrExpiryHours), project.MaxDownloads); UrlText.Text=url;
            var deepLink=$"fieldtak://provision?url={Uri.EscapeDataString(url)}";
            ShowQr(deepLink,_qr.CreatePng(deepLink));
            Log(string.Format(T("LogLanStarted","LAN distribution started. HTTP Range/206 resume is enabled. Plain HTTP is intended only for private LAN; Hub rejects public cleartext URLs.\r\nDescriptor: {0}\r\nQR deep-link: {1}"),url,deepLink));
        }
        catch(Exception ex){ Error(ex); }
    }
    private void StopServer_Click(object sender, RoutedEventArgs e){ _server.Stop(); UrlText.Text=""; QrImage.Source=null; _lastQrPng=null; _lastQrDeepLink=null; Log(T("LogDistributionStopped","Distribution stopped.")); }

    private void SelectCloudPackage_Click(object sender, RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="Field TAK package (*.ftak)|*.ftak|All files (*.*)|*.*",InitialDirectory=Directory.Exists(OutputBox.Text)?OutputBox.Text:string.Empty};
        if(d.ShowDialog()==true){CloudPackageBox.Text=d.FileName;_lastPackage=d.FileName;CloudStatusText.Text=T("CloudPackageSelected","Selected local package for cloud QR.");}
    }

    private async void TestCloudLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CloudStatusText.Text=T("CloudTesting","Testing external HTTPS link…");
            var result=await _cloud.TestAsync(CloudUrlBox.Text);
            if(result.Success && result.WasNormalized) CloudUrlBox.Text=result.DownloadUrl;
            CloudStatusText.Text=(result.Success?"OK: ":"FAIL: ")+result.Detail;
            Log(string.Format(T("LogCloudTest","CLOUD LINK TEST\r\n{0}"),CloudStatusText.Text));
        }
        catch(Exception ex){CloudStatusText.Text="FAIL: "+ex.Message;Error(ex);}
    }

    private void GenerateCloudQr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var local=CloudPackageBox.Text.Trim();
            if(string.IsNullOrWhiteSpace(local) || !File.Exists(local)){MessageBox.Show(T("SelectCloudPackageFirst","Select the exact local .ftak file that you uploaded to the cloud."),T("WindowTitle","Field TAK Hub Builder"));return;}
            var project=FromUi();
            var resolved=CloudDistributionService.ResolvePackageUrl(CloudUrlBox.Text);
            var deepLink=CloudDistributionService.CreateDeepLink(resolved.DownloadUrl,local,DateTimeOffset.UtcNow.AddHours(project.QrExpiryHours),project.Name);
            if(resolved.WasNormalized) CloudUrlBox.Text=resolved.DownloadUrl;
            var png=_qr.CreatePng(deepLink); ShowQr(deepLink,png);
            Directory.CreateDirectory(project.OutputDirectory);
            var stem=Path.GetFileNameWithoutExtension(local);
            var qrPath=Path.Combine(project.OutputDirectory,stem+"-cloud-QR.png");
            var txtPath=Path.Combine(project.OutputDirectory,stem+"-cloud-QR.txt");
            File.WriteAllBytes(qrPath,png); File.WriteAllText(txtPath,deepLink);
            CloudStatusText.Text=string.Format(T("CloudQrReady","Cloud QR ready. Saved: {0}"),qrPath);
            Log(string.Format(T("LogCloudQr","Generated direct-cloud QR. SHA-256 is bound to the local .ftak.\r\nPackage: {0}\r\nCloud URL: {1}\r\nQR: {2}"),local,CloudUrlBox.Text.Trim(),qrPath));
        }
        catch(Exception ex){Error(ex);}
    }

    private void CopyCloudDeepLink_Click(object sender, RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(_lastQrDeepLink)){MessageBox.Show(T("GenerateQrFirst","Generate a QR first."),T("WindowTitle","Field TAK Hub Builder"));return;}
        Clipboard.SetText(_lastQrDeepLink); CloudStatusText.Text=T("DeepLinkCopied","Provisioning deep-link copied to clipboard.");
    }

    private void SaveQr_Click(object sender, RoutedEventArgs e)
    {
        if(_lastQrPng==null){MessageBox.Show(T("GenerateQrFirst","Generate a QR first."),T("WindowTitle","Field TAK Hub Builder"));return;}
        var d=new SaveFileDialog{Filter="PNG image (*.png)|*.png",FileName="FieldTAK-provision-QR.png",InitialDirectory=Directory.Exists(OutputBox.Text)?OutputBox.Text:string.Empty};
        if(d.ShowDialog()==true){File.WriteAllBytes(d.FileName,_lastQrPng);Log(string.Format(T("LogQrSaved","Saved QR PNG: {0}"),d.FileName));}
    }

    private void ShowQr(string deepLink,byte[] png)
    {
        _lastQrDeepLink=deepLink; _lastQrPng=png;
        using var ms=new MemoryStream(png); var bi=new BitmapImage(); bi.BeginInit(); bi.CacheOption=BitmapCacheOption.OnLoad; bi.StreamSource=ms; bi.EndInit(); bi.Freeze(); QrImage.Source=bi;
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var initialName = WorkspaceService.Slug(NameBox.Text) + ".fthproj";
        var d=new SaveFileDialog{Filter="Field TAK project (*.fthproj)|*.fthproj",FileName=initialName,InitialDirectory=CurrentProjectRoot()};
        if(d.ShowDialog()==true)
        {
            try
            {
                var p=FromUi(); ServerValidator.Validate(p.Server); EnsureCurrentStructure(log:false); _projects.Save(d.FileName,p); _currentProjectPath=d.FileName;
                Log(string.Format(T("LogSavedProject","Saved {0}"),d.FileName));
            }
            catch(Exception ex){Error(ex);}
        }
    }

    private void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="Field TAK project (*.fthproj)|*.fthproj",InitialDirectory=_workspace.DefaultProjectsRoot};
        if(d.ShowDialog()==true)
        {
            try
            {
                var p=_projects.Load(d.FileName); _workspace.EnsureSourceTree(p.SourceDirectory,p.OutputDirectory); ToUi(p); _currentProjectPath=d.FileName;
                _items=Array.Empty<ContentItem>(); ContentList.ItemsSource=_items; RefreshFolderCounts(); LoadMeshtasticConfig();
                Log(string.Format(T("LogLoadedProject","Loaded {0}"),d.FileName));
                Log(T("LogFoldersRepaired","Project folders checked and missing folders created."));
            }
            catch(Exception ex){Error(ex);}
        }
    }

    private void ImportLegacy_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Filter = T("LegacyZipFilter", "TAK Field Hub 1.x ZIP (*.zip)|*.zip|All files (*.*)|*.*"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (d.ShowDialog() != true) return;
        try
        {
            var publisher = string.IsNullOrWhiteSpace(PublisherBox.Text) ? Environment.UserName : PublisherBox.Text.Trim();
            var expiry = int.TryParse(PackageExpiryBox.Text, out var h) ? Math.Max(1, h) : 72;
            var maxDownloads = int.TryParse(MaxDownloadsBox.Text, out var md) ? Math.Max(1, md) : 50;
            var result = _legacy.Import(d.FileName, _workspace, publisher, expiry, maxDownloads);
            _projects.Save(result.ProjectFile, result.Project);
            _currentProjectPath = result.ProjectFile;
            ToUi(result.Project);
            EnsureCurrentStructure(log:false);
            _items = _analyzer.Analyze(result.Project.SourceDirectory);
            ContentList.ItemsSource = _items;
            RefreshFolderCounts();
            var sig = result.SignatureVerified ? T("LegacyVerified", "verified") : T("LegacyNotPresent", "not present");
            Log(string.Format(T("LogLegacyImported", "Imported legacy 1.x ZIP into a new 2.x project. Files: {0}; RSA signature: {1}; SHA-256: OK. Project: {2}"), result.ImportedFiles, sig, result.ProjectFile));
            MessageBox.Show(string.Format(T("LegacyImportSummary", "Legacy package imported successfully.\n\nFiles: {0}\nRSA signature: {1}\nSHA-256: OK\n\nClick BUILD PACKAGE to create a new Ed25519-signed .ftak v2."), result.ImportedFiles, sig), T("ImportLegacy", "Import legacy 1.x ZIP"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch(Exception ex){ Error(ex); }
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewProjectDialog { Owner=this };
        if(dialog.ShowDialog()!=true) return;
        try
        {
            var layout=_workspace.CreateNewProject(dialog.ProjectName);
            NameBox.Text=dialog.ProjectName; IdBox.Text=dialog.PackageId; VersionBox.Text=string.IsNullOrWhiteSpace(dialog.PackageVersion)?"1.0.0":dialog.PackageVersion;
            SourceBox.Text=layout.SourceDirectory; OutputBox.Text=layout.OutputDirectory; SyncExtraOptionsFields();
            var p=FromUi(); _projects.Save(layout.ProjectFile,p); _currentProjectPath=layout.ProjectFile;
            _items=Array.Empty<ContentItem>(); ContentList.ItemsSource=_items; RefreshFolderCounts(); LoadMeshtasticConfig();
            Log(string.Format(T("LogProjectCreated","Created project: {0}"),layout.RootDirectory));
            OpenPath(layout.RootDirectory);
        }
        catch(Exception ex){Error(ex);}
    }

    private void RepairFolders_Click(object sender, RoutedEventArgs e)
    {
        try { EnsureCurrentStructure(log:true); RefreshFolderCounts(); }
        catch(Exception ex){ Error(ex); }
    }

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        try { EnsureCurrentStructure(log:false); OpenPath(CurrentProjectRoot()); }
        catch(Exception ex){ Error(ex); }
    }

    private void OpenSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if(sender is not FrameworkElement element || element.Tag is not string folder) return;
            EnsureCurrentStructure(log:false); OpenPath(WorkspaceService.FolderPath(SourceBox.Text,folder));
        }
        catch(Exception ex){ Error(ex); }
    }

    private void ProjectFolder_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ProjectFolder_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if(sender is not FrameworkElement element || element.Tag is not string folder || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            EnsureCurrentStructure(log:false);
            var target=WorkspaceService.FolderPath(SourceBox.Text,folder); var paths=(string[])e.Data.GetData(DataFormats.FileDrop)!; var copied=0;
            foreach(var path in paths)
            {
                if(File.Exists(path)) { CopyUnique(path,target); copied++; }
                else if(Directory.Exists(path))
                {
                    foreach(var file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories)) { CopyUnique(file,target); copied++; }
                }
            }
            RefreshFolderCounts(); _items=Array.Empty<ContentItem>(); ContentList.ItemsSource=_items;
            Log(string.Format(T("LogFilesDropped","Copied {0} file(s) to {1}."),copied,folder));
        }
        catch(Exception ex){ Error(ex); }
    }

    private string MeshtasticChannelFile => Path.Combine(WorkspaceService.FolderPath(SourceBox.Text, "meshtastic"), "channel.url");

    private void LoadMeshtasticConfig()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SourceBox.Text)) return;
            var file = MeshtasticChannelFile;
            if (File.Exists(file))
            {
                MeshtasticUrlBox.Text = File.ReadAllText(file, new UTF8Encoding(false)).Trim();
                MeshtasticStatusText.Text = T("MeshtasticLoaded", "Meshtastic channel URL loaded from source/meshtastic/channel.url.");
                var profileFile=Path.Combine(WorkspaceService.FolderPath(SourceBox.Text, "meshtastic"), "profile.json");
                if(File.Exists(profileFile)) { try { var m=System.Text.Json.JsonSerializer.Deserialize<MeshtasticProfile>(File.ReadAllText(profileFile,new UTF8Encoding(false)),JsonDefaults.Options); if(m!=null) MeshtasticToUi(m); } catch { } }
            }
            else
            {
                MeshtasticUrlBox.Clear();
                MeshtasticStatusText.Text = T("MeshtasticNotConfigured", "Meshtastic is optional. No channel URL is configured.");
            }
        }
        catch (Exception ex) { MeshtasticStatusText.Text = ex.Message; }
    }

    private static bool IsMeshtasticChannelUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
        uri.Host.Equals("meshtastic.org", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/e/", StringComparison.OrdinalIgnoreCase);

    private void MeshtasticSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureCurrentStructure(log:false);
            var value = MeshtasticUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                File.Delete(MeshtasticChannelFile);
                MeshtasticStatusText.Text = T("MeshtasticRemoved", "Meshtastic configuration removed from this project.");
                Log(T("MeshtasticRemoved", "Meshtastic configuration removed from this project."));
                return;
            }
            if (!IsMeshtasticChannelUrl(value))
                throw new InvalidDataException(T("MeshtasticInvalid", "Invalid Meshtastic channel URL. Paste the canonical https://meshtastic.org/e/#... link."));
            File.WriteAllText(MeshtasticChannelFile, value + Environment.NewLine, new UTF8Encoding(false));
            MeshtasticUrlBox.Text = value;
            MeshtasticStatusText.Text = T("MeshtasticSaved", "Meshtastic channel URL saved. It will be included in the next .ftak build.");
            RefreshFolderCounts();
            Log(T("MeshtasticSaved", "Meshtastic channel URL saved. It will be included in the next .ftak build."));
        }
        catch(Exception ex) { Error(ex); }
    }

    private void MeshtasticOpen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var value = MeshtasticUrlBox.Text.Trim();
            if (!IsMeshtasticChannelUrl(value))
                throw new InvalidDataException(T("MeshtasticInvalid", "Invalid Meshtastic channel URL. Paste the canonical https://meshtastic.org/e/#... link."));
            Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
        }
        catch(Exception ex) { Error(ex); }
    }

    private void MeshtasticCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var value = MeshtasticUrlBox.Text.Trim();
            if (!IsMeshtasticChannelUrl(value))
                throw new InvalidDataException(T("MeshtasticInvalid", "Invalid Meshtastic channel URL. Paste the canonical https://meshtastic.org/e/#... link."));
            Clipboard.SetText(value);
            MeshtasticStatusText.Text = T("MeshtasticCopied", "Meshtastic channel URL copied to clipboard.");
        }
        catch(Exception ex) { Error(ex); }
    }

    private void MeshtasticClear_Click(object sender, RoutedEventArgs e)
    {
        MeshtasticUrlBox.Clear();
        MeshtasticStatusText.Text = T("MeshtasticReady", "Paste a Meshtastic channel URL, then click Save.");
    }

    private string EnrollmentFile => Path.Combine(WorkspaceService.FolderPath(SourceBox.Text, "enrollment"), "enroll.url");

    private void LoadEnrollmentConfig()
    {
        try
        {
            var file = EnrollmentFile;
            if (File.Exists(file))
            {
                EnrollmentUriBox.Text = File.ReadAllText(file, new UTF8Encoding(false)).Trim();
                EnrollmentStatusText.Text = T("EnrollmentLoaded", "Enrollment URI loaded from source/enrollment/enroll.url.");
            }
            else
            {
                EnrollmentUriBox.Clear();
                EnrollmentStatusText.Text = T("EnrollmentNotConfigured", "Optional. Use a short-lived OpenTAK enrollment URI for one-scan deployment.");
            }
        }
        catch (Exception ex) { EnrollmentStatusText.Text = ex.Message; }
    }

    private static bool IsEnrollmentUri(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!uri.Scheme.Equals("tak", StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals("com.atakmap.app", StringComparison.OrdinalIgnoreCase)) return false;
        var action = uri.AbsolutePath.Trim('/').Split('/')[0];
        return action.Equals("enroll", StringComparison.OrdinalIgnoreCase) &&
               QueryValue(uri, "host") is { Length: > 0 } &&
               QueryValue(uri, "username") is { Length: > 0 } &&
               QueryValue(uri, "token") is { Length: > 0 };
    }

    private void EnrollmentSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureCurrentStructure(log:false);
            var value = EnrollmentUriBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                if (File.Exists(EnrollmentFile)) File.Delete(EnrollmentFile);
                EnrollmentStatusText.Text = T("EnrollmentRemoved", "Enrollment removed from this project.");
                return;
            }
            if (!IsEnrollmentUri(value)) throw new InvalidDataException(T("EnrollmentInvalid", "Invalid ATAK enrollment URI. Expected tak://com.atakmap.app/enroll with host, username and token."));
            File.WriteAllText(EnrollmentFile, value + Environment.NewLine, new UTF8Encoding(false));
            EnrollmentUriBox.Text = value;
            EnrollmentStatusText.Text = T("EnrollmentSaved", "Enrollment saved. It will be included in the next .ftak build. Prefer a short-lived, one-time token.");
            Log(T("EnrollmentSaved", "Enrollment saved. It will be included in the next .ftak build. Prefer a short-lived, one-time token."));
        }
        catch(Exception ex) { Error(ex); }
    }

    private void EnrollmentClear_Click(object sender, RoutedEventArgs e)
    {
        try { if(File.Exists(EnrollmentFile)) File.Delete(EnrollmentFile); EnrollmentUriBox.Clear(); EnrollmentStatusText.Text=T("EnrollmentNotConfigured", "Optional. Use a short-lived OpenTAK enrollment URI for one-scan deployment."); } catch(Exception ex){Error(ex);}
    }

    private static string? QueryValue(Uri uri, string key) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0].Equals(key, StringComparison.OrdinalIgnoreCase)).Select(x => Uri.UnescapeDataString(x[1])).FirstOrDefault();

    private void EnsureCurrentStructure(bool log)
    {
        if (string.IsNullOrWhiteSpace(SourceBox.Text) || string.IsNullOrWhiteSpace(OutputBox.Text))
        {
            var layout = _workspace.EnsureDefaultProject();
            if (string.IsNullOrWhiteSpace(SourceBox.Text)) SourceBox.Text = layout.SourceDirectory;
            if (string.IsNullOrWhiteSpace(OutputBox.Text)) OutputBox.Text = layout.OutputDirectory;
        }
        _workspace.EnsureSourceTree(SourceBox.Text, OutputBox.Text);
        RefreshFolderCounts();
        if(log) Log(T("LogFoldersRepaired","Project folders checked and missing folders created."));
    }

    private void WriteMeshtasticProfile()
    {
        try
        {
            EnsureCurrentStructure(log:false);
            var profile = MeshtasticFromUi();
            var file = Path.Combine(SourceBox.Text.Trim(), "meshtastic", "profile.json");
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(profile, JsonDefaults.Options) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
    }

    private void RefreshFolderCounts()
    {
        if(string.IsNullOrWhiteSpace(SourceBox.Text)) return;
        static string Count(string root,string folder)
        {
            var path=Path.Combine(root,folder); return Directory.Exists(path) ? Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories).Count().ToString() : "0";
        }
        AtakCountText.Text=Count(SourceBox.Text,"atak"); PluginsCountText.Text=Count(SourceBox.Text,"plugins"); MapsCountText.Text=Count(SourceBox.Text,"maps");
        OverlaysCountText.Text=Count(SourceBox.Text,"overlays"); ConfigCountText.Text=Count(SourceBox.Text,"config"); DataCountText.Text=Count(SourceBox.Text,"data"); MeshtasticCountText.Text=Count(SourceBox.Text,"meshtastic");
    }

    private string CurrentProjectRoot()
    {
        if(!string.IsNullOrWhiteSpace(_currentProjectPath)) return Path.GetDirectoryName(Path.GetFullPath(_currentProjectPath))!;
        if(!string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            var full=Path.GetFullPath(SourceBox.Text); var di=new DirectoryInfo(full); if(di.Name.Equals("source",StringComparison.OrdinalIgnoreCase) && di.Parent!=null) return di.Parent.FullName;
            return full;
        }
        return _workspace.DefaultProjectsRoot;
    }

    private static void OpenPath(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    }

    private static void CopyUnique(string file,string target)
    {
        Directory.CreateDirectory(target); var name=Path.GetFileName(file); var dest=Path.Combine(target,name);
        if(File.Exists(dest))
        {
            var stem=Path.GetFileNameWithoutExtension(name); var ext=Path.GetExtension(name); var i=2;
            do dest=Path.Combine(target,$"{stem} ({i++}){ext}"); while(File.Exists(dest));
        }
        File.Copy(file,dest,false);
    }

    private void LoadServerTxt_Click(object sender, RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="TAK server profile (server.txt;*.txt)|server.txt;*.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*"};
        if(d.ShowDialog()==true){ try{var s=_serverText.Load(d.FileName);ServerValidator.Validate(s);ServerToUi(s);Log(string.Format(T("LogLoadedServer","Loaded server configuration: {0}"),d.FileName));}catch(Exception ex){Error(ex);} }
    }

    private void SaveServerTxt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var server=ServerFromUi(); ServerValidator.Validate(server);
            var d=new SaveFileDialog{Filter="TAK server profile (*.txt)|*.txt",FileName="server.txt"};
            if(d.ShowDialog()==true){_serverText.Save(d.FileName,server);Log(string.Format(T("LogSavedServer","Saved server configuration: {0}"),d.FileName));}
        }
        catch(Exception ex){Error(ex);}
    }

    private void ExportKey_Click(object sender,RoutedEventArgs e)
    {
        var d=new SaveFileDialog{Filter="Field TAK publisher key (*.fthkey)|*.fthkey",FileName="FieldTAK-Publisher.fthkey"}; if(d.ShowDialog()!=true)return;
        var prompt=new PasswordPrompt{Owner=this}; if(prompt.ShowDialog()!=true)return;
        try{_signing.ExportEncryptedBackup(d.FileName,prompt.Password);Log(string.Format(T("LogKeyExported","Encrypted publisher key backup exported: {0}"),d.FileName));}catch(Exception ex){Error(ex);}
    }

    private void ImportKey_Click(object sender,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="Field TAK publisher key (*.fthkey)|*.fthkey"}; if(d.ShowDialog()!=true)return;
        var prompt=new PasswordPrompt{Owner=this}; if(prompt.ShowDialog()!=true)return;
        try
        {
            var fp=_signing.ImportEncryptedBackup(d.FileName,prompt.Password); FingerprintRun.Text=ShortFingerprint(fp);
            Log(string.Format(T("LogKeyRestored","Publisher key restored. Fingerprint: {0}"),fp)); MessageBox.Show(T("KeyRestoredMessage","Publisher key restored. Verify the fingerprint before building production packages."),T("WindowTitle","Field TAK Hub"),MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){Error(ex);}
    }

    private async void CheckUpdates_Click(object sender,RoutedEventArgs e)=>await CheckUpdatesAsync(silent:false);
    private async Task CheckUpdatesAsync(bool silent)
    {
        try
        {
            var update=await _updates.CheckAsync();
            if(update==null){if(!silent)Log(T("NoBuilderUpdate","No newer Builder release on the configured channel."));return;}
            Log(string.Format(T("BuilderUpdateAvailable","Builder update available: {0} → {1}"),"2.2.0-rc1",update.Version));
            if(silent)return;
            if(MessageBox.Show(string.Format(T("BuilderUpdatePrompt","Builder {0} is available. Download the verified ZIP now?"),update.Version),T("UpdateTitle","Field TAK Hub Update"),MessageBoxButton.YesNo,MessageBoxImage.Information)!=MessageBoxResult.Yes)return;
            var path=await _updates.DownloadAsync(update); Log(string.Format(T("UpdateDownloaded","Update downloaded and SHA-256 verified: {0}"),path)); UpdateService.ShowInExplorer(path);
        }
        catch(Exception ex){if(!silent)Error(ex);else Log(string.Format(T("UpdateSkipped","Update check skipped: {0}"),ex.Message));}
    }

    private void LanguageBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_languageReady || LanguageBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)return;
        LocalizationService.Apply(tag);
    }

    private void SetPackage6_Click(object sender, RoutedEventArgs e) => PackageExpiryBox.Text = "6";
    private void SetPackage24_Click(object sender, RoutedEventArgs e) => PackageExpiryBox.Text = "24";
    private void SetPackage48_Click(object sender, RoutedEventArgs e) => PackageExpiryBox.Text = "48";
    private void SetQr6_Click(object sender, RoutedEventArgs e) => QrExpiryBox.Text = "6";
    private void SetLanguagePl_Click(object sender, RoutedEventArgs e) => LanguageBox.SelectedIndex = 0;
    private void SetLanguageEn_Click(object sender, RoutedEventArgs e) => LanguageBox.SelectedIndex = 1;

    private static string T(string key,string fallback)=>LocalizationService.Text(key,fallback).Replace("\\r\\n","\r\n").Replace("\\n","\n");
    private void Log(string s)=>StatusBox.Text=$"[{DateTime.Now:HH:mm:ss}] {s}\r\n\r\n"+StatusBox.Text;
    private void Error(Exception ex){ Log(T("ErrorPrefix","ERROR")+": "+ex.Message); MessageBox.Show(ex.Message,T("WindowTitle","Field TAK Hub"),MessageBoxButton.OK,MessageBoxImage.Error); }
    private static string HumanBytes(long bytes)=>bytes>=1024L*1024*1024?$"{bytes/1024d/1024d/1024d:F2} GB":bytes>=1024L*1024?$"{bytes/1024d/1024d:F1} MB":$"{bytes} B";
    private static string ShortFingerprint(string fp)=>fp.Length<=24?fp:$"{fp[..12]}…{fp[^12..]}";
    protected override void OnClosed(EventArgs e){ _server.Dispose(); _cloud.Dispose(); base.OnClosed(e); }
}
