using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RangeSector = FieldTakHub.Builder.Services.TacticalMapGeneratorView.TacticalRangeSectorItem;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>
/// Historia cofania: stan bazowy (ostatnio zapisany) oraz stosy „cofnij” i „ponów”.
/// Nowy krok powstaje tylko wtedy, gdy zapisywany stan różni się od bazowego (porównanie po podpisie).
/// </summary>
internal sealed class EditHistory<T> where T : class
{
    private readonly List<T> _undo = new List<T>();
    private readonly List<T> _redo = new List<T>();
    private readonly Func<T, string> _signature;
    private readonly int _capacity;

    public EditHistory(Func<T, string> signature, int capacity)
    {
        _signature = signature;
        _capacity = Math.Max(1, capacity);
    }

    public T? Baseline { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>Nowy dokument (np. wczytany projekt): historia od zera.</summary>
    public void Reset(T state)
    {
        _undo.Clear();
        _redo.Clear();
        Baseline = state;
    }

    /// <summary>Zapisuje stan po zmianach. Zwraca true, gdy powstał nowy krok cofania (wtedy znika „ponów”).</summary>
    public bool Commit(T state)
    {
        if (Baseline == null)
        {
            Baseline = state;
            return false;
        }
        if (string.Equals(_signature(state), _signature(Baseline), StringComparison.Ordinal)) return false;
        _undo.Add(Baseline);
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _redo.Clear();
        Baseline = state;
        return true;
    }

    /// <summary>
    /// Podmienia stan bazowy bez ruszania stosów. Po cofnięciu widok może jeszcze uzupełnić przywrócony stan
    /// (np. domyślny opis w legendzie) — taka poprawka nie jest nowym krokiem i nie może skasować „ponów”.
    /// </summary>
    public void Rebase(T state) => Baseline = state;

    /// <summary>Stan sprzed ostatniego kroku albo null, gdy nie ma czego cofać.</summary>
    public T? Undo()
    {
        if (_undo.Count == 0 || Baseline == null) return null;
        var previous = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Baseline);
        Baseline = previous;
        return previous;
    }

    /// <summary>Stan cofniętego kroku albo null, gdy nie ma czego ponawiać.</summary>
    public T? Redo()
    {
        if (_redo.Count == 0 || Baseline == null) return null;
        var next = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Baseline);
        Baseline = next;
        return next;
    }
}

/// <summary>Kopia obiektu mapy (znacznik, kropka, AO, sektor, trasa) do historii cofania.</summary>
internal sealed class MapObjectSnapshot
{
    private readonly (double Lat, double Lon)[] _points;

    public MapObjectSnapshot(TacticalMapObjectItem item)
    {
        Id = item.Id ?? "";
        Kind = item.Kind ?? "";
        SymbolCode = item.SymbolCode ?? "";
        Name = item.Name ?? "";
        LegendDescription = item.LegendDescription ?? "";
        ColorHex = item.ColorHex ?? "";
        ShowLabel = item.ShowLabel;
        SizeMultiplier = item.SizeMultiplier;
        RotationDeg = item.RotationDeg;
        _points = (item.Points ?? new List<(double Lat, double Lon)>()).ToArray();
    }

    public string Id { get; }
    public string Kind { get; }
    public string SymbolCode { get; }
    public string Name { get; }
    public string LegendDescription { get; }
    public string ColorHex { get; }
    public bool ShowLabel { get; }
    public double SizeMultiplier { get; }
    public double RotationDeg { get; }
    public IReadOnlyList<(double Lat, double Lon)> Points => _points;

    /// <summary>Nowy, niezależny obiekt z tym samym identyfikatorem (zaznaczenie po cofnięciu zostaje na miejscu).</summary>
    public TacticalMapObjectItem ToItem() => new TacticalMapObjectItem
    {
        Id = Id,
        Kind = Kind,
        SymbolCode = SymbolCode,
        Name = Name,
        LegendDescription = LegendDescription,
        ColorHex = ColorHex,
        ShowLabel = ShowLabel,
        SizeMultiplier = SizeMultiplier,
        RotationDeg = RotationDeg,
        Points = _points.ToList()
    };

    internal void AppendSignature(StringBuilder sb)
    {
        sb.Append("O|").Append(Id).Append('|').Append(Kind).Append('|').Append(SymbolCode).Append('|').Append(Name)
          .Append('|').Append(LegendDescription).Append('|').Append(ColorHex).Append('|').Append(ShowLabel ? '1' : '0')
          .Append('|').Append(Num(SizeMultiplier)).Append('|').Append(Num(RotationDeg));
        foreach (var p in _points) sb.Append('|').Append(Num(p.Lat)).Append(',').Append(Num(p.Lon));
        sb.Append('\n');
    }

    internal static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Kopia sektora ognia do historii cofania (bez elementów rysowanych na mapie).</summary>
internal sealed class RangeSectorSnapshot
{
    public RangeSectorSnapshot(RangeSector sector)
    {
        Id = sector.Id ?? "";
        Name = sector.Name ?? "";
        WeaponPreset = sector.WeaponPreset ?? "";
        BaseRadiusMeters = sector.BaseRadiusMeters;
        AzimuthDeg = sector.AzimuthDeg;
        SpreadAngleDeg = sector.SpreadAngleDeg;
        FillOpacity = sector.FillOpacity;
        ColorHex = sector.ColorHex ?? "";
        ElevationDeltaMeters = sector.ElevationDeltaMeters;
        ShooterAltMasl = sector.ShooterAltMasl;
        TargetAltMasl = sector.TargetAltMasl;
        IsVisible = sector.IsVisible;
        OffsetEastMeters = sector.OffsetEastMeters;
        OffsetNorthMeters = sector.OffsetNorthMeters;
        HasGeoAnchor = sector.HasGeoAnchor;
        ShooterLat = sector.ShooterLat;
        ShooterLon = sector.ShooterLon;
    }

    public string Id { get; }
    public string Name { get; }
    public string WeaponPreset { get; }
    public double BaseRadiusMeters { get; }
    public double AzimuthDeg { get; }
    public double SpreadAngleDeg { get; }
    public double FillOpacity { get; }
    public string ColorHex { get; }
    public double ElevationDeltaMeters { get; }
    public double ShooterAltMasl { get; }
    public double TargetAltMasl { get; }
    public bool IsVisible { get; }
    public double OffsetEastMeters { get; }
    public double OffsetNorthMeters { get; }
    public bool HasGeoAnchor { get; }
    public double ShooterLat { get; }
    public double ShooterLon { get; }

    public RangeSector ToItem() => new RangeSector
    {
        Id = Id,
        Name = Name,
        WeaponPreset = WeaponPreset,
        BaseRadiusMeters = BaseRadiusMeters,
        AzimuthDeg = AzimuthDeg,
        SpreadAngleDeg = SpreadAngleDeg,
        FillOpacity = FillOpacity,
        ColorHex = ColorHex,
        ElevationDeltaMeters = ElevationDeltaMeters,
        ShooterAltMasl = ShooterAltMasl,
        TargetAltMasl = TargetAltMasl,
        IsVisible = IsVisible,
        OffsetEastMeters = OffsetEastMeters,
        OffsetNorthMeters = OffsetNorthMeters,
        HasGeoAnchor = HasGeoAnchor,
        ShooterLat = ShooterLat,
        ShooterLon = ShooterLon
    };

    /// <summary>
    /// Przesunięcie względem środka mapy liczy się z kotwicy geograficznej przy każdym przesunięciu mapy —
    /// dla sektora z kotwicą nie jest jego zmianą, więc nie wchodzi do podpisu.
    /// </summary>
    internal void AppendSignature(StringBuilder sb)
    {
        sb.Append("S|").Append(Id).Append('|').Append(Name).Append('|').Append(WeaponPreset)
          .Append('|').Append(MapObjectSnapshot.Num(BaseRadiusMeters)).Append('|').Append(MapObjectSnapshot.Num(AzimuthDeg))
          .Append('|').Append(MapObjectSnapshot.Num(SpreadAngleDeg)).Append('|').Append(MapObjectSnapshot.Num(FillOpacity))
          .Append('|').Append(ColorHex).Append('|').Append(MapObjectSnapshot.Num(ElevationDeltaMeters))
          .Append('|').Append(MapObjectSnapshot.Num(ShooterAltMasl)).Append('|').Append(MapObjectSnapshot.Num(TargetAltMasl))
          .Append('|').Append(IsVisible ? '1' : '0').Append('|').Append(HasGeoAnchor ? '1' : '0');
        if (HasGeoAnchor)
            sb.Append('|').Append(MapObjectSnapshot.Num(ShooterLat)).Append(',').Append(MapObjectSnapshot.Num(ShooterLon));
        else
            sb.Append('|').Append(MapObjectSnapshot.Num(OffsetEastMeters)).Append(',').Append(MapObjectSnapshot.Num(OffsetNorthMeters));
        sb.Append('\n');
    }
}

/// <summary>Stan edytora mapy do cofania: obiekty i sektory ognia oraz skrót SHA-256 ich treści.</summary>
internal sealed record MapEditState(IReadOnlyList<MapObjectSnapshot> Objects, IReadOnlyList<RangeSectorSnapshot> Sectors, string Signature);

internal static class MapEditSnapshots
{
    public const int HistoryCapacity = 60;

    public static MapEditState Capture(IEnumerable<TacticalMapObjectItem> objects, IEnumerable<RangeSector> sectors)
    {
        var objectSnapshots = objects.Where(o => o != null).Select(o => new MapObjectSnapshot(o)).ToList();
        var sectorSnapshots = sectors.Where(s => s != null).Select(s => new RangeSectorSnapshot(s)).ToList();
        var sb = new StringBuilder();
        foreach (var o in objectSnapshots) o.AppendSignature(sb);
        foreach (var s in sectorSnapshots) s.AppendSignature(sb);
        string signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        return new MapEditState(objectSnapshots, sectorSnapshots, signature);
    }

    public static EditHistory<MapEditState> CreateHistory() => new EditHistory<MapEditState>(s => s.Signature, HistoryCapacity);
}
