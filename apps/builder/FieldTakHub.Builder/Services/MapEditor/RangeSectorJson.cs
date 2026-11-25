using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using RangeSector = FieldTakHub.Builder.Services.TacticalMapGeneratorView.TacticalRangeSectorItem;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>
/// Sektory ognia w stanie mapy (sesja robocza, projekt .fthproj) pod kluczem „rangeSectors”. Wcześniej sektory
/// żyły tylko w pamięci i znikały po ponownym uruchomieniu Buildera, jeśli nie zapisano ich osobno do .KMZ.
/// Wartości spoza zakresów edytora są przycinane przy wczytywaniu, a liczby nieskończone zastępowane domyślnymi
/// (JSON nie przyjmuje NaN — jedna zła wartość wywróciłaby zapis całej sesji).
/// </summary>
internal static class RangeSectorJson
{
    public const string Key = "rangeSectors";

    public const double MinRadiusMeters = 5.0, MaxRadiusMeters = 500.0;
    public const double MinSpreadDeg = 5.0, MaxSpreadDeg = 360.0;
    public const double MinFillOpacity = 0.05, MaxFillOpacity = 0.65;
    public const double MaxElevationDeltaMeters = 150.0;

    public static JsonArray ToJsonArray(IEnumerable<RangeSector> sectors)
    {
        var array = new JsonArray();
        foreach (var s in sectors)
        {
            if (s == null) continue;
            array.Add(new JsonObject
            {
                ["id"] = s.Id ?? "",
                ["name"] = s.Name ?? "",
                ["weaponPreset"] = s.WeaponPreset ?? "",
                ["baseRadiusM"] = Finite(s.BaseRadiusMeters, 60.0),
                ["azimuthDeg"] = Finite(s.AzimuthDeg, 0.0),
                ["spreadDeg"] = Finite(s.SpreadAngleDeg, 60.0),
                ["fillOpacity"] = Finite(s.FillOpacity, 0.24),
                ["color"] = s.ColorHex ?? "",
                ["elevationDeltaM"] = Finite(s.ElevationDeltaMeters, 0.0),
                ["shooterAltMasl"] = Finite(s.ShooterAltMasl, 0.0),
                ["targetAltMasl"] = Finite(s.TargetAltMasl, 0.0),
                ["visible"] = s.IsVisible,
                ["offsetEastM"] = Finite(s.OffsetEastMeters, 0.0),
                ["offsetNorthM"] = Finite(s.OffsetNorthMeters, 0.0),
                ["hasGeoAnchor"] = s.HasGeoAnchor,
                ["shooterLat"] = Finite(s.ShooterLat, 0.0),
                ["shooterLon"] = Finite(s.ShooterLon, 0.0)
            });
        }
        return array;
    }

    public static List<RangeSector> FromJsonArray(JsonArray? array)
    {
        var sectors = new List<RangeSector>();
        if (array == null) return sectors;
        foreach (var node in array)
        {
            if (node is not JsonObject o) continue;
            var s = new RangeSector();
            string? id = Text(o, "id");
            if (!string.IsNullOrWhiteSpace(id)) s.Id = id!;
            s.Name = Text(o, "name") ?? s.Name;
            s.WeaponPreset = Text(o, "weaponPreset") ?? s.WeaponPreset;
            s.BaseRadiusMeters = Math.Clamp(Number(o, "baseRadiusM", s.BaseRadiusMeters), MinRadiusMeters, MaxRadiusMeters);
            s.AzimuthDeg = MapObjectCardSpec.NormalizeRotation(Number(o, "azimuthDeg", s.AzimuthDeg));
            s.SpreadAngleDeg = Math.Clamp(Number(o, "spreadDeg", s.SpreadAngleDeg), MinSpreadDeg, MaxSpreadDeg);
            s.FillOpacity = Math.Clamp(Number(o, "fillOpacity", s.FillOpacity), MinFillOpacity, MaxFillOpacity);
            string? color = Text(o, "color");
            if (!string.IsNullOrWhiteSpace(color)) s.ColorHex = color!;
            s.ElevationDeltaMeters = Math.Clamp(Number(o, "elevationDeltaM", 0.0), -MaxElevationDeltaMeters, MaxElevationDeltaMeters);
            s.ShooterAltMasl = Number(o, "shooterAltMasl", 0.0);
            s.TargetAltMasl = Number(o, "targetAltMasl", 0.0);
            s.IsVisible = Flag(o, "visible", true);
            s.OffsetEastMeters = Number(o, "offsetEastM", 0.0);
            s.OffsetNorthMeters = Number(o, "offsetNorthM", 0.0);
            s.ShooterLat = Number(o, "shooterLat", 0.0);
            s.ShooterLon = Number(o, "shooterLon", 0.0);
            s.HasGeoAnchor = Flag(o, "hasGeoAnchor", false) && Math.Abs(s.ShooterLat) <= 90.0 && Math.Abs(s.ShooterLon) <= 180.0;
            sectors.Add(s);
        }
        return sectors;
    }

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private static string? Text(JsonObject o, string key) =>
        o[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static double Number(JsonObject o, string key, double fallback)
    {
        var node = o[key];
        if (node is JsonValue value && value.TryGetValue(out double number) && double.IsFinite(number)) return number;
        return double.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed)
            ? parsed
            : fallback;
    }

    private static bool Flag(JsonObject o, string key, bool fallback)
    {
        var node = o[key];
        if (node is JsonValue value && value.TryGetValue(out bool flag)) return flag;
        return bool.TryParse(node?.ToString(), out bool parsed) ? parsed : fallback;
    }
}
