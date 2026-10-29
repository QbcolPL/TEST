#nullable disable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace FieldTakHub.Builder.Core
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetField<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class VoiceChannelModel : ObservableObject
    {
        private string _role = "Dowodzenie HQ";
        private string _band = "PMR";
        private string _frequency = "CH 1 (446.00625 MHz)";
        private string _tone = "CTCSS 67.0 Hz";

        public string Role
        {
            get => _role;
            set => SetField(ref _role, value, nameof(Role));
        }

        public string Band
        {
            get => _band;
            set => SetField(ref _band, value, nameof(Band));
        }

        public string Frequency
        {
            get => _frequency;
            set => SetField(ref _frequency, value, nameof(Frequency));
        }

        public string Tone
        {
            get => _tone;
            set => SetField(ref _tone, value, nameof(Tone));
        }

        public string FormattedSummary =>
            (string.IsNullOrWhiteSpace(Role) ? "Kanał" : Role.Trim()) +
            " [" + (string.IsNullOrWhiteSpace(Band) ? "PMR" : Band.Trim()) + "]: " +
            (string.IsNullOrWhiteSpace(Frequency) ? "-" : Frequency.Trim()) +
            " (" + (string.IsNullOrWhiteSpace(Tone) ? "Brak tonu" : Tone.Trim()) + ")";
    }

    public sealed class FactionProfileModel : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "ORGA";
        private string _colorHex = "#2684FF";
        private string _channelName = "ORGA-MESH";
        private string _pskBase64 = "";
        private string _loraRegion = "EU_868";
        private string _modemPreset = "SHORT_FAST";
        private string _otsHost = "ggzstak.duckdns.org";
        private int _otsPort = 8089;
        private bool _useSsl = true;
        private string _emergencyPhone = "+48 600 000 000";
        private int _expirationHours = 0; // 0 = bezterminowo
        private bool _pliEnabled = true;
        private bool _geoChatEnabled = true;

        public string Id
        {
            get => _id;
            set => SetField(ref _id, value, nameof(Id));
        }

        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value, nameof(Name)))
                {
                    OnPropertyChanged(nameof(HeaderTitle));
                    OnPropertyChanged(nameof(KmlFolderRelativePath));
                }
            }
        }

        public string HeaderTitle => LocalizationManager.T("Generator Stron — ", "Faction Profile — ") + Name;
        public string KmlFolderRelativePath => "factions/" + (Name ?? "orga").Trim().ToLowerInvariant();

        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (SetField(ref _colorHex, value, nameof(ColorHex)))
                {
                    OnPropertyChanged(nameof(AccentBrush));
                }
            }
        }

        public SolidColorBrush AccentBrush
        {
            get
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_colorHex ?? "#2684FF"));
                }
                catch
                {
                    return new SolidColorBrush(Color.FromRgb(38, 132, 255));
                }
            }
        }

        public string ChannelName
        {
            get => _channelName;
            set => SetField(ref _channelName, value, nameof(ChannelName));
        }

        public string PskBase64
        {
            get => _pskBase64;
            set => SetField(ref _pskBase64, value, nameof(PskBase64));
        }

        public string LoraRegion
        {
            get => _loraRegion;
            set => SetField(ref _loraRegion, value, nameof(LoraRegion));
        }

        public string ModemPreset
        {
            get => _modemPreset;
            set => SetField(ref _modemPreset, value, nameof(ModemPreset));
        }

        public string OtsHost
        {
            get => _otsHost;
            set => SetField(ref _otsHost, value, nameof(OtsHost));
        }

        public int OtsPort
        {
            get => _otsPort;
            set => SetField(ref _otsPort, value, nameof(OtsPort));
        }

        public bool UseSsl
        {
            get => _useSsl;
            set => SetField(ref _useSsl, value, nameof(UseSsl));
        }

        public string EmergencyPhone
        {
            get => _emergencyPhone;
            set => SetField(ref _emergencyPhone, value, nameof(EmergencyPhone));
        }

        public int ExpirationHours
        {
            get => _expirationHours;
            set
            {
                if (SetField(ref _expirationHours, Math.Max(0, value), nameof(ExpirationHours)))
                {
                    OnPropertyChanged(nameof(IsPermanent));
                    OnPropertyChanged(nameof(ExpirationDisplayText));
                }
            }
        }

        public bool IsPermanent => _expirationHours <= 0;

        public string ExpirationDisplayText => IsPermanent
            ? LocalizationManager.T("Bezterminowo (0 h)", "Permanent (0 h)")
            : (_expirationHours + " h");

        public bool PliEnabled
        {
            get => _pliEnabled;
            set => SetField(ref _pliEnabled, value, nameof(PliEnabled));
        }

        public bool GeoChatEnabled
        {
            get => _geoChatEnabled;
            set => SetField(ref _geoChatEnabled, value, nameof(GeoChatEnabled));
        }

        public ObservableCollection<VoiceChannelModel> VoiceChannels { get; } = new ObservableCollection<VoiceChannelModel>();
    }

    public sealed class MapVertexModel : ObservableObject
    {
        private double _lat;
        private double _lon;

        public double Lat
        {
            get => _lat;
            set => SetField(ref _lat, value, nameof(Lat));
        }

        public double Lon
        {
            get => _lon;
            set => SetField(ref _lon, value, nameof(Lon));
        }
    }

    public sealed class MapPoiModel : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "INFIL-1";
        private string _category = "INFIL"; // INFIL, RESPAWN, PARKING, KOD, HQ, OBJ, ENEMY
        private string _natoSymbol = "SFGPUCI---";
        private double _lat = 49.849444;
        private double _lon = 18.812667;
        private double _rotationDeg = 0;
        private string _notes = "";

        public string Id { get => _id; set => SetField(ref _id, value, nameof(Id)); }
        public string Name { get => _name; set => SetField(ref _name, value, nameof(Name)); }
        public string Category { get => _category; set => SetField(ref _category, value, nameof(Category)); }
        public string NatoSymbol { get => _natoSymbol; set => SetField(ref _natoSymbol, value, nameof(NatoSymbol)); }
        public double Lat { get => _lat; set => SetField(ref _lat, value, nameof(Lat)); }
        public double Lon { get => _lon; set => SetField(ref _lon, value, nameof(Lon)); }
        public double RotationDeg { get => _rotationDeg; set => SetField(ref _rotationDeg, value, nameof(RotationDeg)); }
        public string Notes { get => _notes; set => SetField(ref _notes, value, nameof(Notes)); }
    }

    public sealed class TacticalMapConfigModel : ObservableObject
    {
        private string _mapTitle = "OPERACJA ZBYTKÓW / STRUMIEŃ";
        private int _scaleDenominator = 5000; // 1:5000 (2 cm = 100 m na A4)
        private string _sheetFormat = "A4_PORTRAIT"; // A4_PORTRAIT, A4_LANDSCAPE, A3, WRIST_MAPNIK, A5_NOTEBOOK
        private string _coordinateSystem = "MGRS"; // MGRS, UTM, DMS, WGS84
        private string _subdivisionMode = "KEYPAD_9"; // NONE, QUARTERS_4, KEYPAD_9
        private int _lidarOpacityPercent = 45;
        private string _baseLayer = "HYBRID_ORTHO_LIDAR";
        private bool _maskOutsideAo = true;
        private double _viewportCenterLat = 49.849444; // 49°50'58.0"N
        private double _viewportCenterLon = 18.812667; // 18°48'45.6"E

        public string MapTitle { get => _mapTitle; set => SetField(ref _mapTitle, value, nameof(MapTitle)); }
        public int ScaleDenominator { get => _scaleDenominator; set => SetField(ref _scaleDenominator, value, nameof(ScaleDenominator)); }
        public string SheetFormat { get => _sheetFormat; set => SetField(ref _sheetFormat, value, nameof(SheetFormat)); }
        public string CoordinateSystem { get => _coordinateSystem; set => SetField(ref _coordinateSystem, value, nameof(CoordinateSystem)); }
        public string SubdivisionMode { get => _subdivisionMode; set => SetField(ref _subdivisionMode, value, nameof(SubdivisionMode)); }
        public int LidarOpacityPercent { get => _lidarOpacityPercent; set => SetField(ref _lidarOpacityPercent, Math.Clamp(value, 0, 100), nameof(LidarOpacityPercent)); }
        public string BaseLayer { get => _baseLayer; set => SetField(ref _baseLayer, value, nameof(BaseLayer)); }
        public bool MaskOutsideAo { get => _maskOutsideAo; set => SetField(ref _maskOutsideAo, value, nameof(MaskOutsideAo)); }
        public double ViewportCenterLat { get => _viewportCenterLat; set => SetField(ref _viewportCenterLat, value, nameof(ViewportCenterLat)); }
        public double ViewportCenterLon { get => _viewportCenterLon; set => SetField(ref _viewportCenterLon, value, nameof(ViewportCenterLon)); }

        public ObservableCollection<MapVertexModel> AoVertices { get; } = new ObservableCollection<MapVertexModel>();
        public ObservableCollection<MapPoiModel> Pois { get; } = new ObservableCollection<MapPoiModel>();

        /// <summary>
        /// Zwraca True WYŁĄCZNIE wtedy, gdy użytkownik faktycznie wyznaczył obszar gry (minimum 3 wierzchołki AO)
        /// lub postawił punkty taktyczne POI na mapie. Sam pusty arkusz mapy zwraca False!
        /// </summary>
        public bool HasExplicitAoMarked => AoVertices.Count >= 3 || Pois.Count > 0;

        public Tuple<double, double> GetExplicitAoCenter()
        {
            if (AoVertices.Count >= 3)
            {
                double avgLat = AoVertices.Average(v => v.Lat);
                double avgLon = AoVertices.Average(v => v.Lon);
                return Tuple.Create(avgLat, avgLon);
            }
            if (Pois.Count > 0)
            {
                double avgLat = Pois.Average(p => p.Lat);
                double avgLon = Pois.Average(p => p.Lon);
                return Tuple.Create(avgLat, avgLon);
            }
            return Tuple.Create(_viewportCenterLat, _viewportCenterLon);
        }
    }

    public sealed class SquadMemberRoleModel : ObservableObject
    {
        private string _callsign = "ALFA-1";
        private string _primaryRole = "Lider (Dowódca Sekcji)";
        private string _secondaryRole = "RTO (Radiooperator)";
        private string _gearAndTasks = "ATAK + Heltec V3 + Radio dwupasmowe";

        public string Callsign { get => _callsign; set => SetField(ref _callsign, value, nameof(Callsign)); }
        public string PrimaryRole { get => _primaryRole; set => SetField(ref _primaryRole, value, nameof(PrimaryRole)); }
        public string SecondaryRole { get => _secondaryRole; set => SetField(ref _secondaryRole, value, nameof(SecondaryRole)); }
        public string GearAndTasks { get => _gearAndTasks; set => SetField(ref _gearAndTasks, value, nameof(GearAndTasks)); }
    }

    public sealed class OpordDayPlanModel : ObservableObject
    {
        private string _dayTitle = "DZIEŃ 1";
        private string _dateIso = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private bool _includeInExport = true;
        private string _operationNumber = "OPORD 01/2026";
        private string _classification = "🔓 JAWNE (UNCLASSIFIED)";
        private string _issuingHq = "DOWÓDZTWO ZGRUPOWANIA TAKTYCZNEGO";
        private string _locationName = ""; // Domyślnie puste, aby działała walidacja METOC!
        private string _locationLat = "";
        private string _locationLon = "";
        private string _metocQuickSummary = "Brak pobranego raportu METOC — zaznacz AO na mapie lub wpisz miejscowość i kliknij 'Pobierz METOC'.";
        private string _enemyForces = "Siły dywersyjne przeciwnika w sile plutonu prowadzące obronę manewrową w rejonie obiektów kluczowych.";
        private string _friendlyForces = "Siły własne działające w strukturze sekcji szturmowo-rozpoznawczych ze wsparciem sieci LoRa Meshtastic i serwera OpenTAKServer.";
        private string _terrainAndWeather = "";
        private string _areaOfInterestPoiDesc = "Brak zsynchronizowanych punktów z Generatora Map 1:1.";
        private string _missionFullStatement = "KTO: Sekcja Taktyczna | CO: Opanować i utrzymać wyznaczone obiekty w AO | KIEDY: Zgodnie z harmonogramem Dnia | GDZIE: Wyznaczony sektor AO | W JAKIM CELU: Zapewnienia swobody manewru siłom głównym.";
        private string _additionalTasksSummary = "• Utrzymać łączność meldunkową co 30 min.\n• Dokumentować fotograficznie zdobyte kody i obiekty.";
        private string _commandersIntent = "Skryte podejście z wykorzystaniem rzeźby terenu LiDAR, uderzenie z flanki i natychmiastowe przejście do obrony okrężnej.";
        private string _daySpecificTasks = "08:00 — Odprawa i kontrola łączności\n09:00 — Przekroczenie linii LD (Infil)\n12:00 — Szturm na obiekt główny\n15:00 — Eksfiltracja (Exfil)";
        private string _coordinationMeasures = "Granice AO wiążące. Zakaz otwierania ognia w strefach Safezone i Respawn.";
        private string _syncedCommsMatrixSummary = "";
        private bool _includeCustomChannelsTable = false;
        private string _sarnegKeyWord10 = "PRZEDMIOTY";
        private string _challengeAndPassword = "Hasło: GROM / Odzew: BŁYSK | Liczba kontrolna (Suma): 9";
        private string _targetCodeNames = "OBJ ALFA = TARTAK | OBJ BRAVO = WIEŻA | PUNKT ZBIÓRKI = GNIAZDO";
        private string _successionOfCommand = "1. Lider (ALFA-1) -> 2. Adiutant (ALFA-2) -> 3. Nawigator (ALFA-3)";
        private string _medevacRules = "Trafiony oznacza się czerwoną szmatą / światłem, wzywa Medyka. Czas krwawienia: 10 min. Ewakuacja do Respawnu po 2 opatrunkach.";
        private string _squadGearChecklist = "Replika + zapas kulek, okulary balistyczne, radio PMR/VHF + słuchawka, telefon z ATAK + płytka Heltec V3 LoRa, czerwona szmata, latarka + światło czerwone, woda 2L, apteczka indywidualna.";
        private bool _includeAnnex1Roles = true;
        private bool _includeAnnex2Sarneg = true;
        private bool _includeAnnex3Map = false;
        private string _annex3MapImagePath = "";
        private string _annex3MapNotes = "Mapa taktyczna 1:5000 z siatką MGRS i wyznaczoną granicą AO.";

        public string DayTitle { get => _dayTitle; set => SetField(ref _dayTitle, value, nameof(DayTitle)); }
        public string DateIso { get => _dateIso; set => SetField(ref _dateIso, value, nameof(DateIso)); }
        public bool IncludeInExport { get => _includeInExport; set => SetField(ref _includeInExport, value, nameof(IncludeInExport)); }
        public string OperationNumber { get => _operationNumber; set => SetField(ref _operationNumber, value, nameof(OperationNumber)); }
        public string Classification { get => _classification; set => SetField(ref _classification, value, nameof(Classification)); }
        public string IssuingHq { get => _issuingHq; set => SetField(ref _issuingHq, value, nameof(IssuingHq)); }
        public string LocationName { get => _locationName; set => SetField(ref _locationName, value, nameof(LocationName)); }
        public string LocationLat { get => _locationLat; set => SetField(ref _locationLat, value, nameof(LocationLat)); }
        public string LocationLon { get => _locationLon; set => SetField(ref _locationLon, value, nameof(LocationLon)); }
        public string MetocQuickSummary { get => _metocQuickSummary; set => SetField(ref _metocQuickSummary, value, nameof(MetocQuickSummary)); }
        public string EnemyForces { get => _enemyForces; set => SetField(ref _enemyForces, value, nameof(EnemyForces)); }
        public string FriendlyForces { get => _friendlyForces; set => SetField(ref _friendlyForces, value, nameof(FriendlyForces)); }
        public string TerrainAndWeather { get => _terrainAndWeather; set => SetField(ref _terrainAndWeather, value, nameof(TerrainAndWeather)); }
        public string AreaOfInterestPoiDesc { get => _areaOfInterestPoiDesc; set => SetField(ref _areaOfInterestPoiDesc, value, nameof(AreaOfInterestPoiDesc)); }
        public string MissionFullStatement { get => _missionFullStatement; set => SetField(ref _missionFullStatement, value, nameof(MissionFullStatement)); }
        public string AdditionalTasksSummary { get => _additionalTasksSummary; set => SetField(ref _additionalTasksSummary, value, nameof(AdditionalTasksSummary)); }
        public string CommandersIntent { get => _commandersIntent; set => SetField(ref _commandersIntent, value, nameof(CommandersIntent)); }
        public string DaySpecificTasks { get => _daySpecificTasks; set => SetField(ref _daySpecificTasks, value, nameof(DaySpecificTasks)); }
        public string CoordinationMeasures { get => _coordinationMeasures; set => SetField(ref _coordinationMeasures, value, nameof(CoordinationMeasures)); }
        public string SyncedCommsMatrixSummary { get => _syncedCommsMatrixSummary; set => SetField(ref _syncedCommsMatrixSummary, value, nameof(SyncedCommsMatrixSummary)); }
        public bool IncludeCustomChannelsTable { get => _includeCustomChannelsTable; set => SetField(ref _includeCustomChannelsTable, value, nameof(IncludeCustomChannelsTable)); }
        public string SarnegKeyWord10 { get => _sarnegKeyWord10; set => SetField(ref _sarnegKeyWord10, value, nameof(SarnegKeyWord10)); }
        public string ChallengeAndPassword { get => _challengeAndPassword; set => SetField(ref _challengeAndPassword, value, nameof(ChallengeAndPassword)); }
        public string TargetCodeNames { get => _targetCodeNames; set => SetField(ref _targetCodeNames, value, nameof(TargetCodeNames)); }
        public string SuccessionOfCommand { get => _successionOfCommand; set => SetField(ref _successionOfCommand, value, nameof(SuccessionOfCommand)); }
        public string MedevacRules { get => _medevacRules; set => SetField(ref _medevacRules, value, nameof(MedevacRules)); }
        public string SquadGearChecklist { get => _squadGearChecklist; set => SetField(ref _squadGearChecklist, value, nameof(SquadGearChecklist)); }
        public bool IncludeAnnex1Roles { get => _includeAnnex1Roles; set => SetField(ref _includeAnnex1Roles, value, nameof(IncludeAnnex1Roles)); }
        public bool IncludeAnnex2Sarneg { get => _includeAnnex2Sarneg; set => SetField(ref _includeAnnex2Sarneg, value, nameof(IncludeAnnex2Sarneg)); }
        public bool IncludeAnnex3Map { get => _includeAnnex3Map; set => SetField(ref _includeAnnex3Map, value, nameof(IncludeAnnex3Map)); }
        public string Annex3MapImagePath { get => _annex3MapImagePath; set => SetField(ref _annex3MapImagePath, value, nameof(Annex3MapImagePath)); }
        public string Annex3MapNotes { get => _annex3MapNotes; set => SetField(ref _annex3MapNotes, value, nameof(Annex3MapNotes)); }

        // Opcjonalna tabela dodatkowych kanałów wpisywanych ręcznie w OPORD ("na ptaszek")
        public ObservableCollection<VoiceChannelModel> CustomOpordChannels { get; } = new ObservableCollection<VoiceChannelModel>();

        // Tabela D Składu Oddziału i Ról
        public ObservableCollection<SquadMemberRoleModel> SquadRoster { get; } = new ObservableCollection<SquadMemberRoleModel>();
    }

    public sealed class OpordDocumentModel : ObservableObject
    {
        private string _eventName = "OPERACJA MILSIM 2026";
        private string _targetFaction = "ORGA";
        private int _activeDayIndex = 0;

        public string EventName { get => _eventName; set => SetField(ref _eventName, value, nameof(EventName)); }
        public string TargetFaction { get => _targetFaction; set => SetField(ref _targetFaction, value, nameof(TargetFaction)); }
        public int ActiveDayIndex { get => _activeDayIndex; set => SetField(ref _activeDayIndex, value, nameof(ActiveDayIndex)); }

        public ObservableCollection<OpordDayPlanModel> Days { get; } = new ObservableCollection<OpordDayPlanModel>();
    }

    public sealed class UsbBoardDeviceModel : ObservableObject
    {
        private string _portName = "COM3";
        private string _deviceName = "Heltec WiFi LoRa 32 V3 (ESP32-S3 / CP2102)";
        private string _status = "GOTOWY DO WGRANIA";
        private bool _isSelected = true;

        public string PortName { get => _portName; set => SetField(ref _portName, value, nameof(PortName)); }
        public string DeviceName { get => _deviceName; set => SetField(ref _deviceName, value, nameof(DeviceName)); }
        public string Status { get => _status; set => SetField(ref _status, value, nameof(Status)); }
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value, nameof(IsSelected)); }
    }
}
